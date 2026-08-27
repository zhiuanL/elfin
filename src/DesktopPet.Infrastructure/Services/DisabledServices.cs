using DesktopPet.Application.Contracts;

namespace DesktopPet.Infrastructure.Services;

public sealed class NoOpUpdateService : IUpdateService
{
    public Task<OptionalServiceStatus> CheckAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(OptionalServiceStatus.Disabled);
    }
}
public sealed class NoOpSyncService : ISyncService
{
    public Task<OptionalServiceStatus> SyncAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(OptionalServiceStatus.Disabled);
    }
}
public sealed class NoOpCrashReportingService : ICrashReportingService
{
    public Task ReportAsync(CrashReport report, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }
}
