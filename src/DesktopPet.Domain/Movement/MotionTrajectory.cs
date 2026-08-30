using DesktopPet.Domain.Platform;

namespace DesktopPet.Domain.Movement;

// Quintic/cubic easing on a straight safe segment; both endpoints have zero velocity.
// Duration is derived from the peak derivatives, so speed and acceleration caps hold analytically.
public sealed class MotionTrajectory
{
    private readonly MovementPlan _plan;
    public TimeSpan Duration { get; }
    public MotionTrajectory(MovementPlan plan)
    {
        if (!double.IsFinite(plan.StartDpi.X) || !double.IsFinite(plan.StartDpi.Y) || plan.StartDpi.X <= 0 || plan.StartDpi.Y <= 0)
            throw new ArgumentException("Invalid starting DPI.");
        if (!MovementGeometry.Contains(plan.Start, plan.EnvelopeSize, plan.SafeArea) ||
            !MovementGeometry.Contains(plan.Target, plan.EnvelopeSize, plan.SafeArea)) throw new ArgumentException("Unsafe movement plan.");
        _plan = plan;
        var dipDistance = Math.Sqrt(Math.Pow((plan.Target.X - plan.Start.X) / plan.StartDpi.X, 2) +
            Math.Pow((plan.Target.Y - plan.Start.Y) / plan.StartDpi.Y, 2));
        var motion = new MotionPolicy().Resolve(null, new() { Speed = plan.Motion.Speed, Acceleration = plan.Motion.Acceleration,
            Deceleration = plan.Motion.Deceleration, Easing = plan.Motion.Easing }, null);
        var quintic = motion.Easing == MotionEasing.SmootherStep;
        var speedTime = (quintic ? 1.875 : 1.5) * dipDistance / motion.Speed;
        var accelerationTime = Math.Sqrt((quintic ? 5.774 : 6) * dipDistance / Math.Min(motion.Acceleration, motion.Deceleration));
        Duration = TimeSpan.FromSeconds(Math.Max(.05, Math.Max(speedTime, accelerationTime)));
    }
    public PixelPoint At(TimeSpan elapsed)
    {
        var t = Math.Clamp(elapsed.TotalSeconds / Duration.TotalSeconds, 0, 1);
        var progress = _plan.Motion.Easing == MotionEasing.SmootherStep ? t * t * t * (10 + t * (-15 + 6 * t)) : t * t * (3 - 2 * t);
        return new(_plan.Start.X + (_plan.Target.X - _plan.Start.X) * progress,
            _plan.Start.Y + (_plan.Target.Y - _plan.Start.Y) * progress);
    }
}
