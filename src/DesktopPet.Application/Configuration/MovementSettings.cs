using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Configuration;

public sealed record MovementSettings
{
    public HomePosition? Home { get; init; }
    public bool UpdateHomeOnDrag { get; init; } = true;
    public IReadOnlyList<string> SelectedDisplays { get; init; } = [];
    public MotionStyle? UserMotionStyle { get; init; }
    public MotionOverrides Overrides { get; init; } = new();
    public bool IsValid => SelectedDisplays is { Count: <= 32 } && SelectedDisplays.All(id => !string.IsNullOrWhiteSpace(id) && id.Length <= 128) &&
        Overrides is not null && (UserMotionStyle is null || Enum.IsDefined(UserMotionStyle.Value)) &&
        (Overrides.Easing is null || Enum.IsDefined(Overrides.Easing.Value));
    public bool Equals(MovementSettings? other) => ReferenceEquals(this, other) || other is not null &&
        Equals(Home, other.Home) && UpdateHomeOnDrag == other.UpdateHomeOnDrag && UserMotionStyle == other.UserMotionStyle &&
        Equals(Overrides, other.Overrides) && (SelectedDisplays is null ? other.SelectedDisplays is null :
            other.SelectedDisplays is not null && SelectedDisplays.SequenceEqual(other.SelectedDisplays));
    public override int GetHashCode()
    {
        var hash = new HashCode(); hash.Add(Home); hash.Add(UpdateHomeOnDrag); hash.Add(UserMotionStyle); hash.Add(Overrides);
        if (SelectedDisplays is not null) foreach (var id in SelectedDisplays) hash.Add(id);
        return hash.ToHashCode();
    }
}
