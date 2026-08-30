using DesktopPet.Application.Movement;
using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;
using DesktopPet.Tests.Shared;

namespace DesktopPet.Tests.Unit;

public sealed class MovementEngineTests
{
    internal static MovementPlan Plan => new(new(-600, -300), new(-400, -200), new(-1000, -600, 1800, 1400),
        new(220, 220), new(1.5, 1.5), MotionPolicy.Preset(MotionStyle.Natural), "screen", FacingDirection.Right);
    [Theory]
    [InlineData(MotionEasing.SmoothStep)]
    [InlineData(MotionEasing.SmootherStep)]
    public void TrajectoryHasExactEndpointsZeroEndpointVelocityAndBoundedDerivatives(MotionEasing easing)
    {
        var plan = Plan with { Motion = Plan.Motion with { Easing = easing } };
        var path = new MotionTrajectory(plan);
        Assert.Equal(plan.Start, path.At(TimeSpan.Zero)); Assert.Equal(plan.Target, path.At(path.Duration));
        var dt = path.Duration.TotalSeconds / 1000;
        var previousSpeed = 0.0;
        for (var i = 1; i <= 1000; i++)
        {
            var speed = MovementGeometry.Distance(path.At(TimeSpan.FromSeconds(dt * i)), path.At(TimeSpan.FromSeconds(dt * (i - 1)))) / dt / 1.5;
            Assert.InRange(speed, 0, plan.Motion.Speed + .1);
            Assert.InRange(Math.Abs(speed - previousSpeed) / dt, 0, Math.Min(plan.Motion.Acceleration, plan.Motion.Deceleration) + 1);
            previousSpeed = speed;
        }
        Assert.InRange(previousSpeed, 0, 1);
    }
    [Fact]
    public async Task EngineMovesOnlyWhileActiveAndCompletesAtTargetWithVirtualTime()
    {
        var clock = new ManualTimeProvider();
        var surface = new Surface(Plan.Start);
        await using var engine = new MovementEngine(surface, clock);
        var task = engine.MoveAsync(Plan, default);
        await AdvanceToEnd(clock, task);
        await task;
        Assert.Equal(Plan.Target, surface.Origin);
        Assert.False(engine.IsMoving);
        var count = surface.Moves;
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(count, surface.Moves);
        await engine.StopAsync(); await engine.StopAsync();
    }
    [Theory]
    [InlineData("cancel")]
    [InlineData("drag")]
    [InlineData("hide")]
    [InlineData("resize")]
    [InlineData("pause")]
    public async Task MovementStopsOnCancellationOwnershipVisibilitySizeOrLongFrameGap(string reason)
    {
        var clock = new ManualTimeProvider();
        var surface = new Surface(Plan.Start);
        await using var engine = new MovementEngine(surface, clock);
        using var cancellation = new CancellationTokenSource();
        var task = engine.MoveAsync(Plan, cancellation.Token);
        if (reason == "cancel") cancellation.Cancel();
        if (reason == "drag") surface.UserOwned = true;
        if (reason == "hide") surface.Visible = false;
        if (reason == "resize") surface.Size = new(500, 500);
        if (reason == "pause") clock.Jump(TimeSpan.FromMinutes(5));
        else clock.Advance(MotionPolicy.FrameInterval);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(3)));
        var count = surface.Moves;
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(count, surface.Moves);
    }
    [Fact]
    public async Task InvalidTargetAndConcurrentMovementAreRejected()
    {
        var clock = new ManualTimeProvider();
        var surface = new Surface(Plan.Start);
        await using var engine = new MovementEngine(surface, clock);
        await Assert.ThrowsAsync<ArgumentException>(() => engine.MoveAsync(Plan with { Target = new(99999, 0) }, default));
        var task = engine.MoveAsync(Plan, default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.MoveAsync(Plan, default));
        await engine.StopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
    private static async Task AdvanceToEnd(ManualTimeProvider clock, Task task)
    {
        for (var i = 0; !task.IsCompleted && i < 1000; i++)
        {
            await Task.WhenAny(task, clock.WaitForTimerAsync()).WaitAsync(TimeSpan.FromSeconds(3));
            if (task.IsCompleted) break;
            clock.Advance(MotionPolicy.FrameInterval);
        }
        await task.WaitAsync(TimeSpan.FromSeconds(3));
    }
    internal sealed class Surface(PixelPoint origin) : IMovementSurface
    {
        public PixelPoint Origin { get; private set; } = origin;
        public PixelSize Size { get; set; } = new(220, 220);
        public bool UserOwned { get; set; }
        public bool Visible { get; set; } = true;
        public int Moves { get; private set; }
        public Task<MovementSurfaceSnapshot> ReadAsync(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.FromResult(new MovementSurfaceSnapshot(new(Origin.X, Origin.Y, Size.Width, Size.Height), new(1.5, 1.5), Visible, UserOwned)); }
        public Task<bool> TryMoveAsync(PixelPoint point, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested(); if (UserOwned || !Visible) return Task.FromResult(false);
            Origin = point; Moves++; return Task.FromResult(true);
        }
        public Task RecoverAsync(PixelPoint point, CancellationToken ct) { ct.ThrowIfCancellationRequested(); Origin = point; return Task.CompletedTask; }
    }
}
