using System.Net;
using System.Text;
using DesktopPet.AI.Contracts;
using DesktopPet.AI.Providers;
using DesktopPet.AI.Security;
using DesktopPet.AI.Services;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Runtime;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Tests.Unit;

public sealed class PhaseSevenAiTests
{
    [Fact]
    public async Task CredentialVaultSupportsSavedReplaceDeleteAndSessionOnly()
    {
        var store = new MemorySecretStore(); using var vault = new AiCredentialVault(store); var id = Guid.NewGuid();
        var saved = await vault.StoreAsync(id, "first".AsMemory(), CredentialPersistence.Saved, default);
        Assert.Equal("first", Encoding.UTF8.GetString((await vault.ReadAsync(saved, default))!));
        saved = await vault.StoreAsync(id, "second".AsMemory(), CredentialPersistence.Saved, default);
        Assert.Equal("second", Encoding.UTF8.GetString((await vault.ReadAsync(saved, default))!));
        await vault.DeleteAsync(saved, default); Assert.Null(await vault.ReadAsync(saved, default));
        var session = await vault.StoreAsync(id, "session".AsMemory(), CredentialPersistence.SessionOnly, default);
        Assert.StartsWith("session:", session.Value); Assert.Equal("session", Encoding.UTF8.GetString((await vault.ReadAsync(session, default))!));
        vault.Dispose(); Assert.Null(await vault.ReadAsync(session, default));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, ConnectionStatus.Unauthorized, 1)]
    [InlineData(HttpStatusCode.Forbidden, ConnectionStatus.Unauthorized, 1)]
    [InlineData(HttpStatusCode.TooManyRequests, ConnectionStatus.Success, 2)]
    [InlineData(HttpStatusCode.InternalServerError, ConnectionStatus.Success, 2)]
    public async Task ProviderClassifiesAndRetriesOnlyAllowedStatuses(HttpStatusCode first, ConnectionStatus expected, int calls)
    {
        var handler = new SequenceHandler(first, HttpStatusCode.OK); var provider = Provider(handler);
        var result = await provider.TestConnectionAsync(Connection(), default);
        Assert.Equal(expected, result.Status); Assert.Equal(calls, handler.Calls);
    }

    [Fact]
    public async Task ProviderStreamsDeltasAndDoesNotSendTools()
    {
        const string sse = "data: {\"choices\":[{\"delta\":{\"content\":\"hel\"}}]}\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"lo\"}}]}\n\ndata: [DONE]\n\n";
        var handler = new ContentHandler(sse); var provider = Provider(handler); var deltas = new List<ChatDelta>();
        await foreach (var delta in provider.StreamAsync(new(Guid.NewGuid(), new("pet"), Connection(), [new(ChatRole.User, "hi")]), default)) deltas.Add(delta);
        Assert.Equal("hello", string.Concat(deltas.Select(x => x.Text))); Assert.True(deltas[^1].IsComplete);
        Assert.DoesNotContain("tools", handler.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderFetchesAuthorizedModelsForEditableSelection()
    {
        const string response = "{\"object\":\"list\",\"data\":[{\"id\":\"model-z\"},{\"id\":\"model-a\"},{\"id\":\"model-a\"}]}";
        var handler = new ContentHandler(response); var result = await Provider(handler).ListModelsAsync(Connection(), default);
        Assert.True(result.Succeeded); Assert.Equal(["model-a", "model-z"], result.Models); Assert.Equal(HttpMethod.Get, handler.Method);
        Assert.Equal("https://example.test/v1/models", handler.Uri?.ToString()); Assert.True(handler.HasBearer);
    }

    [Fact]
    public async Task ModelDiscoveryUsesTypedKeyOnlyAsTemporarySessionCredential()
    {
        var profile = new AiProviderProfile(Guid.NewGuid(), AiProviderType.OpenAI, "test", new("https://example.test/v1/"),
            "model", TimeSpan.FromSeconds(30), null, true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var vault = new RecordingVault(); var provider = new RecordingModelProvider();
        var service = new AiProviderService(new ProfileRepository(profile), vault, [provider], TimeProvider.System);

        var result = await service.DiscoverModelsAsync(null, AiProviderType.OpenAI, profile.BaseUrl,
            profile.Timeout, "typed-key".AsMemory(), default);

        Assert.True(result.Succeeded); Assert.Equal(["model-a"], result.Models);
        Assert.Equal(CredentialPersistence.SessionOnly, vault.Persistence); Assert.Equal(1, vault.StoreCount);
        Assert.Equal(1, vault.DeleteCount); Assert.Equal(vault.StoredReference, provider.Credential);
    }

    [Theory]
    [InlineData(AiProviderType.OpenAI, "https://api.openai.com/v1/")]
    [InlineData(AiProviderType.DeepSeek, "https://api.deepseek.com/")]
    [InlineData(AiProviderType.AzureOpenAI, "https://your-resource-name.openai.azure.com/")]
    [InlineData(AiProviderType.OpenAICompatible, "https://your-provider.example/v1/")]
    public void ProviderSelectionHasAVisibleSuggestedBaseUrl(AiProviderType type, string expected) =>
        Assert.Equal(expected, AiProviderDefaults.SuggestedBaseUrl(type));

    [Theory]
    [InlineData(AiProviderType.OpenAI, "https://api.example/v1/chat/completions", false)]
    [InlineData(AiProviderType.DeepSeek, "https://api.example/chat/completions", false)]
    [InlineData(AiProviderType.AzureOpenAI, "https://api.example/openai/v1/chat/completions", true)]
    [InlineData(AiProviderType.OpenAICompatible, "https://api.example/custom/chat/completions", false)]
    public async Task AllProviderTypesUseExpectedEndpointAndAuthentication(AiProviderType type, string endpoint, bool azureKey)
    {
        var handler = new CaptureHandler(); var provider = new ChatCompletionsProvider(type, new HttpClient(handler), new FixedVault(), new NoDelay());
        var settings = Connection(url: new(type switch { AiProviderType.OpenAI => "https://api.example/v1/", AiProviderType.DeepSeek => "https://api.example/", AiProviderType.AzureOpenAI => "https://api.example/", _ => "https://api.example/custom/" })) with { ProviderType = type };
        Assert.True((await provider.TestConnectionAsync(settings, default)).Succeeded); Assert.Equal(endpoint, handler.Uri?.ToString());
        Assert.Equal(azureKey, handler.HasApiKey); Assert.Equal(!azureKey, handler.HasBearer);
    }

    [Fact]
    public async Task RetryPolicyUsesFiniteCancelableOneThreeSevenFifteenBackoff()
    {
        var handler = new SequenceHandler(Enumerable.Repeat(HttpStatusCode.InternalServerError, 5).ToArray()); var delay = new RecordingDelay();
        var result = await new ChatCompletionsProvider(AiProviderType.OpenAI, new HttpClient(handler), new FixedVault(), delay).TestConnectionAsync(Connection(), default);
        Assert.Equal(ConnectionStatus.ProviderError, result.Status); Assert.Equal(5, handler.Calls); Assert.Equal([1, 3, 7, 15], delay.Delays.Select(x => (int)x.TotalSeconds).ToArray());
    }

    [Fact]
    public async Task ProviderTimeoutCancelAndInvalidConfigurationAreDistinct()
    {
        var timeout = new ChatCompletionsProvider(AiProviderType.OpenAI, new HttpClient(new WaitingHandler()), new FixedVault(), new NoDelay());
        var timed = await timeout.TestConnectionAsync(Connection(timeout: TimeSpan.FromMilliseconds(20)), default);
        Assert.Equal(ConnectionStatus.InvalidConfiguration, timed.Status); // policy rejects sub-second timeouts before network.
        var actualTimeout = await new ChatCompletionsProvider(AiProviderType.OpenAI, new HttpClient(new ImmediateTimeoutHandler()), new FixedVault(), new NoDelay()).TestConnectionAsync(Connection(), default);
        Assert.Equal(ConnectionStatus.Timeout, actualTimeout.Status);
        var invalid = await Provider(new SequenceHandler(HttpStatusCode.OK)).TestConnectionAsync(Connection(url: new("http://example.com")), default);
        Assert.Equal(ConnectionStatus.InvalidConfiguration, invalid.Status);
        using var cts = new CancellationTokenSource(); cts.Cancel(); var cancelled = await timeout.TestConnectionAsync(Connection(), cts.Token);
        Assert.Equal(ConnectionStatus.Cancelled, cancelled.Status);
    }

    [Fact]
    public async Task MemoryIsCharacterIsolatedBoundedDeduplicatedAndRejectsSensitiveAutoContent()
    {
        var repository = new MemoryRepository(); var service = new MemoryService(repository, TimeProvider.System); var a = new CharacterId("a"); var b = new CharacterId("b");
        await service.SaveAsync(a, new(MemoryCategory.Preference, "likes tea", 5, ["drink"], ["tea"]), default);
        await service.SaveAsync(b, new(MemoryCategory.Preference, "likes coffee", 5, [], ["coffee"]), default);
        Assert.Single(await service.ListAsync(a, default)); Assert.DoesNotContain(await service.ListAsync(a, default), x => x.CharacterId == b);
        Assert.False(await service.TrySaveAutomaticAsync(a, "password: secret", null, default));
        await service.SetAutoEnabledAsync(a, true, default); Assert.True(await service.TrySaveAutomaticAsync(a, "I prefer quiet music", null, default));
        Assert.False(await service.TrySaveAutomaticAsync(a, "I prefer quiet music", null, default)); Assert.Single(await service.FindAsync(a, "tea", 1, default));
        await service.ClearCharacterAsync(a, default); Assert.Empty(await service.ListAsync(a, default)); Assert.Single(await service.ListAsync(b, default));
        await service.ClearAllAsync(default); Assert.Empty(await service.ListAsync(b, default));
    }

    [Fact]
    public async Task ContextIncludesPersonaMemorySummaryRecentHistoryAndStaysWithinBudget()
    {
        var conversation = new Conversation(Guid.NewGuid(), new("pet"), ConversationType.Topic, "topic", "older", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var repository = new ContextConversationRepository(conversation, Enumerable.Range(0, 30).Select(i => new ConversationMessage(Guid.NewGuid(), conversation.Id, i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, new string('x', 700), DateTimeOffset.UtcNow.AddMinutes(i), null, null, null, MessageStatus.Complete)).ToArray());
        var memoryRepository = new MemoryRepository(); await memoryRepository.SaveAsync(new(Guid.NewGuid(), conversation.CharacterId, MemoryCategory.Fact, "uses C#", 5, [], ["C#"], null, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), default);
        var builder = new AiContextBuilder(new FixedPersona(), new MemoryService(memoryRepository, TimeProvider.System), repository);
        var result = await builder.BuildAsync(conversation, "C# question", default);
        Assert.Contains(result, x => x.Content.Contains("persona", StringComparison.OrdinalIgnoreCase)); Assert.Contains(result, x => x.Content.Contains("uses C#"));
        Assert.Contains(result, x => x.Content.Contains("older")); Assert.Equal("C# question", result[^1].Content); Assert.True(result.Sum(x => x.Content.Length) <= AiContextBuilder.CharacterBudget);
    }

    [Fact]
    public async Task CancelledGenerationPersistsPartialAssistantMessageAsInterrupted()
    {
        var conversation = new Conversation(Guid.NewGuid(), new("pet"), ConversationType.Main, "main", null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var repository = new MutableConversationRepository(conversation); var profile = new AiProviderProfile(Guid.NewGuid(), AiProviderType.OpenAI, "test", new("https://example.test/v1/"), "model", TimeSpan.FromSeconds(30), new("saved:test"), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        using var service = new AiChatService(repository, new ProfileRepository(profile), [new PausingProvider()], new FixedContext(), new MemoryService(new MemoryRepository(), TimeProvider.System), new ResponseInterpreter(new RecordingPresentation()), TimeProvider.System);
        await using var stream = service.SendAsync(conversation.Id, "hello", default).GetAsyncEnumerator();
        Assert.True(await stream.MoveNextAsync()); Assert.Equal("partial", stream.Current.Text); await service.StopAsync(default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await stream.MoveNextAsync());
        var assistant = repository.Messages.Single(x => x.Role == ChatRole.Assistant); Assert.Equal("partial", assistant.Content); Assert.Equal(MessageStatus.Interrupted, assistant.Status);
    }

    [Theory]
    [InlineData("answer<pet-hint>{\"emotionHint\":\"happy\",\"animationSemantic\":\"wave\",\"ttsPreference\":\"calm\"}</pet-hint>", true, "wave")]
    [InlineData("answer<pet-hint>{\"animationSemantic\":\"../../evil.png\"}</pet-hint>", false, null)]
    [InlineData("answer<pet-hint>{\"method\":\"RunShell\"}</pet-hint>", false, null)]
    [InlineData("answer<pet-hint>{broken}</pet-hint>", false, null)]
    public async Task ResponseInterpreterAllowsOnlySafeSemanticHints(string response, bool valid, string? semantic)
    {
        var presentation = new RecordingPresentation(); var interpreter = new ResponseInterpreter(presentation); var result = interpreter.Interpret(response);
        Assert.Equal(valid, result.Hint is not null); await interpreter.ApplyAsync(result.Hint, default); Assert.Equal(semantic, presentation.Played?.Value);
    }

    private static ChatCompletionsProvider Provider(HttpMessageHandler handler) => new(AiProviderType.OpenAI, new HttpClient(handler), new FixedVault(), new NoDelay());
    private static AiConnectionSettings Connection(Uri? url = null, TimeSpan? timeout = null) => new(Guid.NewGuid(), AiProviderType.OpenAI, url ?? new("https://example.test/v1/"), "model", timeout ?? TimeSpan.FromSeconds(5), new("saved:test"));
    private sealed class FixedVault : IAiCredentialVault { public Task<SecretReference> StoreAsync(Guid id, ReadOnlyMemory<char> key, CredentialPersistence p, CancellationToken ct) => Task.FromResult(new SecretReference("x")); public Task<byte[]?> ReadAsync(SecretReference r, CancellationToken ct) => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("key")); public Task DeleteAsync(SecretReference r, CancellationToken ct) => Task.CompletedTask; }
    private sealed class RecordingVault : IAiCredentialVault { public int StoreCount { get; private set; } public int DeleteCount { get; private set; } public CredentialPersistence? Persistence { get; private set; } public SecretReference StoredReference { get; } = new("session:test"); public Task<SecretReference> StoreAsync(Guid id, ReadOnlyMemory<char> key, CredentialPersistence persistence, CancellationToken ct) { StoreCount++; Persistence = persistence; return Task.FromResult(StoredReference); } public Task<byte[]?> ReadAsync(SecretReference r, CancellationToken ct) => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("typed-key")); public Task DeleteAsync(SecretReference r, CancellationToken ct) { Assert.Equal(StoredReference, r); DeleteCount++; return Task.CompletedTask; } }
    private sealed class RecordingModelProvider : IChatModelProvider { public AiProviderType ProviderType => AiProviderType.OpenAI; public SecretReference? Credential { get; private set; } public Task<TestConnectionResult> TestConnectionAsync(AiConnectionSettings settings, CancellationToken ct) => Task.FromResult(new TestConnectionResult(ConnectionStatus.Success)); public Task<ModelDiscoveryResult> ListModelsAsync(AiConnectionSettings settings, CancellationToken ct) { Credential = settings.Credential; return Task.FromResult(new ModelDiscoveryResult(ConnectionStatus.Success, ["model-a"])); } public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { await Task.CompletedTask; yield break; } }
    private sealed class NoDelay : IAiRetryDelay { public Task DelayAsync(TimeSpan d, CancellationToken ct) => Task.CompletedTask; }
    private sealed class RecordingDelay : IAiRetryDelay { public List<TimeSpan> Delays { get; } = []; public Task DelayAsync(TimeSpan d, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Delays.Add(d); return Task.CompletedTask; } }
    private sealed class SequenceHandler(params HttpStatusCode[] statuses) : HttpMessageHandler { public int Calls { get; private set; } protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { var status = statuses[Math.Min(Calls++, statuses.Length - 1)]; return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") }); } }
    private sealed class ContentHandler(string content) : HttpMessageHandler { public string Body { get; private set; } = ""; public Uri? Uri { get; private set; } public HttpMethod? Method { get; private set; } public bool HasBearer { get; private set; } protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Uri = request.RequestUri; Method = request.Method; HasBearer = request.Headers.Authorization?.Scheme == "Bearer"; Body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(ct); return new(HttpStatusCode.OK) { Content = new StringContent(content) }; } }
    private sealed class CaptureHandler : HttpMessageHandler { public Uri? Uri { get; private set; } public bool HasApiKey { get; private set; } public bool HasBearer { get; private set; } protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { Uri = request.RequestUri; HasApiKey = request.Headers.Contains("api-key"); HasBearer = request.Headers.Authorization?.Scheme == "Bearer"; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }); } }
    private sealed class WaitingHandler : HttpMessageHandler { protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(HttpStatusCode.OK); } }
    private sealed class ImmediateTimeoutHandler : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromException<HttpResponseMessage>(new TaskCanceledException("timeout")); }
    private sealed class MemorySecretStore : ISecretStore { private readonly Dictionary<string, byte[]> values = []; public Task StoreAsync(SecretReference r, ReadOnlyMemory<byte> s, CancellationToken ct) { values[r.Value] = s.ToArray(); return Task.CompletedTask; } public Task<byte[]?> ReadAsync(SecretReference r, CancellationToken ct) => Task.FromResult(values.TryGetValue(r.Value, out var x) ? x.ToArray() : null); public Task DeleteAsync(SecretReference r, CancellationToken ct) { values.Remove(r.Value); return Task.CompletedTask; } }
    private sealed class RecordingPresentation : ICharacterPresentation { public CharacterPackage? Current => null; public AnimationSemantic? Played { get; private set; } public Task<CharacterOperationResult> ActivateAsync(CharacterId id, CancellationToken ct) => throw new NotSupportedException(); public Task PlayAsync(AnimationSemantic semantic, CancellationToken ct) { Played = semantic; return Task.CompletedTask; } }
    private sealed class FixedPersona : ICharacterPersonaSource { public Task<string?> GetPersonaAsync(CharacterId id, CancellationToken ct) => Task.FromResult<string?>("persona"); }
    private sealed class FixedContext : IAiContextBuilder { public Task<IReadOnlyList<ChatMessage>> BuildAsync(Conversation conversation, string current, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>([new(ChatRole.User, current)]); }
    private sealed class PausingProvider : IChatModelProvider { public AiProviderType ProviderType => AiProviderType.OpenAI; public Task<TestConnectionResult> TestConnectionAsync(AiConnectionSettings settings, CancellationToken ct) => Task.FromResult(new TestConnectionResult(ConnectionStatus.Success)); public Task<ModelDiscoveryResult> ListModelsAsync(AiConnectionSettings settings, CancellationToken ct) => Task.FromResult(new ModelDiscoveryResult(ConnectionStatus.Success, ["model"])); public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { yield return new("partial"); await Task.Delay(Timeout.InfiniteTimeSpan, ct); } }
    private sealed class ProfileRepository(AiProviderProfile profile) : IAiProviderProfileRepository { public Task<IReadOnlyList<AiProviderProfile>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AiProviderProfile>>([profile]); public Task<AiProviderProfile?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<AiProviderProfile?>(profile); public Task SaveAsync(AiProviderProfile value, CancellationToken ct) => Task.CompletedTask; public Task SetActiveAsync(Guid id, CancellationToken ct) => Task.CompletedTask; public Task DeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask; }
    private sealed class MemoryRepository : IMemoryRepository { public List<MemoryItem> Items { get; } = []; private readonly Dictionary<string, bool> enabled = []; public Task<IReadOnlyList<MemoryItem>> ListAsync(CharacterId id, CancellationToken ct) => Task.FromResult<IReadOnlyList<MemoryItem>>(Items.Where(x => x.CharacterId == id).ToArray()); public Task SaveAsync(MemoryItem item, CancellationToken ct) { Items.RemoveAll(x => x.Id == item.Id); Items.Add(item); return Task.CompletedTask; } public Task DeleteAsync(Guid id, CancellationToken ct) { Items.RemoveAll(x => x.Id == id); return Task.CompletedTask; } public Task ClearAsync(CharacterId? id, CancellationToken ct) { Items.RemoveAll(x => id is null || x.CharacterId == id); return Task.CompletedTask; } public Task<bool> GetAutoEnabledAsync(CharacterId id, CancellationToken ct) => Task.FromResult(enabled.GetValueOrDefault(id.Value)); public Task SetAutoEnabledAsync(CharacterId id, bool value, CancellationToken ct) { enabled[id.Value] = value; return Task.CompletedTask; } }
    private sealed class ContextConversationRepository(Conversation item, IReadOnlyList<ConversationMessage> messages) : IConversationRepository { public Task<Conversation> GetOrCreateMainAsync(CharacterId id, CancellationToken ct) => Task.FromResult(item); public Task<Conversation> CreateAsync(CharacterId id, ConversationType type, string title, CancellationToken ct) => Task.FromResult(item); public Task<IReadOnlyList<Conversation>> ListAsync(CharacterId id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([item]); public Task<Conversation?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(item); public Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(Guid id, CancellationToken ct) => Task.FromResult(messages); public Task SaveMessageAsync(ConversationMessage message, CancellationToken ct) => Task.CompletedTask; public Task SaveUsageAsync(AiUsage usage, CancellationToken ct) => Task.CompletedTask; }
    private sealed class MutableConversationRepository(Conversation item) : IConversationRepository { public List<ConversationMessage> Messages { get; } = []; public Task<Conversation> GetOrCreateMainAsync(CharacterId id, CancellationToken ct) => Task.FromResult(item); public Task<Conversation> CreateAsync(CharacterId id, ConversationType type, string title, CancellationToken ct) => Task.FromResult(item); public Task<IReadOnlyList<Conversation>> ListAsync(CharacterId id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([item]); public Task<Conversation?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(item); public Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<ConversationMessage>>(Messages.ToArray()); public Task SaveMessageAsync(ConversationMessage message, CancellationToken ct) { Messages.RemoveAll(x => x.Id == message.Id); Messages.Add(message); return Task.CompletedTask; } public Task SaveUsageAsync(AiUsage usage, CancellationToken ct) => Task.CompletedTask; }
}
