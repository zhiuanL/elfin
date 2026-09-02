using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Productivity;
using DesktopPet.Domain.Productivity;
using DesktopPet.Tests.Shared;

namespace DesktopPet.Tests.Unit;

public sealed class PhaseSixProductivityTests
{
    [Fact]
    public async Task PomodoroUsesAbsoluteTimeAndPauseExcludesElapsedWallTime()
    {
        var clock = new ManualTimeProvider();
        var repository = new MemoryPomodoros();
        await using var service = CreatePomodoro(repository, clock);
        await service.StartAsync(PomodoroPhase.Focus, TimeSpan.FromMinutes(10), null, default);
        clock.Jump(TimeSpan.FromMinutes(2));
        await service.PauseAsync(default);
        var paused = Assert.IsType<PomodoroSession>(await service.GetCurrentAsync(default));
        Assert.Equal(PomodoroStatus.Paused, paused.Status);
        Assert.Equal(TimeSpan.FromMinutes(8), paused.PausedRemaining);
        clock.Jump(TimeSpan.FromMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(8), (await service.GetSnapshotAsync(default)).Remaining);
        await service.ResumeAsync(default);
        clock.Jump(TimeSpan.FromMinutes(8));
        await service.RefreshAsync(default);
        var completed = Assert.IsType<PomodoroSession>(await service.GetCurrentAsync(default));
        Assert.Equal(PomodoroStatus.Completed, completed.Status);
        Assert.Equal(TimeSpan.FromMinutes(10), completed.ActualDuration);
    }

    [Fact]
    public async Task StopIsDistinctAndDoesNotCountAsCompleted()
    {
        var clock = new ManualTimeProvider();
        var repository = new MemoryPomodoros();
        await using var service = CreatePomodoro(repository, clock);
        await service.StartAsync(PomodoroPhase.Focus, TimeSpan.FromMinutes(10), null, default);
        clock.Jump(TimeSpan.FromMinutes(3));
        await service.StopAsync(default);
        var stopped = Assert.IsType<PomodoroSession>(await service.GetCurrentAsync(default));
        Assert.Equal(PomodoroStatus.Stopped, stopped.Status);
        Assert.Equal(TimeSpan.FromMinutes(3), stopped.ActualDuration);
    }

    [Fact]
    public async Task AutoCycleSelectsShortThenLongBreakWithoutOverlap()
    {
        var clock = new ManualTimeProvider();
        var repository = new MemoryPomodoros();
        var settings = new TestSettingsService
        {
            Current = new AppSettings { Productivity = new() { Pomodoro = new()
                { FocusMinutes = 1, ShortBreakMinutes = 1, LongBreakMinutes = 1, LongBreakInterval = 2, AutoStartNextPhase = true } } }
        };
        await using var service = CreatePomodoro(repository, clock, settings);
        await service.StartAsync(PomodoroPhase.Focus, TimeSpan.FromMinutes(1), null, default);
        clock.Jump(TimeSpan.FromMinutes(1)); await service.RefreshAsync(default);
        Assert.Equal(PomodoroPhase.ShortBreak, (await service.GetCurrentAsync(default))!.Phase);
        clock.Jump(TimeSpan.FromMinutes(1)); await service.RefreshAsync(default);
        Assert.Equal(PomodoroPhase.Focus, (await service.GetCurrentAsync(default))!.Phase);
        clock.Jump(TimeSpan.FromMinutes(1)); await service.RefreshAsync(default);
        Assert.Equal(PomodoroPhase.LongBreak, (await service.GetCurrentAsync(default))!.Phase);
        Assert.Single(repository.Items, x => x.IsActive);
    }

    [Fact]
    public async Task RunningAndPausedSessionsRecoverAcrossServiceRestart()
    {
        var clock = new ManualTimeProvider();
        var repository = new MemoryPomodoros();
        await using (var first = CreatePomodoro(repository, clock))
        {
            await first.StartAsync(PomodoroPhase.Focus, TimeSpan.FromMinutes(5), null, default);
            clock.Jump(TimeSpan.FromMinutes(1));
            await first.StopSchedulerAsync(default);
        }
        await using (var restored = CreatePomodoro(repository, clock))
        {
            await restored.InitializeAsync(default);
            Assert.Equal(TimeSpan.FromMinutes(4), (await restored.GetSnapshotAsync(default)).Remaining);
            await restored.PauseAsync(default);
            await restored.StopSchedulerAsync(default);
        }
        clock.Jump(TimeSpan.FromHours(2));
        await using var paused = CreatePomodoro(repository, clock);
        await paused.InitializeAsync(default);
        Assert.Equal(PomodoroStatus.Paused, (await paused.GetSnapshotAsync(default)).Status);
        Assert.Equal(TimeSpan.FromMinutes(4), (await paused.GetSnapshotAsync(default)).Remaining);
    }

    [Fact]
    public async Task SleepOrDispatcherDelayCompletesOnceAtTarget()
    {
        var clock = new ManualTimeProvider();
        var repository = new MemoryPomodoros();
        var events = new ProductivityEventHub();
        var completed = 0;
        events.Published += (_, e) => { if (e.Kind == ProductivityEventKind.PomodoroCompleted) completed++; };
        await using var service = CreatePomodoro(repository, clock, events: events);
        await service.StartAsync(PomodoroPhase.Focus, TimeSpan.FromMinutes(1), null, default);
        clock.Jump(TimeSpan.FromHours(1));
        await service.RefreshAsync(default);
        await service.RefreshAsync(default);
        Assert.Equal(1, completed);
        Assert.Single(repository.Items, x => x.Status == PomodoroStatus.Completed);
    }

    [Fact]
    public void DstInvalidAndAmbiguousTimesHaveDeterministicPolicy()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var invalid = ReminderScheduleCalculator.ResolveLocal(new DateTime(2026, 3, 8, 2, 30, 0), zone);
        Assert.Equal(new DateTime(2026, 3, 8, 3, 0, 0), TimeZoneInfo.ConvertTime(invalid, zone).DateTime);
        var ambiguous = ReminderScheduleCalculator.ResolveLocal(new DateTime(2026, 11, 1, 1, 30, 0), zone);
        Assert.Equal(TimeSpan.FromHours(-4), TimeZoneInfo.ConvertTime(ambiguous, zone).Offset);
    }

    [Fact]
    public void RecurringRulesCalculateDailyWeeklySelectedAndInterval()
    {
        var zone = TimeZoneInfo.Utc;
        var created = new DateTimeOffset(2026, 8, 24, 8, 0, 0, TimeSpan.Zero); // Monday
        var after = new DateTimeOffset(2026, 8, 25, 10, 0, 0, TimeSpan.Zero);
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero),
            ReminderScheduleCalculator.NextAfter(new RecurringSchedule(1,
                new(RecurrenceKind.Daily, new(9, 0), new HashSet<DayOfWeek>())), zone, after, created));
        Assert.Equal(new DateTimeOffset(2026, 8, 31, 9, 0, 0, TimeSpan.Zero),
            ReminderScheduleCalculator.NextAfter(new RecurringSchedule(1,
                new(RecurrenceKind.Weekly, new(9, 0), new HashSet<DayOfWeek>())), zone, after, created));
        Assert.Equal(new DateTimeOffset(2026, 8, 28, 9, 0, 0, TimeSpan.Zero),
            ReminderScheduleCalculator.NextAfter(new RecurringSchedule(1,
                new(RecurrenceKind.SelectedWeekdays, new(9, 0), new HashSet<DayOfWeek> { DayOfWeek.Friday })), zone, after, created));
        Assert.Equal(new DateTimeOffset(2026, 8, 26, 9, 0, 0, TimeSpan.Zero),
            ReminderScheduleCalculator.NextAfter(new RecurringSchedule(1,
                new(RecurrenceKind.Interval, new(9, 0), new HashSet<DayOfWeek>(), 2)), zone, after, created));
    }

    [Fact]
    public async Task ReminderCrudEnableDisableAndMissedRecurringOnlyUsesLatest()
    {
        var clock = new ManualTimeProvider();
        var repository = new MemoryReminders();
        var service = new ReminderService(repository, clock);
        var createdAt = clock.GetUtcNow();
        var created = await service.CreateAsync(new(Guid.Empty, "stand up", null,
            new RecurringSchedule(1, new(RecurrenceKind.Daily, TimeOnly.FromDateTime(createdAt.UtcDateTime),
                new HashSet<DayOfWeek>())), "UTC", true, MissedReminderPolicy.LatestOnly,
            ReminderChannels.PetBubble, createdAt, createdAt, null), default);
        Assert.NotEqual(Guid.Empty, created.Id);
        await service.SetEnabledAsync(created.Id, false, default);
        Assert.False((await service.GetAsync(created.Id, default))!.Enabled);
        await service.SetEnabledAsync(created.Id, true, default);
        clock.Jump(TimeSpan.FromDays(3) + TimeSpan.FromMinutes(1));
        var processor = new RecordingProcessor();
        var resolver = new MissedReminderResolver(repository, processor, new TestSettingsService(), clock);
        Assert.Equal(1, await resolver.ResolveAsync(default));
        Assert.Single(processor.Occurrences);
        Assert.InRange(clock.GetUtcNow() - processor.Occurrences[0], TimeSpan.Zero, TimeSpan.FromDays(1));
        await service.DeleteAsync(created.Id, default);
        Assert.Null(await service.GetAsync(created.Id, default));
    }

    [Fact]
    public async Task SmartMissedPolicyDeliversRecentAndSuppressesStaleOccurrences()
    {
        var clock = new ManualTimeProvider();
        var repository = new MemoryReminders();
        var service = new ReminderService(repository, clock);
        var recent = await service.CreateAsync(NewRelativeReminder("recent", clock.GetUtcNow().AddMinutes(1)), default);
        var stale = await service.CreateAsync(NewRelativeReminder("stale", clock.GetUtcNow().AddMinutes(1)), default);
        clock.Jump(TimeSpan.FromMinutes(10));
        var processor = new RecordingProcessor();
        var resolver = new MissedReminderResolver(repository, processor, new TestSettingsService
        {
            Current = new AppSettings { Productivity = new() { Reminders = new() { SmartMissedWindowMinutes = 15 } } }
        }, clock);
        Assert.Equal(2, await resolver.ResolveAsync(default));
        Assert.All(processor.Deliveries, item => Assert.True(item.Deliver));

        repository = new MemoryReminders();
        service = new ReminderService(repository, clock);
        await service.CreateAsync(NewRelativeReminder("stale", clock.GetUtcNow().AddMinutes(1)), default);
        clock.Jump(TimeSpan.FromMinutes(20));
        processor = new RecordingProcessor();
        resolver = new MissedReminderResolver(repository, processor, new TestSettingsService(), clock);
        Assert.Equal(0, await resolver.ResolveAsync(default));
        Assert.False(Assert.Single(processor.Deliveries).Deliver);
        Assert.NotEqual(recent.Id, stale.Id);
    }

    [Fact]
    public async Task ReminderSchedulerStopCancelsPendingTimerAndLeavesNoWork()
    {
        var clock = new ManualTimeProvider();
        var repository = new MemoryReminders();
        var service = new ReminderService(repository, clock);
        await service.CreateAsync(NewRelativeReminder("later", clock.GetUtcNow().AddHours(1)), default);
        var processor = new RecordingProcessor();
        await using var scheduler = new ReminderScheduler(service, repository, processor, clock);
        await scheduler.StartAsync(default);
        await clock.WaitForTimerAsync().WaitAsync(TimeSpan.FromSeconds(1));
        await scheduler.StopAsync(default);
        clock.Jump(TimeSpan.FromHours(2));
        Assert.Empty(processor.Deliveries);
    }

    private static Reminder NewRelativeReminder(string title, DateTimeOffset dueAtUtc) =>
        new(Guid.Empty, title, null, new RelativeOneTimeSchedule(dueAtUtc), "UTC", true,
            MissedReminderPolicy.Smart, ReminderChannels.PetBubble, dueAtUtc, dueAtUtc, null);

    private static PomodoroService CreatePomodoro(MemoryPomodoros repository, ManualTimeProvider clock,
        TestSettingsService? settings = null, ProductivityEventHub? events = null) =>
        new(repository, settings ?? new TestSettingsService(), events ?? new ProductivityEventHub(), clock);

    private sealed class MemoryPomodoros : IPomodoroRepository
    {
        private readonly Dictionary<Guid, PomodoroSession> _items = [];
        public IReadOnlyCollection<PomodoroSession> Items => _items.Values;
        public Task<PomodoroSession?> GetActiveAsync(CancellationToken ct) => Task.FromResult(
            _items.Values.OrderByDescending(x => x.StartedAtUtc).FirstOrDefault(x => x.IsActive));
        public Task<PomodoroSession?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task SaveAsync(PomodoroSession session, CancellationToken ct) { _items[session.Id] = session; return Task.CompletedTask; }
        public Task<IReadOnlyList<PomodoroSession>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<PomodoroSession>>(_items.Values.Where(x => x.StartedAtUtc >= fromUtc && x.StartedAtUtc < toUtc).ToArray());
        public Task<int> CountRecentCompletedFocusAsync(CancellationToken ct) => Task.FromResult(
            _items.Values.Where(x => x.Phase == PomodoroPhase.Focus && x.Status == PomodoroStatus.Completed)
                .OrderByDescending(x => x.EndedAtUtc).FirstOrDefault()?.FocusSequence ?? 0);
    }

    private sealed class MemoryReminders : IReminderRepository
    {
        private readonly Dictionary<Guid, Reminder> _items = [];
        public Task<Reminder?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult(_items.GetValueOrDefault(id));
        public Task<IReadOnlyList<Reminder>> ListAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<Reminder>>(_items.Values.ToArray());
        public Task SaveAsync(Reminder reminder, CancellationToken ct) { _items[reminder.Id] = reminder; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken ct) { _items.Remove(id); return Task.CompletedTask; }
        public Task<bool> TryRecordExecutionAsync(ReminderExecution execution, Reminder updatedReminder, CancellationToken ct)
        { _items[updatedReminder.Id] = updatedReminder; return Task.FromResult(true); }
    }
    private sealed class RecordingProcessor : IReminderOccurrenceProcessor
    {
        public List<DateTimeOffset> Occurrences { get; } = [];
        public List<(DateTimeOffset Occurrence, bool Deliver)> Deliveries { get; } = [];
        public Task<bool> ProcessAsync(Reminder reminder, DateTimeOffset occurrenceAtUtc, bool deliver, CancellationToken ct)
        { Occurrences.Add(occurrenceAtUtc); Deliveries.Add((occurrenceAtUtc, deliver)); return Task.FromResult(true); }
    }
}
