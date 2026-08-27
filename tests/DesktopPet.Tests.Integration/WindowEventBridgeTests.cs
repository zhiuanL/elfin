using DesktopPet.Application.Commands;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Windows;
using DesktopPet.App.Bootstrap;
using DesktopPet.App.ViewModels;
using DesktopPet.Domain.Platform;
using DesktopPet.Infrastructure.Localization;

namespace DesktopPet.Tests.Integration;

public sealed class WindowEventBridgeTests
{
    [Fact]
    public void TrayAndViewModelIntentsReachTheSameRegistryAndDetachOnDispose()
    {
        using var env = new TestEnvironment();
        var pet = new EventPort();
        var control = new EventPort();
        var tray = new EventPort();
        var model = new MainWindowViewModel(new ResourceTextLocalizer("en-US"));
        var calls = new List<CommandId>();
        var commands = new CommandRegistry(new[] { CommandId.ShowPet, CommandId.HidePet, CommandId.OpenControlCenter,
            CommandId.Exit, CommandId.TogglePetVisibility, CommandId.CloseControlCenter }.Select(id => new RecordingCommand(id, calls)));
        var windows = new RecordingWindows();
        using var bridge = new WindowEventBridge(pet, control, tray, model, commands, windows, new ExceptionHandler(env.Logger, TimeProvider.System));
        bridge.Attach();
        bridge.Attach();
        foreach (var item in TrayMenuDefinition.Create()) tray.EmitCommand(item.Command);
        model.ToggleCommand.Execute(null);
        model.CloseCommand.Execute(null);
        pet.EmitDrag();
        pet.EmitMetrics();
        pet.EmitContextMenu();
        Assert.Equal(new[] { CommandId.ShowPet, CommandId.HidePet, CommandId.OpenControlCenter, CommandId.Exit,
            CommandId.TogglePetVisibility, CommandId.CloseControlCenter }, calls);
        Assert.Equal(2, windows.PositionSaves);
        Assert.Equal(1, tray.ContextMenus);
        bridge.Dispose();
        tray.EmitCommand(CommandId.Exit);
        pet.EmitDrag();
        Assert.Equal(6, calls.Count);
        Assert.Equal(2, windows.PositionSaves);
    }

    private sealed class RecordingCommand(CommandId id, List<CommandId> calls) : IAppCommand
    {
        public CommandId Id => id;
        public Task<CommandResult> ExecuteAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            calls.Add(id);
            return Task.FromResult(new CommandResult(CommandStatus.Completed));
        }
    }
    private sealed class RecordingWindows : IWindowService
    {
        public int PositionSaves { get; private set; }
        public Task SavePositionAsync(CancellationToken ct) { PositionSaves++; return Task.CompletedTask; }
        public Task InitializeAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ShowPetAsync(CancellationToken ct) => Task.CompletedTask;
        public Task HidePetAsync(CancellationToken ct) => Task.CompletedTask;
        public Task TogglePetAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ShowControlCenterAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CloseControlCenterAsync(CancellationToken ct) => Task.CompletedTask;
        public Task ExitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task StopAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class EventPort : IPetWindow, IControlCenterWindow, ITrayService
    {
        public bool IsVisible => true;
        public PixelRect Bounds => new(0, 0, 220, 220);
        public DpiScale Dpi => new(1, 1);
        public int ContextMenus { get; private set; }
        public event EventHandler? DragCompleted;
        public event EventHandler? DisplayMetricsChanged;
        public event EventHandler<WindowCommandEventArgs>? CommandRequested;
        public event EventHandler<ContextMenuRequestEventArgs>? ContextMenuRequested;
        public void EmitCommand(CommandId command) => CommandRequested?.Invoke(this, new(command));
        public void EmitDrag() => DragCompleted?.Invoke(this, EventArgs.Empty);
        public void EmitMetrics() => DisplayMetricsChanged?.Invoke(this, EventArgs.Empty);
        public void EmitContextMenu() => ContextMenuRequested?.Invoke(this, new(new(10, 10)));
        public void ShowContextMenu(PixelPoint position) => ContextMenus++;
        public void EnsureCreated() { }
        public void MoveTo(PixelPoint origin) { }
        public void SetTopmost(bool topmost) { }
        public void Show() { }
        public void Hide() { }
        public void Close() { }
        public void Start() { }
        public void Dispose() { }
    }
}
