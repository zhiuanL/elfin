using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Storage;
using DesktopPet.Domain.Pets;
using Microsoft.Data.Sqlite;

namespace DesktopPet.Infrastructure.Persistence;

internal static class AiSql
{
    public static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    public static DateTimeOffset Utc(SqliteDataReader reader, int ordinal) => DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    public static Guid Guid(SqliteDataReader reader, int ordinal) => System.Guid.Parse(reader.GetString(ordinal));
    public static byte[] Protect(IDataProtectionService protection, string value, string purpose)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try { return protection.Protect(bytes, purpose); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public static string Unprotect(IDataProtectionService protection, byte[] value, string purpose)
    {
        var bytes = protection.Unprotect(value, purpose);
        try { return Encoding.UTF8.GetString(bytes); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}

public sealed class SqliteAiProviderProfileRepository(ISqliteConnectionFactory connections) : IAiProviderProfileRepository
{
    public async Task<IReadOnlyList<AiProviderProfile>> ListAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand();
        command.CommandText = Select + " ORDER BY IsActive DESC, DisplayName;";
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<AiProviderProfile>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader)); return result;
    }
    public async Task<AiProviderProfile?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand();
        command.CommandText = Select + " WHERE Id=$id;"; command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task SaveAsync(AiProviderProfile profile, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO AiProviderProfiles(Id,ProviderType,DisplayName,BaseUrl,Model,TimeoutSeconds,SecretReference,IsActive,CreatedAtUtc,UpdatedAtUtc)
            VALUES($id,$type,$name,$url,$model,$timeout,$secret,$active,$created,$updated)
            ON CONFLICT(Id) DO UPDATE SET ProviderType=excluded.ProviderType,DisplayName=excluded.DisplayName,
              BaseUrl=excluded.BaseUrl,Model=excluded.Model,TimeoutSeconds=excluded.TimeoutSeconds,
              SecretReference=excluded.SecretReference,UpdatedAtUtc=excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", profile.Id.ToString("D")); command.Parameters.AddWithValue("$type", (int)profile.ProviderType);
        command.Parameters.AddWithValue("$name", profile.DisplayName); command.Parameters.AddWithValue("$url", (object?)profile.BaseUrl?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$model", profile.Model); command.Parameters.AddWithValue("$timeout", (int)profile.Timeout.TotalSeconds);
        command.Parameters.AddWithValue("$secret", (object?)profile.SecretReference?.Value ?? DBNull.Value); command.Parameters.AddWithValue("$active", profile.IsActive);
        command.Parameters.AddWithValue("$created", AiSql.Utc(profile.CreatedAtUtc)); command.Parameters.AddWithValue("$updated", AiSql.Utc(profile.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task SetActiveAsync(Guid id, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var tx = db.BeginTransaction();
        using (var clear = db.CreateCommand()) { clear.Transaction = tx; clear.CommandText = "UPDATE AiProviderProfiles SET IsActive=0;"; await clear.ExecuteNonQueryAsync(ct); }
        using (var set = db.CreateCommand()) { set.Transaction = tx; set.CommandText = "UPDATE AiProviderProfiles SET IsActive=1 WHERE Id=$id;"; set.Parameters.AddWithValue("$id", id.ToString("D")); if (await set.ExecuteNonQueryAsync(ct) != 1) throw new KeyNotFoundException("Provider profile not found."); }
        await tx.CommitAsync(ct);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = "DELETE FROM AiProviderProfiles WHERE Id=$id;"; command.Parameters.AddWithValue("$id", id.ToString("D")); await command.ExecuteNonQueryAsync(ct); }
    private const string Select = "SELECT Id,ProviderType,DisplayName,BaseUrl,Model,TimeoutSeconds,SecretReference,IsActive,CreatedAtUtc,UpdatedAtUtc FROM AiProviderProfiles";
    private static AiProviderProfile Read(SqliteDataReader r) => new(AiSql.Guid(r, 0), (AiProviderType)r.GetInt32(1), r.GetString(2), r.IsDBNull(3) ? null : new(r.GetString(3)), r.GetString(4), TimeSpan.FromSeconds(r.GetInt32(5)), r.IsDBNull(6) ? null : new(r.GetString(6)), r.GetBoolean(7), AiSql.Utc(r, 8), AiSql.Utc(r, 9));
}

public sealed class SqliteConversationRepository(ISqliteConnectionFactory connections, IDataProtectionService protection,
    TimeProvider clock) : IConversationRepository
{
    public async Task<Conversation> GetOrCreateMainAsync(CharacterId characterId, CancellationToken ct)
    {
        var list = await ListAsync(characterId, ct); var existing = list.FirstOrDefault(x => x.Type == ConversationType.Main);
        if (existing is not null) return existing;
        try { return await CreateAsync(characterId, ConversationType.Main, "Main", ct); }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        { return (await ListAsync(characterId, ct)).Single(x => x.Type == ConversationType.Main); }
    }
    public async Task<Conversation> CreateAsync(CharacterId characterId, ConversationType type, string title, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(characterId.Value) || string.IsNullOrWhiteSpace(title) || title.Length > 120) throw new ArgumentException("Invalid conversation.");
        if (type == ConversationType.Main) { var current = (await ListAsync(characterId, ct)).FirstOrDefault(x => x.Type == type); if (current is not null) return current; }
        var now = clock.GetUtcNow(); var item = new Conversation(Guid.NewGuid(), characterId, type, title.Trim(), null, now, now);
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand();
        command.CommandText = "INSERT INTO Conversations(Id,CharacterId,Type,Title,ProtectedOlderSummary,CreatedAtUtc,UpdatedAtUtc) VALUES($id,$character,$type,$title,NULL,$created,$updated);";
        command.Parameters.AddWithValue("$id", item.Id.ToString("D")); command.Parameters.AddWithValue("$character", item.CharacterId.Value); command.Parameters.AddWithValue("$type", (int)item.Type); command.Parameters.AddWithValue("$title", item.Title); command.Parameters.AddWithValue("$created", AiSql.Utc(now)); command.Parameters.AddWithValue("$updated", AiSql.Utc(now)); await command.ExecuteNonQueryAsync(ct); return item;
    }
    public async Task<IReadOnlyList<Conversation>> ListAsync(CharacterId characterId, CancellationToken ct)
    { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = Select + " WHERE CharacterId=$character ORDER BY UpdatedAtUtc DESC;"; command.Parameters.AddWithValue("$character", characterId.Value); await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<Conversation>(); while (await reader.ReadAsync(ct)) result.Add(ReadConversation(reader)); return result; }
    public async Task<Conversation?> GetAsync(Guid id, CancellationToken ct)
    { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = Select + " WHERE Id=$id;"; command.Parameters.AddWithValue("$id", id.ToString("D")); await using var reader = await command.ExecuteReaderAsync(ct); return await reader.ReadAsync(ct) ? ReadConversation(reader) : null; }
    public async Task<IReadOnlyList<ConversationMessage>> ListMessagesAsync(Guid conversationId, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand();
        command.CommandText = "SELECT Id,ConversationId,Role,ProtectedContent,CreatedAtUtc,Provider,Model,TokenUsage,Status FROM Messages WHERE ConversationId=$id ORDER BY CreatedAtUtc,Id;"; command.Parameters.AddWithValue("$id", conversationId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct); var result = new List<ConversationMessage>();
        while (await reader.ReadAsync(ct)) { var id = AiSql.Guid(reader, 0); result.Add(new(id, AiSql.Guid(reader, 1), (ChatRole)reader.GetInt32(2), AiSql.Unprotect(protection, (byte[])reader[3], "ai-message:" + id.ToString("D")), AiSql.Utc(reader, 4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt32(7), (MessageStatus)reader.GetInt32(8))); }
        return result;
    }
    public async Task SaveMessageAsync(ConversationMessage message, CancellationToken ct)
    {
        var encrypted = AiSql.Protect(protection, message.Content, "ai-message:" + message.Id.ToString("D"));
        try { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = """
            INSERT INTO Messages(Id,ConversationId,Role,ProtectedContent,CreatedAtUtc,Provider,Model,TokenUsage,Status)
            VALUES($id,$conversation,$role,$content,$created,$provider,$model,$tokens,$status)
            ON CONFLICT(Id) DO UPDATE SET ProtectedContent=excluded.ProtectedContent,TokenUsage=excluded.TokenUsage,Status=excluded.Status;
            UPDATE Conversations SET UpdatedAtUtc=$created WHERE Id=$conversation;
            """; command.Parameters.AddWithValue("$id", message.Id.ToString("D")); command.Parameters.AddWithValue("$conversation", message.ConversationId.ToString("D")); command.Parameters.AddWithValue("$role", (int)message.Role); command.Parameters.Add("$content", SqliteType.Blob).Value = encrypted; command.Parameters.AddWithValue("$created", AiSql.Utc(message.CreatedAtUtc)); command.Parameters.AddWithValue("$provider", (object?)message.Provider ?? DBNull.Value); command.Parameters.AddWithValue("$model", (object?)message.Model ?? DBNull.Value); command.Parameters.AddWithValue("$tokens", (object?)message.TokenUsage ?? DBNull.Value); command.Parameters.AddWithValue("$status", (int)message.Status); await command.ExecuteNonQueryAsync(ct); }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }
    public async Task SaveUsageAsync(AiUsage usage, CancellationToken ct)
    { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = "INSERT INTO AiUsage(Id,ConversationId,MessageId,Provider,Model,InputTokens,OutputTokens,CreatedAtUtc) VALUES($id,$conversation,$message,$provider,$model,$input,$output,$created);"; command.Parameters.AddWithValue("$id", usage.Id.ToString("D")); command.Parameters.AddWithValue("$conversation", usage.ConversationId.ToString("D")); command.Parameters.AddWithValue("$message", (object?)usage.MessageId?.ToString("D") ?? DBNull.Value); command.Parameters.AddWithValue("$provider", usage.Provider); command.Parameters.AddWithValue("$model", usage.Model); command.Parameters.AddWithValue("$input", (object?)usage.InputTokens ?? DBNull.Value); command.Parameters.AddWithValue("$output", (object?)usage.OutputTokens ?? DBNull.Value); command.Parameters.AddWithValue("$created", AiSql.Utc(usage.CreatedAtUtc)); await command.ExecuteNonQueryAsync(ct); }
    private const string Select = "SELECT Id,CharacterId,Type,Title,ProtectedOlderSummary,CreatedAtUtc,UpdatedAtUtc FROM Conversations";
    private Conversation ReadConversation(SqliteDataReader r) { var id = AiSql.Guid(r, 0); return new(id, new(r.GetString(1)), (ConversationType)r.GetInt32(2), r.GetString(3), r.IsDBNull(4) ? null : AiSql.Unprotect(protection, (byte[])r[4], "ai-summary:" + id.ToString("D")), AiSql.Utc(r, 5), AiSql.Utc(r, 6)); }
}

public sealed class SqliteMemoryRepository(ISqliteConnectionFactory connections, IDataProtectionService protection) : IMemoryRepository
{
    public async Task<IReadOnlyList<MemoryItem>> ListAsync(CharacterId characterId, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct);
        var tags = new Dictionary<Guid, (List<string> tags, List<string> keywords)>();
        using (var tagCommand = db.CreateCommand())
        {
            tagCommand.CommandText = "SELECT t.MemoryId,t.Value,t.Kind FROM MemoryTags t JOIN Memories m ON m.Id=t.MemoryId WHERE m.CharacterId=$character ORDER BY t.Value;";
            tagCommand.Parameters.AddWithValue("$character", characterId.Value); await using var reader = await tagCommand.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) { var id = System.Guid.Parse(reader.GetString(0)); if (!tags.TryGetValue(id, out var pair)) tags[id] = pair = ([], []); (reader.GetInt32(2) == 0 ? pair.tags : pair.keywords).Add(reader.GetString(1)); }
        }
        using var command = db.CreateCommand(); command.CommandText = "SELECT Id,CharacterId,Category,ProtectedContent,Importance,SourceMessageId,IsAuto,CreatedAtUtc,UpdatedAtUtc FROM Memories WHERE CharacterId=$character ORDER BY Importance DESC,UpdatedAtUtc DESC;"; command.Parameters.AddWithValue("$character", characterId.Value);
        await using var memoryReader = await command.ExecuteReaderAsync(ct); var result = new List<MemoryItem>();
        while (await memoryReader.ReadAsync(ct)) { var id = AiSql.Guid(memoryReader, 0); tags.TryGetValue(id, out var values); result.Add(new(id, new(memoryReader.GetString(1)), (MemoryCategory)memoryReader.GetInt32(2), AiSql.Unprotect(protection, (byte[])memoryReader[3], "ai-memory:" + id.ToString("D")), memoryReader.GetInt32(4), values.tags ?? [], values.keywords ?? [], memoryReader.IsDBNull(5) ? null : System.Guid.Parse(memoryReader.GetString(5)), memoryReader.GetBoolean(6), AiSql.Utc(memoryReader, 7), AiSql.Utc(memoryReader, 8))); }
        return result;
    }
    public async Task SaveAsync(MemoryItem item, CancellationToken ct)
    {
        var encrypted = AiSql.Protect(protection, item.Content, "ai-memory:" + item.Id.ToString("D"));
        try
        {
            await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var tx = db.BeginTransaction();
            using (var command = db.CreateCommand()) { command.Transaction = tx; command.CommandText = """
                INSERT INTO Memories(Id,CharacterId,Category,ProtectedContent,Importance,SourceMessageId,IsAuto,CreatedAtUtc,UpdatedAtUtc)
                VALUES($id,$character,$category,$content,$importance,$source,$auto,$created,$updated)
                ON CONFLICT(Id) DO UPDATE SET Category=excluded.Category,ProtectedContent=excluded.ProtectedContent,
                  Importance=excluded.Importance,SourceMessageId=excluded.SourceMessageId,IsAuto=excluded.IsAuto,UpdatedAtUtc=excluded.UpdatedAtUtc;
                """; command.Parameters.AddWithValue("$id", item.Id.ToString("D")); command.Parameters.AddWithValue("$character", item.CharacterId.Value); command.Parameters.AddWithValue("$category", (int)item.Category); command.Parameters.Add("$content", SqliteType.Blob).Value = encrypted; command.Parameters.AddWithValue("$importance", item.Importance); command.Parameters.AddWithValue("$source", (object?)item.SourceMessageId?.ToString("D") ?? DBNull.Value); command.Parameters.AddWithValue("$auto", item.IsAuto); command.Parameters.AddWithValue("$created", AiSql.Utc(item.CreatedAtUtc)); command.Parameters.AddWithValue("$updated", AiSql.Utc(item.UpdatedAtUtc)); await command.ExecuteNonQueryAsync(ct); }
            using (var remove = db.CreateCommand()) { remove.Transaction = tx; remove.CommandText = "DELETE FROM MemoryTags WHERE MemoryId=$id;"; remove.Parameters.AddWithValue("$id", item.Id.ToString("D")); await remove.ExecuteNonQueryAsync(ct); }
            foreach (var value in item.Tags.Select(x => (x, 0)).Concat(item.Keywords.Select(x => (x, 1)))) { using var tag = db.CreateCommand(); tag.Transaction = tx; tag.CommandText = "INSERT INTO MemoryTags(MemoryId,Value,Kind) VALUES($id,$value,$kind);"; tag.Parameters.AddWithValue("$id", item.Id.ToString("D")); tag.Parameters.AddWithValue("$value", value.x); tag.Parameters.AddWithValue("$kind", value.Item2); await tag.ExecuteNonQueryAsync(ct); }
            await tx.CommitAsync(ct);
        }
        finally { CryptographicOperations.ZeroMemory(encrypted); }
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = "DELETE FROM Memories WHERE Id=$id;"; command.Parameters.AddWithValue("$id", id.ToString("D")); await command.ExecuteNonQueryAsync(ct); }
    public async Task ClearAsync(CharacterId? characterId, CancellationToken ct)
    { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = characterId is null ? "DELETE FROM Memories;" : "DELETE FROM Memories WHERE CharacterId=$character;"; if (characterId is not null) command.Parameters.AddWithValue("$character", characterId.Value.Value); await command.ExecuteNonQueryAsync(ct); }
    public async Task<bool> GetAutoEnabledAsync(CharacterId characterId, CancellationToken ct)
    { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = "SELECT AutoMemoryEnabled FROM AiCharacterPreferences WHERE CharacterId=$character;"; command.Parameters.AddWithValue("$character", characterId.Value); var value = await command.ExecuteScalarAsync(ct); return value is not null and not DBNull && Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
    public async Task SetAutoEnabledAsync(CharacterId characterId, bool enabled, CancellationToken ct)
    { await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct); using var command = db.CreateCommand(); command.CommandText = "INSERT INTO AiCharacterPreferences(CharacterId,AutoMemoryEnabled) VALUES($character,$enabled) ON CONFLICT(CharacterId) DO UPDATE SET AutoMemoryEnabled=excluded.AutoMemoryEnabled;"; command.Parameters.AddWithValue("$character", characterId.Value); command.Parameters.AddWithValue("$enabled", enabled); await command.ExecuteNonQueryAsync(ct); }
}

public sealed class SqliteAiToolAuditRepository(ISqliteConnectionFactory connections) : IAiToolAuditRepository
{
    public async Task SaveAsync(AiToolAuditEntry entry, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct);
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO AiToolAudit(Id,TimestampUtc,ConversationId,ToolCallId,ToolId,RiskLevel,
              ParameterSummary,ConfirmationResult,ExecutionStatus,DurationMilliseconds,ErrorCategory)
            VALUES($id,$timestamp,$conversation,$call,$tool,$risk,$summary,$confirmation,$status,$duration,$error);
            """;
        command.Parameters.AddWithValue("$id", entry.Id.ToString("D"));
        command.Parameters.AddWithValue("$timestamp", AiSql.Utc(entry.TimestampUtc));
        command.Parameters.AddWithValue("$conversation", entry.ConversationId.ToString("D"));
        command.Parameters.AddWithValue("$call", entry.ToolCallId);
        command.Parameters.AddWithValue("$tool", entry.ToolId);
        command.Parameters.AddWithValue("$risk", (int)entry.RiskLevel);
        command.Parameters.AddWithValue("$summary", entry.ParameterSummary);
        command.Parameters.AddWithValue("$confirmation", (int)entry.ConfirmationResult);
        command.Parameters.AddWithValue("$status", (int)entry.ExecutionStatus);
        command.Parameters.AddWithValue("$duration", entry.DurationMilliseconds);
        command.Parameters.AddWithValue("$error", (object?)entry.ErrorCategory ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<AiToolAuditEntry>> ListRecentAsync(int limit, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.Ai, ct);
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT Id,TimestampUtc,ConversationId,ToolCallId,ToolId,RiskLevel,ParameterSummary,
              ConfirmationResult,ExecutionStatus,DurationMilliseconds,ErrorCategory
            FROM AiToolAudit ORDER BY TimestampUtc DESC, Id DESC LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<AiToolAuditEntry>();
        while (await reader.ReadAsync(ct))
            result.Add(new(AiSql.Guid(reader, 0), AiSql.Utc(reader, 1), AiSql.Guid(reader, 2), reader.GetString(3),
                reader.GetString(4), (ToolRiskLevel)reader.GetInt32(5), reader.GetString(6),
                (ToolConfirmationResult)reader.GetInt32(7), (ToolExecutionStatus)reader.GetInt32(8),
                reader.GetInt64(9), reader.IsDBNull(10) ? null : reader.GetString(10)));
        return result;
    }
}
