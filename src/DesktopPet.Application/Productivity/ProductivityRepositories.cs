using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Productivity;

public interface IPomodoroRepository
{
    Task<PomodoroSession?> GetActiveAsync(CancellationToken ct);
    Task<PomodoroSession?> GetAsync(Guid id, CancellationToken ct);
    Task SaveAsync(PomodoroSession session, CancellationToken ct);
    Task<IReadOnlyList<PomodoroSession>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
    Task<int> CountRecentCompletedFocusAsync(CancellationToken ct);
}
public interface ITaskRepository
{
    Task<FocusTask?> GetAsync(Guid id, CancellationToken ct);
    Task SaveAsync(FocusTask task, CancellationToken ct);
    Task<IReadOnlyList<FocusTask>> ListAsync(bool includeArchived, CancellationToken ct);
    Task SetTagsAsync(Guid taskId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct);
    Task<IReadOnlyList<Tag>> GetTagsAsync(Guid taskId, CancellationToken ct);
}
public interface ITagRepository
{
    Task<Tag?> GetAsync(Guid id, CancellationToken ct);
    Task SaveAsync(Tag tag, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Tag>> ListAsync(CancellationToken ct);
}
public interface IReminderRepository
{
    Task<Reminder?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<Reminder>> ListAsync(CancellationToken ct);
    Task SaveAsync(Reminder reminder, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task<bool> TryRecordExecutionAsync(ReminderExecution execution, Reminder updatedReminder, CancellationToken ct);
}
