using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;
using DesktopPet.Application.Contracts;

namespace DesktopPet.AI.Services;

public sealed class AiChatService(IConversationRepository conversations, IAiProviderProfileRepository profiles,
    IEnumerable<IChatModelProvider> providers, IAiContextBuilder context, IMemoryService memories,
    IResponseInterpreter interpreter, TimeProvider clock, IAiToolRegistry? tools = null, ISpeechService? speech = null) : IAiChatService, IDisposable
{
    private const int MaximumToolRounds = 4;
    private const int MaximumToolCalls = 8;
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _generation;
    public Task<Conversation> GetMainAsync(CharacterId id, CancellationToken ct) => conversations.GetOrCreateMainAsync(id, ct);
    public Task<Conversation> CreateAsync(CharacterId id, ConversationType type, string title, CancellationToken ct) => conversations.CreateAsync(id, type, title, ct);
    public Task<IReadOnlyList<Conversation>> ListAsync(CharacterId id, CancellationToken ct) => conversations.ListAsync(id, ct);
    public Task<IReadOnlyList<ConversationMessage>> MessagesAsync(Guid id, CancellationToken ct) => conversations.ListMessagesAsync(id, ct);

    public async IAsyncEnumerable<AiTurnDelta> SendAsync(Guid conversationId, string text,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 16_000) throw new ArgumentException("Message is empty or too long.", nameof(text));
        await _lifecycle.WaitAsync(ct);
        CancellationTokenSource generation;
        try
        {
            _generation?.Cancel(); _generation?.Dispose();
            _generation = generation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        }
        finally { _lifecycle.Release(); }
        var conversation = await conversations.GetAsync(conversationId, generation.Token) ?? throw new KeyNotFoundException("Conversation not found.");
        var profile = (await profiles.ListAsync(generation.Token)).SingleOrDefault(x => x.IsActive)
            ?? throw new InvalidOperationException("Configure and activate an AI provider first.");
        if (profile.SecretReference is not { } secret) throw new InvalidOperationException("The active provider has no credential.");
        var provider = providers.Single(x => x.ProviderType == profile.ProviderType);
        var now = clock.GetUtcNow();
        var user = new ConversationMessage(Guid.NewGuid(), conversation.Id, ChatRole.User, text.Trim(), now,
            profile.ProviderType.ToString(), profile.Model, null, MessageStatus.Complete);
        var messages = await context.BuildAsync(conversation, text.Trim(), generation.Token);
        await conversations.SaveMessageAsync(user, generation.Token);
        var assistantId = Guid.NewGuid();
        var buffer = new StringBuilder();
        var status = MessageStatus.Interrupted;
        Exception? failure = null;
        var providerMessages = messages.ToList();
        try
        {
            var toolRound = 0;
            var toolCallCount = 0;
            var allowTools = true;
            while (true)
            {
                IReadOnlyList<ModelToolCall> requestedCalls = [];
                var completed = false;
                var definitions = allowTools && tools is not null ? tools.GetAvailableTools() : [];
                var enumerator = provider.StreamAsync(new(conversation.Id, conversation.CharacterId,
                    AiProviderService.Connection(profile, secret), providerMessages, definitions), generation.Token)
                    .GetAsyncEnumerator(generation.Token);
                try
                {
                    while (true)
                    {
                        bool moved;
                        try { moved = await enumerator.MoveNextAsync(); }
                        catch (Exception exception) { failure = exception; break; }
                        if (!moved) break;
                        var delta = enumerator.Current;
                        if (delta.Text.Length > 0) { buffer.Append(delta.Text); yield return new(delta.Text, false, MessageStatus.Interrupted); }
                        if (delta.ToolCalls is { Count: > 0 }) requestedCalls = delta.ToolCalls;
                        if (delta.IsComplete) { completed = true; break; }
                    }
                }
                finally { await enumerator.DisposeAsync(); }
                if (failure is not null) break;
                if (!completed) { status = MessageStatus.Complete; break; }
                if (requestedCalls.Count == 0)
                {
                    status = MessageStatus.Complete;
                    yield return new(string.Empty, true, status);
                    break;
                }
                if (!allowTools)
                {
                    const string limitMessage = "I could not complete the request because the tool-call safety limit was reached.";
                    buffer.Append(limitMessage); status = MessageStatus.Complete;
                    yield return new(limitMessage, true, status);
                    break;
                }

                providerMessages.Add(new(ChatRole.Assistant, string.Empty, ToolCalls: requestedCalls));
                foreach (var call in requestedCalls)
                {
                    AiToolResult result;
                    if (!allowTools || toolRound >= MaximumToolRounds || toolCallCount >= MaximumToolCalls)
                        result = new(ToolExecutionStatus.Denied, "tool_call_limit_reached");
                    else
                    {
                        toolCallCount++;
                        result = tools is null
                            ? new(ToolExecutionStatus.Denied, "tools_unavailable")
                            : await tools.ExecuteAsync(new(call.ToolCallId, call.ToolId, conversation.Id, call.ArgumentsJson), generation.Token);
                    }
                    providerMessages.Add(new(ChatRole.Tool, result.ToModelJson(), call.ToolCallId));
                }
                toolRound++;
                if (toolRound >= MaximumToolRounds || toolCallCount >= MaximumToolCalls) allowTools = false;
            }
        }
        finally
        {
            if (failure is not null && failure is not OperationCanceledException) status = MessageStatus.Failed;
            var raw = buffer.ToString();
            var interpreted = interpreter.Interpret(raw);
            await conversations.SaveMessageAsync(new(assistantId, conversation.Id, ChatRole.Assistant,
                interpreted.DisplayText, clock.GetUtcNow(), profile.ProviderType.ToString(), profile.Model, null, status), CancellationToken.None);
            if (status == MessageStatus.Complete)
            {
                await conversations.SaveUsageAsync(new(Guid.NewGuid(), conversation.Id, assistantId,
                    profile.ProviderType.ToString(), profile.Model, null, null, clock.GetUtcNow()), CancellationToken.None);
                await interpreter.ApplyAsync(interpreted.Hint, CancellationToken.None);
                if (speech is not null && !string.IsNullOrWhiteSpace(interpreted.DisplayText))
                    _ = speech.SpeakAsync(new(new PetInstanceId(Guid.Empty), interpreted.DisplayText,
                        string.Empty, SpeechOrigin.AiAutomatic, interpreted.Hint?.TtsPreference), CancellationToken.None);
                await memories.TrySaveAutomaticAsync(conversation.CharacterId, text.Trim(), user.Id, CancellationToken.None);
            }
            await _lifecycle.WaitAsync(CancellationToken.None);
            try { if (ReferenceEquals(_generation, generation)) { _generation.Dispose(); _generation = null; } }
            finally { _lifecycle.Release(); }
        }
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    public async Task StopAsync(CancellationToken ct)
    {
        await _lifecycle.WaitAsync(ct);
        try { _generation?.Cancel(); }
        finally { _lifecycle.Release(); }
    }
    public void Dispose() { _generation?.Cancel(); _generation?.Dispose(); _lifecycle.Dispose(); }
}

public sealed class ResponseInterpreter(ICharacterPresentation presentation) : IResponseInterpreter
{
    private static readonly Regex Marker = new(@"<pet-hint>(?<json>.*?)</pet-hint>\s*$", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Semantic = new(@"^[a-z][a-z0-9-]{0,31}$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly HashSet<string> Emotions = new(StringComparer.OrdinalIgnoreCase) { "happy", "calm", "focused", "surprised", "rest" };
    private static readonly HashSet<string> TtsPreferences = new(StringComparer.OrdinalIgnoreCase) { "speak", "silent", "calm", "cheerful" };
    public InterpretedResponse Interpret(string response)
    {
        var match = Marker.Match(response);
        if (!match.Success) return new(response, null);
        try
        {
            using var json = JsonDocument.Parse(match.Groups["json"].Value);
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Any(x => x.Name is not ("emotionHint" or "animationSemantic" or "ttsPreference")))
                return new(response[..match.Index].TrimEnd(), null);
            string? Read(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            var emotion = Read("emotionHint"); var animation = Read("animationSemantic"); var tts = Read("ttsPreference");
            if (emotion is not null && !Emotions.Contains(emotion)) return new(response[..match.Index].TrimEnd(), null);
            if (animation is not null && !Semantic.IsMatch(animation)) return new(response[..match.Index].TrimEnd(), null);
            if (tts is not null && !TtsPreferences.Contains(tts)) return new(response[..match.Index].TrimEnd(), null);
            return new(response[..match.Index].TrimEnd(), new(emotion, animation, tts));
        }
        catch (JsonException) { return new(response[..match.Index].TrimEnd(), null); }
    }
    public Task ApplyAsync(PetResponseHint? hint, CancellationToken ct)
    {
        var semantic = hint?.AnimationSemantic ?? hint?.EmotionHint;
        return semantic is null ? Task.CompletedTask : presentation.PlayAsync(new AnimationSemantic(semantic.ToLowerInvariant()), ct);
    }
}
