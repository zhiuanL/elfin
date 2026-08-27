using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Storage;

namespace DesktopPet.Application.Startup;

public sealed record StartupResult(SettingsLoadStatus SettingsStatus, bool AiStorageAvailable, AppFailure? AiFailure);
public interface IRecoveryCoordinator
{
    Task<StartupResult> InitializeAsync(CancellationToken ct);
}
public sealed class RecoveryCoordinator(IAppDataDirectories directories, IDatabaseMigrator migrator,
    ISettingsService settings, IExceptionHandler exceptions) : IRecoveryCoordinator
{
    public async Task<StartupResult> InitializeAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        directories.EnsureCreated();
        await migrator.MigrateAsync(DatabaseKind.App, ct);
        AppFailure? aiFailure = null;
        try
        {
            await migrator.MigrateAsync(DatabaseKind.Ai, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // AI storage is optional. Never use a database whose migration failed.
            aiFailure = exceptions.Report(exception, ErrorCode.AiStorageUnavailable, ErrorOrigin.AiStorage);
        }
        var result = await settings.LoadAsync(ct);
        // Pet/timer/reminder recovery hooks are introduced in their respective phases.
        return new(result.Status, aiFailure is null, aiFailure);
    }
}
