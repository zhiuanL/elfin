using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Productivity;

public sealed class PomodoroService(IPomodoroRepository repository, ISettingsService settings,
    IProductivityEventPublisher events, TimeProvider clock) : IPomodoroService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _timer;
    private Task _timerTask = Task.CompletedTask;
    private PomodoroSession? _current;
    private int _consecutiveFocus;
    private bool _initialized;
    private bool _disposed;
    public event EventHandler? Changed;

    public async Task InitializeAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            _current = await repository.GetActiveAsync(ct);
            _consecutiveFocus = await repository.CountRecentCompletedFocusAsync(ct);
            _initialized = true;
            if (_current is { Status: PomodoroStatus.Running } running && running.TargetAtUtc <= clock.GetUtcNow())
                await CompleteLockedAsync(running, ct);
            ScheduleLocked();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task<PomodoroSession?> GetCurrentAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        return _current;
    }

    public async Task<PomodoroSnapshot> GetSnapshotAsync(CancellationToken ct)
    {
        await RefreshAsync(ct);
        var now = clock.GetUtcNow();
        return new(_current, SuggestNext(_current), _consecutiveFocus, now);
    }

    public async Task StartAsync(PomodoroPhase phase, TimeSpan duration, Guid? taskId, CancellationToken ct)
    {
        if (!Enum.IsDefined(phase) || duration < TimeSpan.FromSeconds(1) || duration > TimeSpan.FromHours(4))
            throw new ArgumentOutOfRangeException(nameof(duration));
        await EnsureInitializedAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            if (_current?.IsActive == true) return;
            var now = clock.GetUtcNow();
            var sequence = phase == PomodoroPhase.Focus ? _consecutiveFocus + 1 : _consecutiveFocus;
            _current = new(Guid.NewGuid(), taskId, phase, now, now + duration, null,
                PomodoroStatus.Running, duration, TimeSpan.Zero, TimeSpan.Zero, sequence);
            await repository.SaveAsync(_current, ct);
            events.Publish(new(phase == PomodoroPhase.Focus ? ProductivityEventKind.PomodoroStarted :
                ProductivityEventKind.BreakStarted, now, phase, _current.Id));
            ScheduleLocked();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task PauseAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            if (_current is not { Status: PomodoroStatus.Running } current) return;
            var now = clock.GetUtcNow();
            if (current.TargetAtUtc <= now) { await CompleteLockedAsync(current, ct); return; }
            var remaining = current.TargetAtUtc - now;
            _current = current with { Status = PomodoroStatus.Paused, PausedRemaining = remaining,
                ActualDuration = current.PlannedDuration - remaining };
            await repository.SaveAsync(_current, ct);
            events.Publish(new(ProductivityEventKind.PomodoroPaused, now, current.Phase, current.Id));
            CancelTimerLocked();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ResumeAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            if (_current is not { Status: PomodoroStatus.Paused } current) return;
            var now = clock.GetUtcNow();
            _current = current with { Status = PomodoroStatus.Running, TargetAtUtc = now + current.PausedRemaining,
                PausedRemaining = TimeSpan.Zero };
            await repository.SaveAsync(_current, ct);
            events.Publish(new(ProductivityEventKind.PomodoroResumed, now, current.Phase, current.Id));
            ScheduleLocked();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task StopAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            if (_current is not { IsActive: true } current) return;
            var now = clock.GetUtcNow();
            var remaining = current.RemainingAt(now);
            _current = current with { Status = PomodoroStatus.Stopped, EndedAtUtc = now,
                ActualDuration = current.PlannedDuration - remaining, PausedRemaining = TimeSpan.Zero };
            await repository.SaveAsync(_current, ct);
            events.Publish(new(ProductivityEventKind.PomodoroStopped, now, current.Phase, current.Id));
            CancelTimerLocked();
        }
        finally { _gate.Release(); }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task RefreshAsync(CancellationToken ct)
    {
        await EnsureInitializedAsync(ct);
        var changed = false;
        await _gate.WaitAsync(ct);
        try
        {
            if (_current is { Status: PomodoroStatus.Running } running && running.TargetAtUtc <= clock.GetUtcNow())
            {
                await CompleteLockedAsync(running, ct);
                changed = true;
            }
        }
        finally { _gate.Release(); }
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task CompleteLockedAsync(PomodoroSession current, CancellationToken ct)
    {
        if (_current?.Id != current.Id || _current.Status != PomodoroStatus.Running) return;
        var now = clock.GetUtcNow();
        _current = current with { Status = PomodoroStatus.Completed, EndedAtUtc = current.TargetAtUtc,
            ActualDuration = current.PlannedDuration, PausedRemaining = TimeSpan.Zero };
        await repository.SaveAsync(_current, ct);
        if (current.Phase == PomodoroPhase.Focus) _consecutiveFocus = current.FocusSequence;
        else if (current.Phase == PomodoroPhase.LongBreak) _consecutiveFocus = 0;
        events.Publish(new(ProductivityEventKind.PomodoroCompleted, now, current.Phase, current.Id));
        CancelTimerLocked();
        if (!settings.Current.Productivity.Pomodoro.AutoStartNextPhase) return;
        var next = SuggestNext(_current);
        var duration = DurationFor(next, settings.Current.Productivity.Pomodoro);
        var sequence = next == PomodoroPhase.Focus ? _consecutiveFocus + 1 : _consecutiveFocus;
        _current = new(Guid.NewGuid(), next == PomodoroPhase.Focus ? current.TaskId : null, next, now, now + duration,
            null, PomodoroStatus.Running, duration, TimeSpan.Zero, TimeSpan.Zero, sequence);
        await repository.SaveAsync(_current, ct);
        events.Publish(new(next == PomodoroPhase.Focus ? ProductivityEventKind.PomodoroStarted :
            ProductivityEventKind.BreakStarted, now, next, _current.Id));
        ScheduleLocked();
    }

    private PomodoroPhase SuggestNext(PomodoroSession? session)
    {
        if (session is null) return PomodoroPhase.Focus;
        if (session.IsActive) return session.Phase;
        if (session.Phase is PomodoroPhase.ShortBreak or PomodoroPhase.LongBreak) return PomodoroPhase.Focus;
        return _consecutiveFocus > 0 &&
            _consecutiveFocus % settings.Current.Productivity.Pomodoro.LongBreakInterval == 0
            ? PomodoroPhase.LongBreak : PomodoroPhase.ShortBreak;
    }
    private static TimeSpan DurationFor(PomodoroPhase phase, PomodoroSettings settings) => TimeSpan.FromMinutes(phase switch
    {
        PomodoroPhase.Focus => settings.FocusMinutes,
        PomodoroPhase.ShortBreak => settings.ShortBreakMinutes,
        _ => settings.LongBreakMinutes
    });
    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (!_initialized) await InitializeAsync(ct);
    }
    private void ScheduleLocked()
    {
        CancelTimerLocked();
        if (_current is not { Status: PomodoroStatus.Running } current) return;
        _timer = new CancellationTokenSource();
        _timerTask = ObserveTimerAsync(current.Id, current.TargetAtUtc, _timer.Token);
    }
    private async Task ObserveTimerAsync(Guid id, DateTimeOffset target, CancellationToken ct)
    {
        try
        {
            var delay = target - clock.GetUtcNow();
            if (delay > TimeSpan.Zero) await Task.Delay(delay, clock, ct);
            if (!ct.IsCancellationRequested)
            {
                await RefreshAsync(ct);
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
    }
    private void CancelTimerLocked() { _timer?.Cancel(); _timer?.Dispose(); _timer = null; }
    public async Task StopSchedulerAsync(CancellationToken ct)
    {
        CancellationTokenSource? timer;
        Task task;
        await _gate.WaitAsync(ct);
        try { timer = _timer; task = _timerTask; _timer = null; timer?.Cancel(); }
        finally { _gate.Release(); }
        try { await task.WaitAsync(ct); } catch (OperationCanceledException) { }
        timer?.Dispose();
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopSchedulerAsync(CancellationToken.None);
        _gate.Dispose();
    }
}
