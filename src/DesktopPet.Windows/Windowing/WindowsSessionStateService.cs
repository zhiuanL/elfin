using System.Runtime.InteropServices;
using System.Windows.Interop;
using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsSessionStateService : ISessionStateService
{
    private const int WmPowerBroadcast = 0x0218;
    private const int WmWtsSessionChange = 0x02B1;
    private const int SessionLock = 0x7;
    private const int SessionUnlock = 0x8;
    private const int PowerSuspend = 0x4;
    private const int PowerResumeSuspend = 0x7;
    private const int PowerResumeAutomatic = 0x12;
    private HwndSource? _source;
    private bool _disposed;
    public SessionState State { get; private set; } = SessionState.Active;
    public event EventHandler? StateChanged;
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_source is not null) return;
        _source = new HwndSource(new HwndSourceParameters("DesktopPet.SessionState")
        {
            // A hidden top-level HWND receives WM_POWERBROADCAST; message-only HWNDs do not receive broadcasts.
            ParentWindow = IntPtr.Zero, Width = 0, Height = 0, WindowStyle = 0
        });
        _source.AddHook(WndProc);
        if (!WTSRegisterSessionNotification(_source.Handle, 0))
        {
            var error = Marshal.GetLastWin32Error();
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
            throw new System.ComponentModel.Win32Exception(error);
        }
    }
    private IntPtr WndProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmWtsSessionChange)
        {
            if (wParam.ToInt32() == SessionLock) Set(SessionState.Locked);
            else if (wParam.ToInt32() == SessionUnlock) Set(SessionState.Resuming);
        }
        else if (message == WmPowerBroadcast)
        {
            if (wParam.ToInt32() == PowerSuspend) Set(SessionState.Sleeping);
            else if (wParam.ToInt32() is PowerResumeSuspend or PowerResumeAutomatic) Set(SessionState.Resuming);
        }
        return IntPtr.Zero;
    }
    private void Set(SessionState state)
    {
        if (State == state) return;
        State = state;
        StateChanged?.Invoke(this, EventArgs.Empty);
        if (state == SessionState.Resuming && _source is not null)
            _source.Dispatcher.BeginInvoke(() => Set(SessionState.Active));
    }
    public Task StopAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_source is not null)
        {
            WTSUnRegisterSessionNotification(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
        State = SessionState.Active;
        return Task.CompletedTask;
    }
    public void Dispose()
    {
        if (_disposed) return;
        StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        _disposed = true;
    }
    [DllImport("wtsapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(IntPtr hWnd, uint flags);
    [DllImport("wtsapi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(IntPtr hWnd);
}
