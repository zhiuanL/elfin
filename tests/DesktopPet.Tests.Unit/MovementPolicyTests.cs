using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Tests.Unit;

public sealed class MovementPolicyTests
{
    private static readonly DisplayInfo Primary = new("primary", new(100, 100, 1920, 1080), new(100, 100, 1920, 1040), new(1, 1), true);
    private static readonly DisplayInfo Left = new("left", new(-1820, 100, 1920, 1080), new(-1820, 100, 1920, 1040), new(1.5, 1.5), false);
    private static readonly DisplayTopology Topology = new([Primary, Left], [new("primary", "left")]);
    private static MovementContext Context() => new(new(Topology, "primary", new(220, 220), new(1, 1)),
        new(800, 400), new(new(new(910, 620), "primary"), new()), TimeSpan.Zero, false);
    private static MotionProfile Motion => MotionPolicy.Preset(MotionStyle.Natural) with { PauseProbability = 0 };

    [Theory]
    [InlineData(MovementMode.Fixed)]
    [InlineData(MovementMode.Local)]
    [InlineData(MovementMode.Desktop)]
    [InlineData(MovementMode.Hybrid)]
    public void FourModesRespectTheirScopeAndDefaultDoesNotCrossMonitors(MovementMode mode)
    {
        var context = Context();
        var policy = new MovementTargetPolicy(new(), new SeededRandomSource(45));
        var planned = 0;
        for (var i = 0; i < 100; i++)
        {
            var plan = policy.Choose(context, mode, HybridMovementStrategy.SmartHybrid, DisplayPolicy.LockedCurrent, [], Motion);
            if (mode == MovementMode.Fixed) { Assert.Null(plan); continue; }
            if (plan is null) continue; // A target within two physical pixels is a deliberate no-op.
            planned++;
            Assert.Equal("primary", plan.TargetDisplayId);
            Assert.True(MovementGeometry.Contains(plan.Target, plan.EnvelopeSize, Primary.WorkingArea));
            if (mode is MovementMode.Local or MovementMode.Hybrid)
                Assert.InRange(MovementGeometry.Distance(plan.Target, context.Origin), 0, Motion.WanderRadius + .001);
        }
        if (mode != MovementMode.Fixed) Assert.InRange(planned, 90, 100);
    }
    [Theory]
    [InlineData(DisplayPolicy.PrimaryOnly, "primary", 1)]
    [InlineData(DisplayPolicy.LockedCurrent, "left", 1)]
    [InlineData(DisplayPolicy.SelectedMonitors, "left", 1)]
    [InlineData(DisplayPolicy.AllMonitors, "left", 2)]
    public void DisplaySelectionHonorsPrimaryCurrentSelectedAndAll(DisplayPolicy policy, string expected, int count)
    {
        var result = new DisplayMovementPolicy().Allowed(Topology, policy, ["left"], "left");
        Assert.Equal(count, result.Count);
        Assert.Contains(result, d => d.Id == expected);
        Assert.Empty(new DisplayMovementPolicy().Allowed(Topology, DisplayPolicy.SelectedMonitors, ["missing"], "left"));
    }
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void DpiAffectsSizeNotGlobalNegativeOrigin(double scale)
    {
        var size = DpiMath.ToPixels(new(220, 400), new(scale, scale));
        var area = new PixelRect(-2500, -1600, 2200, 1500);
        var origin = MovementGeometry.Clamp(new(-900, -900), size, area);
        Assert.True(MovementGeometry.Contains(origin, size, area));
        Assert.True(origin.X < 0 && origin.Y < 0);
        var visual = new VisualAnchor(.4, .95);
        Assert.Equal(origin, visual.ToOrigin(visual.FromOrigin(origin, size), size));
        Assert.Equal(220 * scale, size.Width);
    }
    [Fact]
    public void InvalidHomeAndRemovedMonitorRecoverToNearestSafeArea()
    {
        var policy = new DisplayMovementPolicy();
        var home = policy.RestoreHome(new(new(-99999, -99999), "removed"), new(600, 400), new(220, 220), new(), [Left, Primary]);
        Assert.Equal("left", home.DisplayId);
        Assert.True(MovementGeometry.Contains(new VisualAnchor().ToOrigin(home.Position, new(220, 220)), new(220, 220), Left.WorkingArea));
        var invalid = policy.RestoreHome(new(new(double.NaN, 0), "removed"), new(600, 400), new(220, 220), new(), [Primary]);
        Assert.Equal(new PixelPoint(710, 620), invalid.Position);
    }
    [Fact]
    public void CrossMonitorPathsRequireAdjacencyAndRectangularWorkAreaUnion()
    {
        var policy = new DisplayMovementPolicy();
        Assert.NotNull(policy.RouteArea(Topology, Primary, Left));
        Assert.Null(policy.RouteArea(Topology with { Adjacencies = [] }, Primary, Left));
        var gap = Left with { WorkingArea = Left.WorkingArea with { Width = 1800 } };
        Assert.Null(policy.RouteArea(Topology, Primary, gap));
        var offset = Left with { WorkingArea = Left.WorkingArea with { Y = 200 } };
        Assert.Null(policy.RouteArea(Topology, Primary, offset));
        var context = Context();
        var chooser = new MovementTargetPolicy(policy, new SeededRandomSource(32));
        var cross = Enumerable.Range(0, 200).Select(_ => chooser.Choose(context, MovementMode.Desktop,
            HybridMovementStrategy.SmartHybrid, DisplayPolicy.AllMonitors, [], Motion)).Where(p => p?.TargetDisplayId == "left").ToArray();
        Assert.NotEmpty(cross);
        Assert.All(cross, plan =>
        {
            Assert.Equal(new PixelSize(330, 330), plan!.EnvelopeSize);
            var trajectory = new MotionTrajectory(plan);
            for (var i = 0; i <= 20; i++)
                Assert.True(MovementGeometry.Contains(trajectory.At(trajectory.Duration * (i / 20.0)), plan.EnvelopeSize, plan.SafeArea));
        });
    }
    [Fact]
    public void HybridCanReturnHomeAndOnlyRoamsAfterIdleThreshold()
    {
        var chooser = new MovementTargetPolicy(new(), new ConstantRandom(.1));
        var homeContext = Context() with { Origin = new(1100, 600), ReturnHome = true };
        var home = chooser.Choose(homeContext, MovementMode.Hybrid, HybridMovementStrategy.SmartHybrid, DisplayPolicy.LockedCurrent, [], Motion);
        Assert.Equal(new PixelPoint(800, 400), home!.Target);
        var local = chooser.Choose(Context(), MovementMode.Hybrid, HybridMovementStrategy.SmartHybrid, DisplayPolicy.LockedCurrent, [], Motion)!;
        var roam = chooser.Choose(Context() with { SinceInteraction = TimeSpan.FromMinutes(4) }, MovementMode.Hybrid,
            HybridMovementStrategy.SmartHybrid, DisplayPolicy.LockedCurrent, [], Motion)!;
        Assert.InRange(MovementGeometry.Distance(local.Target, Context().Origin), 0, Motion.WanderRadius);
        Assert.True(MovementGeometry.Distance(roam.Target, Context().Origin) > Motion.WanderRadius);
    }
    [Fact]
    public void FullDesktopSamplesTargetsBeyondEveryLocalPresetRadius()
    {
        var context = Context();
        var chooser = new MovementTargetPolicy(new(), new SeededRandomSource(20260830));
        var distances = Enumerable.Range(0, 500)
            .Select(_ => chooser.Choose(context, MovementMode.Desktop, HybridMovementStrategy.SmartHybrid,
                DisplayPolicy.LockedCurrent, [], MotionPolicy.Preset(MotionStyle.Lively) with { PauseProbability = 0 }))
            .Where(plan => plan is not null)
            .Select(plan => MovementGeometry.Distance(context.Origin, plan!.Target))
            .ToArray();

        Assert.InRange(distances.Length, 490, 500);
        Assert.Contains(distances, distance => distance > MotionPolicy.Preset(MotionStyle.Lively).WanderRadius * 3);
        Assert.All(distances, distance => Assert.InRange(distance, 2, 2_000));
    }
    [Fact]
    public void SmartHybridNeverRoamsBeforeIdleAndRoamsStatisticallyAfterIdle()
    {
        static int CountExpandedTargets(TimeSpan idle, int seed)
        {
            var context = Context() with { SinceInteraction = idle };
            var chooser = new MovementTargetPolicy(new(), new SeededRandomSource(seed));
            return Enumerable.Range(0, 2_000)
                .Select(_ => chooser.Choose(context, MovementMode.Hybrid, HybridMovementStrategy.SmartHybrid,
                    DisplayPolicy.LockedCurrent, [], Motion))
                .Count(plan => plan is not null && MovementGeometry.Distance(context.Origin, plan.Target) > Motion.WanderRadius + .001);
        }

        Assert.Equal(0, CountExpandedTargets(TimeSpan.FromSeconds(119), 41));
        Assert.InRange(CountExpandedTargets(TimeSpan.FromMinutes(4), 42), 250, 450);
    }
    [Fact]
    public void PresetMotionParametersAndEquivalentTrajectoriesAreOrdered()
    {
        var quiet = MotionPolicy.Preset(MotionStyle.Quiet);
        var natural = MotionPolicy.Preset(MotionStyle.Natural);
        var lively = MotionPolicy.Preset(MotionStyle.Lively);
        Assert.True(quiet.Speed < natural.Speed && natural.Speed < lively.Speed);
        Assert.True(quiet.MovementInterval > natural.MovementInterval && natural.MovementInterval > lively.MovementInterval);
        Assert.True(quiet.WanderRadius < natural.WanderRadius && natural.WanderRadius < lively.WanderRadius);
        Assert.True(quiet.PauseProbability > natural.PauseProbability && natural.PauseProbability > lively.PauseProbability);

        static TimeSpan Duration(MotionProfile motion) => new MotionTrajectory(new(
            new(100, 100), new(500, 100), new(0, 0, 1_000, 500), new(220, 220), new(1, 1), motion,
            "primary", FacingDirection.Right)).Duration;

        Assert.True(Duration(quiet) > Duration(natural));
        Assert.True(Duration(natural) > Duration(lively));
    }
    [Theory]
    [InlineData(MotionStyle.Quiet)]
    [InlineData(MotionStyle.Natural)]
    [InlineData(MotionStyle.Lively)]
    public void PresetsAndOverridesStayInsideSystemCaps(MotionStyle style)
    {
        var policy = new MotionPolicy();
        var preset = policy.Resolve(style, null, new() { Speed = 99999 });
        Assert.Equal(MotionPolicy.Preset(style), preset);
        var character = policy.Resolve(null, null, new() { Speed = 120 });
        Assert.Equal(120, character.Speed);
        var user = policy.Resolve(null, new() { Speed = 99999, Acceleration = -10, MovementIntervalSeconds = 0 }, new() { Speed = 20 });
        Assert.Equal(MotionPolicy.MaxSpeed, user.Speed);
        Assert.Equal(20, user.Acceleration);
        Assert.Equal(MotionPolicy.MinMovementInterval, user.MovementInterval);
    }
    [Theory]
    [InlineData(FacingDirection.Left, true, "walk-right", true)]
    [InlineData(FacingDirection.Left, false, "idle", false)]
    [InlineData(FacingDirection.Right, true, "walk-right", false)]
    public void FacingUsesDirectionalCapabilitiesThenMirrorThenFallback(FacingDirection direction, bool mirror, string semantic, bool flipped)
    {
        var result = MovementAnimationResolver.Resolve(new HashSet<AnimationSemantic>([new("idle"), new("walk-right")]), mirror, direction);
        Assert.Equal(semantic, result.Semantic.Value); Assert.Equal(flipped, result.Mirrored);
        var native = MovementAnimationResolver.Resolve(new HashSet<AnimationSemantic>([new("walk-left"), new("walk-right")]), true, FacingDirection.Left);
        Assert.False(native.Mirrored); Assert.Equal("walk-left", native.Semantic.Value);
        Assert.Equal("walk", MovementAnimationResolver.Resolve(new HashSet<AnimationSemantic>([new("walk")]), false, direction).Semantic.Value);
    }
    [Fact]
    public void OldSchemaOneRemainsValidAndOptionalMovementFieldsAreValidated()
    {
        Assert.True(CharacterValidatorTests.Validate(CharacterValidatorTests.Basic).CanInstall);
        var result = CharacterValidatorTests.Validate(CharacterValidatorTests.Basic with
            { VisualAnchor = new(.5, .95), SupportsMirroring = true, Movement = new() { Speed = 99999 } });
        Assert.True(result.CanInstall);
        Assert.False(CharacterValidatorTests.Validate(CharacterValidatorTests.Basic with { VisualAnchor = new(-1, 1) }).CanInstall);
    }
    private sealed class ConstantRandom(double value) : IRandomSource { public double NextUnit() => value; }
}
