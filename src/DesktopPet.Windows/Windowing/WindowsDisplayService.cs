using DesktopPet.Application.Windows;
using DesktopPet.Domain.Platform;
using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Movement;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsDisplayService : IDisplayService, IDisplayTopologyService
{
    public IReadOnlyList<DisplayArea> GetDisplays() => NativeDesktop.GetDisplays();
    public event EventHandler? TopologyChanged;
    public DisplayTopology GetTopology()
    {
        var displays = GetDisplays().Select(d => new DisplayInfo(d.Id, d.Bounds, d.WorkingArea, MonitorDpiProbe.Read(d), d.IsPrimary)).ToArray();
        var edges = new List<DisplayAdjacency>();
        for (var first = 0; first < displays.Length; first++)
            for (var second = first + 1; second < displays.Length; second++)
                if (MovementGeometry.Adjacent(displays[first].Bounds, displays[second].Bounds))
                    edges.Add(new(displays[first].Id, displays[second].Id));
        return new(Array.AsReadOnly(displays), edges.AsReadOnly());
    }
    public void NotifyChanged() => TopologyChanged?.Invoke(this, EventArgs.Empty);
}
