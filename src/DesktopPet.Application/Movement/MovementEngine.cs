using DesktopPet.Domain.Movement;
using DesktopPet.Application.Contracts;

namespace DesktopPet.Application.Movement;

public sealed class MovementEngine(IMovementSurface surface, TimeProvider clock) : IMovementController, IAsyncDisposable, IDisposable
{
    private CancellationTokenSource? _active;
    private Task _completion = Task.CompletedTask;
    private bool _disposed;
    public bool IsMoving => !_completion.IsCompleted;
    public Task MoveAsync(MovementPlan plan, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsMoving) throw new InvalidOperationException("Movement is already running.");
        var trajectory = new MotionTrajectory(plan);
        if (trajectory.Duration > MotionPolicy.MaxMovementDuration) throw new ArgumentException("Movement duration exceeds safety limit.");
        _active?.Dispose();
        _active = CancellationTokenSource.CreateLinkedTokenSource(ct);
        return _completion = RunAsync(plan, trajectory, _active.Token);
    }
    private async Task RunAsync(MovementPlan plan, MotionTrajectory trajectory, CancellationToken ct)
    {
        var initial = await surface.ReadAsync(ct);
        if (!initial.IsVisible || initial.IsUserOwned ||
            MovementGeometry.Distance(new(initial.Bounds.X, initial.Bounds.Y), plan.Start) > 2)
            throw new OperationCanceledException("Window ownership or origin changed.", ct);
        var elapsed = TimeSpan.Zero;
        var previous = clock.GetTimestamp();
        while (elapsed < trajectory.Duration)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(MotionPolicy.FrameInterval, clock, ct);
            var current = await surface.ReadAsync(ct);
            if (!current.IsVisible || current.IsUserOwned ||
                current.Bounds.Width > plan.EnvelopeSize.Width + 1 || current.Bounds.Height > plan.EnvelopeSize.Height + 1)
                throw new OperationCanceledException("Window is no longer safe to move.", ct);
            var now = clock.GetTimestamp();
            var delta = clock.GetElapsedTime(previous, now);
            if (delta > MotionPolicy.MaxFrameGap) throw new OperationCanceledException("Movement paused too long; replan instead of jumping.", ct);
            previous = now;
            elapsed += delta;
            var point = trajectory.At(elapsed);
            if (!MovementGeometry.Contains(point, new(current.Bounds.Width, current.Bounds.Height), plan.SafeArea) ||
                !await surface.TryMoveAsync(point, ct)) throw new OperationCanceledException("Movement ownership was revoked.", ct);
        }
    }
    public void Stop() { if (!_disposed) _active?.Cancel(); }
    public async Task StopAsync()
    {
        Stop();
        try { await _completion; }
        catch (OperationCanceledException) { /* Cancellation is the expected ownership handoff. */ }
    }
    public async ValueTask DisposeAsync() { if (_disposed) return; await StopAsync(); Dispose(); }
    public void Dispose() { if (_disposed) return; Stop(); _disposed = true; _active?.Dispose(); }
}
