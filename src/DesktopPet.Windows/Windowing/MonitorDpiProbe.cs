using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Windows.Windowing;

internal static class MonitorDpiProbe
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetThreadDpiAwarenessContext(nint context);
    public static DpiScale Read(DisplayArea display)
    {
        // A short-lived, never-shown top-level HWND obtains DPI through the recommended GetDpiForWindow API.
        // No process awareness changes. Restore the calling thread's previous context in all cases.
        var previous = SetThreadDpiAwarenessContext(-4); // PER_MONITOR_AWARE_V2
        if (previous == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            using var probe = new HwndSource(new HwndSourceParameters("DesktopPet DPI probe")
            {
                PositionX = checked((int)display.Bounds.X + 1), PositionY = checked((int)display.Bounds.Y + 1),
                Width = 1, Height = 1, WindowStyle = unchecked((int)0x80000000),
                ExtendedWindowStyle = 0x08000080 // NOACTIVATE | TOOLWINDOW; no WS_VISIBLE
            });
            return NativeDesktop.GetDpi(probe.Handle);
        }
        finally { SetThreadDpiAwarenessContext(previous); }
    }
}
