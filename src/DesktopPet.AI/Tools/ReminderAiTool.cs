using System.Globalization;
using System.Text.Json;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Contracts;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.AI.Tools;

public enum ReminderToolKind { List, Create, Update, Enable, Disable, Delete }

public sealed class ReminderAiTool(ReminderToolKind kind, IReminderService reminders, TimeProvider clock) : IAiTool
{
    public AiToolDefinition Definition { get; } = kind switch
    {
        ReminderToolKind.List => new("reminder.list", "List reminders with identifiers, enabled state, schedule type, and next trigger.",
            AiToolSchemas.Empty, ToolRiskLevel.Low, ConfirmationPolicy.None, true),
        ReminderToolKind.Create => Medium("reminder.create", "Create a one-time, daily, or weekly reminder.", AiToolSchemas.ReminderCreate),
        ReminderToolKind.Update => Medium("reminder.update", "Update an existing reminder using its identifier.", AiToolSchemas.ReminderUpdate),
        ReminderToolKind.Enable => Medium("reminder.enable", "Enable an existing reminder.", AiToolSchemas.Identifier),
        ReminderToolKind.Disable => Medium("reminder.disable", "Disable an existing reminder.", AiToolSchemas.Identifier),
        ReminderToolKind.Delete => new("reminder.delete", "Permanently delete an existing reminder.", AiToolSchemas.Identifier,
            ToolRiskLevel.High, ConfirmationPolicy.Always, true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public async Task<AiToolResult> ExecuteAsync(JsonElement arguments, CancellationToken ct)
    {
        return kind switch
        {
            ReminderToolKind.List => await ListAsync(ct),
            ReminderToolKind.Create => await CreateAsync(arguments, ct),
            ReminderToolKind.Update => await UpdateAsync(arguments, ct),
            ReminderToolKind.Enable => await SetEnabledAsync(arguments, true, ct),
            ReminderToolKind.Disable => await SetEnabledAsync(arguments, false, ct),
            ReminderToolKind.Delete => await DeleteAsync(arguments, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
    }

    private async Task<AiToolResult> ListAsync(CancellationToken ct)
    {
        var items = (await reminders.ListAsync(ct)).OrderBy(item => item.NextTriggerAtUtc).Take(50)
            .Select(item => new { id = item.Id, item.Title, scheduleType = item.Schedule.Type.ToString(), item.Enabled,
                nextTriggerAtUtc = item.NextTriggerAtUtc }).ToArray();
        return AiToolResult.Success("reminder_list", new { count = items.Length, reminders = items });
    }
    private async Task<AiToolResult> CreateAsync(JsonElement arguments, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var zoneId = OptionalString(arguments, "timeZoneId") ?? TimeZoneInfo.Local.Id;
        var schedule = ReadSchedule(arguments, now, zoneId);
        var reminder = new Reminder(Guid.NewGuid(), arguments.GetProperty("title").GetString()!,
            OptionalString(arguments, "description"), schedule, zoneId, true, MissedReminderPolicy.Smart,
            ReminderChannels.PetBubble | ReminderChannels.PetAction | ReminderChannels.WindowsNotification,
            now, now, null);
        var created = await reminders.CreateAsync(reminder, ct);
        return AiToolResult.Success("reminder_created", new { id = created.Id, created.Title,
            scheduleType = created.Schedule.Type.ToString(), created.NextTriggerAtUtc });
    }
    private async Task<AiToolResult> UpdateAsync(JsonElement arguments, CancellationToken ct)
    {
        var id = Guid.Parse(arguments.GetProperty("id").GetString()!);
        var current = await reminders.GetAsync(id, ct) ?? throw new KeyNotFoundException();
        var hasSchedule = arguments.TryGetProperty("scheduleType", out _);
        if (!hasSchedule && (arguments.TryGetProperty("dueAtUtc", out _) || arguments.TryGetProperty("localTime", out _) ||
            arguments.TryGetProperty("weekdays", out _) || arguments.TryGetProperty("timeZoneId", out _)))
            throw new ArgumentException("scheduleType is required when changing a schedule.");
        var title = OptionalString(arguments, "title") ?? current.Title;
        var description = arguments.TryGetProperty("description", out var descriptionNode) ? descriptionNode.GetString() : current.Description;
        var zoneId = OptionalString(arguments, "timeZoneId") ?? current.TimeZoneId;
        var schedule = hasSchedule ? ReadSchedule(arguments, clock.GetUtcNow(), zoneId) : current.Schedule;
        if (title == current.Title && description == current.Description && zoneId == current.TimeZoneId && Equals(schedule, current.Schedule))
            return new(ToolExecutionStatus.Conflict, "no_changes");
        var updated = await reminders.UpdateAsync(current with { Title = title, Description = description,
            TimeZoneId = zoneId, Schedule = schedule }, ct);
        return AiToolResult.Success("reminder_updated", new { id = updated.Id, updated.Title,
            scheduleType = updated.Schedule.Type.ToString(), updated.NextTriggerAtUtc });
    }
    private async Task<AiToolResult> SetEnabledAsync(JsonElement arguments, bool enabled, CancellationToken ct)
    {
        var id = Guid.Parse(arguments.GetProperty("id").GetString()!);
        var current = await reminders.GetAsync(id, ct) ?? throw new KeyNotFoundException();
        if (current.Enabled == enabled) return new(ToolExecutionStatus.Conflict, enabled ? "already_enabled" : "already_disabled");
        await reminders.SetEnabledAsync(id, enabled, ct);
        return AiToolResult.Success(enabled ? "reminder_enabled" : "reminder_disabled", new { id });
    }
    private async Task<AiToolResult> DeleteAsync(JsonElement arguments, CancellationToken ct)
    {
        var id = Guid.Parse(arguments.GetProperty("id").GetString()!);
        if (await reminders.GetAsync(id, ct) is null) throw new KeyNotFoundException();
        await reminders.DeleteAsync(id, ct);
        return AiToolResult.Success("reminder_deleted", new { id });
    }
    private static ReminderSchedule ReadSchedule(JsonElement arguments, DateTimeOffset now, string zoneId)
    {
        _ = TimeZoneInfo.FindSystemTimeZoneById(zoneId);
        return arguments.GetProperty("scheduleType").GetString() switch
        {
            "oneTime" => new RelativeOneTimeSchedule(ReadFutureUtc(arguments, now)),
            "daily" => new RecurringSchedule(1, new(RecurrenceKind.Daily, ReadLocalTime(arguments), new HashSet<DayOfWeek>())),
            "weekly" => new RecurringSchedule(1, new(RecurrenceKind.SelectedWeekdays, ReadLocalTime(arguments), ReadWeekdays(arguments))),
            _ => throw new ArgumentException("Invalid schedule type.")
        };
    }
    private static DateTimeOffset ReadFutureUtc(JsonElement arguments, DateTimeOffset now)
    {
        if (!arguments.TryGetProperty("dueAtUtc", out var node) || !DateTimeOffset.TryParse(node.GetString(),
                CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var due) || due <= now)
            throw new ArgumentException("A future dueAtUtc is required.");
        return due.ToUniversalTime();
    }
    private static TimeOnly ReadLocalTime(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("localTime", out var node) || !TimeOnly.TryParse(node.GetString(),
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new ArgumentException("localTime is required.");
        return time;
    }
    private static IReadOnlySet<DayOfWeek> ReadWeekdays(JsonElement arguments)
    {
        if (!arguments.TryGetProperty("weekdays", out var node)) throw new ArgumentException("weekdays are required.");
        var values = node.EnumerateArray().Select(item => Enum.Parse<DayOfWeek>(item.GetString()!, true)).ToHashSet();
        return values.Count == 0 ? throw new ArgumentException("weekdays are required.") : values;
    }
    private static string? OptionalString(JsonElement arguments, string name) =>
        arguments.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim() : null;
    private static AiToolDefinition Medium(string id, string description, string schema) =>
        new(id, description, schema, ToolRiskLevel.Medium, ConfirmationPolicy.ConfirmOrUndo, true);
}
