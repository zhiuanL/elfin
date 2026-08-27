using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Storage;

namespace DesktopPet.Tests.Unit;

internal sealed class RecordingLogger : IAppLogger
{
    public List<AppLogEntry> Entries { get; } = [];
    public LogOptions Policy { get; private set; } = new();
    public void Configure(LogOptions options) => Policy = options;
    public void Write(AppLogEntry entry) => Entries.Add(entry);
}
internal sealed class TestSettingsService : ISettingsService
{
    public AppSettings Current { get; set; } = new();
    public int LoadCount { get; private set; }
    public bool FailWrites { get; set; }
    public Task<SettingsLoadResult> LoadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        LoadCount++;
        return Task.FromResult(new SettingsLoadResult(Current, SettingsLoadStatus.Loaded));
    }
    public Task SaveAsync(AppSettings settings, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (FailWrites) throw new IOException("Test storage unavailable.");
        Current = settings;
        return Task.CompletedTask;
    }
    public Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct) => SaveAsync(update(Current), ct);
}
internal sealed class MemoryDirectories : IAppDataDirectories
{
    public int EnsureCount { get; private set; }
    public string Root => "unused";
    public string Config => Root;
    public string Data => Root;
    public string Characters => Root;
    public string Cache => Root;
    public string Logs => Root;
    public string Backups => Root;
    public void EnsureCreated() => EnsureCount++;
}
internal sealed class TestMigrator(Func<DatabaseKind, CancellationToken, Task>? behavior = null) : IDatabaseMigrator
{
    public List<DatabaseKind> Calls { get; } = [];
    public Task MigrateAsync(DatabaseKind database, CancellationToken ct)
    {
        Calls.Add(database);
        return behavior?.Invoke(database, ct) ?? Task.CompletedTask;
    }
}
