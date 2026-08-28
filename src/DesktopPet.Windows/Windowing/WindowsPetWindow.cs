using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Windows;
using DesktopPet.Domain.Platform;
using DesktopPet.Application.Runtime;
using DpiScale = DesktopPet.Domain.Platform.DpiScale;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsPetWindow : IPetWindow, IPetInteractionSource
{
    private readonly Window _window;
    private HwndSource? _source;
    private nint _handle;
    private bool _allowClose, _disposed, _closed, _dragging, _metricsQueued;
    public WindowsPetWindow(Window window)
    {
        _window = window;
        window.MouseLeftButtonDown += OnMouseDown;
        window.MouseRightButtonUp += OnRightClick;
        window.Closing += OnClosing;
        window.Closed += OnClosed;
    }
    public bool IsVisible => _window.IsVisible;
    public PixelRect Bounds => NativeDesktop.GetBounds(Handle);
    public DpiScale Dpi => NativeDesktop.GetDpi(Handle);
    private nint Handle { get { EnsureCreated(); return _handle; } }
    public event EventHandler? DragCompleted;
    public event EventHandler<PetInteractionEventArgs>? Interaction;
    public event EventHandler? DisplayMetricsChanged;
    public event EventHandler<WindowCommandEventArgs>? CommandRequested;
    public event EventHandler<ContextMenuRequestEventArgs>? ContextMenuRequested;

    public void EnsureCreated()
    {
        ObjectDisposedException.ThrowIf(_disposed || _closed, this);
        _window.Dispatcher.VerifyAccess();
        if (_handle != 0) return;
        _handle = new WindowInteropHelper(_window).EnsureHandle();
        _source = HwndSource.FromHwnd(_handle) ?? throw new InvalidOperationException("WPF HWND source unavailable.");
        _source.AddHook(WindowMessage);
    }
    public void MoveTo(PixelPoint origin) => NativeDesktop.Move(Handle, origin);
    public void SetTopmost(bool topmost) => _window.Topmost = topmost;
    public void Show() { EnsureCreated(); _window.Show(); }
    public void Hide() => _window.Hide();
    public void Close()
    {
        if (_closed) return;
        _allowClose = true;
        _window.Close();
    }
    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            CommandRequested?.Invoke(this, new(CommandId.OpenControlCenter));
            return;
        }
        if (e.ButtonState != MouseButtonState.Pressed) return;
        var before = Bounds;
        Interaction?.Invoke(this, new(PetInteractionKind.PointerPressed));
        _dragging = true;
        try { _window.DragMove(); }
        // The button can be released between receiving the message and entering the native move loop.
        catch (InvalidOperationException) when (Mouse.LeftButton == MouseButtonState.Released) { }
        finally { _dragging = false; }
        if (!_closed)
        {
            var after = Bounds;
            Interaction?.Invoke(this, new(before.X == after.X && before.Y == after.Y ? PetInteractionKind.Click : PetInteractionKind.DragEnded));
            DragCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
    private void OnRightClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        ContextMenuRequested?.Invoke(this, new(NativeDesktop.GetCursor()));
    }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        CommandRequested?.Invoke(this, new(CommandId.HidePet));
    }
    private void OnClosed(object? sender, EventArgs e) { _closed = true; DetachHook(); }
    private nint WindowMessage(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message is NativeDesktop.WmDpiChanged or NativeDesktop.WmDisplayChange or NativeDesktop.WmSettingChange)
        {
            // WPF owns WM_DPICHANGED and its suggested RECT. Never scale a second time here.
            if (!_metricsQueued)
            {
                _metricsQueued = true;
                _window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    _metricsQueued = false;
                    if (!_disposed && !_closed && !_dragging) DisplayMetricsChanged?.Invoke(this, EventArgs.Empty);
                });
            }
        }
        return 0;
    }
    private void DetachHook() { _source?.RemoveHook(WindowMessage); _source = null; }
    public void Dispose()
    {
        if (_disposed) return;
        Close();
        DetachHook();
        _window.MouseLeftButtonDown -= OnMouseDown;
        _window.MouseRightButtonUp -= OnRightClick;
        _window.Closing -= OnClosing;
        _window.Closed -= OnClosed;
        _disposed = true;
    }
}
