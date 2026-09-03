using System.Text.Json;

namespace DesktopPet.AI.Tools;

public static class AiToolParameterRedactor
{
    private static readonly HashSet<string> SafeStrings = new(StringComparer.OrdinalIgnoreCase)
    { "id", "page", "mode", "phase", "setting", "scheduleType", "dueAtUtc", "localTime", "timeZoneId" };
    private static readonly string[] SensitiveNames = ["key", "secret", "token", "password", "credential", "authorization"];
    public static string Summarize(string json)
    {
        if (json.Length > 32_768) return "payload=too-large";
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "payload=non-object";
            var parts = document.RootElement.EnumerateObject().Take(16).Select(SummarizeProperty);
            var result = string.Join(", ", parts);
            return result.Length <= 512 ? result : result[..509] + "...";
        }
        catch (JsonException) { return "payload=invalid-json"; }
    }
    private static string SummarizeProperty(JsonProperty property)
    {
        if (SensitiveNames.Any(name => property.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
            return $"{property.Name}=[redacted]";
        return property.Value.ValueKind switch
        {
            JsonValueKind.String when SafeStrings.Contains(property.Name) => $"{property.Name}={Safe(property.Value.GetString()!)}",
            JsonValueKind.String => $"{property.Name}=[text:{property.Value.GetString()!.Length}]",
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => $"{property.Name}={property.Value.GetRawText()}",
            JsonValueKind.Array => $"{property.Name}=[items:{property.Value.GetArrayLength()}]",
            JsonValueKind.Null => $"{property.Name}=null",
            _ => $"{property.Name}=[object]"
        };
    }
    private static string Safe(string value) => value.Length <= 96 && value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ':' or '+' or '/' or ' ')
        ? value : $"[text:{value.Length}]";
}
