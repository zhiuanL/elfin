using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Productivity;

public sealed class TaskService(ITaskRepository repository, ITagRepository tags, TimeProvider clock) : ITaskService
{
    public async Task<FocusTask> CreateAsync(string title, string? description, CancellationToken ct)
    {
        title = Required(title, nameof(title)); var now = clock.GetUtcNow();
        var task = new FocusTask(Guid.NewGuid(), title, Clean(description), FocusTaskStatus.Active, now, now);
        await repository.SaveAsync(task, ct); return task;
    }
    public async Task<FocusTask> UpdateAsync(Guid id, string title, string? description, CancellationToken ct)
    {
        var current = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException("Task not found.");
        var updated = current with { Title = Required(title, nameof(title)), Description = Clean(description), UpdatedAtUtc = clock.GetUtcNow() };
        await repository.SaveAsync(updated, ct); return updated;
    }
    public async Task ArchiveAsync(Guid id, CancellationToken ct)
    {
        var current = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException("Task not found.");
        await repository.SaveAsync(current with { Status = FocusTaskStatus.Archived, UpdatedAtUtc = clock.GetUtcNow() }, ct);
    }
    public Task<IReadOnlyList<FocusTask>> ListAsync(bool includeArchived, CancellationToken ct) => repository.ListAsync(includeArchived, ct);
    public async Task SetTagsAsync(Guid taskId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct)
    {
        if (await repository.GetAsync(taskId, ct) is null) throw new KeyNotFoundException("Task not found.");
        foreach (var id in tagIds.Distinct())
            if (await tags.GetAsync(id, ct) is null) throw new KeyNotFoundException("Tag not found.");
        await repository.SetTagsAsync(taskId, tagIds, ct);
    }
    public Task<IReadOnlyList<Tag>> GetTagsAsync(Guid taskId, CancellationToken ct) => repository.GetTagsAsync(taskId, ct);
    private static string Required(string value, string name)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 200) throw new ArgumentException("Text must be 1-200 characters.", name);
        return value;
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 2000)];
}

public sealed class TagService(ITagRepository repository, TimeProvider clock) : ITagService
{
    public async Task<Tag> CreateAsync(string name, CancellationToken ct)
    {
        var now = clock.GetUtcNow(); var tag = new Tag(Guid.NewGuid(), Required(name), now, now);
        await repository.SaveAsync(tag, ct); return tag;
    }
    public async Task<Tag> RenameAsync(Guid id, string name, CancellationToken ct)
    {
        var current = await repository.GetAsync(id, ct) ?? throw new KeyNotFoundException("Tag not found.");
        var updated = current with { Name = Required(name), UpdatedAtUtc = clock.GetUtcNow() };
        await repository.SaveAsync(updated, ct); return updated;
    }
    public Task DeleteAsync(Guid id, CancellationToken ct) => repository.DeleteAsync(id, ct);
    public Task<IReadOnlyList<Tag>> ListAsync(CancellationToken ct) => repository.ListAsync(ct);
    private static string Required(string value)
    {
        value = value?.Trim() ?? string.Empty;
        if (value.Length is < 1 or > 64) throw new ArgumentException("Tag name must be 1-64 characters.", nameof(value));
        return value;
    }
}
