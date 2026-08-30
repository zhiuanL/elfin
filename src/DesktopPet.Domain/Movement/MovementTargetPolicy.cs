using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Domain.Movement;

public sealed class MovementTargetPolicy(DisplayMovementPolicy displays, IRandomSource random)
{
    public MovementPlan? Choose(MovementContext context, MovementMode mode, HybridMovementStrategy hybrid,
        DisplayPolicy displayPolicy, IReadOnlyList<string> selected, MotionProfile motion)
    {
        if (mode == MovementMode.Fixed) return null;
        var allowed = displays.Allowed(context.Display.Topology, displayPolicy, selected, context.Display.CurrentDisplayId);
        var current = allowed.FirstOrDefault(d => d.Id == context.Display.CurrentDisplayId);
        if (current is null || random.NextUnit() < motion.PauseProbability) return null;
        var home = displays.RestoreHome(context.Anchor.Home, context.Origin, context.Display.WindowSize, context.Anchor.Visual, allowed);
        var roam = mode == MovementMode.Desktop || (mode == MovementMode.Hybrid && hybrid == HybridMovementStrategy.Roaming) ||
            (mode == MovementMode.Hybrid && hybrid != HybridMovementStrategy.Anchor &&
             context.SinceInteraction >= MotionPolicy.RoamingIdleThreshold && random.NextUnit() < MotionPolicy.RoamingProbability);
        var returnHome = mode == MovementMode.Hybrid && context.ReturnHome;
        var candidates = roam ? allowed.Where(d => displays.RouteArea(context.Display.Topology, current, d) is not null).ToArray() : [current];
        var targetDisplay = returnHome ? allowed.FirstOrDefault(d => d.Id == home.DisplayId) ?? current :
            candidates[Math.Min(candidates.Length - 1, (int)(RuntimeLimits.Clamp(random.NextUnit(), 0, .999999) * candidates.Length))];
        var area = displays.RouteArea(context.Display.Topology, current, targetDisplay);
        if (area is null) return null;
        var window = context.Display.WindowSize;
        var dpi = context.Display.WindowDpi;
        var targetSize = DpiMath.ToPixels(new(window.Width / dpi.X, window.Height / dpi.Y), targetDisplay.Dpi);
        var envelope = new PixelSize(Math.Max(window.Width, targetSize.Width), Math.Max(window.Height, targetSize.Height));
        if (!MovementGeometry.Contains(context.Origin, envelope, area.Value) || !MovementGeometry.Fits(envelope, targetDisplay.WorkingArea)) return null;
        var anchor = context.Anchor.Visual;
        PixelPoint target;
        if (returnHome) target = anchor.ToOrigin(home.Position, envelope);
        else if (roam) target = new(targetDisplay.WorkingArea.X + random.NextUnit() * (targetDisplay.WorkingArea.Width - envelope.Width),
            targetDisplay.WorkingArea.Y + random.NextUnit() * (targetDisplay.WorkingArea.Height - envelope.Height));
        else
        {
            var angle = random.NextUnit() * Math.Tau;
            var radius = Math.Sqrt(RuntimeLimits.Clamp(random.NextUnit(), 0, 1)) * motion.WanderRadius;
            target = anchor.ToOrigin(new(home.Position.X + Math.Cos(angle) * radius * dpi.X,
                home.Position.Y + Math.Sin(angle) * radius * dpi.Y), envelope);
        }
        target = MovementGeometry.Clamp(target, envelope, targetDisplay.WorkingArea);
        if (MovementGeometry.Distance(context.Origin, target) < 2) return null;
        var plan = new MovementPlan(context.Origin, target, area.Value, envelope, dpi, motion, targetDisplay.Id,
            target.X < context.Origin.X ? FacingDirection.Left : FacingDirection.Right);
        return new MotionTrajectory(plan).Duration <= MotionPolicy.MaxMovementDuration ? plan : null;
    }
}
