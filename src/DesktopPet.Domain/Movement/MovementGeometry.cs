using DesktopPet.Domain.Platform;

namespace DesktopPet.Domain.Movement;

public static class MovementGeometry
{
    public static bool Valid(PixelPoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y) &&
        p.X is > int.MinValue and < int.MaxValue && p.Y is > int.MinValue and < int.MaxValue;
    public static bool Valid(PixelRect r) => Valid(new PixelPoint(r.X, r.Y)) && double.IsFinite(r.Width) &&
        double.IsFinite(r.Height) && r.Width > 0 && r.Height > 0 && Valid(new PixelPoint(r.X + r.Width, r.Y + r.Height));
    public static bool Valid(PixelSize s) => double.IsFinite(s.Width) && double.IsFinite(s.Height) && s.Width > 0 && s.Height > 0;
    public static bool Fits(PixelSize size, PixelRect area) => Valid(size) && Valid(area) && size.Width <= area.Width && size.Height <= area.Height;
    public static bool Contains(PixelPoint origin, PixelSize size, PixelRect area) =>
        Valid(origin) && Fits(size, area) && origin.X >= area.X && origin.Y >= area.Y &&
        origin.X + size.Width <= area.X + area.Width && origin.Y + size.Height <= area.Y + area.Height;
    public static PixelPoint Clamp(PixelPoint origin, PixelSize size, PixelRect area) => new(
        Math.Clamp(origin.X, area.X, Math.Max(area.X, area.X + area.Width - size.Width)),
        Math.Clamp(origin.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height)));
    public static double Distance(PixelPoint a, PixelPoint b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));
    public static DisplayInfo Nearest(PixelPoint point, IReadOnlyList<DisplayInfo> displays) =>
        displays.MinBy(d => Distance(point, new(Math.Clamp(point.X, d.WorkingArea.X, d.WorkingArea.X + d.WorkingArea.Width),
            Math.Clamp(point.Y, d.WorkingArea.Y, d.WorkingArea.Y + d.WorkingArea.Height)))) ??
        throw new InvalidOperationException("No available display.");
    public static bool Adjacent(PixelRect a, PixelRect b) =>
        ((a.X + a.Width == b.X || b.X + b.Width == a.X) && Math.Min(a.Y + a.Height, b.Y + b.Height) > Math.Max(a.Y, b.Y)) ||
        ((a.Y + a.Height == b.Y || b.Y + b.Height == a.Y) && Math.Min(a.X + a.Width, b.X + b.Width) > Math.Max(a.X, b.X));
    // A deliberately conservative one-edge route: only unions that are solid rectangles.
    public static PixelRect? ContinuousUnion(PixelRect a, PixelRect b)
    {
        if (!Adjacent(a, b)) return null;
        if ((a.Y == b.Y && a.Height == b.Height) || (a.X == b.X && a.Width == b.Width))
            return new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
                Math.Max(a.X + a.Width, b.X + b.Width) - Math.Min(a.X, b.X),
                Math.Max(a.Y + a.Height, b.Y + b.Height) - Math.Min(a.Y, b.Y));
        return null;
    }
}
