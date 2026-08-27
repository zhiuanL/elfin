namespace DesktopPet.Domain.Productivity;

public enum PomodoroPhase { Focus, ShortBreak, LongBreak }
public enum PomodoroStatus { Running, Paused, Completed, Stopped }
public sealed record PomodoroSession(Guid Id, Guid? TaskId, PomodoroPhase Phase,
    DateTimeOffset StartedAtUtc, DateTimeOffset TargetAtUtc, DateTimeOffset? EndedAtUtc,
    PomodoroStatus Status, TimeSpan PlannedDuration, TimeSpan ActualDuration);

public enum ReminderScheduleType { Once, Recurring }
public enum MissedReminderPolicy { Smart, Skip, LatestOnly }
[Flags]
public enum ReminderChannels { None = 0, PetBubble = 1, PetAction = 2, WindowsNotification = 4, Sound = 8 }
public abstract record ReminderSchedule(ReminderScheduleType Type);
public sealed record OnceSchedule(DateTimeOffset DueAtUtc) : ReminderSchedule(ReminderScheduleType.Once);
// Recurrence rules are versioned when Phase 6 defines calendar/DST semantics.
public sealed record RecurringSchedule(int SchemaVersion, string Rule, string TimeZoneId)
    : ReminderSchedule(ReminderScheduleType.Recurring);
public sealed record Reminder(Guid Id, string Title, string? Description, ReminderSchedule Schedule,
    bool Enabled, MissedReminderPolicy MissedPolicy, ReminderChannels Channels,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record FocusTask(Guid Id, string Title);
public sealed record Tag(Guid Id, string Name);
public sealed record StatisticsSummary(TimeSpan FocusDuration, int CompletedSessions, int StreakDays);
