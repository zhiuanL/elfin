using System.Security.Cryptography;
using System.Text;
using DesktopPet.Application.Storage;

namespace DesktopPet.Infrastructure.Persistence;

public interface ISqliteMigration
{
    DatabaseKind Database { get; }
    int Version { get; }
    string Name { get; }
    string Sql { get; }
    string Checksum { get; }
}
public sealed record SqliteMigration(DatabaseKind Database, int Version, string Name, string Sql) : ISqliteMigration
{
    public string Checksum => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Sql)));
}
public static class InitialMigrations
{
    // Phase 0 establishes only the migration ledger. Business schemas belong to later phases.
    public const string HistorySql = """
        CREATE TABLE SchemaMigrations (
            Version INTEGER NOT NULL PRIMARY KEY CHECK (Version > 0),
            Name TEXT NOT NULL,
            Checksum TEXT NOT NULL,
            AppliedAtUtc TEXT NOT NULL
        );
        """;

    public const string ProductivitySql = """
        CREATE TABLE Tasks (
            Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL, Description TEXT NULL,
            Status INTEGER NOT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL
        );
        CREATE TABLE Tags (
            Id TEXT NOT NULL PRIMARY KEY, Name TEXT NOT NULL COLLATE NOCASE UNIQUE,
            CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL
        );
        CREATE TABLE TaskTags (
            TaskId TEXT NOT NULL REFERENCES Tasks(Id) ON DELETE CASCADE,
            TagId TEXT NOT NULL REFERENCES Tags(Id) ON DELETE CASCADE,
            PRIMARY KEY (TaskId, TagId)
        );
        CREATE TABLE PomodoroSessions (
            Id TEXT NOT NULL PRIMARY KEY,
            TaskId TEXT NULL REFERENCES Tasks(Id) ON DELETE SET NULL,
            Phase INTEGER NOT NULL, StartedAtUtc TEXT NOT NULL, TargetAtUtc TEXT NOT NULL,
            EndedAtUtc TEXT NULL, Status INTEGER NOT NULL, PlannedMinutes REAL NOT NULL,
            ActualSeconds REAL NOT NULL, PausedRemainingSeconds REAL NOT NULL DEFAULT 0,
            FocusSequence INTEGER NOT NULL DEFAULT 0
        );
        CREATE UNIQUE INDEX IX_PomodoroSessions_OneActive ON PomodoroSessions((1)) WHERE Status IN (1, 2);
        CREATE INDEX IX_PomodoroSessions_StartedAtUtc ON PomodoroSessions(StartedAtUtc);
        CREATE TABLE Reminders (
            Id TEXT NOT NULL PRIMARY KEY, Title TEXT NOT NULL, Description TEXT NULL,
            ScheduleType INTEGER NOT NULL, ScheduleJson TEXT NOT NULL, TimeZoneId TEXT NOT NULL,
            Enabled INTEGER NOT NULL, MissedPolicy INTEGER NOT NULL, Channels INTEGER NOT NULL,
            NextTriggerAtUtc TEXT NULL, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL
        );
        CREATE INDEX IX_Reminders_NextTriggerAtUtc ON Reminders(Enabled, NextTriggerAtUtc);
        CREATE TABLE ReminderExecutions (
            Id TEXT NOT NULL PRIMARY KEY,
            ReminderId TEXT NULL REFERENCES Reminders(Id) ON DELETE SET NULL,
            OccurrenceAtUtc TEXT NOT NULL, ExecutedAtUtc TEXT NOT NULL, Status INTEGER NOT NULL,
            TitleSnapshot TEXT NOT NULL
        );
        CREATE UNIQUE INDEX UX_ReminderExecutions_Occurrence ON ReminderExecutions(ReminderId, OccurrenceAtUtc);
        """;

    public const string AiCoreSql = """
        CREATE TABLE AiProviderProfiles (
            Id TEXT NOT NULL PRIMARY KEY, ProviderType INTEGER NOT NULL, DisplayName TEXT NOT NULL,
            BaseUrl TEXT NULL, Model TEXT NOT NULL, TimeoutSeconds INTEGER NOT NULL,
            SecretReference TEXT NULL, IsActive INTEGER NOT NULL DEFAULT 0,
            CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL
        );
        CREATE UNIQUE INDEX UX_AiProviderProfiles_Active ON AiProviderProfiles((1)) WHERE IsActive=1;
        CREATE TABLE Conversations (
            Id TEXT NOT NULL PRIMARY KEY, CharacterId TEXT NOT NULL, Type INTEGER NOT NULL,
            Title TEXT NOT NULL, ProtectedOlderSummary BLOB NULL,
            CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL
        );
        CREATE UNIQUE INDEX UX_Conversations_MainCharacter ON Conversations(CharacterId) WHERE Type=0;
        CREATE INDEX IX_Conversations_Character ON Conversations(CharacterId, UpdatedAtUtc DESC);
        CREATE TABLE Messages (
            Id TEXT NOT NULL PRIMARY KEY, ConversationId TEXT NOT NULL REFERENCES Conversations(Id) ON DELETE CASCADE,
            Role INTEGER NOT NULL, ProtectedContent BLOB NOT NULL, CreatedAtUtc TEXT NOT NULL,
            Provider TEXT NULL, Model TEXT NULL, TokenUsage INTEGER NULL, Status INTEGER NOT NULL
        );
        CREATE INDEX IX_Messages_ConversationTime ON Messages(ConversationId, CreatedAtUtc, Id);
        CREATE TABLE Memories (
            Id TEXT NOT NULL PRIMARY KEY, CharacterId TEXT NOT NULL, Category INTEGER NOT NULL,
            ProtectedContent BLOB NOT NULL, Importance INTEGER NOT NULL CHECK(Importance BETWEEN 1 AND 5),
            SourceMessageId TEXT NULL REFERENCES Messages(Id) ON DELETE SET NULL, IsAuto INTEGER NOT NULL,
            CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL
        );
        CREATE INDEX IX_Memories_Character ON Memories(CharacterId, Importance DESC, UpdatedAtUtc DESC);
        CREATE TABLE MemoryTags (
            MemoryId TEXT NOT NULL REFERENCES Memories(Id) ON DELETE CASCADE,
            Value TEXT NOT NULL, Kind INTEGER NOT NULL, PRIMARY KEY(MemoryId, Value, Kind)
        );
        CREATE TABLE AiUsage (
            Id TEXT NOT NULL PRIMARY KEY, ConversationId TEXT NOT NULL REFERENCES Conversations(Id) ON DELETE CASCADE,
            MessageId TEXT NULL REFERENCES Messages(Id) ON DELETE SET NULL, Provider TEXT NOT NULL,
            Model TEXT NOT NULL, InputTokens INTEGER NULL, OutputTokens INTEGER NULL, CreatedAtUtc TEXT NOT NULL
        );
        CREATE TABLE AiCharacterPreferences (
            CharacterId TEXT NOT NULL PRIMARY KEY, AutoMemoryEnabled INTEGER NOT NULL DEFAULT 0
        );
        """;

    public static IReadOnlyList<ISqliteMigration> Create() =>
    [
        new SqliteMigration(DatabaseKind.App, 1, "schema-history", HistorySql),
        new SqliteMigration(DatabaseKind.App, 2, "productivity-core", ProductivitySql),
        new SqliteMigration(DatabaseKind.Ai, 1, "schema-history", HistorySql),
        new SqliteMigration(DatabaseKind.Ai, 2, "ai-core", AiCoreSql)
    ];
}
public sealed class MigrationHistoryException(string message) : InvalidOperationException(message);
