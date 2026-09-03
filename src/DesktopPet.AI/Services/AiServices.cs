using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;

namespace DesktopPet.AI.Services;

public sealed class AiProviderService(IAiProviderProfileRepository profiles, IAiCredentialVault credentials,
    IEnumerable<IChatModelProvider> providers, TimeProvider clock) : IAiProviderService
{
    public Task<IReadOnlyList<AiProviderProfile>> ListAsync(CancellationToken ct) => profiles.ListAsync(ct);
    public async Task<AiProviderProfile> SaveAsync(AiProviderProfile profile, ReadOnlyMemory<char> key,
        CredentialPersistence persistence, CancellationToken ct)
    {
        Validate(profile);
        var now = clock.GetUtcNow();
        var reference = profile.SecretReference;
        if (!key.IsEmpty) reference = await credentials.StoreAsync(profile.Id, key, persistence, ct);
        if (reference is null) throw new ArgumentException("API key is required.");
        var saved = profile with { SecretReference = reference, CreatedAtUtc = profile.CreatedAtUtc == default ? now : profile.CreatedAtUtc, UpdatedAtUtc = now };
        await profiles.SaveAsync(saved, ct); return saved;
    }
    public async Task ReplaceKeyAsync(Guid profileId, ReadOnlyMemory<char> key, CredentialPersistence persistence, CancellationToken ct)
    {
        var profile = await profiles.GetAsync(profileId, ct) ?? throw new KeyNotFoundException("Provider profile not found.");
        if (profile.SecretReference is { } old) await credentials.DeleteAsync(old, ct);
        var reference = await credentials.StoreAsync(profileId, key, persistence, ct);
        await profiles.SaveAsync(profile with { SecretReference = reference, UpdatedAtUtc = clock.GetUtcNow() }, ct);
    }
    public async Task<TestConnectionResult> TestAsync(Guid profileId, CancellationToken ct)
    {
        var profile = await profiles.GetAsync(profileId, ct);
        if (profile?.SecretReference is not { } secret) return new(ConnectionStatus.InvalidConfiguration, "missing_profile_or_key");
        try
        {
            var provider = providers.Single(x => x.ProviderType == profile.ProviderType);
            return await provider.TestConnectionAsync(Connection(profile, secret), ct);
        }
        catch (InvalidOperationException) { return new(ConnectionStatus.InvalidConfiguration, "provider_unavailable"); }
    }
    public async Task<ModelDiscoveryResult> DiscoverModelsAsync(Guid? profileId, AiProviderType providerType,
        Uri? baseUrl, TimeSpan timeout, ReadOnlyMemory<char> key, CancellationToken ct)
    {
        SecretReference? temporary = null;
        try
        {
            SecretReference? secret = null;
            if (!key.IsEmpty && !key.Span.Trim().IsEmpty)
            {
                var temporaryId = Guid.NewGuid();
                temporary = await credentials.StoreAsync(temporaryId, key, CredentialPersistence.SessionOnly, ct);
                secret = temporary;
            }
            else if (profileId is { } id) secret = (await profiles.GetAsync(id, ct))?.SecretReference;
            if (secret is null) return new(ConnectionStatus.InvalidConfiguration, [], "credential_required");
            Uri endpoint;
            try { endpoint = baseUrl ?? DefaultBase(providerType); }
            catch (ArgumentException) { return new(ConnectionStatus.InvalidConfiguration, [], "base_url_required"); }
            var provider = providers.SingleOrDefault(x => x.ProviderType == providerType);
            return provider is null ? new(ConnectionStatus.InvalidConfiguration, [], "provider_unavailable")
                : await provider.ListModelsAsync(new(Guid.Empty, providerType, endpoint, "model-discovery", timeout, secret.Value), ct);
        }
        finally { if (temporary is { } reference) await credentials.DeleteAsync(reference, CancellationToken.None); }
    }
    public Task SetActiveAsync(Guid profileId, CancellationToken ct) => profiles.SetActiveAsync(profileId, ct);
    public async Task DeleteAsync(Guid profileId, CancellationToken ct)
    {
        var profile = await profiles.GetAsync(profileId, ct);
        if (profile?.SecretReference is { } reference) await credentials.DeleteAsync(reference, ct);
        await profiles.DeleteAsync(profileId, ct);
    }
    internal static AiConnectionSettings Connection(AiProviderProfile profile, DesktopPet.Application.Contracts.SecretReference secret) =>
        new(profile.Id, profile.ProviderType, profile.BaseUrl ?? DefaultBase(profile.ProviderType), profile.Model, profile.Timeout, secret);
    public static Uri DefaultBase(AiProviderType type) => type switch
    {
        AiProviderType.OpenAI => new("https://api.openai.com/v1/"),
        AiProviderType.DeepSeek => new("https://api.deepseek.com/"),
        _ => throw new ArgumentException("Base URL is required for this provider.")
    };
    private static void Validate(AiProviderProfile profile)
    {
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.DisplayName) || string.IsNullOrWhiteSpace(profile.Model))
            throw new ArgumentException("Provider name and model are required.");
        if (profile.Timeout < TimeSpan.FromSeconds(1) || profile.Timeout > TimeSpan.FromMinutes(5)) throw new ArgumentOutOfRangeException(nameof(profile));
        if (profile.ProviderType is AiProviderType.AzureOpenAI or AiProviderType.OpenAICompatible && profile.BaseUrl is null)
            throw new ArgumentException("Base URL is required.");
    }
}

public sealed class MemoryService(IMemoryRepository repository, TimeProvider clock) : IMemoryService
{
    private static readonly Regex Sensitive = new(@"(?i)(api[ _-]?key|password|passwd|secret|token|credential)\s*[:=]", RegexOptions.Compiled);
    public Task<IReadOnlyList<MemoryItem>> ListAsync(CharacterId id, CancellationToken ct) => repository.ListAsync(id, ct);
    public async Task<IReadOnlyList<MemoryItem>> FindAsync(CharacterId id, string query, int limit, CancellationToken ct)
    {
        if (limit is < 1 or > 50) throw new ArgumentOutOfRangeException(nameof(limit));
        var terms = query.Split([' ', ',', ';', '，', '；'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var items = await repository.ListAsync(id, ct);
        return items.Select(item => (item, score: Score(item, terms, clock.GetUtcNow())))
            .Where(x => terms.Length == 0 || x.score > 0).OrderByDescending(x => x.score)
            .ThenByDescending(x => x.item.UpdatedAtUtc).Take(limit).Select(x => x.item).ToArray();
    }
    public async Task<MemoryItem> SaveAsync(CharacterId id, MemoryDraft draft, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id.Value) || string.IsNullOrWhiteSpace(draft.Content) || draft.Content.Length > 2000 || draft.Importance is < 1 or > 5)
            throw new ArgumentException("Invalid memory.");
        var now = clock.GetUtcNow();
        var item = new MemoryItem(draft.Id ?? Guid.NewGuid(), id, draft.Category, draft.Content.Trim(), draft.Importance,
            Normalize(draft.Tags), Normalize(draft.Keywords), draft.SourceMessageId, draft.IsAuto, now, now);
        await repository.SaveAsync(item, ct); return item;
    }
    public Task DeleteAsync(Guid id, CancellationToken ct) => repository.DeleteAsync(id, ct);
    public Task ClearCharacterAsync(CharacterId id, CancellationToken ct) => repository.ClearAsync(id, ct);
    public Task ClearAllAsync(CancellationToken ct) => repository.ClearAsync(null, ct);
    public Task<bool> GetAutoEnabledAsync(CharacterId id, CancellationToken ct) => repository.GetAutoEnabledAsync(id, ct);
    public Task SetAutoEnabledAsync(CharacterId id, bool enabled, CancellationToken ct) => repository.SetAutoEnabledAsync(id, enabled, ct);
    public async Task<bool> TrySaveAutomaticAsync(CharacterId id, string candidate, Guid? source, CancellationToken ct)
    {
        if (!await GetAutoEnabledAsync(id, ct) || string.IsNullOrWhiteSpace(candidate) || candidate.Length > 500 || Sensitive.IsMatch(candidate)) return false;
        var normalized = NormalizeText(candidate);
        if ((await repository.ListAsync(id, ct)).Any(x => NormalizeText(x.Content) == normalized)) return false;
        var importance = candidate.Contains("喜欢", StringComparison.OrdinalIgnoreCase) || candidate.Contains("prefer", StringComparison.OrdinalIgnoreCase) ? 4 : 3;
        if (importance < 3) return false;
        await SaveAsync(id, new(MemoryCategory.General, candidate, importance, [], Keywords(candidate), source, true), ct);
        return true;
    }
    private static double Score(MemoryItem item, string[] terms, DateTimeOffset now)
    {
        var searchable = string.Join(' ', new[] { item.Content, item.Category.ToString() }.Concat(item.Tags).Concat(item.Keywords));
        var matches = terms.Count(term => searchable.Contains(term, StringComparison.OrdinalIgnoreCase));
        return matches * 10 + item.Importance * 2 + 1d / (1 + Math.Max(0, (now - item.UpdatedAtUtc).TotalDays));
    }
    private static string[] Normalize(IEnumerable<string> values) => values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim().ToLowerInvariant()).Distinct().Take(20).ToArray();
    private static string NormalizeText(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c))).ToLowerInvariant();
    private static string[] Keywords(string value) => value.Split([' ', ',', '.', '，', '。'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => x.Length > 1).Take(8).ToArray();
}

public sealed class AiContextBuilder(ICharacterPersonaSource personas, IMemoryService memories,
    IConversationRepository conversations) : IAiContextBuilder
{
    public const int CharacterBudget = 12_000;
    public const int RecentMessageLimit = 20;
    public async Task<IReadOnlyList<ChatMessage>> BuildAsync(Conversation conversation, string current, CancellationToken ct)
    {
        var result = new List<ChatMessage>();
        var persona = await personas.GetPersonaAsync(conversation.CharacterId, ct);
        Add(result, ChatRole.System, string.IsNullOrWhiteSpace(persona) ? "You are a friendly, concise desktop companion. Never claim to control files, programs, or the operating system." : persona, CharacterBudget);
        var relevant = await memories.FindAsync(conversation.CharacterId, current, 8, ct);
        if (relevant.Count > 0) Add(result, ChatRole.System, "Relevant user memories:\n" + string.Join("\n", relevant.Select(x => $"- [{x.Category}] {x.Content}")), CharacterBudget);
        if (!string.IsNullOrWhiteSpace(conversation.OlderSummary)) Add(result, ChatRole.System, "Earlier conversation summary:\n" + conversation.OlderSummary, CharacterBudget);
        var history = (await conversations.ListMessagesAsync(conversation.Id, ct)).TakeLast(RecentMessageLimit);
        foreach (var message in history) Add(result, message.Role, message.Content, CharacterBudget - current.Length);
        while (result.Sum(x => x.Content.Length) + current.Length > CharacterBudget && result.Count > 1) result.RemoveAt(1);
        Add(result, ChatRole.User, current, CharacterBudget);
        return result;
    }
    private static void Add(List<ChatMessage> result, ChatRole role, string content, int budget)
    { if (!string.IsNullOrWhiteSpace(content) && result.Sum(x => x.Content.Length) + content.Length <= budget) result.Add(new(role, content)); }
}
