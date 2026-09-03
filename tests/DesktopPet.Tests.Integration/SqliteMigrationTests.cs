using DesktopPet.Application.Storage;
using DesktopPet.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

namespace DesktopPet.Tests.Integration;

public sealed class SqliteMigrationTests
{
    private static ISqliteMigration Schema(int version, string sql) =>
        new SqliteMigration(DatabaseKind.App, version, "test-" + version, sql);
    private static IEnumerable<ISqliteMigration> LedgerOnly() =>
        InitialMigrations.Create().Where(item => item.Database != DatabaseKind.App || item.Version == 1);

    [Fact]
    public async Task EmptyDatabasesCreateIndependentLedgersAndAreIdempotent()
    {
        using var env = new TestEnvironment();
        foreach (var database in Enum.GetValues<DatabaseKind>())
        {
            await env.Migrator().MigrateAsync(database, default);
            await env.Migrator().MigrateAsync(database, default);
            await using var connection = await env.Connections.OpenAsync(database, default);
            Assert.Equal(2L, await Scalar(connection, "SELECT COUNT(*) FROM SchemaMigrations;"));
            Assert.Equal(database == DatabaseKind.App ? 7L : 8L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table';"));
        }
        Assert.True(File.Exists(Path.Combine(env.Directories.Data, "app.db")));
        Assert.True(File.Exists(Path.Combine(env.Directories.Data, "ai.db")));
        Assert.Empty(Directory.GetFiles(env.Directories.Backups));
    }

    [Fact]
    public async Task UpgradePreservesDataAndMakesRestorableWalAwareBackup()
    {
        using var env = new TestEnvironment();
        var old = LedgerOnly().Append(Schema(2, "CREATE TABLE TestValues (Value TEXT NOT NULL);"));
        await env.Migrator(old).MigrateAsync(DatabaseKind.App, default);
        await using (var connection = await env.Connections.OpenAsync(DatabaseKind.App, default))
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO TestValues VALUES ('preserved');";
            await insert.ExecuteNonQueryAsync();
            var newer = old.Append(Schema(3, "ALTER TABLE TestValues ADD COLUMN Extra INTEGER NOT NULL DEFAULT 0;"));
            await env.Migrator(newer).MigrateAsync(DatabaseKind.App, default);
        }
        await using (var upgraded = await env.Connections.OpenAsync(DatabaseKind.App, default))
        {
            Assert.Equal("preserved", await Scalar(upgraded, "SELECT Value FROM TestValues;"));
            Assert.Equal(3L, await Scalar(upgraded, "SELECT MAX(Version) FROM SchemaMigrations;"));
        }
        var backupPath = Directory.GetFiles(env.Directories.Backups).OrderBy(File.GetLastWriteTimeUtc).Last();
        await using var backup = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
        await backup.OpenAsync();
        Assert.Equal("preserved", await Scalar(backup, "SELECT Value FROM TestValues;"));
        Assert.Equal(2L, await Scalar(backup, "SELECT MAX(Version) FROM SchemaMigrations;"));
    }

    [Fact]
    public async Task FailedUpgradeRollsBackEveryPendingMigrationAndHistory()
    {
        using var env = new TestEnvironment();
        await env.Migrator().MigrateAsync(DatabaseKind.App, default);
        var pending = InitialMigrations.Create()
            .Append(Schema(3, "CREATE TABLE PendingWork (Value TEXT);"))
            .Append(Schema(4, "INSERT INTO MissingTable VALUES (1);"));
        await Assert.ThrowsAsync<SqliteException>(() => env.Migrator(pending).MigrateAsync(DatabaseKind.App, default));
        await using var connection = await env.Connections.OpenAsync(DatabaseKind.App, default);
        Assert.Equal(2L, await Scalar(connection, "SELECT MAX(Version) FROM SchemaMigrations;"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name='PendingWork';"));
        Assert.Single(Directory.GetFiles(env.Directories.Backups));
        await env.Migrator().MigrateAsync(DatabaseKind.App, default);
    }

    [Fact]
    public async Task FailedInitialCreationLeavesNoPartialLedger()
    {
        using var env = new TestEnvironment();
        var broken = new[] { Schema(1, InitialMigrations.HistorySql + " INSERT INTO MissingTable VALUES (1);") };
        await Assert.ThrowsAsync<SqliteException>(() => env.Migrator(broken).MigrateAsync(DatabaseKind.App, default));
        await using var connection = await env.Connections.OpenAsync(DatabaseKind.App, default);
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE type='table';"));
    }

    [Fact]
    public async Task ChangedOrNewerHistoryCannotBeSilentlyDowngraded()
    {
        using var env = new TestEnvironment();
        await env.Migrator().MigrateAsync(DatabaseKind.App, default);
        var changed = new[] { new SqliteMigration(DatabaseKind.App, 1, "schema-history", InitialMigrations.HistorySql + "\n") };
        await Assert.ThrowsAsync<MigrationHistoryException>(() => env.Migrator(changed).MigrateAsync(DatabaseKind.App, default));
        var newer = InitialMigrations.Create().Append(Schema(3, "CREATE TABLE FutureData (Id INTEGER);"));
        await env.Migrator(newer).MigrateAsync(DatabaseKind.App, default);
        await Assert.ThrowsAsync<MigrationHistoryException>(() => env.Migrator().MigrateAsync(DatabaseKind.App, default));
    }

    [Fact]
    public async Task ExistingUnversionedDatabaseIsNeverAdoptedOrOverwritten()
    {
        using var env = new TestEnvironment();
        await using var connection = await env.Connections.OpenAsync(DatabaseKind.App, default);
        using var create = connection.CreateCommand();
        create.CommandText = "CREATE TABLE UserOwned (Value TEXT); INSERT INTO UserOwned VALUES ('keep');";
        await create.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<MigrationHistoryException>(() => env.Migrator().MigrateAsync(DatabaseKind.App, default));
        Assert.Equal("keep", await Scalar(connection, "SELECT Value FROM UserOwned;"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name='SchemaMigrations';"));
    }

    [Fact]
    public async Task GappedDefinitionsAreRejectedBeforeWriting()
    {
        using var env = new TestEnvironment();
        var invalid = InitialMigrations.Create().Append(Schema(4, "CREATE TABLE Invalid (Id INTEGER);"));
        await Assert.ThrowsAsync<MigrationHistoryException>(() => env.Migrator(invalid).MigrateAsync(DatabaseKind.App, default));
        Assert.False(File.Exists(Path.Combine(env.Directories.Data, "app.db")));
    }

    [Fact]
    public async Task ConcurrentMigratorIsRejectedByFileLease()
    {
        using var env = new TestEnvironment();
        await using var lease = new FileStream(Path.Combine(env.Directories.Data, "App.migration.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await Assert.ThrowsAsync<IOException>(() => env.Migrator().MigrateAsync(DatabaseKind.App, default));
    }

    [Fact]
    public async Task CancellationDuringUpgradeRollsBackPendingDdl()
    {
        using var env = new TestEnvironment();
        using var cancellation = new CancellationTokenSource();
        await env.Migrator().MigrateAsync(DatabaseKind.App, default);
        var pending = InitialMigrations.Create().Append(Schema(3, "CREATE TABLE PendingWork (Id INTEGER);"))
            .Append(new CancellingMigration(cancellation));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            env.Migrator(pending).MigrateAsync(DatabaseKind.App, cancellation.Token));
        await using var connection = await env.Connections.OpenAsync(DatabaseKind.App, default);
        Assert.Equal(2L, await Scalar(connection, "SELECT MAX(Version) FROM SchemaMigrations;"));
        Assert.Equal(0L, await Scalar(connection, "SELECT COUNT(*) FROM sqlite_master WHERE name='PendingWork';"));
    }

    private sealed class CancellingMigration(CancellationTokenSource cancellation) : ISqliteMigration
    {
        private int _reads;
        public DatabaseKind Database => DatabaseKind.App;
        public int Version => 4;
        public string Name => "cancel";
        public string Checksum => "cancel-test";
        public string Sql
        {
            get
            {
                // The first read validates the catalog; cancel on execution after migration 3 ran.
                if (++_reads > 1) cancellation.Cancel();
                return "CREATE TABLE CancelledWork (Id INTEGER);";
            }
        }
    }
    private static async Task<object?> Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }
}
