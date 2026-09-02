using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DesktopPet.Application.Productivity;
using DesktopPet.Application.Storage;
using DesktopPet.Domain.Productivity;
using Microsoft.Data.Sqlite;

namespace DesktopPet.Infrastructure.Persistence;

internal static class ProductivitySql
{
    public static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    public static DateTimeOffset Utc(SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    public static Guid Guid(SqliteDataReader reader, int ordinal) => System.Guid.Parse(reader.GetString(ordinal));
}

public sealed class SqlitePomodoroRepository(ISqliteConnectionFactory connections) : IPomodoroRepository
{
    public async Task<PomodoroSession?> GetActiveAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = Select + " WHERE Status IN (1,2) ORDER BY StartedAtUtc DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task<PomodoroSession?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = Select + " WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }

    public async Task SaveAsync(PomodoroSession session, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO PomodoroSessions
                (Id,TaskId,Phase,StartedAtUtc,TargetAtUtc,EndedAtUtc,Status,PlannedMinutes,ActualSeconds,PausedRemainingSeconds,FocusSequence)
            VALUES ($id,$task,$phase,$started,$target,$ended,$status,$planned,$actual,$remaining,$sequence)
            ON CONFLICT(Id) DO UPDATE SET
                TaskId=excluded.TaskId, Phase=excluded.Phase, StartedAtUtc=excluded.StartedAtUtc,
                TargetAtUtc=excluded.TargetAtUtc, EndedAtUtc=excluded.EndedAtUtc, Status=excluded.Status,
                PlannedMinutes=excluded.PlannedMinutes, ActualSeconds=excluded.ActualSeconds,
                PausedRemainingSeconds=excluded.PausedRemainingSeconds, FocusSequence=excluded.FocusSequence;
            """;
        command.Parameters.AddWithValue("$id", session.Id.ToString("D"));
        command.Parameters.AddWithValue("$task", session.TaskId is { } task ? task.ToString("D") : DBNull.Value);
        command.Parameters.AddWithValue("$phase", (int)session.Phase);
        command.Parameters.AddWithValue("$started", ProductivitySql.Utc(session.StartedAtUtc));
        command.Parameters.AddWithValue("$target", ProductivitySql.Utc(session.TargetAtUtc));
        command.Parameters.AddWithValue("$ended", session.EndedAtUtc is { } ended ? ProductivitySql.Utc(ended) : DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)session.Status);
        command.Parameters.AddWithValue("$planned", session.PlannedDuration.TotalMinutes);
        command.Parameters.AddWithValue("$actual", session.ActualDuration.TotalSeconds);
        command.Parameters.AddWithValue("$remaining", session.PausedRemaining.TotalSeconds);
        command.Parameters.AddWithValue("$sequence", session.FocusSequence);
        await command.ExecuteNonQueryAsync(ct);
    }

    public async Task<IReadOnlyList<PomodoroSession>> ListAsync(DateTimeOffset fromUtc, DateTimeOffset toUtc, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = Select + " WHERE StartedAtUtc >= $from AND StartedAtUtc < $to ORDER BY StartedAtUtc;";
        command.Parameters.AddWithValue("$from", ProductivitySql.Utc(fromUtc));
        command.Parameters.AddWithValue("$to", ProductivitySql.Utc(toUtc));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<PomodoroSession>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }

    public async Task<int> CountRecentCompletedFocusAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT FocusSequence FROM PomodoroSessions
            WHERE Phase=0 AND Status=3 ORDER BY EndedAtUtc DESC LIMIT 1;
            """;
        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private const string Select = """
        SELECT Id,TaskId,Phase,StartedAtUtc,TargetAtUtc,EndedAtUtc,Status,PlannedMinutes,
               ActualSeconds,PausedRemainingSeconds,FocusSequence FROM PomodoroSessions
        """;
    private static PomodoroSession Read(SqliteDataReader reader) => new(
        ProductivitySql.Guid(reader, 0), reader.IsDBNull(1) ? null : ProductivitySql.Guid(reader, 1),
        (PomodoroPhase)reader.GetInt32(2), ProductivitySql.Utc(reader, 3), ProductivitySql.Utc(reader, 4),
        reader.IsDBNull(5) ? null : ProductivitySql.Utc(reader, 5), (PomodoroStatus)reader.GetInt32(6),
        TimeSpan.FromMinutes(reader.GetDouble(7)), TimeSpan.FromSeconds(reader.GetDouble(8)),
        TimeSpan.FromSeconds(reader.GetDouble(9)), reader.GetInt32(10));
}

public sealed class SqliteTaskRepository(ISqliteConnectionFactory connections) : ITaskRepository
{
    public async Task<FocusTask?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT Id,Title,Description,Status,CreatedAtUtc,UpdatedAtUtc FROM Tasks WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadTask(reader) : null;
    }
    public async Task SaveAsync(FocusTask task, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO Tasks (Id,Title,Description,Status,CreatedAtUtc,UpdatedAtUtc)
            VALUES ($id,$title,$description,$status,$created,$updated)
            ON CONFLICT(Id) DO UPDATE SET Title=excluded.Title,Description=excluded.Description,
                Status=excluded.Status,UpdatedAtUtc=excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", task.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", task.Title);
        command.Parameters.AddWithValue("$description", (object?)task.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (int)task.Status);
        command.Parameters.AddWithValue("$created", ProductivitySql.Utc(task.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", ProductivitySql.Utc(task.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<IReadOnlyList<FocusTask>> ListAsync(bool includeArchived, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT Id,Title,Description,Status,CreatedAtUtc,UpdatedAtUtc FROM Tasks" +
            (includeArchived ? string.Empty : " WHERE Status=0") + " ORDER BY UpdatedAtUtc DESC;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<FocusTask>();
        while (await reader.ReadAsync(ct)) result.Add(ReadTask(reader));
        return result;
    }
    public async Task SetTagsAsync(Guid taskId, IReadOnlyCollection<Guid> tagIds, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var transaction = db.BeginTransaction(deferred: false);
        using (var remove = db.CreateCommand())
        {
            remove.Transaction = transaction; remove.CommandText = "DELETE FROM TaskTags WHERE TaskId=$task;";
            remove.Parameters.AddWithValue("$task", taskId.ToString("D")); await remove.ExecuteNonQueryAsync(ct);
        }
        foreach (var tagId in tagIds.Distinct())
        {
            using var add = db.CreateCommand();
            add.Transaction = transaction;
            add.CommandText = "INSERT INTO TaskTags(TaskId,TagId) VALUES($task,$tag);";
            add.Parameters.AddWithValue("$task", taskId.ToString("D"));
            add.Parameters.AddWithValue("$tag", tagId.ToString("D"));
            await add.ExecuteNonQueryAsync(ct);
        }
        await transaction.CommitAsync(ct);
    }
    public async Task<IReadOnlyList<Tag>> GetTagsAsync(Guid taskId, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = """
            SELECT g.Id,g.Name,g.CreatedAtUtc,g.UpdatedAtUtc FROM Tags g
            JOIN TaskTags x ON x.TagId=g.Id WHERE x.TaskId=$task ORDER BY g.Name;
            """;
        command.Parameters.AddWithValue("$task", taskId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Tag>();
        while (await reader.ReadAsync(ct)) result.Add(ReadTag(reader));
        return result;
    }
    internal static FocusTask ReadTask(SqliteDataReader reader) => new(ProductivitySql.Guid(reader, 0), reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2), (FocusTaskStatus)reader.GetInt32(3),
        ProductivitySql.Utc(reader, 4), ProductivitySql.Utc(reader, 5));
    internal static Tag ReadTag(SqliteDataReader reader) => new(ProductivitySql.Guid(reader, 0), reader.GetString(1),
        ProductivitySql.Utc(reader, 2), ProductivitySql.Utc(reader, 3));
}

public sealed class SqliteTagRepository(ISqliteConnectionFactory connections) : ITagRepository
{
    public async Task<Tag?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT Id,Name,CreatedAtUtc,UpdatedAtUtc FROM Tags WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? SqliteTaskRepository.ReadTag(reader) : null;
    }
    public async Task SaveAsync(Tag tag, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = """
            INSERT INTO Tags(Id,Name,CreatedAtUtc,UpdatedAtUtc) VALUES($id,$name,$created,$updated)
            ON CONFLICT(Id) DO UPDATE SET Name=excluded.Name,UpdatedAtUtc=excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", tag.Id.ToString("D"));
        command.Parameters.AddWithValue("$name", tag.Name);
        command.Parameters.AddWithValue("$created", ProductivitySql.Utc(tag.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", ProductivitySql.Utc(tag.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM Tags WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<IReadOnlyList<Tag>> ListAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = "SELECT Id,Name,CreatedAtUtc,UpdatedAtUtc FROM Tags ORDER BY Name;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Tag>();
        while (await reader.ReadAsync(ct)) result.Add(SqliteTaskRepository.ReadTag(reader));
        return result;
    }
}

public sealed class SqliteReminderRepository(ISqliteConnectionFactory connections) : IReminderRepository
{
    public async Task<Reminder?> GetAsync(Guid id, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = Select + " WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Read(reader) : null;
    }
    public async Task<IReadOnlyList<Reminder>> ListAsync(CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = Select + " ORDER BY Enabled DESC,NextTriggerAtUtc,UpdatedAtUtc DESC;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var result = new List<Reminder>();
        while (await reader.ReadAsync(ct)) result.Add(Read(reader));
        return result;
    }
    public async Task SaveAsync(Reminder reminder, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        await SaveAsync(db, null, reminder, ct);
    }
    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var command = db.CreateCommand();
        command.CommandText = "DELETE FROM Reminders WHERE Id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString("D"));
        await command.ExecuteNonQueryAsync(ct);
    }
    public async Task<bool> TryRecordExecutionAsync(ReminderExecution execution, Reminder updatedReminder, CancellationToken ct)
    {
        await using var db = await connections.OpenAsync(DatabaseKind.App, ct);
        using var transaction = db.BeginTransaction(deferred: false);
        using var add = db.CreateCommand();
        add.Transaction = transaction;
        add.CommandText = """
            INSERT OR IGNORE INTO ReminderExecutions
                (Id,ReminderId,OccurrenceAtUtc,ExecutedAtUtc,Status,TitleSnapshot)
            VALUES($id,$reminder,$occurrence,$executed,$status,$title);
            """;
        add.Parameters.AddWithValue("$id", execution.Id.ToString("D"));
        add.Parameters.AddWithValue("$reminder", execution.ReminderId is { } id ? id.ToString("D") : DBNull.Value);
        add.Parameters.AddWithValue("$occurrence", ProductivitySql.Utc(execution.OccurrenceAtUtc));
        add.Parameters.AddWithValue("$executed", ProductivitySql.Utc(execution.ExecutedAtUtc));
        add.Parameters.AddWithValue("$status", (int)execution.Status);
        add.Parameters.AddWithValue("$title", execution.TitleSnapshot);
        if (await add.ExecuteNonQueryAsync(ct) == 0) { await transaction.RollbackAsync(ct); return false; }
        await SaveAsync(db, transaction, updatedReminder, ct);
        await transaction.CommitAsync(ct);
        return true;
    }
    private static async Task SaveAsync(SqliteConnection db, SqliteTransaction? transaction, Reminder reminder, CancellationToken ct)
    {
        using var command = db.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO Reminders
                (Id,Title,Description,ScheduleType,ScheduleJson,TimeZoneId,Enabled,MissedPolicy,Channels,NextTriggerAtUtc,CreatedAtUtc,UpdatedAtUtc)
            VALUES($id,$title,$description,$type,$json,$zone,$enabled,$missed,$channels,$next,$created,$updated)
            ON CONFLICT(Id) DO UPDATE SET Title=excluded.Title,Description=excluded.Description,
                ScheduleType=excluded.ScheduleType,ScheduleJson=excluded.ScheduleJson,TimeZoneId=excluded.TimeZoneId,
                Enabled=excluded.Enabled,MissedPolicy=excluded.MissedPolicy,Channels=excluded.Channels,
                NextTriggerAtUtc=excluded.NextTriggerAtUtc,UpdatedAtUtc=excluded.UpdatedAtUtc;
            """;
        command.Parameters.AddWithValue("$id", reminder.Id.ToString("D"));
        command.Parameters.AddWithValue("$title", reminder.Title);
        command.Parameters.AddWithValue("$description", (object?)reminder.Description ?? DBNull.Value);
        command.Parameters.AddWithValue("$type", (int)reminder.Schedule.Type);
        command.Parameters.AddWithValue("$json", ReminderScheduleJson.Serialize(reminder.Schedule));
        command.Parameters.AddWithValue("$zone", reminder.TimeZoneId);
        command.Parameters.AddWithValue("$enabled", reminder.Enabled);
        command.Parameters.AddWithValue("$missed", (int)reminder.MissedPolicy);
        command.Parameters.AddWithValue("$channels", (int)reminder.Channels);
        command.Parameters.AddWithValue("$next", reminder.NextTriggerAtUtc is { } next ? ProductivitySql.Utc(next) : DBNull.Value);
        command.Parameters.AddWithValue("$created", ProductivitySql.Utc(reminder.CreatedAtUtc));
        command.Parameters.AddWithValue("$updated", ProductivitySql.Utc(reminder.UpdatedAtUtc));
        await command.ExecuteNonQueryAsync(ct);
    }
    private const string Select = """
        SELECT Id,Title,Description,ScheduleType,ScheduleJson,TimeZoneId,Enabled,MissedPolicy,Channels,
               NextTriggerAtUtc,CreatedAtUtc,UpdatedAtUtc FROM Reminders
        """;
    private static Reminder Read(SqliteDataReader reader) => new(ProductivitySql.Guid(reader, 0), reader.GetString(1),
        reader.IsDBNull(2) ? null : reader.GetString(2),
        ReminderScheduleJson.Deserialize((ReminderScheduleType)reader.GetInt32(3), reader.GetString(4)),
        reader.GetString(5), reader.GetBoolean(6), (MissedReminderPolicy)reader.GetInt32(7),
        (ReminderChannels)reader.GetInt32(8), ProductivitySql.Utc(reader, 10), ProductivitySql.Utc(reader, 11),
        reader.IsDBNull(9) ? null : ProductivitySql.Utc(reader, 9));
}

internal static class ReminderScheduleJson
{
    private sealed record Dto(DateTimeOffset? DueAtUtc, DateTime? LocalDateTime, RecurrenceKind? Kind,
        TimeOnly? LocalTime, DayOfWeek[]? Weekdays, int IntervalDays = 1, int SchemaVersion = 1);
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) }
    };
    public static string Serialize(ReminderSchedule schedule) => JsonSerializer.Serialize(schedule switch
    {
        RelativeOneTimeSchedule relative => new Dto(relative.DueAtUtc, null, null, null, null),
        AbsoluteOneTimeSchedule absolute => new Dto(null, absolute.LocalDateTime, null, null, null),
        RecurringSchedule recurring => new Dto(null, null, recurring.Rule.Kind, recurring.Rule.LocalTime,
            recurring.Rule.Weekdays.ToArray(), recurring.Rule.IntervalDays, recurring.SchemaVersion),
        _ => throw new ArgumentOutOfRangeException(nameof(schedule))
    }, Options);
    public static ReminderSchedule Deserialize(ReminderScheduleType type, string json)
    {
        var dto = JsonSerializer.Deserialize<Dto>(json, Options) ?? throw new JsonException("Invalid reminder schedule.");
        return type switch
        {
            ReminderScheduleType.RelativeOneTime when dto.DueAtUtc is { } due => new RelativeOneTimeSchedule(due),
            ReminderScheduleType.AbsoluteOneTime when dto.LocalDateTime is { } local => new AbsoluteOneTimeSchedule(local),
            ReminderScheduleType.Recurring when dto.Kind is { } kind && dto.LocalTime is { } time =>
                new RecurringSchedule(dto.SchemaVersion, new(kind, time, (dto.Weekdays ?? []).ToHashSet(), dto.IntervalDays)),
            _ => throw new JsonException("Reminder schedule payload does not match its type.")
        };
    }
}
