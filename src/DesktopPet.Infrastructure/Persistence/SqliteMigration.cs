using System.Security.Cryptography;
using System.Text;
using DesktopPet.Application.Storage;

namespace DesktopPet.Infrastructure.Persistence;

public interface ISqliteMigration
{
    DatabaseKind Database { get; }
    int Version { get; }
    string Name { get; }
    string Sql { get; }
    string Checksum { get; }
}
public sealed record SqliteMigration(DatabaseKind Database, int Version, string Name, string Sql) : ISqliteMigration
{
    public string Checksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sql)));
}
public static class InitialMigrations
{
    // Phase 0 establishes only the migration ledger. Business schemas belong to later phases.
    public const string HistorySql = """
        CREATE TABLE SchemaMigrations (
            Version INTEGER NOT NULL PRIMARY KEY CHECK (Version > 0),
            Name TEXT NOT NULL,
            Checksum TEXT NOT NULL,
            AppliedAtUtc TEXT NOT NULL
        );
        """;

    public static IReadOnlyList<ISqliteMigration> Create() =>
    [
        new SqliteMigration(DatabaseKind.App, 1, "schema-history", HistorySql),
        new SqliteMigration(DatabaseKind.Ai, 1, "schema-history", HistorySql)
    ];
}
public sealed class MigrationHistoryException(string message) : InvalidOperationException(message);
