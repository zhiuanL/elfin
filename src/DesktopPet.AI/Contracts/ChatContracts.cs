using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Pets;

namespace DesktopPet.AI.Contracts;

public enum AiProviderType { OpenAI, DeepSeek, AzureOpenAI, OpenAICompatible }
public enum CredentialPersistence { Saved, SessionOnly }
public enum ConnectionStatus { Success, Unauthorized, RateLimited, Timeout, NetworkError, InvalidConfiguration, ProviderError, Cancelled }
public enum ChatRole { System, User, Assistant, Tool }
public enum ConversationType { Main, Temporary, Topic }
public enum MessageStatus { Complete, Interrupted, Failed }
public enum MemoryCategory { Preference, Fact, Relationship, Work, General }

public sealed record AiProviderProfile(Guid Id, AiProviderType ProviderType, string DisplayName, Uri? BaseUrl,
    string Model, TimeSpan Timeout, SecretReference? SecretReference, bool IsActive,
    DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record AiConnectionSettings(Guid ProfileId, AiProviderType ProviderType, Uri BaseUrl, string Model,
    TimeSpan Timeout, SecretReference Credential);
public sealed record TestConnectionResult(ConnectionStatus Status, string? ErrorCode = null)
{ public bool Succeeded => Status == ConnectionStatus.Success; }
public sealed record ModelDiscoveryResult(ConnectionStatus Status, IReadOnlyList<string> Models, string? ErrorCode = null)
{ public bool Succeeded => Status == ConnectionStatus.Success; }
public sealed record ModelToolCall(string ToolCallId, string ToolId, string ArgumentsJson);
public sealed record ChatMessage(ChatRole Role, string Content, string? ToolCallId = null,
    IReadOnlyList<ModelToolCall>? ToolCalls = null);
public sealed record ChatRequest(Guid ConversationId, CharacterId CharacterId, AiConnectionSettings Connection,
    IReadOnlyList<ChatMessage> Messages, IReadOnlyList<AiToolDefinition>? Tools = null);
public sealed record ChatDelta(string Text, bool IsComplete = false, IReadOnlyList<ModelToolCall>? ToolCalls = null);
public interface IChatModelProvider
{
    AiProviderType ProviderType { get; }
    Task<TestConnectionResult> TestConnectionAsync(AiConnectionSettings settings, CancellationToken ct);
    Task<ModelDiscoveryResult> ListModelsAsync(AiConnectionSettings settings, CancellationToken ct);
    IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken ct);
}

public sealed record Conversation(Guid Id, CharacterId CharacterId, ConversationType Type, string Title,
    string? OlderSummary, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record ConversationMessage(Guid Id, Guid ConversationId, ChatRole Role, string Content,
    DateTimeOffset CreatedAtUtc, string? Provider, string? Model, int? TokenUsage, MessageStatus Status);
public sealed record AiUsage(Guid Id, Guid ConversationId, Guid? MessageId, string Provider, string Model,
    int? InputTokens, int? OutputTokens, DateTimeOffset CreatedAtUtc);
public sealed record MemoryItem(Guid Id, CharacterId CharacterId, MemoryCategory Category, string Content,
    int Importance, IReadOnlyList<string> Tags, IReadOnlyList<string> Keywords, Guid? SourceMessageId,
    bool IsAuto, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record MemoryDraft(MemoryCategory Category, string Content, int Importance,
    IReadOnlyList<string> Tags, IReadOnlyList<string> Keywords, Guid? SourceMessageId = null, bool IsAuto = false,
    Guid? Id = null);

public interface IAiProviderProfileRepository
{
    Task<IReadOnlyList<AiProviderProfile>> ListAsync(CancellationToken ct);
    Task<AiProviderProfile?> GetAsync(Guid id, CancellationToken ct);
    Task SaveAsync(AiProviderProfile profile, CancellationToken ct);
    Task SetActiveAsync(Guid id, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}
public interface IConversationRepository
{
    Task<Conversation> GetOrCreateMainAsync(CharacterId characterId, CancellationToken ct);
    Task<Conversation> CreateAsync(CharacterId characterId, ConversationType type, string title, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListAsync(CharacterId characterId, CancellationToken ct);
    Task<Conversation?> GetAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(Guid conversationId, CancellationToken ct);
    Task SaveMessageAsync(ConversationMessage message, CancellationToken ct);
    Task SaveUsageAsync(AiUsage usage, CancellationToken ct);
}
public interface IMemoryRepository
{
    Task<IReadOnlyList<MemoryItem>> ListAsync(CharacterId characterId, CancellationToken ct);
    Task SaveAsync(MemoryItem item, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task ClearAsync(CharacterId? characterId, CancellationToken ct);
    Task<bool> GetAutoEnabledAsync(CharacterId characterId, CancellationToken ct);
    Task SetAutoEnabledAsync(CharacterId characterId, bool enabled, CancellationToken ct);
}
public interface IAiCredentialVault
{
    Task<SecretReference> StoreAsync(Guid profileId, ReadOnlyMemory<char> key, CredentialPersistence persistence, CancellationToken ct);
    Task<byte[]?> ReadAsync(SecretReference reference, CancellationToken ct);
    Task DeleteAsync(SecretReference reference, CancellationToken ct);
}
public interface IAiProviderService
{
    Task<IReadOnlyList<AiProviderProfile>> ListAsync(CancellationToken ct);
    Task<AiProviderProfile> SaveAsync(AiProviderProfile profile, ReadOnlyMemory<char> key, CredentialPersistence persistence, CancellationToken ct);
    Task ReplaceKeyAsync(Guid profileId, ReadOnlyMemory<char> key, CredentialPersistence persistence, CancellationToken ct);
    Task<TestConnectionResult> TestAsync(Guid profileId, CancellationToken ct);
    Task<ModelDiscoveryResult> DiscoverModelsAsync(Guid? profileId, AiProviderType providerType, Uri? baseUrl,
        TimeSpan timeout, ReadOnlyMemory<char> key, CancellationToken ct);
    Task SetActiveAsync(Guid profileId, CancellationToken ct);
    Task DeleteAsync(Guid profileId, CancellationToken ct);
}

public static class AiProviderDefaults
{
    public static string SuggestedBaseUrl(AiProviderType type) => type switch
    {
        AiProviderType.OpenAI => "https://api.openai.com/v1/",
        AiProviderType.DeepSeek => "https://api.deepseek.com/",
        AiProviderType.AzureOpenAI => "https://your-resource-name.openai.azure.com/",
        AiProviderType.OpenAICompatible => "https://your-provider.example/v1/",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    public static bool IsPlaceholder(Uri value) => value.Host.Equals("your-resource-name.openai.azure.com", StringComparison.OrdinalIgnoreCase)
        || value.Host.Equals("your-provider.example", StringComparison.OrdinalIgnoreCase);
}
public interface IMemoryService
{
    Task<IReadOnlyList<MemoryItem>> ListAsync(CharacterId characterId, CancellationToken ct);
    Task<IReadOnlyList<MemoryItem>> FindAsync(CharacterId characterId, string query, int limit, CancellationToken ct);
    Task<MemoryItem> SaveAsync(CharacterId characterId, MemoryDraft draft, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
    Task ClearCharacterAsync(CharacterId characterId, CancellationToken ct);
    Task ClearAllAsync(CancellationToken ct);
    Task<bool> GetAutoEnabledAsync(CharacterId characterId, CancellationToken ct);
    Task SetAutoEnabledAsync(CharacterId characterId, bool enabled, CancellationToken ct);
    Task<bool> TrySaveAutomaticAsync(CharacterId characterId, string candidate, Guid? sourceMessageId, CancellationToken ct);
}
public interface ICharacterPersonaSource { Task<string?> GetPersonaAsync(CharacterId characterId, CancellationToken ct); }
public interface IAiContextBuilder
{
    Task<IReadOnlyList<ChatMessage>> BuildAsync(Conversation conversation, string currentUserMessage, CancellationToken ct);
}
public sealed record AiTurnDelta(string Text, bool IsComplete, MessageStatus Status);
public interface IAiChatService
{
    Task<Conversation> GetMainAsync(CharacterId characterId, CancellationToken ct);
    Task<Conversation> CreateAsync(CharacterId characterId, ConversationType type, string title, CancellationToken ct);
    Task<IReadOnlyList<Conversation>> ListAsync(CharacterId characterId, CancellationToken ct);
    Task<IReadOnlyList<ConversationMessage>> MessagesAsync(Guid conversationId, CancellationToken ct);
    IAsyncEnumerable<AiTurnDelta> SendAsync(Guid conversationId, string text, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
public sealed record PetResponseHint(string? EmotionHint, string? AnimationSemantic, string? TtsPreference);
public sealed record InterpretedResponse(string DisplayText, PetResponseHint? Hint);
public interface IResponseInterpreter
{
    InterpretedResponse Interpret(string response);
    Task ApplyAsync(PetResponseHint? hint, CancellationToken ct);
}
