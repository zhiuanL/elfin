using System.Text;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Storage;
using DesktopPet.Infrastructure.Persistence;
using DesktopPet.Infrastructure.Security;
using DesktopPet.Windows.Security;
using DesktopPet.Domain.Pets;
using Microsoft.Data.Sqlite;

namespace DesktopPet.Tests.Integration;

public sealed class PhaseSevenAiPersistenceTests
{
    [Fact]
    public async Task AiDatabaseCreatesRequiredSchemaAndEnforcesOneMainPerCharacter()
    {
        using var env = new TestEnvironment(); await env.Migrator().MigrateAsync(DatabaseKind.Ai, default);
        var protection = new DpapiDataProtectionService(); var repository = new SqliteConversationRepository(env.Connections, protection, TimeProvider.System); var character = new CharacterId("dev-standard");
        var main = await repository.GetOrCreateMainAsync(character, default); var same = await repository.GetOrCreateMainAsync(character, default);
        var temporary = await repository.CreateAsync(character, ConversationType.Temporary, "temp", default); var topic = await repository.CreateAsync(character, ConversationType.Topic, "topic", default);
        Assert.Equal(main.Id, same.Id); Assert.Equal(3, (await repository.ListAsync(character, default)).Select(x => x.Type).Distinct().Count());
        await using var db = await env.Connections.OpenAsync(DatabaseKind.Ai, default);
        foreach (var table in new[] { "AiProviderProfiles", "Conversations", "Messages", "Memories", "MemoryTags", "AiUsage" }) Assert.Equal(1L, await Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='" + table + "';"));
        Assert.NotEqual(temporary.Id, topic.Id);
    }

    [Fact]
    public async Task MessageAndMemoryRoundTripButPlaintextNeverAppearsInDatabase()
    {
        using var env = new TestEnvironment(); await env.Migrator().MigrateAsync(DatabaseKind.Ai, default); var protection = new DpapiDataProtectionService();
        var conversations = new SqliteConversationRepository(env.Connections, protection, TimeProvider.System); var memories = new SqliteMemoryRepository(env.Connections, protection); var character = new CharacterId("dev-standard"); var conversation = await conversations.GetOrCreateMainAsync(character, default);
        const string messageSecret = "unique-private-message-70919"; const string memorySecret = "unique-private-memory-88421";
        var first = new ConversationMessage(Guid.NewGuid(), conversation.Id, ChatRole.User, messageSecret, DateTimeOffset.UtcNow.AddSeconds(-1), "OpenAI", "test", null, MessageStatus.Complete);
        var second = new ConversationMessage(Guid.NewGuid(), conversation.Id, ChatRole.Assistant, "reply", DateTimeOffset.UtcNow, "DeepSeek", "other", 4, MessageStatus.Interrupted);
        await conversations.SaveMessageAsync(first, default); await conversations.SaveMessageAsync(second, default);
        await memories.SaveAsync(new(Guid.NewGuid(), character, MemoryCategory.Preference, memorySecret, 5, ["private"], ["memory"], first.Id, false, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), default);
        var loaded = await conversations.ListMessagesAsync(conversation.Id, default); Assert.Equal([messageSecret, "reply"], loaded.Select(x => x.Content).ToArray()); Assert.Equal(MessageStatus.Interrupted, loaded[1].Status);
        var memory = Assert.Single(await memories.ListAsync(character, default)); Assert.Equal(memorySecret, memory.Content); Assert.Equal(["private"], memory.Tags); Assert.Equal(["memory"], memory.Keywords);
        await using var db = await env.Connections.OpenAsync(DatabaseKind.Ai, default); using var command = db.CreateCommand(); command.CommandText = "SELECT CAST(ProtectedContent AS TEXT) FROM Messages UNION ALL SELECT CAST(ProtectedContent AS TEXT) FROM Memories;"; await using var reader = await command.ExecuteReaderAsync(); while (await reader.ReadAsync()) { var raw = reader.GetString(0); Assert.DoesNotContain(messageSecret, raw); Assert.DoesNotContain(memorySecret, raw); }
    }

    [Fact]
    public async Task ProviderProfileStoresOnlyReferenceAndDpapiSecretFileNeverContainsKey()
    {
        using var env = new TestEnvironment(); await env.Migrator().MigrateAsync(DatabaseKind.Ai, default); var protection = new DpapiDataProtectionService(); var secrets = new DpapiFileSecretStore(env.Directories, protection); const string apiKey = "not-a-real-key-phase7-plain-value"; var reference = new SecretReference("saved:" + Guid.NewGuid().ToString("D"));
        await secrets.StoreAsync(reference, Encoding.UTF8.GetBytes(apiKey), default); var repository = new SqliteAiProviderProfileRepository(env.Connections); var now = DateTimeOffset.UtcNow; var profile = new AiProviderProfile(Guid.NewGuid(), AiProviderType.OpenAI, "OpenAI", null, "model", TimeSpan.FromSeconds(30), reference, true, now, now); await repository.SaveAsync(profile, default);
        Assert.Equal(apiKey, Encoding.UTF8.GetString((await secrets.ReadAsync(reference, default))!));
        foreach (var file in Directory.EnumerateFiles(env.Directories.Root, "*", SearchOption.AllDirectories)) Assert.DoesNotContain(apiKey, Encoding.Latin1.GetString(await File.ReadAllBytesAsync(file)));
        var loaded = Assert.Single(await repository.ListAsync(default)); Assert.Equal(reference, loaded.SecretReference); await secrets.DeleteAsync(reference, default); Assert.Null(await secrets.ReadAsync(reference, default));
    }

    [Fact]
    public async Task FailedAiUpgradeRollsBackSchemaAndHistory()
    {
        using var env = new TestEnvironment(); await env.Migrator().MigrateAsync(DatabaseKind.Ai, default);
        var pending = InitialMigrations.Create().Append(new SqliteMigration(DatabaseKind.Ai, 4, "pending", "CREATE TABLE PendingAi(Value TEXT);")).Append(new SqliteMigration(DatabaseKind.Ai, 5, "broken", "INSERT INTO MissingAi VALUES(1);"));
        await Assert.ThrowsAsync<SqliteException>(() => env.Migrator(pending).MigrateAsync(DatabaseKind.Ai, default));
        await using var db = await env.Connections.OpenAsync(DatabaseKind.Ai, default); Assert.Equal(3L, await Scalar(db, "SELECT MAX(Version) FROM SchemaMigrations;")); Assert.Equal(0L, await Scalar(db, "SELECT COUNT(*) FROM sqlite_master WHERE name='PendingAi';"));
    }
    private static async Task<object?> Scalar(SqliteConnection connection, string sql) { using var command = connection.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync(); }
}
