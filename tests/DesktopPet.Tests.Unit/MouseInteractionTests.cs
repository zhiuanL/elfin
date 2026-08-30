using DesktopPet.Application.Commands;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Movement;
using DesktopPet.Application.Windows;
using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Platform;
using DesktopPet.Tests.Shared;

namespace DesktopPet.Tests.Unit;

public sealed class MouseInteractionTests
{
    [Fact]
    public async Task CommandsMapToInputModesAndTemporaryModeRestoresWithoutRealDelay()
    {
        var window = new InputWindow();
        var clock = new ManualTimeProvider();
        using var input = new MouseInteractionService(window, new InlineDispatcher(), clock, new ExceptionHandler(new RecordingLogger(), clock));
        var registry = new CommandRegistry(new[] { CommandId.SetInteractive, CommandId.SetClickThrough, CommandId.ToggleClickThrough, CommandId.TemporaryClickThrough }
            .Select(id => new MouseInteractionCommand(id, input)));
        await registry.ExecuteAsync(CommandId.SetClickThrough, default); Assert.True(window.ClickThrough);
        await registry.ExecuteAsync(CommandId.ToggleClickThrough, default); Assert.False(window.ClickThrough);
        await registry.ExecuteAsync(CommandId.TemporaryClickThrough, default); Assert.Equal(MouseInteractionMode.TemporaryPassThrough, input.Mode);
        var restored = window.NextRestoration;
        clock.Advance(MotionPolicy.TemporaryPassThroughDuration);
        await restored.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(MouseInteractionMode.Interactive, input.Mode);
        Assert.All(TrayMenuDefinition.InputItems(), item => Assert.Contains(item.Command, registry.RegisteredCommands));
        await input.StopAsync(default);
    }
    [Fact]
    public async Task ExplicitOverrideCannotBeUndoneByOldTimerAndHideExitRestoreInteraction()
    {
        var window = new InputWindow();
        var clock = new ManualTimeProvider();
        using var input = new MouseInteractionService(window, new InlineDispatcher(), clock, new ExceptionHandler(new RecordingLogger(), clock));
        await input.SetModeAsync(MouseInteractionMode.TemporaryPassThrough, default);
        await input.SetModeAsync(MouseInteractionMode.ClickThrough, default);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(MouseInteractionMode.ClickThrough, input.Mode);
        window.IsVisible = false;
        await input.ResetAsync(default);
        Assert.False(window.ClickThrough);
        await input.SetModeAsync(MouseInteractionMode.ClickThrough, default);
        Assert.False(window.ClickThrough);
        window.IsVisible = true;
        await input.SetModeAsync(MouseInteractionMode.ClickThrough, default);
        await input.StopAsync(default); await input.StopAsync(default);
        Assert.False(window.ClickThrough);
        await input.SetModeAsync(MouseInteractionMode.ClickThrough, default);
        Assert.False(window.ClickThrough);
    }
    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return action(); }
    }
    private sealed class InputWindow : IPetWindow, IPetMovementPort
    {
        private TaskCompletionSource _restored = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task NextRestoration => _restored.Task;
        public bool IsVisible { get; set; } = true;
        public bool IsUserOwned => false;
        public bool ClickThrough { get; private set; }
        public PixelRect Bounds => new(0, 0, 220, 220);
        public DpiScale Dpi => new(1, 1);
        public void SetClickThrough(bool enabled)
        {
            ClickThrough = enabled;
            if (enabled && _restored.Task.IsCompleted) _restored = new(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!enabled) _restored.TrySetResult();
        }
        public bool TryMoveAutonomously(PixelPoint origin) => IsVisible;
        public event EventHandler? DragCompleted { add { } remove { } }
        public event EventHandler? DisplayMetricsChanged { add { } remove { } }
        public event EventHandler<WindowCommandEventArgs>? CommandRequested { add { } remove { } }
        public event EventHandler<ContextMenuRequestEventArgs>? ContextMenuRequested { add { } remove { } }
        public void EnsureCreated() { }
        public void MoveTo(PixelPoint origin) { }
        public void SetTopmost(bool topmost) { }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close() => IsVisible = false;
        public void Dispose() { }
    }
}
