using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Contracts;

public interface IPomodoroService : IAsyncDisposable
{
    event EventHandler? Changed;
    Task InitializeAsync(CancellationToken ct);
    Task<PomodoroSession?> GetCurrentAsync(CancellationToken ct);
    Task<PomodoroSnapshot> GetSnapshotAsync(CancellationToken ct);
    Task StartAsync(PomodoroPhase phase, TimeSpan duration, Guid? taskId, CancellationToken ct);
    Task PauseAsync(CancellationToken ct);
    Task ResumeAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task RefreshAsync(CancellationToken ct);
    Task StopSchedulerAsync(CancellationToken ct);
}
public interface ITaskService
{
    Task<FocusTask> CreateAsync(string title, string? description, CancellationToken ct);
    Task<FocusTask> UpdateAsync(Guid id, string title, string? description, CancellationToken ct);
    Task ArchiveAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<FocusTask>> ListAsync(bool includeArchived, CancellationToken ct);
    Task SetTagsAsync(Guid taskId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct);
    Task<IReadOnlyList<Tag>> GetTagsAsync(Guid taskId, CancellationToken ct);
}
public interface ITagService
{
    Task<Tag> CreateAsync(string name, CancellationToken ct);
    Task<Tag> RenameAsync(Guid id, string name, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken ct);
}
public interface IReminderService
{
    event EventHandler? Changed;
    Task<Reminder> CreateAsync(Reminder reminder, CancellationToken ct);
    Task<Reminder?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Reminder>> ListAsync(CancellationToken ct);
    Task<Reminder> UpdateAsync(Reminder reminder, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct);
}
public interface IReminderScheduler : IAsyncDisposable
{
    Task StartAsync(CancellationToken ct);
    Task ReconcileAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
public interface IMissedReminderResolver { Task<int> ResolveAsync(CancellationToken ct); }
public interface IStatisticsService
{
    Task<StatisticsSummary> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
    Task<ProductivityStatistics> GetOverviewAsync(DateOnly localToday, TimeZoneInfo timeZone, CancellationToken ct);
}
public interface IStatisticsExporter
{
    Task ExportCsvAsync(Stream destination, DateOnly from, DateOnly to, TimeZoneInfo timeZone, CancellationToken ct);
}
public interface IReminderNotificationChannel
{
    ReminderChannels Channel { get; }
    Task NotifyAsync(Reminder reminder, DateTimeOffset occurrenceAtUtc, CancellationToken ct);
}
public interface IPetBubbleService
{
    Task ShowAsync(string message, CancellationToken ct);
}
public interface IProductivityEventPublisher
{
    event EventHandler<ProductivityEvent>? Published;
    void Publish(ProductivityEvent notification);
}
public interface IProductivityRecoveryService
{
    Task RecoverAsync(CancellationToken ct);
    Task ReconcileAfterResumeAsync(CancellationToken ct);
}
