using DesktopPet.Application.Storage;
using Microsoft.Data.Sqlite;

namespace DesktopPet.Infrastructure.Persistence;

public interface ISqliteConnectionFactory
{
    Task<SqliteConnection> OpenAsync(DatabaseKind database, CancellationToken ct);
}
public sealed class SqliteConnectionFactory(IAppDataDirectories directories) : ISqliteConnectionFactory
{
    public async Task<SqliteConnection> OpenAsync(DatabaseKind database, CancellationToken ct)
    {
        var name = database switch
        {
            DatabaseKind.App => "app.db",
            DatabaseKind.Ai => "ai.db",
            _ => throw new ArgumentOutOfRangeException(nameof(database))
        };
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directories.Data, name),
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            Pooling = false,
            DefaultTimeout = 5
        }.ToString());
        try
        {
            await connection.OpenAsync(ct);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=FULL;";
            await command.ExecuteNonQueryAsync(ct);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}
