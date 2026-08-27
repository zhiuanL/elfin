using DesktopPet.Application.Storage;
using DesktopPet.Domain.Repositories;
using Microsoft.Data.Sqlite;

namespace DesktopPet.Infrastructure.Persistence;

// Transaction/use-case semantics remain in concrete repositories; no generic SQL string API leaks to callers.
public abstract class SqliteRepository<TEntity, TId>(ISqliteConnectionFactory connections, DatabaseKind database)
    : IRepository<TEntity, TId> where TEntity : class
{
    protected Task<SqliteConnection> OpenAsync(CancellationToken ct) => connections.OpenAsync(database, ct);
    public abstract Task<TEntity?> FindAsync(TId id, CancellationToken ct);
    public abstract Task SaveAsync(TEntity entity, CancellationToken ct);
    public abstract Task DeleteAsync(TId id, CancellationToken ct);
}
