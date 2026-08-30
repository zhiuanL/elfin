using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Domain.Movement;

public enum FacingDirection { Left, Right }
public enum MotionEasing { SmoothStep, SmootherStep }
public enum MouseInteractionMode { Interactive, ClickThrough, TemporaryPassThrough }
public sealed record VisualAnchor(double X = .5, double Y = 1)
{
    public bool IsValid => double.IsFinite(X) && double.IsFinite(Y) && X is >= 0 and <= 1 && Y is >= 0 and <= 1;
    public PixelPoint FromOrigin(PixelPoint origin, PixelSize size) => new(origin.X + X * size.Width, origin.Y + Y * size.Height);
    public PixelPoint ToOrigin(PixelPoint point, PixelSize size) => new(point.X - X * size.Width, point.Y - Y * size.Height);
}
public sealed record HomePosition(PixelPoint Position, string DisplayId);
public sealed record MovementAnchor(HomePosition Home, VisualAnchor Visual);
public sealed record MotionOverrides
{
    public double? Speed { get; init; }
    public double? Acceleration { get; init; }
    public double? Deceleration { get; init; }
    public double? PauseProbability { get; init; }
    public double? MovementIntervalSeconds { get; init; }
    public double? WanderRadius { get; init; }
    public MotionEasing? Easing { get; init; }
}
public sealed record MotionProfile(double Speed, double Acceleration, double Deceleration, double PauseProbability,
    TimeSpan MovementInterval, double WanderRadius, MotionEasing Easing);
public sealed record MovementPlan(PixelPoint Start, PixelPoint Target, PixelRect SafeArea, PixelSize EnvelopeSize,
    DpiScale StartDpi, MotionProfile Motion, string TargetDisplayId, FacingDirection Facing);
public sealed record MovementAnimationSemantic(AnimationSemantic Semantic, FacingDirection Facing, bool Mirrored);
public sealed record DisplayContext(DisplayTopology Topology, string CurrentDisplayId, PixelSize WindowSize, DpiScale WindowDpi);
public sealed record MovementContext(DisplayContext Display, PixelPoint Origin, MovementAnchor Anchor,
    TimeSpan SinceInteraction, bool ReturnHome);
public sealed record MovementDiagnostic(bool IsMoving, HomePosition? Home, PixelPoint? Target, FacingDirection Facing,
    MovementMode Mode, DisplayPolicy Displays);

public static class MovementAnimationResolver
{
    public static MovementAnimationSemantic Resolve(IReadOnlySet<AnimationSemantic> capabilities, bool supportsMirroring, FacingDirection facing)
    {
        var left = new AnimationSemantic("walk-left");
        var right = new AnimationSemantic("walk-right");
        var walk = new AnimationSemantic("walk");
        var requested = facing == FacingDirection.Left ? left : right;
        var opposite = facing == FacingDirection.Left ? right : left;
        if (capabilities.Contains(requested)) return new(requested, facing, false);
        if (supportsMirroring && capabilities.Contains(opposite)) return new(opposite, facing, true);
        if (capabilities.Contains(walk)) return new(walk, facing, supportsMirroring && facing == FacingDirection.Left);
        return new(AnimationSemantic.Idle, facing, false);
    }
}
