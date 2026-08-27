using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Contracts;

public interface IPomodoroService
{
    Task<PomodoroSession?> GetCurrentAsync(CancellationToken ct);
    Task StartAsync(PomodoroPhase phase, TimeSpan duration, Guid? taskId, CancellationToken ct);
    Task PauseAsync(CancellationToken ct);
    Task ResumeAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
public interface IReminderService
{
    Task<IReadOnlyList<Reminder>> ListAsync(CancellationToken ct);
    Task SaveAsync(Reminder reminder, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task SetEnabledAsync(Guid id, bool enabled, CancellationToken ct);
}
public interface IStatisticsService
{
    Task<StatisticsSummary> QueryAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct);
}
