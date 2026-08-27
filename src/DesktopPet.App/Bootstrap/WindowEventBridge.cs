using DesktopPet.Application.Commands;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Windows;
using DesktopPet.App.ViewModels;

namespace DesktopPet.App.Bootstrap;

/// <summary>Async UI event boundary; the same registry can serve future input sources.</summary>
public sealed class WindowEventBridge(IPetWindow pet, IControlCenterWindow control, ITrayService tray,
    MainWindowViewModel viewModel, ICommandRegistry commands, IWindowService windows, IExceptionHandler exceptions) : IDisposable
{
    private readonly CancellationTokenSource _events = new();
    private bool _attached, _disposed;
    public void Attach()
    {
        if (_attached) return;
        _attached = true;
        pet.CommandRequested += OnCommand;
        control.CommandRequested += OnCommand;
        tray.CommandRequested += OnCommand;
        viewModel.CommandRequested += OnCommand;
        pet.DragCompleted += OnPositionChanged;
        pet.DisplayMetricsChanged += OnPositionChanged;
        pet.ContextMenuRequested += OnContextMenu;
    }
    private async void OnCommand(object? sender, WindowCommandEventArgs e) =>
        await AtBoundaryAsync(async () => { await commands.ExecuteAsync(e.Command, _events.Token); });
    private async void OnPositionChanged(object? sender, EventArgs e) =>
        await AtBoundaryAsync(() => windows.SavePositionAsync(_events.Token));
    private async void OnContextMenu(object? sender, ContextMenuRequestEventArgs e) =>
        await AtBoundaryAsync(() => { tray.ShowContextMenu(e.ScreenPosition); return Task.CompletedTask; });
    private async Task AtBoundaryAsync(Func<Task> action)
    {
        if (_disposed) return;
        try { await action(); }
        catch (OperationCanceledException) when (_events.IsCancellationRequested) { }
        catch (Exception exception)
        {
            exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command);
            viewModel.ReportCommandFailure();
            if (!_disposed) control.Show();
        }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _events.Cancel();
        pet.CommandRequested -= OnCommand;
        control.CommandRequested -= OnCommand;
        tray.CommandRequested -= OnCommand;
        viewModel.CommandRequested -= OnCommand;
        pet.DragCompleted -= OnPositionChanged;
        pet.DisplayMetricsChanged -= OnPositionChanged;
        pet.ContextMenuRequested -= OnContextMenu;
        _events.Dispose();
    }
}
