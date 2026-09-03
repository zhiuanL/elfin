using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Hotkeys;
using DesktopPet.Application.Navigation;

namespace DesktopPet.Tests.Unit;

public sealed class PhaseFiveFoundationTests
{
    [Fact]
    public void NavigationStartsAtHomeRejectsFuturePagesAndDoesNotRepeatEvents()
    {
        var navigation = new ControlCenterNavigationService();
        var changes = 0;
        navigation.Changed += (_, _) => changes++;
        Assert.Equal(AppPage.Home, navigation.Current);
        navigation.Navigate(AppPage.Home);
        navigation.Navigate(AppPage.Characters);
        navigation.Navigate(AppPage.Characters);
        Assert.Equal(AppPage.Characters, navigation.Current);
        Assert.Equal(1, changes);
        navigation.Navigate(AppPage.AI);
        Assert.Equal(AppPage.AI, navigation.Current);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void HotkeySettingsRejectDuplicateEnabledGesturesAndUnsupportedCommands()
    {
        var duplicate = HotkeyCatalog.Defaults().ToArray();
        duplicate[1] = duplicate[1] with { Gesture = duplicate[0].Gesture };
        Assert.False(new HotkeySettings { Bindings = duplicate }.IsValid);
        Assert.False(new HotkeyCommandBinding { Command = CommandId.OpenAi,
            Gesture = new() { Modifiers = HotkeyModifiers.Control, Key = HotkeyKey.A } }.IsValid);
        Assert.True(new HotkeySettings().IsValid);
    }

    [Fact]
    public async Task CoordinatorAppliesPersistsRollsBackConflictDispatchesAndCleansUp()
    {
        var platform = new RecordingHotkeys();
        var settings = new TestSettingsService();
        var command = new RecordingCommand(CommandId.ShowPet);
        var logger = new RecordingLogger();
        await using var coordinator = new HotkeyCoordinator(platform, new CommandRegistry([command]), settings,
            new ExceptionHandler(logger, TimeProvider.System));
        Assert.True((await coordinator.InitializeAsync(default)).Succeeded);
        Assert.Equal(7, platform.RegisteredCommands.Count);

        platform.Emit(CommandId.ShowPet);
        await command.Called.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Equal(1, command.Calls);

        var prior = coordinator.Current;
        var changed = prior.Bindings.Select(item => item.Command == CommandId.HidePet
            ? item with { Gesture = new() { Modifiers = HotkeyModifiers.Control | HotkeyModifiers.Shift, Key = HotkeyKey.H } }
            : item).ToArray();
        platform.FailNext = CommandId.HidePet;
        var conflict = await coordinator.ApplyAsync(new() { Bindings = changed }, default);
        Assert.False(conflict.Succeeded);
        Assert.Equal(CommandId.HidePet, conflict.FailedCommand);
        Assert.Equal(prior, coordinator.Current);
        Assert.Equal(7, platform.RegisteredCommands.Count);

        var disabled = new HotkeySettings { Bindings = prior.Bindings.Select(item =>
            item.Command == CommandId.HidePet ? item with { Enabled = false } : item).ToArray() };
        Assert.True((await coordinator.ApplyAsync(disabled, default)).Succeeded);
        Assert.Equal(disabled, settings.Current.Hotkeys);
        Assert.DoesNotContain(CommandId.HidePet, platform.RegisteredCommands);
        await coordinator.StopAsync(default);
        Assert.Empty(platform.RegisteredCommands);
    }

    private sealed class RecordingCommand(CommandId id) : IAppCommand
    {
        public CommandId Id { get; } = id;
        public int Calls { get; private set; }
        public TaskCompletionSource Called { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<CommandResult> ExecuteAsync(CancellationToken ct)
        {
            Calls++; Called.TrySetResult(); return Task.FromResult(new CommandResult(CommandStatus.Completed));
        }
    }
    private sealed class RecordingHotkeys : IHotkeyService
    {
        private readonly HashSet<CommandId> _registered = [];
        public CommandId? FailNext { get; set; }
        public IReadOnlyCollection<CommandId> RegisteredCommands => _registered.ToArray();
        public event EventHandler<HotkeyInvokedEventArgs>? Invoked;
        public Task<HotkeyRegistrationResult> RegisterAsync(HotkeyCommandBinding binding, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (FailNext == binding.Command) { FailNext = null; return Task.FromResult(new HotkeyRegistrationResult(HotkeyRegistrationStatus.Conflict, "InUse")); }
            _registered.Add(binding.Command);
            return Task.FromResult(new HotkeyRegistrationResult(HotkeyRegistrationStatus.Registered));
        }
        public Task UnregisterAsync(CommandId command, CancellationToken ct) { _registered.Remove(command); return Task.CompletedTask; }
        public Task UnregisterAllAsync(CancellationToken ct) { _registered.Clear(); return Task.CompletedTask; }
        public void Emit(CommandId command) => Invoked?.Invoke(this, new(command));
        public void Dispose() => _registered.Clear();
    }
}
