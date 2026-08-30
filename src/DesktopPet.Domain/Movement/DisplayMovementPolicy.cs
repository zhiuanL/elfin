using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Domain.Movement;

public sealed class DisplayMovementPolicy
{
    public IReadOnlyList<DisplayInfo> Allowed(DisplayTopology topology, DisplayPolicy policy, IReadOnlyList<string> selected, string current)
    {
        var valid = topology.Displays.Where(d => MovementGeometry.Valid(d.WorkingArea)).ToArray();
        if (valid.Length == 0) throw new InvalidOperationException("No usable display.");
        var primary = valid.FirstOrDefault(d => d.IsPrimary) ?? valid[0];
        var here = valid.FirstOrDefault(d => d.Id == current) ?? primary;
        return policy switch
        {
            DisplayPolicy.PrimaryOnly => [primary],
            DisplayPolicy.AllMonitors => valid,
            DisplayPolicy.SelectedMonitors => valid.Where(d => selected.Contains(d.Id, StringComparer.Ordinal)).ToArray(),
            _ => [here]
        };
    }
    public HomePosition RestoreHome(HomePosition? saved, PixelPoint current, PixelSize size, VisualAnchor anchor, IReadOnlyList<DisplayInfo> allowed)
    {
        if (allowed.Count == 0) throw new InvalidOperationException("No permitted display is connected.");
        var point = saved is not null && MovementGeometry.Valid(saved.Position) ? saved.Position : anchor.FromOrigin(current, size);
        var display = allowed.FirstOrDefault(d => d.Id == saved?.DisplayId) ?? MovementGeometry.Nearest(point, allowed);
        var origin = MovementGeometry.Clamp(anchor.ToOrigin(point, size), size, display.WorkingArea);
        return new(anchor.FromOrigin(origin, size), display.Id);
    }
    public PixelRect? RouteArea(DisplayTopology topology, DisplayInfo start, DisplayInfo target)
    {
        if (start.Id == target.Id) return start.WorkingArea;
        if (!topology.Adjacencies.Any(a => (a.FirstDisplayId == start.Id && a.SecondDisplayId == target.Id) ||
            (a.FirstDisplayId == target.Id && a.SecondDisplayId == start.Id))) return null;
        return MovementGeometry.ContinuousUnion(start.WorkingArea, target.WorkingArea);
    }
}
