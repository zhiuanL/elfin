namespace DesktopPet.Domain.Platform;

// Desktop coordinates are physical pixels, not WPF DIPs. Negative origins are valid.
public readonly record struct PixelPoint(double X, double Y);
public readonly record struct PixelRect(double X, double Y, double Width, double Height);
public readonly record struct DpiScale(double X, double Y);
public sealed record DisplayInfo(string Id, PixelRect Bounds, PixelRect WorkingArea, DpiScale Dpi, bool IsPrimary);
public sealed record DisplayTopology(IReadOnlyList<DisplayInfo> Displays,
    IReadOnlyList<DisplayAdjacency> Adjacencies);
public sealed record DisplayAdjacency(string FirstDisplayId, string SecondDisplayId);
public enum SessionState { Active, Locked, Sleeping, Resuming }
public enum ForegroundWindowState { Normal, Maximized, FullScreen }
