namespace DesktopPet.AI.Tools;

internal static class AiToolSchemas
{
    public const string Empty = """{"type":"object","properties":{},"additionalProperties":false}""";
    public const string Identifier = """{"type":"object","properties":{"id":{"type":"string","format":"uuid"}},"required":["id"],"additionalProperties":false}""";
    public const string PomodoroStart = """{"type":"object","properties":{"phase":{"type":"string","enum":["focus","shortBreak","longBreak"]},"minutes":{"type":"integer","minimum":1,"maximum":240}},"additionalProperties":false}""";
    public const string UiPage = """{"type":"object","properties":{"page":{"type":"string","enum":["home","ai","pomodoro","reminders","statistics","characters","settings"]}},"required":["page"],"additionalProperties":false}""";
    public const string ClickThrough = """{"type":"object","properties":{"enabled":{"type":"boolean"}},"required":["enabled"],"additionalProperties":false}""";
    public const string MovementMode = """{"type":"object","properties":{"mode":{"type":"string","enum":["fixed","local","desktop","hybrid"]}},"required":["mode"],"additionalProperties":false}""";
    public const string Settings = """{"type":"object","properties":{"setting":{"type":"string","enum":["motionStyle","theme","alwaysOnTop"]},"value":{"type":"string","minLength":1,"maxLength":32}},"required":["setting","value"],"additionalProperties":false}""";
    public const string ReminderCreate = """{"type":"object","properties":{"title":{"type":"string","minLength":1,"maxLength":200},"description":{"type":"string","maxLength":1000},"scheduleType":{"type":"string","enum":["oneTime","daily","weekly"]},"dueAtUtc":{"type":"string","format":"date-time"},"localTime":{"type":"string","minLength":5,"maxLength":8},"weekdays":{"type":"array","minItems":1,"maxItems":7,"items":{"type":"string","enum":["sunday","monday","tuesday","wednesday","thursday","friday","saturday"]}},"timeZoneId":{"type":"string","minLength":1,"maxLength":128}},"required":["title","scheduleType"],"additionalProperties":false}""";
    public const string ReminderUpdate = """{"type":"object","properties":{"id":{"type":"string","format":"uuid"},"title":{"type":"string","minLength":1,"maxLength":200},"description":{"type":"string","maxLength":1000},"scheduleType":{"type":"string","enum":["oneTime","daily","weekly"]},"dueAtUtc":{"type":"string","format":"date-time"},"localTime":{"type":"string","minLength":5,"maxLength":8},"weekdays":{"type":"array","minItems":1,"maxItems":7,"items":{"type":"string","enum":["sunday","monday","tuesday","wednesday","thursday","friday","saturday"]}},"timeZoneId":{"type":"string","minLength":1,"maxLength":128}},"required":["id"],"additionalProperties":false}""";
}
