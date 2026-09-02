using DesktopPet.Application.Commands;
using DesktopPet.Application.Localization;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Application.Windows;

public interface IUiDispatcher
{
    Task InvokeAsync(Func<Task> action, CancellationToken ct);
}
public interface IDisplayService
{
    IReadOnlyList<DisplayArea> GetDisplays();
}
public sealed class WindowCommandEventArgs(CommandId command) : EventArgs
{
    public CommandId Command { get; } = command;
}
public sealed class ContextMenuRequestEventArgs(PixelPoint screenPosition) : EventArgs
{
    public PixelPoint ScreenPosition { get; } = screenPosition;
}
public interface IPetWindow : IDisposable
{
    bool IsVisible { get; }
    PixelRect Bounds { get; }
    DpiScale Dpi { get; }
    void EnsureCreated();
    void MoveTo(PixelPoint origin);
    void SetTopmost(bool topmost);
    void Show();
    void Hide();
    void Close();
    event EventHandler? DragCompleted;
    event EventHandler? DisplayMetricsChanged;
    event EventHandler<WindowCommandEventArgs>? CommandRequested;
    event EventHandler<ContextMenuRequestEventArgs>? ContextMenuRequested;
}
public interface IControlCenterWindow : IDisposable
{
    bool IsVisible { get; }
    void Show();
    void Hide();
    void Close();
    event EventHandler<WindowCommandEventArgs>? CommandRequested;
}
public sealed record TrayMenuItem(CommandId Command, TextKey Label);
public static class TrayMenuDefinition
{
    public static IReadOnlyList<TrayMenuItem> InputItems() =>
    [
        new(CommandId.SetInteractive, TextKey.SetInteractive),
        new(CommandId.ToggleClickThrough, TextKey.ToggleClickThrough),
        new(CommandId.TemporaryClickThrough, TextKey.TemporaryClickThrough)
    ];
    public static IReadOnlyList<TrayMenuItem> Create() =>
    [
        new(CommandId.ShowPet, TextKey.ShowPet),
        new(CommandId.HidePet, TextKey.HidePet),
        new(CommandId.StartOrPausePomodoro, TextKey.PomodoroStartPause),
        new(CommandId.OpenControlCenter, TextKey.OpenControlCenter),
        new(CommandId.Exit, TextKey.ExitApplication)
    ];
}
public interface ITrayService : IDisposable
{
    bool IsVisible { get; }
    void Start();
    void ShowContextMenu(PixelPoint position);
    event EventHandler<WindowCommandEventArgs>? CommandRequested;
}
