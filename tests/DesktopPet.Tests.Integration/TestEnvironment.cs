using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Infrastructure.Diagnostics;
using DesktopPet.Infrastructure.Persistence;
using DesktopPet.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

internal sealed class TestEnvironment : IDisposable
{
    private readonly string _testParent = Path.Combine(Path.GetTempPath(), "DesktopPet.Phase0.Tests");
    public TestEnvironment()
    {
        Directories = new AppDataDirectories(Path.Combine(_testParent, Guid.NewGuid().ToString("N")));
        Directories.EnsureCreated();
        Connections = new SqliteConnectionFactory(Directories);
        Logger = new RollingFileAppLogger(Directories, Options.Create(new AppSettings()));
    }
    public AppDataDirectories Directories { get; }
    public SqliteConnectionFactory Connections { get; }
    public IAppLogger Logger { get; }
    public SqliteDatabaseMigrator Migrator(IEnumerable<ISqliteMigration>? migrations = null) =>
        new(Connections, migrations ?? InitialMigrations.Create(), Directories, TimeProvider.System, Logger);
    public void Dispose()
    {
        var full = Path.GetFullPath(Directories.Root);
        if (!full.StartsWith(Path.GetFullPath(_testParent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !Guid.TryParseExact(Path.GetFileName(full), "N", out _))
            throw new InvalidOperationException("Refusing to remove a non-test directory.");
        Directory.Delete(full, recursive: true);
    }
}
