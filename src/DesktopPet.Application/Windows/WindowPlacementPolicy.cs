using DesktopPet.Domain.Platform;

namespace DesktopPet.Application.Windows;

public sealed record ResolvedWindowPosition(PixelPoint Origin, string DisplayId);

public sealed class WindowPlacementPolicy
{
    public const double DefaultMarginPixels = 24;

    public ResolvedWindowPosition Resolve(SavedWindowPosition? saved, PixelSize size, IReadOnlyList<DisplayArea> displays)
    {
        if (!ValidSize(size)) throw new ArgumentOutOfRangeException(nameof(size));
        var available = displays.Where(display => ValidRect(display.WorkingArea)).ToArray();
        if (available.Length == 0) throw new InvalidOperationException("No usable display working area.");
        var primary = available.FirstOrDefault(display => display.IsPrimary) ?? available[0];
        if (saved is null || !ValidPoint(saved.Origin))
        {
            var area = primary.WorkingArea;
            return new(Clamp(new(area.X + area.Width - size.Width - DefaultMarginPixels,
                area.Y + area.Height - size.Height - DefaultMarginPixels), size, area), primary.Id);
        }

        var origin = saved.Origin;
        var target = available.FirstOrDefault(display => Contains(display.WorkingArea, origin)) ??
            available.FirstOrDefault(display => display.Id == saved.DisplayId) ??
            available.MinBy(display => DistanceSquared(origin, display.WorkingArea))!;
        return new(Clamp(origin, size, target.WorkingArea), target.Id);
    }

    private static bool ValidPoint(PixelPoint value) => double.IsFinite(value.X) && double.IsFinite(value.Y) &&
        value.X is >= int.MinValue and <= int.MaxValue && value.Y is >= int.MinValue and <= int.MaxValue;
    private static bool ValidSize(PixelSize value) => double.IsFinite(value.Width) && double.IsFinite(value.Height) &&
        value.Width > 0 && value.Height > 0;
    private static bool ValidRect(PixelRect value) => ValidPoint(new(value.X, value.Y)) &&
        ValidSize(new(value.Width, value.Height)) && double.IsFinite(value.X + value.Width) && double.IsFinite(value.Y + value.Height);
    private static bool Contains(PixelRect area, PixelPoint p) =>
        p.X >= area.X && p.X < area.X + area.Width && p.Y >= area.Y && p.Y < area.Y + area.Height;
    private static PixelPoint Clamp(PixelPoint p, PixelSize size, PixelRect area) =>
        new(Math.Clamp(p.X, area.X, Math.Max(area.X, area.X + area.Width - size.Width)),
            Math.Clamp(p.Y, area.Y, Math.Max(area.Y, area.Y + area.Height - size.Height)));
    private static double DistanceSquared(PixelPoint p, PixelRect area)
    {
        var dx = p.X - Math.Clamp(p.X, area.X, area.X + area.Width);
        var dy = p.Y - Math.Clamp(p.Y, area.Y, area.Y + area.Height);
        return dx * dx + dy * dy;
    }
}
