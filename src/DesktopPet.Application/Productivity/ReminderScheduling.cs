using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Productivity;

public static class ReminderScheduleCalculator
{
    public static DateTimeOffset? NextAfter(ReminderSchedule schedule, TimeZoneInfo zone,
        DateTimeOffset afterUtc, DateTimeOffset createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(zone);
        return schedule switch
        {
            RelativeOneTimeSchedule relative => relative.DueAtUtc > afterUtc ? relative.DueAtUtc : null,
            AbsoluteOneTimeSchedule absolute => ResolveLocal(absolute.LocalDateTime, zone) is var due && due > afterUtc ? due : null,
            RecurringSchedule recurring => NextRecurring(recurring, zone, afterUtc, createdAtUtc),
            _ => throw new ArgumentOutOfRangeException(nameof(schedule))
        };
    }

    public static DateTimeOffset ResolveLocal(DateTime localDateTime, TimeZoneInfo zone,
        DstInvalidTimePolicy invalid = DstInvalidTimePolicy.ShiftForward,
        DstAmbiguousTimePolicy ambiguous = DstAmbiguousTimePolicy.EarlierOccurrence)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            if (invalid != DstInvalidTimePolicy.ShiftForward) throw new ArgumentOutOfRangeException(nameof(invalid));
            var limit = local.AddHours(3);
            do { local = local.AddMinutes(1); } while (zone.IsInvalidTime(local) && local <= limit);
            if (zone.IsInvalidTime(local)) throw new InvalidOperationException("Unable to resolve invalid local time.");
        }
        if (zone.IsAmbiguousTime(local))
        {
            var offsets = zone.GetAmbiguousTimeOffsets(local);
            var offset = ambiguous == DstAmbiguousTimePolicy.EarlierOccurrence ? offsets.Max() : offsets.Min();
            return new DateTimeOffset(local, offset).ToUniversalTime();
        }
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }

    private static DateTimeOffset? NextRecurring(RecurringSchedule schedule, TimeZoneInfo zone,
        DateTimeOffset afterUtc, DateTimeOffset createdAtUtc)
    {
        if (schedule.SchemaVersion != 1 || !schedule.Rule.IsValid)
            throw new ArgumentException("Unsupported recurring reminder rule.", nameof(schedule));
        var afterLocal = TimeZoneInfo.ConvertTime(afterUtc, zone);
        var anchor = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(createdAtUtc, zone).DateTime);
        var first = DateOnly.FromDateTime(afterLocal.DateTime);
        for (var offset = 0; offset <= 3700; offset++)
        {
            var day = first.AddDays(offset);
            if (!OccursOn(schedule.Rule, day, anchor)) continue;
            var candidate = ResolveLocal(day.ToDateTime(schedule.Rule.LocalTime, DateTimeKind.Unspecified), zone);
            if (candidate > afterUtc) return candidate;
        }
        throw new InvalidOperationException("Recurring reminder exceeds the supported scheduling horizon.");
    }

    private static bool OccursOn(RecurrenceRule rule, DateOnly day, DateOnly anchor) => rule.Kind switch
    {
        RecurrenceKind.Daily => true,
        RecurrenceKind.Weekly => day.DayOfWeek == anchor.DayOfWeek,
        RecurrenceKind.SelectedWeekdays => rule.Weekdays.Contains(day.DayOfWeek),
        RecurrenceKind.Interval => day.DayNumber >= anchor.DayNumber &&
            (day.DayNumber - anchor.DayNumber) % rule.IntervalDays == 0,
        _ => false
    };
}

public sealed class ReminderService(IReminderRepository repository, TimeProvider clock) : IReminderService
{
    public event EventHandler? Changed;
    public async Task<Reminder> CreateAsync(Reminder reminder, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var created = Normalize(reminder with { Id = reminder.Id == Guid.Empty ? Guid.NewGuid() : reminder.Id,
            CreatedAtUtc = now, UpdatedAtUtc = now }, now);
        await repository.SaveAsync(created, ct);
        Changed?.Invoke(this, EventArgs.Empty);
        return created;
    }
    public Task<Reminder?> GetAsync(Guid id, CancellationToken ct) => repository.GetAsync(id, ct);
    public Task<IReadOnlyList<Reminder>> ListAsync(CancellationToken ct) => repository.ListAsync(ct);
    public async Task<Reminder> UpdateAsync(Reminder reminder, CancellationToken ct)
    {
        var current = await repository.GetAsync(reminder.Id, ct) ?? throw new KeyNotFoundException("Reminder not found.");
        var updated = Normalize(reminder with { CreatedAtUtc = current.CreatedAtUtc, UpdatedAtUtc = clock.GetUtcNow() }, clock.GetUtcNow());
        await repository.SaveAsync(updated, ct);
        Changed?.Invoke(this, EventArgs.Empty);
        return updated;
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await repository.DeleteAsync(id, ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct)
    {
        var current = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException("Reminder not found.");
        var updated = current with { Enabled = enabled, UpdatedAtUtc = clock.GetUtcNow() };
        updated = enabled ? Normalize(updated, clock.GetUtcNow()) : updated with { NextTriggerAtUtc = null };
        await repository.SaveAsync(updated, ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private static Reminder Normalize(Reminder reminder, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reminder.Title) || reminder.Title.Trim().Length > 200)
            throw new ArgumentException("Reminder title must be 1-200 characters.");
        if (!Enum.IsDefined(reminder.MissedPolicy) || (reminder.Channels & ~AllChannels) != 0)
            throw new ArgumentException("Invalid reminder policy or channels.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(reminder.TimeZoneId);
        var next = reminder.Enabled ? ReminderScheduleCalculator.NextAfter(reminder.Schedule, zone,
            now.AddTicks(-1), reminder.CreatedAtUtc) : null;
        return reminder with { Title = reminder.Title.Trim(),
            Description = string.IsNullOrWhiteSpace(reminder.Description) ? null : reminder.Description.Trim(),
            NextTriggerAtUtc = next };
    }
    private const ReminderChannels AllChannels = ReminderChannels.PetBubble | ReminderChannels.PetAction |
        ReminderChannels.WindowsNotification | ReminderChannels.Sound;
}

public interface IReminderOccurrenceProcessor
{
    Task<bool> ProcessAsync(Reminder reminder, DateTimeOffset occurrenceAtUtc, bool deliver, CancellationToken ct);
}

internal sealed class ReminderOccurrenceProcessor(IReminderRepository repository,
    IEnumerable<IReminderNotificationChannel> channels, IProductivityEventPublisher events,
    IExceptionHandler exceptions, TimeProvider clock) : IReminderOccurrenceProcessor
{
    public async Task<bool> ProcessAsync(Reminder reminder, DateTimeOffset occurrenceAtUtc, bool deliver, CancellationToken ct)
    {
        var next = reminder.Schedule is RecurringSchedule
            ? ReminderScheduleCalculator.NextAfter(reminder.Schedule, TimeZoneInfo.FindSystemTimeZoneById(reminder.TimeZoneId),
                occurrenceAtUtc, reminder.CreatedAtUtc) : null;
        var updated = reminder with { Enabled = next is not null, NextTriggerAtUtc = next, UpdatedAtUtc = clock.GetUtcNow() };
        var execution = new ReminderExecution(Guid.NewGuid(), reminder.Id, occurrenceAtUtc, clock.GetUtcNow(),
            deliver ? ReminderExecutionStatus.Delivered : ReminderExecutionStatus.Suppressed, reminder.Title);
        if (!await repository.TryRecordExecutionAsync(execution, updated, ct)) return false;
        if (!deliver) return true;
        foreach (var channel in channels.Where(x => reminder.Channels.HasFlag(x.Channel)))
        {
            try { await channel.NotifyAsync(reminder, occurrenceAtUtc, ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception exception) { exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.BackgroundTask); }
        }
        events.Publish(new(ProductivityEventKind.ReminderTriggered, clock.GetUtcNow(), EntityId: reminder.Id,
            Message: reminder.Title));
        return true;
    }
}

public sealed class MissedReminderResolver(IReminderRepository repository, IReminderOccurrenceProcessor processor,
    ISettingsService settings, TimeProvider clock) : IMissedReminderResolver
{
    public async Task<int> ResolveAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var count = 0;
        foreach (var reminder in (await repository.ListAsync(ct)).Where(x => x.Enabled && x.NextTriggerAtUtc <= now))
        {
            ct.ThrowIfCancellationRequested();
            var occurrence = LatestDue(reminder, now);
            var age = now - occurrence;
            var deliver = reminder.MissedPolicy switch
            {
                MissedReminderPolicy.Skip => false,
                MissedReminderPolicy.LatestOnly => true,
                _ => age <= TimeSpan.FromMinutes(settings.Current.Productivity.Reminders.SmartMissedWindowMinutes)
            };
            if (await processor.ProcessAsync(reminder, occurrence, deliver, ct) && deliver) count++;
        }
        return count;
    }
    private static DateTimeOffset LatestDue(Reminder reminder, DateTimeOffset now)
    {
        var occurrence = reminder.NextTriggerAtUtc!.Value;
        if (reminder.Schedule is not RecurringSchedule) return occurrence;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(reminder.TimeZoneId);
        for (var i = 0; i < 5000; i++)
        {
            var next = ReminderScheduleCalculator.NextAfter(reminder.Schedule, zone, occurrence, reminder.CreatedAtUtc);
            if (next is null || next > now) return occurrence;
            occurrence = next.Value;
        }
        throw new InvalidOperationException("Missed reminder history exceeds the supported bound.");
    }
}

public sealed class ReminderScheduler(IReminderService service, IReminderRepository repository,
    IReminderOccurrenceProcessor processor, TimeProvider clock) : IReminderScheduler
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private CancellationTokenSource? _lifetime;
    private Task _loop = Task.CompletedTask;
    private bool _started;
    public Task StartAsync(CancellationToken ct)
    {
        if (_started) return Task.CompletedTask;
        _started = true;
        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        service.Changed += OnChanged;
        _loop = RunAsync(_lifetime.Token);
        return Task.CompletedTask;
    }
    public Task ReconcileAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Wake();
        return Task.CompletedTask;
    }
    private void OnChanged(object? sender, EventArgs e) => Wake();
    private void Wake() { if (_wake.CurrentCount == 0) _wake.Release(); }
    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var now = clock.GetUtcNow();
            var reminders = (await repository.ListAsync(ct)).Where(x => x.Enabled && x.NextTriggerAtUtc is not null).ToArray();
            var due = reminders.Where(x => x.NextTriggerAtUtc <= now).OrderBy(x => x.NextTriggerAtUtc).ToArray();
            if (due.Length > 0)
            {
                foreach (var reminder in due)
                    await processor.ProcessAsync(reminder, reminder.NextTriggerAtUtc!.Value, true, ct);
                continue;
            }
            var next = reminders.MinBy(x => x.NextTriggerAtUtc)?.NextTriggerAtUtc;
            if (next is null) { await _wake.WaitAsync(ct); DrainWake(); continue; }
            using var iteration = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var delay = Task.Delay(next.Value - now, clock, iteration.Token);
            var wake = _wake.WaitAsync(iteration.Token);
            var completed = await Task.WhenAny(delay, wake);
            iteration.Cancel();
            try { await completed; } catch (OperationCanceledException) when (iteration.IsCancellationRequested) { }
            DrainWake();
        }
    }
    private void DrainWake() { while (_wake.CurrentCount > 0) _wake.Wait(0); }
    public async Task StopAsync(CancellationToken ct)
    {
        if (!_started) return;
        _started = false;
        service.Changed -= OnChanged;
        _lifetime?.Cancel();
        try { await _loop.WaitAsync(ct); } catch (OperationCanceledException) { }
        _lifetime?.Dispose(); _lifetime = null; _loop = Task.CompletedTask;
    }
    public async ValueTask DisposeAsync() { await StopAsync(CancellationToken.None); _wake.Dispose(); }
}

public sealed class ProductivityRecoveryService(IPomodoroService pomodoro, IMissedReminderResolver missed,
    IReminderScheduler reminders) : IProductivityRecoveryService
{
    public async Task RecoverAsync(CancellationToken ct)
    {
        await pomodoro.InitializeAsync(ct);
        await missed.ResolveAsync(ct);
        await reminders.StartAsync(ct);
    }
    public async Task ReconcileAfterResumeAsync(CancellationToken ct)
    {
        await pomodoro.RefreshAsync(ct);
        await missed.ResolveAsync(ct);
        await reminders.ReconcileAsync(ct);
    }
}
