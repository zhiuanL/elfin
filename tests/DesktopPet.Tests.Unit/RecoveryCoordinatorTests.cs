using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Startup;
using DesktopPet.Application.Storage;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Configuration;

namespace DesktopPet.Tests.Unit;

public sealed class RecoveryCoordinatorTests
{
    [Fact]
    public async Task StartupInitializesBothDatabasesAndConfiguration()
    {
        var directories = new MemoryDirectories();
        var migrator = new TestMigrator();
        var settings = new TestSettingsService();
        var coordinator = new RecoveryCoordinator(directories, migrator, settings,
            new ExceptionHandler(new RecordingLogger(), TimeProvider.System));
        var result = await coordinator.InitializeAsync(default);
        Assert.True(result.AiStorageAvailable);
        Assert.Null(result.AiFailure);
        Assert.Equal([DatabaseKind.App, DatabaseKind.Ai], migrator.Calls);
        Assert.Equal(1, directories.EnsureCount);
        Assert.Equal(1, settings.LoadCount);
    }
    [Fact]
    public async Task AppMigrationFailureStopsBeforeConfigurationOrAi()
    {
        var settings = new TestSettingsService();
        var migrator = new TestMigrator((_, _) => throw new InvalidDataException());
        var coordinator = new RecoveryCoordinator(new MemoryDirectories(), migrator, settings,
            new ExceptionHandler(new RecordingLogger(), TimeProvider.System));
        await Assert.ThrowsAsync<InvalidDataException>(() => coordinator.InitializeAsync(default));
        Assert.Equal(DatabaseKind.App, Assert.Single(migrator.Calls));
        Assert.Equal(0, settings.LoadCount);
    }
    [Fact]
    public async Task AiMigrationFailureDoesNotStopOfflineCore()
    {
        var logger = new RecordingLogger();
        var settings = new TestSettingsService();
        var migrator = new TestMigrator((database, _) => database == DatabaseKind.Ai
            ? Task.FromException(new InvalidDataException()) : Task.CompletedTask);
        var coordinator = new RecoveryCoordinator(new MemoryDirectories(), migrator, settings,
            new ExceptionHandler(logger, TimeProvider.System));
        var result = await coordinator.InitializeAsync(default);
        Assert.False(result.AiStorageAvailable);
        Assert.Equal(ErrorCode.AiStorageUnavailable, result.AiFailure?.Code);
        Assert.Single(logger.Entries);
        Assert.Equal(1, settings.LoadCount);
    }
    [Fact]
    public async Task ShutdownCancellationIsNotConvertedToAiFailure()
    {
        using var cancellation = new CancellationTokenSource();
        var logger = new RecordingLogger();
        var migrator = new TestMigrator((database, ct) =>
        {
            if (database == DatabaseKind.Ai) cancellation.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        var coordinator = new RecoveryCoordinator(new MemoryDirectories(), migrator, new TestSettingsService(),
            new ExceptionHandler(logger, TimeProvider.System));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => coordinator.InitializeAsync(cancellation.Token));
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task ProductivityRecoveryRunsAfterDatabasesAndSettings()
    {
        var order = new List<string>();
        var migrator = new TestMigrator((database, _) =>
        {
            order.Add(database == DatabaseKind.App ? "app" : "ai");
            return Task.CompletedTask;
        });
        var settings = new OrderedSettings(order);
        var productivity = new OrderedProductivityRecovery(order);
        var coordinator = new RecoveryCoordinator(new MemoryDirectories(), migrator, settings,
            new ExceptionHandler(new RecordingLogger(), TimeProvider.System), productivity);
        await coordinator.InitializeAsync(default);
        Assert.Equal(["app", "ai", "settings", "productivity"], order);
    }

    private sealed class OrderedSettings(List<string> order) : ISettingsService
    {
        public AppSettings Current { get; private set; } = new();
        public Task<SettingsLoadResult> LoadAsync(CancellationToken ct)
        {
            order.Add("settings");
            return Task.FromResult(new SettingsLoadResult(Current, SettingsLoadStatus.Loaded));
        }
        public Task SaveAsync(AppSettings settings, CancellationToken ct) { Current = settings; return Task.CompletedTask; }
        public Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct) => SaveAsync(update(Current), ct);
    }

    private sealed class OrderedProductivityRecovery(List<string> order) : IProductivityRecoveryService
    {
        public Task RecoverAsync(CancellationToken ct) { order.Add("productivity"); return Task.CompletedTask; }
        public Task ReconcileAfterResumeAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
