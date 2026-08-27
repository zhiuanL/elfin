using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Pets;

namespace DesktopPet.AI.Contracts;

// Credentials are opaque references. Providers resolve secrets only at their execution boundary.
public sealed record AiConnectionSettings(string ProviderId, Uri BaseUrl, string Model, SecretReference? Credential);
public enum ConnectionStatus { Connected, Unauthorized, Unavailable }
public sealed record TestConnectionResult(ConnectionStatus Status, string? ErrorCode);
public enum ChatRole { System, User, Assistant, Tool }
public sealed record ChatMessage(ChatRole Role, string Content);
public sealed record ChatRequest(Guid ConversationId, CharacterId CharacterId, AiConnectionSettings Connection,
    IReadOnlyList<ChatMessage> Messages);
public sealed record ChatDelta(string Text, bool IsComplete);
public interface IChatModelProvider
{
    string ProviderId { get; }
    Task<TestConnectionResult> TestConnectionAsync(AiConnectionSettings settings, CancellationToken ct);
    IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken ct);
}
public enum ConversationType { Main, Temporary, Topic }
public sealed record Conversation(Guid Id, CharacterId CharacterId, ConversationType Type,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record MemoryItem(Guid Id, CharacterId CharacterId, string Content,
    IReadOnlyList<string> Tags, DateTimeOffset UpdatedAtUtc);
public interface IMemoryService
{
    Task<IReadOnlyList<MemoryItem>> FindAsync(CharacterId characterId, string query, CancellationToken ct);
    Task SaveAsync(MemoryItem item, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
