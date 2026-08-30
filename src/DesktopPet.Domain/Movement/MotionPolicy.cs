using DesktopPet.Domain.Pets;

namespace DesktopPet.Domain.Movement;

public sealed class MotionPolicy
{
    // Speeds/accelerations/radius are DIP-based; convert once using the starting monitor's DPI.
    public const double MaxSpeed = 300, MaxAcceleration = 600;
    public static readonly TimeSpan MinMovementInterval = TimeSpan.FromSeconds(8);
    public static readonly TimeSpan MaxMovementDuration = TimeSpan.FromSeconds(45);
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(1000.0 / 30);
    public static readonly TimeSpan MaxFrameGap = TimeSpan.FromMilliseconds(250);
    public static readonly TimeSpan RoamingIdleThreshold = TimeSpan.FromMinutes(2);
    public static readonly TimeSpan TemporaryPassThroughDuration = TimeSpan.FromSeconds(8);
    public const double RoamingProbability = .2;

    public MotionProfile Resolve(MotionStyle? userStyle, MotionOverrides? user, MotionOverrides? character)
    {
        var defaults = Preset(userStyle ?? MotionStyle.Natural);
        // An explicit user preset overrides character recommendations as a whole.
        var recommendation = userStyle is null ? character : null;
        double Pick(double? u, double? c, double d, double min, double max) => RuntimeLimits.Clamp(u ?? c ?? d, min, max);
        return new(Pick(user?.Speed, recommendation?.Speed, defaults.Speed, 10, MaxSpeed),
            Pick(user?.Acceleration, recommendation?.Acceleration, defaults.Acceleration, 20, MaxAcceleration),
            Pick(user?.Deceleration, recommendation?.Deceleration, defaults.Deceleration, 20, MaxAcceleration),
            Pick(user?.PauseProbability, recommendation?.PauseProbability, defaults.PauseProbability, 0, .95),
            TimeSpan.FromSeconds(Pick(user?.MovementIntervalSeconds, recommendation?.MovementIntervalSeconds, defaults.MovementInterval.TotalSeconds, MinMovementInterval.TotalSeconds, 600)),
            Pick(user?.WanderRadius, recommendation?.WanderRadius, defaults.WanderRadius, 20, 500),
            user?.Easing ?? recommendation?.Easing ?? defaults.Easing);
    }
    public static MotionProfile Preset(MotionStyle style) => style switch
    {
        MotionStyle.Quiet => new(40, 80, 100, .45, TimeSpan.FromSeconds(45), 80, MotionEasing.SmootherStep),
        MotionStyle.Lively => new(140, 300, 350, .1, TimeSpan.FromSeconds(15), 180, MotionEasing.SmoothStep),
        _ => new(80, 180, 180, .25, TimeSpan.FromSeconds(25), 120, MotionEasing.SmootherStep)
    };
}
