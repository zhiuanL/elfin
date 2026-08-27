namespace DesktopPet.Domain.Platform;

public readonly record struct PixelSize(double Width, double Height);
public readonly record struct DipSize(double Width, double Height);
public sealed record DisplayArea(string Id, PixelRect Bounds, PixelRect WorkingArea, bool IsPrimary);
public sealed record SavedWindowPosition(PixelPoint Origin, string? DisplayId);

public static class DpiMath
{
    public const double DipsPerInch = 96;
    public static DpiScale FromDpi(uint dpi)
    {
        ArgumentOutOfRangeException.ThrowIfZero(dpi);
        return new(dpi / DipsPerInch, dpi / DipsPerInch);
    }
    public static PixelSize ToPixels(DipSize size, DpiScale scale)
    {
        if (!double.IsFinite(size.Width) || !double.IsFinite(size.Height) || size.Width <= 0 || size.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(size));
        if (!double.IsFinite(scale.X) || !double.IsFinite(scale.Y) || scale.X <= 0 || scale.Y <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale));
        return new(size.Width * scale.X, size.Height * scale.Y);
    }
}
