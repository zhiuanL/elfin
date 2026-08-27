using System.Globalization;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Storage;
using Microsoft.Data.Sqlite;

namespace DesktopPet.Infrastructure.Persistence;

public sealed class SqliteDatabaseMigrator(ISqliteConnectionFactory connections,
    IEnumerable<ISqliteMigration> migrations, IAppDataDirectories directories,
    TimeProvider timeProvider, IAppLogger logger) : IDatabaseMigrator
{
    private readonly ISqliteMigration[] _migrations = migrations.ToArray();

    public async Task MigrateAsync(DatabaseKind database, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(database)) throw new ArgumentOutOfRangeException(nameof(database));
        var ordered = _migrations.Where(m => m.Database == database).OrderBy(m => m.Version).ToArray();
        ValidateDefinitions(ordered);
        directories.EnsureCreated();
        // File lease serializes upgrades across app processes; contention fails safely, never spins.
        await using var lease = new FileStream(Path.Combine(directories.Data, database + ".migration.lock"),
            FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        await using var connection = await connections.OpenAsync(database, ct);
        var history = await ReadHistoryAsync(connection, ct);
        ValidateHistory(ordered, history);
        var pending = ordered.Skip(history.Count).ToArray();
        if (pending.Length == 0) return;
        if (history.Count > 0) await BackupAsync(connection, database, ct);

        // All pending DDL and history records commit together. Dispose rolls back on any failure/cancellation.
        using var transaction = connection.BeginTransaction(deferred: false);
        foreach (var migration in pending)
        {
            ct.ThrowIfCancellationRequested();
            using var apply = connection.CreateCommand();
            apply.Transaction = transaction;
            apply.CommandText = migration.Sql;
            await apply.ExecuteNonQueryAsync(ct);
            using var record = connection.CreateCommand();
            record.Transaction = transaction;
            record.CommandText = """
                INSERT INTO SchemaMigrations (Version, Name, Checksum, AppliedAtUtc)
                VALUES ($version, $name, $checksum, $utc);
                """;
            record.Parameters.AddWithValue("$version", migration.Version);
            record.Parameters.AddWithValue("$name", migration.Name);
            record.Parameters.AddWithValue("$checksum", migration.Checksum);
            record.Parameters.AddWithValue("$utc", timeProvider.GetUtcNow().ToString("O", CultureInfo.InvariantCulture));
            await record.ExecuteNonQueryAsync(ct);
        }
        ct.ThrowIfCancellationRequested();
        await transaction.CommitAsync(ct);
        logger.Write(new(AppEvent.MigrationApplied, timeProvider.GetUtcNow()));
    }

    private static void ValidateDefinitions(ISqliteMigration[] migrations)
    {
        if (migrations.Length == 0) throw new MigrationHistoryException("No migrations registered.");
        for (var i = 0; i < migrations.Length; i++)
            if (migrations[i].Version != i + 1 || string.IsNullOrWhiteSpace(migrations[i].Name) ||
                string.IsNullOrWhiteSpace(migrations[i].Sql))
                throw new MigrationHistoryException("Migrations must be nonempty, unique and consecutive starting at 1.");
    }

    private static void ValidateHistory(ISqliteMigration[] definitions, List<MigrationRecord> history)
    {
        if (history.Count > definitions.Length)
            throw new MigrationHistoryException("Database is newer than this application. Downgrades are not supported.");
        for (var i = 0; i < history.Count; i++)
        {
            var applied = history[i];
            if (applied.Version != definitions[i].Version || applied.Name != definitions[i].Name ||
                applied.Checksum != definitions[i].Checksum)
                throw new MigrationHistoryException("Migration history has gaps or changed definitions. Restore a compatible version.");
        }
    }

    private static async Task<List<MigrationRecord>> ReadHistoryAsync(SqliteConnection connection, CancellationToken ct)
    {
        using var exists = connection.CreateCommand();
        exists.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='SchemaMigrations';";
        if (Convert.ToInt32(await exists.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) == 0)
        {
            using var tables = connection.CreateCommand();
            tables.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
            if (Convert.ToInt32(await tables.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0)
                throw new MigrationHistoryException("Existing database has no migration ledger. Automatic adoption is unsafe.");
            return [];
        }
        using var query = connection.CreateCommand();
        query.CommandText = "SELECT Version, Name, Checksum FROM SchemaMigrations ORDER BY Version;";
        await using var reader = await query.ExecuteReaderAsync(ct);
        var result = new List<MigrationRecord>();
        while (await reader.ReadAsync(ct)) result.Add(new(reader.GetInt32(0), reader.GetString(1), reader.GetString(2)));
        return result;
    }

    private async Task BackupAsync(SqliteConnection source, DatabaseKind database, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var file = $"{database.ToString().ToLowerInvariant()}-before-migration-{timeProvider.GetUtcNow():yyyyMMddHHmmss}-{Guid.NewGuid():N}.db";
        await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directories.Backups, file), Pooling = false
        }.ToString());
        await backup.OpenAsync(ct);
        // SQLite online backup includes WAL content; a file copy of app.db would not.
        source.BackupDatabase(backup);
        ct.ThrowIfCancellationRequested();
    }

    private sealed record MigrationRecord(int Version, string Name, string Checksum);
}
