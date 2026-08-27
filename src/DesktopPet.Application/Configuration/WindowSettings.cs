using DesktopPet.Domain.Platform;

namespace DesktopPet.Application.Configuration;

public enum ControlCenterCloseBehavior { HideToTray, Exit }
public sealed record PetWindowSettings
{
    public SavedWindowPosition? Position { get; init; }
    public bool IsVisible { get; init; } = true;
    public bool Topmost { get; init; } = true;
}
