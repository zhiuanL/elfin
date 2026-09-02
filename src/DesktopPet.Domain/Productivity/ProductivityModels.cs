namespace DesktopPet.Domain.Productivity;

public enum PomodoroPhase { Focus, ShortBreak, LongBreak }
public enum PomodoroStatus { Idle, Running, Paused, Completed, Stopped }
public sealed record PomodoroSession(Guid Id, Guid? TaskId, PomodoroPhase Phase,
    DateTimeOffset StartedAtUtc, DateTimeOffset TargetAtUtc, DateTimeOffset? EndedAtUtc,
    PomodoroStatus Status, TimeSpan PlannedDuration, TimeSpan ActualDuration,
    TimeSpan PausedRemaining, int FocusSequence)
{
    public bool IsActive => Status is PomodoroStatus.Running or PomodoroStatus.Paused;
    public TimeSpan RemainingAt(DateTimeOffset utcNow) => Status switch
    {
        PomodoroStatus.Paused => PausedRemaining,
        PomodoroStatus.Running => TargetAtUtc <= utcNow ? TimeSpan.Zero : TargetAtUtc - utcNow,
        _ => TimeSpan.Zero
    };
}
public sealed record PomodoroSnapshot(PomodoroSession? Session, PomodoroPhase SuggestedPhase,
    int ConsecutiveFocusCount, DateTimeOffset ObservedAtUtc)
{
    public PomodoroStatus Status => Session?.Status ?? PomodoroStatus.Idle;
    public TimeSpan Remaining => Session?.RemainingAt(ObservedAtUtc) ?? TimeSpan.Zero;
}

public enum FocusTaskStatus { Active, Archived }
public sealed record FocusTask(Guid Id, string Title, string? Description, FocusTaskStatus Status,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record Tag(Guid Id, string Name, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);

public enum ReminderScheduleType { RelativeOneTime, AbsoluteOneTime, Recurring }
public enum MissedReminderPolicy { Smart, Skip, LatestOnly }
public enum RecurrenceKind { Daily, Weekly, SelectedWeekdays, Interval }
public enum DstInvalidTimePolicy { ShiftForward }
public enum DstAmbiguousTimePolicy { EarlierOccurrence, LaterOccurrence }
[Flags]
public enum ReminderChannels { None = 0, PetBubble = 1, PetAction = 2, WindowsNotification = 4, Sound = 8 }
public abstract record ReminderSchedule(ReminderScheduleType Type);
public sealed record RelativeOneTimeSchedule(DateTimeOffset DueAtUtc) : ReminderSchedule(ReminderScheduleType.RelativeOneTime);
public sealed record AbsoluteOneTimeSchedule(DateTime LocalDateTime) : ReminderSchedule(ReminderScheduleType.AbsoluteOneTime);
public sealed record RecurrenceRule(RecurrenceKind Kind, TimeOnly LocalTime,
    IReadOnlySet<DayOfWeek> Weekdays, int IntervalDays = 1)
{
    public bool IsValid => Enum.IsDefined(Kind) && IntervalDays is >= 1 and <= 365 &&
        (Kind != RecurrenceKind.SelectedWeekdays || Weekdays is { Count: > 0 });
}
public sealed record RecurringSchedule(int SchemaVersion, RecurrenceRule Rule) : ReminderSchedule(ReminderScheduleType.Recurring);
public sealed record Reminder(Guid Id, string Title, string? Description, ReminderSchedule Schedule,
    string TimeZoneId, bool Enabled, MissedReminderPolicy MissedPolicy, ReminderChannels Channels,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? NextTriggerAtUtc);
public enum ReminderExecutionStatus { Delivered, Suppressed, Failed }
public sealed record ReminderExecution(Guid Id, Guid? ReminderId, DateTimeOffset OccurrenceAtUtc,
    DateTimeOffset ExecutedAtUtc, ReminderExecutionStatus Status, string TitleSnapshot);

public sealed record TrendPoint(DateOnly LocalDate, TimeSpan FocusDuration, int CompletedPomodoros);
public sealed record NamedFocusStatistic(Guid Id, string Name, TimeSpan FocusDuration, int CompletedPomodoros);
public sealed record StatisticsSummary(TimeSpan FocusDuration, int CompletedSessions, int StreakDays);
public sealed record ProductivityStatistics(TimeSpan TodayFocusDuration, int TodayCompletedPomodoros,
    IReadOnlyList<TrendPoint> DailyTrend, IReadOnlyList<TrendPoint> WeeklyTrend,
    IReadOnlyList<TrendPoint> MonthlyTrend, int FocusStreakDays,
    IReadOnlyList<NamedFocusStatistic> TaskStatistics, IReadOnlyList<NamedFocusStatistic> TagStatistics);
public enum ProductivityEventKind
{
    PomodoroStarted, PomodoroPaused, PomodoroResumed, PomodoroStopped,
    PomodoroCompleted, BreakStarted, ReminderTriggered
}
public sealed record ProductivityEvent(ProductivityEventKind Kind, DateTimeOffset OccurredAtUtc,
    PomodoroPhase? Phase = null, Guid? EntityId = null, string? Message = null);
