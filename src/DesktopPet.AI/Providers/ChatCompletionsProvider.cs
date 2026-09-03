using System.Net;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopPet.AI.Contracts;

namespace DesktopPet.AI.Providers;

public interface IAiRetryDelay { Task DelayAsync(TimeSpan delay, CancellationToken ct); }
public sealed class AiRetryDelay : IAiRetryDelay
{ public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct); }
public sealed class AiProviderException(ConnectionStatus status, string code) : Exception(code)
{ public ConnectionStatus Status { get; } = status; public string Code { get; } = code; }

public sealed class ChatCompletionsProvider(AiProviderType providerType, HttpClient http, IAiCredentialVault credentials,
    IAiRetryDelay retryDelay) : IChatModelProvider
{
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(15)];
    public AiProviderType ProviderType { get; } = providerType;

    public async Task<TestConnectionResult> TestConnectionAsync(AiConnectionSettings settings, CancellationToken ct)
    {
        try
        {
            Validate(settings);
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { model = settings.Model, messages = new[] { new { role = "user", content = "ping" } }, max_tokens = 1, stream = false });
            using var response = await SendWithRetryAsync(settings, HttpMethod.Post, ChatEndpoint(settings), payload, HttpCompletionOption.ResponseContentRead, ct);
            return new(ConnectionStatus.Success);
        }
        catch (AiProviderException exception) { return new(exception.Status, exception.Code); }
        catch (OperationCanceledException) { return new(ct.IsCancellationRequested ? ConnectionStatus.Cancelled : ConnectionStatus.Timeout, ct.IsCancellationRequested ? "cancelled" : "timeout"); }
        catch (HttpRequestException) { return new(ConnectionStatus.NetworkError, "network_error"); }
        catch (ArgumentException) { return new(ConnectionStatus.InvalidConfiguration, "invalid_configuration"); }
    }

    public async Task<ModelDiscoveryResult> ListModelsAsync(AiConnectionSettings settings, CancellationToken ct)
    {
        try
        {
            Validate(settings);
            using var response = await SendWithRetryAsync(settings, HttpMethod.Get, ModelsEndpoint(settings), null,
                HttpCompletionOption.ResponseContentRead, ct);
            var body = await response.Content.ReadAsByteArrayAsync(ct);
            if (body.Length > 2 * 1024 * 1024) throw new AiProviderException(ConnectionStatus.ProviderError, "model_list_too_large");
            using var json = JsonDocument.Parse(body);
            if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new AiProviderException(ConnectionStatus.ProviderError, "invalid_model_list");
            var models = data.EnumerateArray().Where(x => x.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(x => x.GetProperty("id").GetString()).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!).Distinct(StringComparer.Ordinal).Order(StringComparer.OrdinalIgnoreCase).ToArray();
            return models.Length == 0 ? new(ConnectionStatus.ProviderError, [], "empty_model_list") : new(ConnectionStatus.Success, models);
        }
        catch (AiProviderException exception) { return new(exception.Status, [], exception.Code); }
        catch (OperationCanceledException) { return new(ct.IsCancellationRequested ? ConnectionStatus.Cancelled : ConnectionStatus.Timeout, [], ct.IsCancellationRequested ? "cancelled" : "timeout"); }
        catch (HttpRequestException) { return new(ConnectionStatus.NetworkError, [], "network_error"); }
        catch (JsonException) { return new(ConnectionStatus.ProviderError, [], "invalid_model_list"); }
        catch (ArgumentException) { return new(ConnectionStatus.InvalidConfiguration, [], "invalid_configuration"); }
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        Validate(request.Connection);
        var payload = CreateChatPayload(request);
        using var response = await SendWithRetryAsync(request.Connection, HttpMethod.Post, ChatEndpoint(request.Connection), payload,
            HttpCompletionOption.ResponseHeadersRead, ct);
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var toolCalls = new SortedDictionary<int, ToolCallAccumulator>();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") { yield return new(string.Empty, true, BuildToolCalls(toolCalls, request.Tools)); yield break; }
            using var json = JsonDocument.Parse(data);
            if (json.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("delta", out var delta))
            {
                AccumulateToolCalls(delta, toolCalls);
                if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                    yield return new(content.GetString() ?? string.Empty);
            }
        }
        yield return new(string.Empty, true, BuildToolCalls(toolCalls, request.Tools));
    }

    internal static byte[] CreateChatPayload(ChatRequest request)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("model", request.Connection.Model);
            writer.WritePropertyName("messages"); writer.WriteStartArray();
            foreach (var message in request.Messages)
            {
                writer.WriteStartObject(); writer.WriteString("role", message.Role.ToString().ToLowerInvariant());
                if (message.Role == ChatRole.Tool) writer.WriteString("tool_call_id", message.ToolCallId);
                if (message.ToolCalls is { Count: > 0 })
                {
                    writer.WriteNull("content"); writer.WritePropertyName("tool_calls"); writer.WriteStartArray();
                    foreach (var call in message.ToolCalls)
                    {
                        writer.WriteStartObject(); writer.WriteString("id", call.ToolCallId); writer.WriteString("type", "function");
                        writer.WritePropertyName("function"); writer.WriteStartObject();
                        writer.WriteString("name", AiToolProtocolName.Encode(call.ToolId)); writer.WriteString("arguments", call.ArgumentsJson);
                        writer.WriteEndObject(); writer.WriteEndObject();
                    }
                    writer.WriteEndArray();
                }
                else writer.WriteString("content", message.Content);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (request.Tools is { Count: > 0 })
            {
                writer.WritePropertyName("tools"); writer.WriteStartArray();
                foreach (var tool in request.Tools)
                {
                    writer.WriteStartObject(); writer.WriteString("type", "function"); writer.WritePropertyName("function"); writer.WriteStartObject();
                    writer.WriteString("name", AiToolProtocolName.Encode(tool.ToolId)); writer.WriteString("description", tool.Description);
                    writer.WritePropertyName("parameters"); using var schema = JsonDocument.Parse(tool.InputJsonSchema); schema.RootElement.WriteTo(writer);
                    writer.WriteEndObject(); writer.WriteEndObject();
                }
                writer.WriteEndArray(); writer.WriteString("tool_choice", "auto");
            }
            writer.WriteBoolean("stream", true); writer.WriteEndObject();
        }
        return stream.ToArray();
    }
    private static void AccumulateToolCalls(JsonElement delta, IDictionary<int, ToolCallAccumulator> target)
    {
        if (!delta.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array) return;
        foreach (var node in calls.EnumerateArray())
        {
            if (!node.TryGetProperty("index", out var indexNode) || !indexNode.TryGetInt32(out var index)) continue;
            if (!target.TryGetValue(index, out var item)) target[index] = item = new();
            if (node.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String) item.Id.Append(id.GetString());
            if (!node.TryGetProperty("function", out var function)) continue;
            if (function.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String) item.Name.Append(name.GetString());
            if (function.TryGetProperty("arguments", out var arguments) && arguments.ValueKind == JsonValueKind.String) item.Arguments.Append(arguments.GetString());
        }
    }
    private static IReadOnlyList<ModelToolCall> BuildToolCalls(IEnumerable<KeyValuePair<int, ToolCallAccumulator>> source,
        IReadOnlyList<AiToolDefinition>? definitions)
    {
        var byProtocolName = (definitions ?? []).ToDictionary(item => AiToolProtocolName.Encode(item.ToolId), item => item.ToolId, StringComparer.Ordinal);
        return source.Select(item => item.Value).Where(item => item.Id.Length > 0 && item.Name.Length > 0)
            .Select(item => new ModelToolCall(item.Id.ToString(), byProtocolName.TryGetValue(item.Name.ToString(), out var id) ? id : item.Name.ToString(),
                item.Arguments.Length == 0 ? "{}" : item.Arguments.ToString())).ToArray();
    }
    private sealed class ToolCallAccumulator { public StringBuilder Id { get; } = new(); public StringBuilder Name { get; } = new(); public StringBuilder Arguments { get; } = new(); }

    private async Task<HttpResponseMessage> SendWithRetryAsync(AiConnectionSettings settings, HttpMethod method,
        Uri endpoint, byte[]? body, HttpCompletionOption completion, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(settings.Timeout);
            try
            {
                using var request = new HttpRequestMessage(method, endpoint);
                if (body is not null)
                {
                    request.Content = new ByteArrayContent(body);
                    request.Content.Headers.ContentType = new("application/json");
                }
                var secret = await credentials.ReadAsync(settings.Credential, timeout.Token)
                    ?? throw new AiProviderException(ConnectionStatus.InvalidConfiguration, "credential_unavailable");
                try
                {
                    var key = Encoding.UTF8.GetString(secret);
                    if (ProviderType == AiProviderType.AzureOpenAI) request.Headers.TryAddWithoutValidation("api-key", key);
                    else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
                }
                finally { CryptographicOperations.ZeroMemory(secret); }
                var response = await http.SendAsync(request, completion, timeout.Token);
                if (response.IsSuccessStatusCode) return response;
                var status = Classify(response.StatusCode);
                response.Dispose();
                if (!Retryable(response.StatusCode) || attempt >= Backoff.Length)
                    throw new AiProviderException(status, $"http_{(int)response.StatusCode}");
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            { throw new AiProviderException(ConnectionStatus.Timeout, "timeout"); }
            catch (HttpRequestException) when (attempt < Backoff.Length)
            { }
            await retryDelay.DelayAsync(Backoff[attempt], ct);
        }
    }
    private Uri ChatEndpoint(AiConnectionSettings settings) => AppendEndpoint(settings, "chat/completions");
    private Uri ModelsEndpoint(AiConnectionSettings settings) => AppendEndpoint(settings, "models");
    private Uri AppendEndpoint(AiConnectionSettings settings, string path)
    {
        var root = settings.BaseUrl.ToString().TrimEnd('/');
        if (ProviderType == AiProviderType.AzureOpenAI && !root.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase)) root += "/openai/v1";
        return new Uri(root + "/" + path, UriKind.Absolute);
    }
    private static void Validate(AiConnectionSettings settings)
    {
        if (settings.BaseUrl.Scheme != Uri.UriSchemeHttps && !(settings.BaseUrl.IsLoopback && settings.BaseUrl.Scheme == Uri.UriSchemeHttp))
            throw new ArgumentException("HTTPS endpoint required.");
        if (AiProviderDefaults.IsPlaceholder(settings.BaseUrl)) throw new ArgumentException("Replace the example endpoint first.");
        if (string.IsNullOrWhiteSpace(settings.Model) || settings.Timeout < TimeSpan.FromSeconds(1) || settings.Timeout > TimeSpan.FromMinutes(5))
            throw new ArgumentException("Invalid model or timeout.");
    }
    private static bool Retryable(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status >= 500;
    private static ConnectionStatus Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => ConnectionStatus.Unauthorized,
        HttpStatusCode.TooManyRequests => ConnectionStatus.RateLimited,
        _ => ConnectionStatus.ProviderError
    };
}
