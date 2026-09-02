using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Productivity;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.App.Bootstrap;

public sealed class PetActionReminderChannel(PetHost pets) : IReminderNotificationChannel
{
    public ReminderChannels Channel => ReminderChannels.PetAction;
    public Task NotifyAsync(Reminder reminder, DateTimeOffset occurrenceAtUtc, CancellationToken ct) =>
        pets.Runtime.PlayAsync(new AnimationSemantic("happy"), ct);
}

public sealed class ProductivityRuntimeBridge(IProductivityEventPublisher events, ISessionStateService session,
    IProductivityRecoveryService recovery, IPomodoroService pomodoro, PetHost pets, IExceptionHandler exceptions) : IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private bool _started;
    public async Task StartAsync(CancellationToken ct)
    {
        if (_started) return;
        _started = true;
        events.Published += OnProductivity;
        session.StateChanged += OnSession;
        session.Start();
        var snapshot = await pomodoro.GetSnapshotAsync(ct);
        await pets.Runtime.SetProductivityContextAsync(snapshot.Session?.Phase,
            snapshot.Status == PomodoroStatus.Running && snapshot.Session?.Phase == PomodoroPhase.Focus, ct);
    }
    private async void OnProductivity(object? sender, ProductivityEvent e) => await BoundaryAsync(async () =>
    {
        switch (e.Kind)
        {
            case ProductivityEventKind.PomodoroStarted:
            case ProductivityEventKind.PomodoroResumed:
                await pets.Runtime.SetProductivityContextAsync(e.Phase, e.Phase == PomodoroPhase.Focus, _lifetime.Token);
                break;
            case ProductivityEventKind.BreakStarted:
            case ProductivityEventKind.PomodoroPaused:
            case ProductivityEventKind.PomodoroStopped:
                await pets.Runtime.SetProductivityContextAsync(e.Phase, false, _lifetime.Token);
                break;
            case ProductivityEventKind.PomodoroCompleted:
                await pets.Runtime.SetProductivityContextAsync(null, false, _lifetime.Token);
                if (e.Phase == PomodoroPhase.Focus)
                    await pets.Runtime.PlayAsync(new AnimationSemantic("happy"), _lifetime.Token);
                break;
        }
    });
    private async void OnSession(object? sender, EventArgs e) => await BoundaryAsync(async () =>
    {
        if (session.State is SessionState.Locked or SessionState.Sleeping)
            await pets.Runtime.SetSessionSuspendedAsync(true, _lifetime.Token);
        else if (session.State == SessionState.Resuming)
        {
            await recovery.ReconcileAfterResumeAsync(_lifetime.Token);
            await pets.Runtime.SetSessionSuspendedAsync(false, _lifetime.Token);
        }
    });
    private async Task BoundaryAsync(Func<Task> action)
    {
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) { exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.BackgroundTask); }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        if (!_started) return;
        _started = false;
        events.Published -= OnProductivity;
        session.StateChanged -= OnSession;
        _lifetime.Cancel();
        await session.StopAsync(ct);
    }
    public void Dispose()
    {
        events.Published -= OnProductivity;
        session.StateChanged -= OnSession;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
