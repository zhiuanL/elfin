using DesktopPet.Application.Windows;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsDisplayService : IDisplayService
{
    public IReadOnlyList<DisplayArea> GetDisplays() => NativeDesktop.GetDisplays();
}
