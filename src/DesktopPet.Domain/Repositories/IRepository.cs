namespace DesktopPet.Domain.Repositories;

// Repository contracts expose domain values, never connections or query text.
public interface IReadRepository<TEntity, in TId> where TEntity : class
{
    Task<TEntity?> FindAsync(TId id, CancellationToken ct);
}
public interface IRepository<TEntity, in TId> : IReadRepository<TEntity, TId> where TEntity : class
{
    Task SaveAsync(TEntity entity, CancellationToken ct);
    Task DeleteAsync(TId id, CancellationToken ct);
}
