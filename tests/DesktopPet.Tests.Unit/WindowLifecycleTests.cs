using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Windows;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Tests.Unit;

public sealed class WindowLifecycleTests
{
    [Fact]
    public async Task StartupRestoresNegativePositionAndPreservesUnrelatedSettings()
    {
        using var fixture = new Fixture();
        fixture.Settings.Current = new AppSettings
        {
            Culture = "en-US", PetWindow = new() { Position = new(new(-1600, -100), "left"), Topmost = false }
        };
        await fixture.Service.InitializeAsync(default);
        Assert.Equal(new PixelRect(-1600, -100, 220, 220), fixture.Pet.Bounds);
        Assert.False(fixture.Pet.Topmost);
        Assert.True(fixture.Pet.IsVisible && fixture.Tray.IsVisible && fixture.Control.IsVisible);
        Assert.Equal("en-US", fixture.Settings.Current.Culture);
    }
    [Fact]
    public async Task StartupWithNoSavedPositionUsesSafeDefault()
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync(default);
        Assert.Equal(new PixelPoint(1676, 796), fixture.Settings.Current.PetWindow.Position!.Origin);
    }
    [Fact]
    public async Task DragEndSavesPositionAndRestartRestoresIt()
    {
        using var first = new Fixture();
        await first.Service.InitializeAsync(default);
        first.Pet.MoveTo(new(-900, 80));
        await first.Service.SavePositionAsync(default);
        using var second = new Fixture();
        second.Settings.Current = first.Settings.Current;
        await second.Service.InitializeAsync(default);
        Assert.Equal(first.Pet.Bounds, second.Pet.Bounds);
        Assert.Equal("left", second.Settings.Current.PetWindow.Position!.DisplayId);
    }
    [Fact]
    public async Task InvalidPositionIsCorrectedAndPersisted()
    {
        using var fixture = new Fixture();
        fixture.Settings.Current = new AppSettings { PetWindow = new() { Position = new(new(99999, 99999), "gone") } };
        await fixture.Service.InitializeAsync(default);
        Assert.Equal(new PixelPoint(1700, 820), fixture.Settings.Current.PetWindow.Position!.Origin);
        fixture.Pet.MoveTo(new(-900, 80));
        fixture.Displays.Areas = [WindowPlacementTests.Displays[0]];
        await fixture.Service.SavePositionAsync(default);
        Assert.Equal(new PixelPoint(0, 80), fixture.Settings.Current.PetWindow.Position!.Origin);
    }
    [Fact]
    public async Task ShowHideToggleAndExitUseRegisteredApplicationCommands()
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync(default);
        foreach (var id in new[] { CommandId.HidePet, CommandId.TogglePetVisibility, CommandId.ShowPet })
        {
            Assert.Equal(CommandStatus.Completed, (await fixture.Commands.ExecuteAsync(id, default)).Status);
            Assert.Equal(id != CommandId.HidePet, fixture.Pet.IsVisible);
            Assert.Equal(fixture.Pet.IsVisible, fixture.Settings.Current.PetWindow.IsVisible);
        }
        await fixture.Commands.ExecuteAsync(CommandId.TogglePetVisibility, default);
        Assert.False(fixture.Pet.IsVisible);
        await fixture.Commands.ExecuteAsync(CommandId.Exit, default);
        Assert.True(fixture.Lifetime.IsShuttingDown);
        await fixture.Service.StopAsync(default);
        await fixture.Service.StopAsync(default);
        Assert.Equal(1, fixture.Tray.DisposeCount);
        Assert.True(fixture.Pet.Closed && fixture.Control.Closed);
        Assert.False(fixture.Settings.Current.PetWindow.IsVisible);
    }
    [Fact]
    public async Task HidePersistsAcrossRestartAndCloseControlCenterDoesNotExitByDefault()
    {
        using var fixture = new Fixture();
        fixture.Settings.Current = new AppSettings { PetWindow = new() { IsVisible = false } };
        await fixture.Service.InitializeAsync(default);
        Assert.False(fixture.Pet.IsVisible);
        await fixture.Commands.ExecuteAsync(CommandId.CloseControlCenter, default);
        Assert.False(fixture.Control.IsVisible);
        Assert.False(fixture.Lifetime.IsShuttingDown);
        Assert.True(fixture.Tray.IsVisible);
        await fixture.Commands.ExecuteAsync(CommandId.OpenControlCenter, default);
        Assert.True(fixture.Control.IsVisible);
        fixture.Settings.Current = fixture.Settings.Current with { ControlCenterCloseBehavior = ControlCenterCloseBehavior.Exit };
        await fixture.Commands.ExecuteAsync(CommandId.CloseControlCenter, default);
        Assert.True(fixture.Lifetime.IsShuttingDown);
    }
    [Fact]
    public async Task TrayMenuMapsExactlyToReusableCommands()
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync(default);
        var items = TrayMenuDefinition.Create();
        Assert.Equal(new[] { CommandId.ShowPet, CommandId.HidePet, CommandId.OpenControlCenter, CommandId.Exit }, items.Select(item => item.Command));
        Assert.Equal(items.Count, items.Select(item => item.Label).Distinct().Count());
        foreach (var item in items)
            Assert.Equal(CommandStatus.Completed, (await fixture.Commands.ExecuteAsync(item.Command, default)).Status);
        Assert.True(fixture.Lifetime.IsShuttingDown);
    }
    [Fact]
    public async Task CancellationDoesNotMutateVisibility()
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync(default);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.HidePetAsync(cts.Token));
        Assert.True(fixture.Pet.IsVisible);
    }
    [Fact]
    public async Task SaveFailureAtExitStillDisposesTrayAndClosesWindows()
    {
        using var fixture = new Fixture();
        await fixture.Service.InitializeAsync(default);
        fixture.Settings.FailWrites = true;
        await Assert.ThrowsAsync<IOException>(() => fixture.Service.StopAsync(default));
        Assert.True(fixture.Pet.Closed && fixture.Control.Closed);
        Assert.Equal(1, fixture.Tray.DisposeCount);
    }

    private sealed class Fixture : IDisposable
    {
        public readonly TestSettingsService Settings = new();
        public readonly FakePet Pet = new();
        public readonly FakeControl Control = new();
        public readonly FakeDisplays Displays = new();
        public readonly FakeTray Tray = new();
        public readonly FakeLifetime Lifetime = new();
        public WindowLifecycleService Service { get; }
        public CommandRegistry Commands { get; }
        public Fixture()
        {
            Service = new(Settings, Pet, Control, Displays, Tray, new InlineDispatcher(), new(), Lifetime);
            Commands = new(new[] { CommandId.ShowPet, CommandId.HidePet, CommandId.TogglePetVisibility,
                CommandId.OpenControlCenter, CommandId.CloseControlCenter, CommandId.Exit }.Select(id => new WindowCommand(id, Service)));
        }
        public void Dispose() => Service.Dispose();
    }
    private sealed class InlineDispatcher : IUiDispatcher
    {
        public Task InvokeAsync(Func<Task> action, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return action(); }
    }
    private sealed class FakeDisplays : IDisplayService
    {
        public IReadOnlyList<DisplayArea> Areas { get; set; } = WindowPlacementTests.Displays;
        public IReadOnlyList<DisplayArea> GetDisplays() => Areas;
    }
    private sealed class FakeLifetime : IAppLifetime
    {
        public bool IsShuttingDown { get; private set; }
        public void RequestShutdown(int exitCode = 0) => IsShuttingDown = true;
    }
    private sealed class FakePet : IPetWindow
    {
        public bool IsVisible { get; private set; }
        public bool Topmost { get; private set; }
        public bool Closed { get; private set; }
        public PixelRect Bounds { get; private set; } = new(0, 0, 220, 220);
        public DpiScale Dpi => new(1, 1);
        public void EnsureCreated() { }
        public void MoveTo(PixelPoint origin) => Bounds = Bounds with { X = origin.X, Y = origin.Y };
        public void SetTopmost(bool topmost) => Topmost = topmost;
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close() { Closed = true; IsVisible = false; }
        public void Dispose() => Close();
        public event EventHandler? DragCompleted { add { } remove { } }
        public event EventHandler? DisplayMetricsChanged { add { } remove { } }
        public event EventHandler<WindowCommandEventArgs>? CommandRequested { add { } remove { } }
        public event EventHandler<ContextMenuRequestEventArgs>? ContextMenuRequested { add { } remove { } }
    }
    private sealed class FakeControl : IControlCenterWindow
    {
        public bool IsVisible { get; private set; }
        public bool Closed { get; private set; }
        public void Show() => IsVisible = true;
        public void Hide() => IsVisible = false;
        public void Close() { Closed = true; IsVisible = false; }
        public void Dispose() => Close();
        public event EventHandler<WindowCommandEventArgs>? CommandRequested { add { } remove { } }
    }
    private sealed class FakeTray : ITrayService
    {
        public bool IsVisible { get; private set; }
        public int DisposeCount { get; private set; }
        public void Start() => IsVisible = true;
        public void ShowContextMenu(PixelPoint position) { }
        public void Dispose() { IsVisible = false; DisposeCount++; }
        public event EventHandler<WindowCommandEventArgs>? CommandRequested { add { } remove { } }
    }
}
