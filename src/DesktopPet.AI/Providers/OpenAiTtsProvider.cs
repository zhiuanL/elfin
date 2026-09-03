using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Contracts;

namespace DesktopPet.AI.Providers;

public sealed class OpenAiTtsProvider(HttpClient http, IAiCredentialVault credentials,
    IAiProviderProfileRepository profiles, IAiRetryDelay retryDelay) : ITtsProvider
{
    private const int MaximumAudioBytes = 16 * 1024 * 1024;
    private static readonly TimeSpan[] Backoff = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(7), TimeSpan.FromSeconds(15)];
    private static readonly string[] Voices = ["alloy", "ash", "ballad", "coral", "echo", "fable", "onyx", "nova", "sage", "shimmer", "verse", "marin", "cedar"];
    public string ProviderId => VoiceProviderIds.OpenAI;
    public bool IsOnline => true;
    public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<TtsVoice>>(Voices.Select((voice, index) => new TtsVoice(voice, voice, "mul", index == 0)).ToArray());
    }

    public async Task<SynthesizedSpeech> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken ct)
    {
        var profile = (await profiles.ListAsync(ct)).SingleOrDefault(item => item.IsActive && item.ProviderType == AiProviderType.OpenAI)
            ?? throw new TtsProviderException("openai_profile_unavailable");
        if (profile.SecretReference is not { } secret) throw new TtsProviderException("credential_unavailable");
        if (profile.BaseUrl is not { } baseUrl ||
            baseUrl.Scheme != Uri.UriSchemeHttps && !(baseUrl.IsLoopback && baseUrl.Scheme == Uri.UriSchemeHttp) ||
            AiProviderDefaults.IsPlaceholder(baseUrl)) throw new TtsProviderException("invalid_endpoint");
        if (!Voices.Contains(request.Voice.VoiceId, StringComparer.OrdinalIgnoreCase)) throw new TtsProviderException("invalid_voice");
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            model = request.Voice.Model,
            input = request.Text,
            voice = request.Voice.VoiceId,
            response_format = "wav",
            speed = Math.Clamp(request.Voice.Speed, .25, 4)
        });
        var endpoint = new Uri(baseUrl.ToString().TrimEnd('/') + "/audio/speech", UriKind.Absolute);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        operation.CancelAfter(request.Timeout);
        try
        {
            using var response = await SendAsync(endpoint, payload, secret, operation.Token);
            if (response.Content.Headers.ContentLength is > MaximumAudioBytes) throw new TtsProviderException("audio_too_large");
            await using var source = await response.Content.ReadAsStreamAsync(operation.Token);
            using var output = new MemoryStream();
            var buffer = new byte[81920];
            while (true)
            {
                var read = await source.ReadAsync(buffer, operation.Token);
                if (read == 0) break;
                if (output.Length + read > MaximumAudioBytes) throw new TtsProviderException("audio_too_large");
                await output.WriteAsync(buffer.AsMemory(0, read), operation.Token);
            }
            if (output.Length == 0) throw new TtsProviderException("empty_audio");
            return new(output.ToArray(), SpeechAudioFormat.Wave, ProviderId, request.Voice.VoiceId);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TtsProviderException("timeout"); }
    }

    private async Task<HttpResponseMessage> SendAsync(Uri endpoint, byte[] payload, SecretReference secretReference,
        CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new ByteArrayContent(payload)
                };
                request.Content.Headers.ContentType = new("application/json");
                var secret = await credentials.ReadAsync(secretReference, ct) ?? throw new TtsProviderException("credential_unavailable");
                try { request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Encoding.UTF8.GetString(secret)); }
                finally { CryptographicOperations.ZeroMemory(secret); }
                var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                if (response.IsSuccessStatusCode) return response;
                var status = response.StatusCode;
                response.Dispose();
                if (!Retryable(status) || attempt >= Backoff.Length) throw new TtsProviderException($"http_{(int)status}");
            }
            catch (HttpRequestException exception) when (attempt >= Backoff.Length)
            { throw new TtsProviderException("network_error", exception); }
            catch (HttpRequestException) { }
            await retryDelay.DelayAsync(Backoff[attempt], ct);
        }
    }
    private static bool Retryable(HttpStatusCode status) => status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}
