using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.AI.Contracts;
using DesktopPet.AI.Providers;
using DesktopPet.AI.Tools;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Contracts;
using DesktopPet.AI.Services;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Appearance;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Tests.Unit;

public sealed class PhaseEightAiToolTests
{
    [Fact]
    public async Task LowRiskExecutesDirectlyAndDuplicateCallIdRunsOnlyOnce()
    {
        var tool = new FakeTool(Definition("pet.show", ToolRiskLevel.Low, ConfirmationPolicy.None));
        var fixture = Fixture(tool);
        var call = new AiToolRequest("call-1", "pet.show", Guid.NewGuid(), "{}");
        Assert.True((await fixture.Registry.ExecuteAsync(call, default)).Succeeded);
        Assert.True((await fixture.Registry.ExecuteAsync(call, default)).Succeeded);
        Assert.Equal(1, tool.Calls); Assert.Single(fixture.Audit.Items);
        Assert.Equal(0, fixture.Confirmation.Calls);
    }

    [Fact]
    public async Task MediumAndHighRiskUseConfirmationAndDenialDoesNotExecute()
    {
        var medium = new FakeTool(Definition("reminder.create", ToolRiskLevel.Medium, ConfirmationPolicy.ConfirmOrUndo));
        var high = new FakeTool(Definition("reminder.delete", ToolRiskLevel.High, ConfirmationPolicy.Always));
        var fixture = Fixture(medium, high); fixture.Confirmation.Allowed = false;
        Assert.Equal(ToolExecutionStatus.Denied, (await fixture.Registry.ExecuteAsync(new("m", medium.Definition.ToolId, Guid.NewGuid(), "{}"), default)).Status);
        await fixture.Registry.SetMediumConfirmationPreferenceAsync(MediumConfirmationPreference.AllowReversibleWithoutPrompt, default);
        Assert.True((await fixture.Registry.ExecuteAsync(new("m2", medium.Definition.ToolId, Guid.NewGuid(), "{}"), default)).Succeeded);
        Assert.Equal(ToolExecutionStatus.Denied, (await fixture.Registry.ExecuteAsync(new("h", high.Definition.ToolId, Guid.NewGuid(), "{}"), default)).Status);
        Assert.Equal(1, medium.Calls); Assert.Equal(0, high.Calls); Assert.Equal(2, fixture.Confirmation.Calls);
    }

    [Fact]
    public async Task SchemaValidationAndDisabledToolsFailClosed()
    {
        var tool = new FakeTool(new("settings.set", "setting", """{"type":"object","properties":{"enabled":{"type":"boolean"}},"required":["enabled"],"additionalProperties":false}""",
            ToolRiskLevel.Low, ConfirmationPolicy.None, true));
        var fixture = Fixture(tool);
        var conversation = Guid.NewGuid();
        Assert.Equal(ToolExecutionStatus.ValidationError,
            (await fixture.Registry.ExecuteAsync(new("bad", tool.Definition.ToolId, conversation, """{"enabled":"yes"}"""), default)).Status);
        Assert.Equal(ToolExecutionStatus.ValidationError,
            (await fixture.Registry.ExecuteAsync(new("malformed", tool.Definition.ToolId, conversation, "{broken"), default)).Status);
        await fixture.Registry.SetToolEnabledAsync(tool.Definition.ToolId, false, default);
        Assert.DoesNotContain(fixture.Registry.GetAvailableTools(), item => item.ToolId == tool.Definition.ToolId);
        Assert.Equal(ToolExecutionStatus.Denied,
            (await fixture.Registry.ExecuteAsync(new("disabled", tool.Definition.ToolId, conversation, """{"enabled":true}"""), default)).Status);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task ForbiddenToolsAreNeverExposedOrExecutable()
    {
        var forbidden = new FakeTool(Definition("system.shell", ToolRiskLevel.Forbidden, ConfirmationPolicy.Always));
        var fixture = Fixture(forbidden);
        Assert.Empty(fixture.Registry.GetAvailableTools());
        Assert.Equal(ToolExecutionStatus.Denied,
            (await fixture.Registry.ExecuteAsync(new("call", forbidden.Definition.ToolId, Guid.NewGuid(), "{}"), default)).Status);
        Assert.Equal(0, forbidden.Calls);
    }

    [Fact]
    public async Task AuditFailureDoesNotTurnSuccessfulSideEffectIntoFailureOrRepeatIt()
    {
        var tool = new FakeTool(Definition("pet.show", ToolRiskLevel.Low, ConfirmationPolicy.None));
        var fixture = Fixture(tool); fixture.Audit.ThrowOnSave = true;
        var request = new AiToolRequest("stable-id", tool.Definition.ToolId, Guid.NewGuid(), "{}");
        Assert.True((await fixture.Registry.ExecuteAsync(request, default)).Succeeded);
        Assert.True((await fixture.Registry.ExecuteAsync(request, default)).Succeeded);
        Assert.Equal(1, tool.Calls);
    }

    [Fact]
    public async Task AuditSummaryRedactsSecretsAndNeverStoresTheirValues()
    {
        var tool = new FakeTool(new("safe.inspect", "inspect",
            """{"type":"object","properties":{"apiKey":{"type":"string"},"mode":{"type":"string"}},"required":["apiKey"],"additionalProperties":false}""",
            ToolRiskLevel.Low, ConfirmationPolicy.None, true));
        var fixture = Fixture(tool);
        const string secret = "sensitive-test-value-884";
        await fixture.Registry.ExecuteAsync(new("call", tool.Definition.ToolId, Guid.NewGuid(), $$"""{"apiKey":"{{secret}}","mode":"safe"}"""), default);
        var summary = Assert.Single(fixture.Audit.Items).ParameterSummary;
        Assert.DoesNotContain(secret, summary); Assert.Contains("[redacted]", summary);
    }

    [Fact]
    public async Task ProviderMapsMultipleStreamedToolCallsAndWritesOfficialToolsShape()
    {
        const string sse = "data: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":0,\"id\":\"a\",\"function\":{\"name\":\"pet__show\",\"arguments\":\"{}\"}},{\"index\":1,\"id\":\"b\",\"function\":{\"name\":\"reminder__list\",\"arguments\":\"{\"}}]}}]}\n\ndata: {\"choices\":[{\"delta\":{\"tool_calls\":[{\"index\":1,\"function\":{\"arguments\":\"}\"}}]}}]}\n\ndata: [DONE]\n\n";
        var handler = new CaptureHandler(sse);
        var provider = new ChatCompletionsProvider(AiProviderType.OpenAI, new HttpClient(handler), new FixedVault(), new NoDelay());
        var definitions = new[] { Definition("pet.show", ToolRiskLevel.Low, ConfirmationPolicy.None), Definition("reminder.list", ToolRiskLevel.Low, ConfirmationPolicy.None) };
        var deltas = new List<ChatDelta>();
        await foreach (var delta in provider.StreamAsync(new(Guid.NewGuid(), new("pet"), Connection(), [new(ChatRole.User, "go")], definitions), default)) deltas.Add(delta);
        Assert.Equal(["pet.show", "reminder.list"], deltas[^1].ToolCalls!.Select(item => item.ToolId));
        using var body = JsonDocument.Parse(handler.Body);
        Assert.Equal("pet__show", body.RootElement.GetProperty("tools")[0].GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("auto", body.RootElement.GetProperty("tool_choice").GetString());
    }

    [Fact]
    public async Task ChatOrchestratorReturnsToolResultToModelBeforeFinalAnswer()
    {
        var conversation = new Conversation(Guid.NewGuid(), new("pet"), ConversationType.Main, "main", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var conversations = new ChatRepository(conversation);
        var profile = new AiProviderProfile(Guid.NewGuid(), AiProviderType.OpenAI, "test", new("https://example.test/v1/"),
            "model", TimeSpan.FromSeconds(5), new("saved:test"), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var provider = new ToolCallingProvider();
        var registry = new RecordingRegistry();
        using var service = new AiChatService(conversations, new Profiles(profile), [provider], new Context(),
            new Memories(), new Interpreter(), TimeProvider.System, registry);
        var output = new StringBuilder();
        await foreach (var delta in service.SendAsync(conversation.Id, "show pet", default)) output.Append(delta.Text);
        Assert.Equal("done", output.ToString()); Assert.Single(registry.Requests); Assert.Equal(2, provider.Calls);
        Assert.Contains(provider.SecondRequest!.Messages, item => item.Role == ChatRole.Tool && item.ToolCallId == "call-1");
        Assert.Contains(conversations.Messages, item => item.Role == ChatRole.Assistant && item.Content == "done");
    }

    [Fact]
    public void V1CatalogContainsOnlyExplicitPomodoroReminderUiPetAndSettingsTools()
    {
        var definitions = Enum.GetValues<PomodoroToolKind>().Select(kind => new PomodoroAiTool(kind, null!, null!).Definition)
            .Concat(Enum.GetValues<ReminderToolKind>().Select(kind => new ReminderAiTool(kind, null!, null!).Definition))
            .Concat(Enum.GetValues<UiToolKind>().Select(kind => new UiAiTool(kind, null!).Definition))
            .Concat(Enum.GetValues<PetToolKind>().Select(kind => new PetAiTool(kind, null!, null!).Definition))
            .Append(new SettingsAiTool(null!, null!, null!).Definition).ToArray();
        Assert.Equal(19, definitions.Length);
        Assert.Contains(definitions, item => item.ToolId == "pomodoro.start" && item.RiskLevel == ToolRiskLevel.Low);
        Assert.Contains(definitions, item => item.ToolId == "reminder.create" && item.RiskLevel == ToolRiskLevel.Medium);
        Assert.Contains(definitions, item => item.ToolId == "reminder.delete" && item.RiskLevel == ToolRiskLevel.High);
        Assert.Contains(definitions, item => item.ToolId == "ui.openSettings" && item.RiskLevel == ToolRiskLevel.Low);
        Assert.Contains(definitions, item => item.ToolId == "pet.setMovementMode" && item.RiskLevel == ToolRiskLevel.Medium);
        Assert.Contains(definitions, item => item.ToolId == "settings.set" && item.RiskLevel == ToolRiskLevel.Medium);
        Assert.All(definitions, item => Assert.NotEqual(ToolRiskLevel.Forbidden, item.RiskLevel));
    }

    [Fact]
    public async Task ConcreteToolFamiliesDelegateToExistingApplicationBoundaries()
    {
        var pomodoro = new FakePomodoro();
        Assert.True((await new PomodoroAiTool(PomodoroToolKind.Start, pomodoro, new FakeSettings())
            .ExecuteAsync(Args("""{"phase":"focus","minutes":15}"""), default)).Succeeded);
        Assert.Equal(PomodoroStatus.Running, (await pomodoro.GetSnapshotAsync(default)).Status);

        var reminders = new FakeReminders();
        var due = DateTimeOffset.UtcNow.AddHours(1).ToString("O");
        Assert.True((await new ReminderAiTool(ReminderToolKind.Create, reminders, TimeProvider.System)
            .ExecuteAsync(Args($$"""{"title":"Review","scheduleType":"oneTime","dueAtUtc":"{{due}}"}"""), default)).Succeeded);
        Assert.Single(await reminders.ListAsync(default));

        var commands = new FakeCommands();
        await new UiAiTool(UiToolKind.OpenSettings, commands).ExecuteAsync(Args("{}"), default);
        await new PetAiTool(PetToolKind.Hide, commands, new FakeSettings()).ExecuteAsync(Args("{}"), default);
        Assert.Equal([CommandId.OpenSettings, CommandId.HidePet], commands.Executed);

        var settings = new FakeSettings(); var appearance = new FakeAppearance();
        Assert.True((await new SettingsAiTool(settings, appearance, commands)
            .ExecuteAsync(Args("""{"setting":"theme","value":"dark"}"""), default)).Succeeded);
        Assert.Equal(ThemeMode.Dark, settings.Current.Appearance.Theme); Assert.Equal(ThemeMode.Dark, appearance.Current);
    }

    [Fact]
    public async Task ToolFailureStillAllowsModelFinalResponse()
    {
        var conversation = new Conversation(Guid.NewGuid(), new("pet"), ConversationType.Main, "main", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var conversations = new ChatRepository(conversation); var provider = new ToolCallingProvider();
        var registry = new RecordingRegistry { Result = new(ToolExecutionStatus.Failed, "test_failure") };
        using var service = Service(conversation, conversations, provider, registry);
        var output = new StringBuilder();
        await foreach (var delta in service.SendAsync(conversation.Id, "try", default)) output.Append(delta.Text);
        Assert.Equal("done", output.ToString());
        Assert.Contains(provider.SecondRequest!.Messages, item => item.Role == ChatRole.Tool && item.Content.Contains("test_failure"));
    }

    [Fact]
    public async Task ToolRoundsAreBoundedAndEndWithSafeFinalMessage()
    {
        var conversation = new Conversation(Guid.NewGuid(), new("pet"), ConversationType.Main, "main", null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var conversations = new ChatRepository(conversation); var provider = new LoopingProvider(); var registry = new RecordingRegistry();
        using var service = Service(conversation, conversations, provider, registry);
        var output = new StringBuilder();
        await foreach (var delta in service.SendAsync(conversation.Id, "loop", default)) output.Append(delta.Text);
        Assert.Equal(4, registry.Requests.Count); Assert.Equal(5, provider.Calls);
        Assert.Contains("safety limit", output.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static AiToolDefinition Definition(string id, ToolRiskLevel risk, ConfirmationPolicy confirmation) =>
        new(id, id, """{"type":"object","properties":{},"additionalProperties":false}""", risk, confirmation, true);
    private static JsonElement Args(string json) { using var document = JsonDocument.Parse(json); return document.RootElement.Clone(); }
    private static (AiToolRegistry Registry, FakeAudit Audit, FakeConfirmation Confirmation) Fixture(params IAiTool[] tools)
    {
        var audit = new FakeAudit(); var confirmation = new FakeConfirmation(); var settings = new FakeSettings();
        return (new(tools, new AiToolSchemaValidator(), confirmation, audit, settings,
            new ExceptionHandler(new NullLogger(), TimeProvider.System), TimeProvider.System), audit, confirmation);
    }
    private static AiConnectionSettings Connection() => new(Guid.NewGuid(), AiProviderType.OpenAI,
        new("https://example.test/v1/"), "model", TimeSpan.FromSeconds(5), new("saved:test"));
    private static AiChatService Service(Conversation conversation, ChatRepository repository, IChatModelProvider provider, IAiToolRegistry registry)
    {
        var profile = new AiProviderProfile(Guid.NewGuid(), AiProviderType.OpenAI, "test", new("https://example.test/v1/"),
            "model", TimeSpan.FromSeconds(5), new("saved:test"), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return new(repository, new Profiles(profile), [provider], new Context(), new Memories(), new Interpreter(), TimeProvider.System, registry);
    }
    private sealed class FakeTool(AiToolDefinition definition) : IAiTool
    { public AiToolDefinition Definition { get; } = definition; public int Calls { get; private set; }
        public Task<AiToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct) { Calls++; return Task.FromResult(AiToolResult.Success("ok")); } }
    private sealed class FakeAudit : IAiToolAuditRepository
    { public List<AiToolAuditEntry> Items { get; } = []; public bool ThrowOnSave { get; set; }
        public Task SaveAsync(AiToolAuditEntry entry, CancellationToken ct) { if (ThrowOnSave) throw new IOException("audit unavailable"); Items.Add(entry); return Task.CompletedTask; }
        public Task<IReadOnlyList<AiToolAuditEntry>> ListRecentAsync(int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<AiToolAuditEntry>>(Items.Take(limit).ToArray()); }
    private sealed class FakeConfirmation : IToolConfirmationService
    { public bool Allowed { get; set; } = true; public int Calls { get; private set; }
        public Task<bool> ConfirmAsync(ToolConfirmationRequest request, CancellationToken ct) { Calls++; return Task.FromResult(Allowed); } }
    private sealed class FakeSettings : ISettingsService
    { public AppSettings Current { get; private set; } = new(); public Task<SettingsLoadResult> LoadAsync(CancellationToken ct) => Task.FromResult(new SettingsLoadResult(Current, SettingsLoadStatus.Loaded));
        public Task SaveAsync(AppSettings settings, CancellationToken ct) { Current = settings; return Task.CompletedTask; }
        public Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct) { Current = update(Current); return Task.CompletedTask; } }
    private sealed class NullLogger : IAppLogger { public void Configure(LogOptions options) { } public void Write(AppLogEntry entry) { } }
    private sealed class FakeCommands : ICommandRegistry
    { public List<CommandId> Executed { get; } = []; public IReadOnlyCollection<CommandId> RegisteredCommands => Executed;
        public Task<CommandResult> ExecuteAsync(CommandId id, CancellationToken ct) { Executed.Add(id); return Task.FromResult(new CommandResult(CommandStatus.Completed)); } }
    private sealed class FakeAppearance : IAppearanceService
    { public ThemeMode Current { get; private set; } = ThemeMode.System; public event EventHandler? Changed;
        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ApplyAsync(ThemeMode theme, CancellationToken ct) { Current = theme; Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; } }
    private sealed class FakePomodoro : IPomodoroService
    {
        private PomodoroSession? _session; public event EventHandler? Changed;
        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask; public Task<PomodoroSession?> GetCurrentAsync(CancellationToken ct) => Task.FromResult(_session);
        public Task<PomodoroSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(new PomodoroSnapshot(_session, PomodoroPhase.Focus, 0, DateTimeOffset.UtcNow));
        public Task StartAsync(PomodoroPhase phase, TimeSpan duration, Guid? taskId, CancellationToken ct)
        { var now = DateTimeOffset.UtcNow; _session = new(Guid.NewGuid(), taskId, phase, now, now + duration, null, PomodoroStatus.Running, duration, TimeSpan.Zero, TimeSpan.Zero, 0); Changed?.Invoke(this, EventArgs.Empty); return Task.CompletedTask; }
        public Task PauseAsync(CancellationToken ct) => Task.CompletedTask; public Task ResumeAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask; public Task RefreshAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopSchedulerAsync(CancellationToken ct) => Task.CompletedTask; public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class FakeReminders : IReminderService
    {
        private readonly List<Reminder> _items = []; public event EventHandler? Changed;
        public Task<Reminder> CreateAsync(Reminder reminder, CancellationToken ct) { _items.Add(reminder); Changed?.Invoke(this, EventArgs.Empty); return Task.FromResult(reminder); }
        public Task<Reminder?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(_items.FirstOrDefault(item => item.Id == id));
        public Task<IReadOnlyList<Reminder>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<Reminder>>(_items);
        public Task<Reminder> UpdateAsync(Reminder reminder, CancellationToken ct) { _items.RemoveAll(item => item.Id == reminder.Id); _items.Add(reminder); return Task.FromResult(reminder); }
        public Task DeleteAsync(Guid id, CancellationToken ct) { _items.RemoveAll(item => item.Id == id); return Task.CompletedTask; }
        public Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct) { var item = _items.Single(value => value.Id == id); _items[_items.IndexOf(item)] = item with { Enabled = enabled }; return Task.CompletedTask; }
    }
    private sealed class FixedVault : IAiCredentialVault
    { public Task<SecretReference> StoreAsync(Guid id, ReadOnlyMemory<char> key, CredentialPersistence p, CancellationToken ct) => Task.FromResult(new SecretReference("x"));
        public Task<byte[]?> ReadAsync(SecretReference r, CancellationToken ct) => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("key")); public Task DeleteAsync(SecretReference r, CancellationToken ct) => Task.CompletedTask; }
    private sealed class NoDelay : IAiRetryDelay { public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.CompletedTask; }
    private sealed class CaptureHandler(string content) : HttpMessageHandler
    { public string Body { get; private set; } = ""; protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Body = await request.Content!.ReadAsStringAsync(ct); return new(HttpStatusCode.OK) { Content = new StringContent(content) }; } }
    private sealed class ToolCallingProvider : IChatModelProvider
    {
        public AiProviderType ProviderType => AiProviderType.OpenAI; public int Calls { get; private set; } public ChatRequest? SecondRequest { get; private set; }
        public Task<TestConnectionResult> TestConnectionAsync(AiConnectionSettings settings, CancellationToken ct) => throw new NotSupportedException();
        public Task<ModelDiscoveryResult> ListModelsAsync(AiConnectionSettings settings, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            Calls++; await Task.CompletedTask;
            if (Calls == 1) yield return new("", true, [new("call-1", "pet.show", "{}")]);
            else { SecondRequest = request; yield return new("done", true); }
        }
    }
    private sealed class LoopingProvider : IChatModelProvider
    {
        public AiProviderType ProviderType => AiProviderType.OpenAI; public int Calls { get; private set; }
        public Task<TestConnectionResult> TestConnectionAsync(AiConnectionSettings settings, CancellationToken ct) => throw new NotSupportedException();
        public Task<ModelDiscoveryResult> ListModelsAsync(AiConnectionSettings settings, CancellationToken ct) => throw new NotSupportedException();
        public async IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        { Calls++; await Task.CompletedTask; yield return new("", true, [new("loop-" + Calls, "pet.show", "{}")]); }
    }
    private sealed class RecordingRegistry : IAiToolRegistry
    {
        public List<AiToolRequest> Requests { get; } = []; public bool ToolsEnabled => true;
        public AiToolResult Result { get; set; } = AiToolResult.Success("shown");
        public MediumConfirmationPreference MediumConfirmationPreference => MediumConfirmationPreference.AlwaysAsk;
        public IReadOnlyList<AiToolDefinition> GetAvailableTools() => [Definition("pet.show", ToolRiskLevel.Low, ConfirmationPolicy.None)];
        public IReadOnlyList<AiToolState> GetToolStates() => []; public Task SetToolsEnabledAsync(bool enabled, CancellationToken ct) => Task.CompletedTask;
        public Task SetToolEnabledAsync(string toolId, bool enabled, CancellationToken ct) => Task.CompletedTask;
        public Task SetMediumConfirmationPreferenceAsync(MediumConfirmationPreference preference, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<AiToolAuditEntry>> GetRecentAuditAsync(int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<AiToolAuditEntry>>([]);
        public Task<AiToolResult> ExecuteAsync(AiToolRequest request, CancellationToken ct) { Requests.Add(request); return Task.FromResult(Result); }
    }
    private sealed class Profiles(AiProviderProfile profile) : IAiProviderProfileRepository
    { public Task<IReadOnlyList<AiProviderProfile>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AiProviderProfile>>([profile]);
        public Task<AiProviderProfile?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<AiProviderProfile?>(profile);
        public Task SaveAsync(AiProviderProfile value, CancellationToken ct) => Task.CompletedTask; public Task SetActiveAsync(Guid id, CancellationToken ct) => Task.CompletedTask; public Task DeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask; }
    private sealed class ChatRepository(Conversation conversation) : IConversationRepository
    { public List<ConversationMessage> Messages { get; } = []; public Task<Conversation> GetOrCreateMainAsync(CharacterId id, CancellationToken ct) => Task.FromResult(conversation);
        public Task<Conversation> CreateAsync(CharacterId id, ConversationType type, string title, CancellationToken ct) => Task.FromResult(conversation);
        public Task<IReadOnlyList<Conversation>> ListAsync(CharacterId id, CancellationToken ct) => Task.FromResult<IReadOnlyList<Conversation>>([conversation]);
        public Task<Conversation?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<Conversation?>(conversation);
        public Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(Guid id, CancellationToken ct) => Task.FromResult<IReadOnlyList<ConversationMessage>>(Messages);
        public Task SaveMessageAsync(ConversationMessage message, CancellationToken ct) { Messages.Add(message); return Task.CompletedTask; }
        public Task SaveUsageAsync(AiUsage usage, CancellationToken ct) => Task.CompletedTask; }
    private sealed class Context : IAiContextBuilder
    { public Task<IReadOnlyList<ChatMessage>> BuildAsync(Conversation conversation, string current, CancellationToken ct) => Task.FromResult<IReadOnlyList<ChatMessage>>([new(ChatRole.User, current)]); }
    private sealed class Memories : IMemoryService
    { public Task<IReadOnlyList<MemoryItem>> ListAsync(CharacterId id, CancellationToken ct) => Task.FromResult<IReadOnlyList<MemoryItem>>([]);
        public Task<IReadOnlyList<MemoryItem>> FindAsync(CharacterId id, string query, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<MemoryItem>>([]);
        public Task<MemoryItem> SaveAsync(CharacterId id, MemoryDraft draft, CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask; public Task ClearCharacterAsync(CharacterId id, CancellationToken ct) => Task.CompletedTask;
        public Task ClearAllAsync(CancellationToken ct) => Task.CompletedTask; public Task<bool> GetAutoEnabledAsync(CharacterId id, CancellationToken ct) => Task.FromResult(false);
        public Task SetAutoEnabledAsync(CharacterId id, bool enabled, CancellationToken ct) => Task.CompletedTask; public Task<bool> TrySaveAutomaticAsync(CharacterId id, string value, Guid? source, CancellationToken ct) => Task.FromResult(false); }
    private sealed class Interpreter : IResponseInterpreter
    { public InterpretedResponse Interpret(string response) => new(response, null); public Task ApplyAsync(PetResponseHint? hint, CancellationToken ct) => Task.CompletedTask; }
}
