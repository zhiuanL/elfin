namespace DesktopPet.Application.Configuration;

public sealed record PomodoroSettings
{
    public int FocusMinutes { get; init; } = 25;
    public int ShortBreakMinutes { get; init; } = 5;
    public int LongBreakMinutes { get; init; } = 15;
    public int LongBreakInterval { get; init; } = 4;
    public bool AutoStartNextPhase { get; init; }
    public bool IsValid => FocusMinutes is >= 1 and <= 240 && ShortBreakMinutes is >= 1 and <= 120 &&
        LongBreakMinutes is >= 1 and <= 240 && LongBreakInterval is >= 2 and <= 12;
}

public sealed record ReminderSettings
{
    public int SmartMissedWindowMinutes { get; init; } = 15;
    public bool IsValid => SmartMissedWindowMinutes is >= 1 and <= 1440;
}

public sealed record ProductivitySettings
{
    public PomodoroSettings Pomodoro { get; init; } = new();
    public ReminderSettings Reminders { get; init; } = new();
    public bool IsValid => Pomodoro is { IsValid: true } && Reminders is { IsValid: true };
}
