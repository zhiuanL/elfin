using DesktopPet.AI.Contracts;
using DesktopPet.Application.Storage;
using DesktopPet.Domain.Pets;
using DesktopPet.Infrastructure.Persistence;
using DesktopPet.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

public sealed class PhaseEightAiToolPersistenceTests
{
    [Fact]
    public async Task AuditMigrationAndRepositoryRoundTripOnlyRedactedSummary()
    {
        using var env = new TestEnvironment();
        await env.Migrator().MigrateAsync(DatabaseKind.Ai, default);
        var conversations = new SqliteConversationRepository(env.Connections,
            new DesktopPet.Windows.Security.DpapiDataProtectionService(), TimeProvider.System);
        var conversation = await conversations.GetOrCreateMainAsync(new CharacterId("audit-pet"), default);
        var repository = new SqliteAiToolAuditRepository(env.Connections);
        var entry = new AiToolAuditEntry(Guid.NewGuid(), DateTimeOffset.UtcNow, conversation.Id, "call-1",
            "reminder.create", ToolRiskLevel.Medium, """{"title":"[string:length=7]","apiKey":"[redacted]"}""",
            ToolConfirmationResult.Allowed, ToolExecutionStatus.Success, 12, null);
        await repository.SaveAsync(entry, default);
        await repository.SaveAsync(entry with { Id = Guid.NewGuid() }, default);
        var loaded = Assert.Single(await repository.ListRecentAsync(10, default));
        Assert.Equal(entry.ToolCallId, loaded.ToolCallId); Assert.Equal(entry.ParameterSummary, loaded.ParameterSummary);
        Assert.Equal(ToolExecutionStatus.Success, loaded.ExecutionStatus);
    }

    [Fact]
    public async Task PhaseSevenSettingsMigrateAndToolPreferencesPersist()
    {
        using var env = new TestEnvironment();
        var path = Path.Combine(env.Directories.Config, "settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":7,"culture":"en-US"}""");
        using (var settings = new JsonSettingsService(env.Directories, Options.Create(new DesktopPet.Application.Configuration.AppSettings()),
                   env.Logger, TimeProvider.System))
        {
            var loaded = await settings.LoadAsync(default);
            Assert.Equal(DesktopPet.Application.Configuration.SettingsLoadStatus.Migrated, loaded.Status);
            await settings.UpdateAsync(value => value with { AiTools = value.AiTools with
            { Enabled = false, DisabledToolIds = ["pet.hide"] } }, default);
        }
        using var reloaded = new JsonSettingsService(env.Directories, Options.Create(new DesktopPet.Application.Configuration.AppSettings()),
            env.Logger, TimeProvider.System);
        var persisted = await reloaded.LoadAsync(default);
        Assert.False(persisted.Settings.AiTools.Enabled);
        Assert.Equal(["pet.hide"], persisted.Settings.AiTools.DisabledToolIds);
    }
}
