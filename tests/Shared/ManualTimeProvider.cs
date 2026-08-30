namespace DesktopPet.Tests.Shared;

// Deterministic virtual timer queue. No real sleeps or UI mocking.
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private readonly List<ManualTimer> _timers = [];
    private long _ticks;
    private TaskCompletionSource _scheduled = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitForTimerAsync()
    {
        lock (_gate)
        {
            if (_timers.Any(t => !t.Disposed && t.Due < long.MaxValue)) return Task.CompletedTask;
            if (_scheduled.Task.IsCompleted) _scheduled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            return _scheduled.Task;
        }
    }
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    public override long GetTimestamp() { lock (_gate) return _ticks; }
    public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 8, 28, 12, 0, 0, TimeSpan.Zero) + TimeSpan.FromTicks(GetTimestamp());
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var timer = new ManualTimer(this, callback, state);
        timer.Change(dueTime, period);
        return timer;
    }
    public void Jump(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount.Ticks);
        ManualTimer[] due;
        lock (_gate)
        {
            _ticks += amount.Ticks;
            due = _timers.Where(t => !t.Disposed && t.Due <= _ticks).ToArray();
            foreach (var timer in due) timer.Due = long.MaxValue;
        }
        foreach (var timer in due) timer.Callback(timer.State);
    }
    public void Advance(TimeSpan amount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(amount.Ticks);
        long target;
        lock (_gate) target = _ticks + amount.Ticks;
        while (true)
        {
            ManualTimer? next;
            lock (_gate)
            {
                next = _timers.Where(timer => !timer.Disposed && timer.Due <= target).OrderBy(timer => timer.Due).FirstOrDefault();
                if (next is null) { _ticks = target; return; }
                _ticks = next.Due;
                next.Due = next.Period > 0 ? next.Due + next.Period : long.MaxValue;
            }
            next.Callback(next.State);
        }
    }
    private sealed class ManualTimer(ManualTimeProvider clock, TimerCallback callback, object? state) : ITimer
    {
        public TimerCallback Callback { get; } = callback;
        public object? State { get; } = state;
        public long Due { get; set; }
        public long Period { get; private set; }
        public bool Disposed { get; private set; }
        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            lock (clock._gate)
            {
                if (Disposed) return false;
                Due = dueTime == Timeout.InfiniteTimeSpan ? long.MaxValue : clock._ticks + dueTime.Ticks;
                Period = period.Ticks;
                if (!clock._timers.Contains(this)) clock._timers.Add(this);
                if (Due < long.MaxValue) clock._scheduled.TrySetResult();
                return true;
            }
        }
        public void Dispose() { lock (clock._gate) { Disposed = true; clock._timers.Remove(this); } }
        public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
    }
}
