using System.ComponentModel;
using System.Runtime.InteropServices;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Windows.Windowing;

internal static class NativeDesktop
{
    internal const int WmDpiChanged = 0x02E0;
    internal const int WmDisplayChange = 0x007E;
    internal const int WmSettingChange = 0x001A;
    private const uint NoSize = 0x0001, NoZOrder = 0x0004, NoActivate = 0x0010;
    private const int ExtendedStyle = -20, TransparentStyle = 0x20;
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint window, int index, nint value);
    internal static void SetClickThrough(nint window, bool enabled)
    {
        var style = GetWindowLongPtr(window, ExtendedStyle).ToInt64();
        var next = enabled ? style | TransparentStyle : style & ~TransparentStyle;
        Marshal.SetLastPInvokeError(0);
        if (SetWindowLongPtr(window, ExtendedStyle, (nint)next) == 0 && Marshal.GetLastPInvokeError() != 0)
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        if (!SetWindowPos(window, 0, 0, 0, 0, 0, NoSize | NoZOrder | NoActivate | 0x0002 | 0x0020))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfo
    {
        public uint Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
    }
    private delegate bool MonitorCallback(nint monitor, nint dc, ref Rect bounds, nint state);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint state);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out Point point);

    internal static IReadOnlyList<DisplayArea> GetDisplays()
    {
        var displays = new List<DisplayArea>();
        var error = 0;
        bool Visit(nint monitor, nint dc, ref Rect rect, nint state)
        {
            var info = new MonitorInfo { Size = (uint)Marshal.SizeOf<MonitorInfo>(), Device = string.Empty };
            if (!GetMonitorInfo(monitor, ref info))
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            displays.Add(new(info.Device, ToRect(info.Monitor), ToRect(info.Work), (info.Flags & 1) != 0));
            return true;
        }
        if (!EnumDisplayMonitors(0, 0, Visit, 0)) throw new Win32Exception(error == 0 ? Marshal.GetLastWin32Error() : error);
        if (displays.Count == 0) throw new InvalidOperationException("Windows returned no active displays.");
        return displays.AsReadOnly();
    }
    internal static PixelRect GetBounds(nint window)
    {
        if (!GetWindowRect(window, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return ToRect(rect);
    }
    internal static DpiScale GetDpi(nint window)
    {
        var dpi = GetDpiForWindow(window);
        if (dpi == 0) throw new InvalidOperationException("Cannot read DPI for an invalid window.");
        return DpiMath.FromDpi(dpi);
    }
    internal static void Move(nint window, PixelPoint origin)
    {
        if (!double.IsFinite(origin.X) || !double.IsFinite(origin.Y)) throw new ArgumentOutOfRangeException(nameof(origin));
        if (!SetWindowPos(window, 0, checked((int)Math.Round(origin.X)), checked((int)Math.Round(origin.Y)),
            0, 0, NoSize | NoZOrder | NoActivate)) throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    internal static PixelPoint GetCursor()
    {
        if (!GetCursorPos(out var point)) throw new Win32Exception(Marshal.GetLastWin32Error());
        return new(point.X, point.Y);
    }
    private static PixelRect ToRect(Rect rect) => new(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
}
