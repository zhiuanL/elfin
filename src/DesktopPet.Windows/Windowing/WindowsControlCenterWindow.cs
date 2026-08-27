using System.ComponentModel;
using System.Windows;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Windows;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsControlCenterWindow : IControlCenterWindow
{
    private readonly Window _window;
    private bool _allowClose, _closed;
    public WindowsControlCenterWindow(Window window)
    {
        _window = window;
        window.Closing += OnClosing;
        window.Closed += OnClosed;
    }
    public bool IsVisible => _window.IsVisible;
    public event EventHandler<WindowCommandEventArgs>? CommandRequested;
    public void Show()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
    }
    public void Hide() => _window.Hide();
    public void Close()
    {
        if (_closed) return;
        _allowClose = true;
        _window.Close();
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        CommandRequested?.Invoke(this, new(CommandId.CloseControlCenter));
    }
    private void OnClosed(object? sender, EventArgs e) => _closed = true;
    public void Dispose()
    {
        Close();
        _window.Closing -= OnClosing;
        _window.Closed -= OnClosed;
    }
}
