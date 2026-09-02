using DesktopPet.Application.Productivity;
using DesktopPet.Domain.Productivity;
using DesktopPet.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DesktopPet.Tests.Integration;

public sealed class ProductivityPersistenceTests
{
    [Fact]
    public async Task EmptyAppDatabaseCreatesProductivitySchemaAndRepositoriesRoundTrip()
    {
        using var env = new TestEnvironment();
        await env.Migrator().MigrateAsync(DesktopPet.Application.Storage.DatabaseKind.App, default);
        await using (var db = await env.Connections.OpenAsync(DesktopPet.Application.Storage.DatabaseKind.App, default))
        {
            Assert.Equal(2L, await Scalar(db, "SELECT MAX(Version) FROM SchemaMigrations;"));
            foreach (var table in new[] { "PomodoroSessions", "Tasks", "Tags", "TaskTags", "Reminders", "ReminderExecutions" })
                Assert.Equal(1L, await Scalar(db, $"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{table}';"));
        }
        var taskRepository = new SqliteTaskRepository(env.Connections);
        var tagRepository = new SqliteTagRepository(env.Connections);
        var taskService = new TaskService(taskRepository, tagRepository, TimeProvider.System);
        var tagService = new TagService(tagRepository, TimeProvider.System);
        var task = await taskService.CreateAsync("write report", "phase six", default);
        var tag = await tagService.CreateAsync("work", default);
        task = await taskService.UpdateAsync(task.Id, "write final report", "phase six verified", default);
        tag = await tagService.RenameAsync(tag.Id, "deep-work", default);
        Assert.Equal("write final report", task.Title);
        Assert.Equal("deep-work", tag.Name);
        await taskService.SetTagsAsync(task.Id, [tag.Id], default);
        Assert.Equal(tag.Id, Assert.Single(await taskService.GetTagsAsync(task.Id, default)).Id);
        await taskService.ArchiveAsync(task.Id, default);
        Assert.Empty(await taskService.ListAsync(false, default));
        Assert.Single(await taskService.ListAsync(true, default));

        var sessions = new SqlitePomodoroRepository(env.Connections);
        var now = DateTimeOffset.UtcNow;
        var session = new PomodoroSession(Guid.NewGuid(), task.Id, PomodoroPhase.Focus, now.AddMinutes(-25), now,
            now, PomodoroStatus.Completed, TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(25), TimeSpan.Zero, 1);
        await sessions.SaveAsync(session, default);
        Assert.Equal(task.Id, (await sessions.GetAsync(session.Id, default))!.TaskId);
        await tagService.DeleteAsync(tag.Id, default);
        Assert.Empty(await taskService.GetTagsAsync(task.Id, default));
        Assert.Equal(task.Id, (await sessions.GetAsync(session.Id, default))!.TaskId);
    }

    [Fact]
    public async Task ReminderScheduleJsonRoundTripsAndExecutionIsDeduplicatedTransactionally()
    {
        using var env = new TestEnvironment();
        await env.Migrator().MigrateAsync(DesktopPet.Application.Storage.DatabaseKind.App, default);
        var repository = new SqliteReminderRepository(env.Connections);
        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder(Guid.NewGuid(), "daily", "hydrate",
            new RecurringSchedule(1, new(RecurrenceKind.SelectedWeekdays, new(9, 30),
                new HashSet<DayOfWeek> { DayOfWeek.Monday, DayOfWeek.Friday })),
            "UTC", true, MissedReminderPolicy.Smart,
            ReminderChannels.PetBubble | ReminderChannels.PetAction | ReminderChannels.WindowsNotification,
            now, now, now.AddHours(1));
        await repository.SaveAsync(reminder, default);
        var loaded = Assert.IsType<Reminder>(await repository.GetAsync(reminder.Id, default));
        var schedule = Assert.IsType<RecurringSchedule>(loaded.Schedule);
        Assert.Equal(RecurrenceKind.SelectedWeekdays, schedule.Rule.Kind);
        Assert.Contains(DayOfWeek.Friday, schedule.Rule.Weekdays);
        var next = reminder with { NextTriggerAtUtc = now.AddDays(1), UpdatedAtUtc = now.AddMinutes(1) };
        var occurrence = now.AddHours(1);
        Assert.True(await repository.TryRecordExecutionAsync(new(Guid.NewGuid(), reminder.Id, occurrence, now,
            ReminderExecutionStatus.Delivered, reminder.Title), next, default));
        Assert.False(await repository.TryRecordExecutionAsync(new(Guid.NewGuid(), reminder.Id, occurrence, now,
            ReminderExecutionStatus.Delivered, reminder.Title), next, default));
        Assert.Equal(next.NextTriggerAtUtc, (await repository.GetAsync(reminder.Id, default))!.NextTriggerAtUtc);
    }

    [Fact]
    public async Task StatisticsUsePersistedTerminalFocusSessionsAndLocalDates()
    {
        using var env = new TestEnvironment();
        await env.Migrator().MigrateAsync(DesktopPet.Application.Storage.DatabaseKind.App, default);
        var sessions = new SqlitePomodoroRepository(env.Connections);
        var tasks = new SqliteTaskRepository(env.Connections);
        var tags = new SqliteTagRepository(env.Connections);
        var today = new DateOnly(2026, 9, 1);
        var start = new DateTimeOffset(2026, 9, 1, 1, 0, 0, TimeSpan.Zero);
        var task = new FocusTask(Guid.NewGuid(), "test", null, FocusTaskStatus.Active, start, start);
        var tag = new DesktopPet.Domain.Productivity.Tag(Guid.NewGuid(), "quality", start, start);
        await tasks.SaveAsync(task, default); await tags.SaveAsync(tag, default); await tasks.SetTagsAsync(task.Id, [tag.Id], default);
        await sessions.SaveAsync(Session(PomodoroStatus.Completed, start, 1500, task.Id), default);
        await sessions.SaveAsync(Session(PomodoroStatus.Stopped, start.AddHours(1), 300, task.Id), default);
        await sessions.SaveAsync(Session(PomodoroStatus.Paused, start.AddHours(2), 600, task.Id), default);
        await sessions.SaveAsync(Session(PomodoroStatus.Completed, start.AddDays(-1), 1500, task.Id), default);
        var service = new StatisticsService(sessions, tasks, tags);
        var value = await service.GetOverviewAsync(today, TimeZoneInfo.Utc, default);
        Assert.Equal(TimeSpan.FromMinutes(30), value.TodayFocusDuration);
        Assert.Equal(1, value.TodayCompletedPomodoros);
        Assert.Equal(2, value.FocusStreakDays);
        Assert.Equal(TimeSpan.FromMinutes(55), Assert.Single(value.TaskStatistics).FocusDuration);
        Assert.Equal(TimeSpan.FromMinutes(55), Assert.Single(value.TagStatistics).FocusDuration);
    }

    private static PomodoroSession Session(PomodoroStatus status, DateTimeOffset started, double seconds, Guid taskId) =>
        new(Guid.NewGuid(), taskId, PomodoroPhase.Focus, started, started.AddMinutes(25),
            status is PomodoroStatus.Completed or PomodoroStatus.Stopped ? started.AddSeconds(seconds) : null,
            status, TimeSpan.FromMinutes(25), TimeSpan.FromSeconds(seconds),
            status == PomodoroStatus.Paused ? TimeSpan.FromMinutes(15) : TimeSpan.Zero, 1);
    private static async Task<object?> Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
    }
}
