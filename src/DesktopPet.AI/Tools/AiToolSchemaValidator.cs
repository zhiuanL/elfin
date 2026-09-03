using System.Text.Json;
using DesktopPet.AI.Contracts;

namespace DesktopPet.AI.Tools;

public sealed class AiToolSchemaValidator : IAiToolSchemaValidator
{
    public bool TryValidate(string schema, JsonElement arguments, out string errorCode)
    {
        errorCode = "invalid_parameter";
        if (schema.Length is 0 or > 16_384 || arguments.ValueKind != JsonValueKind.Object) return false;
        try
        {
            using var document = JsonDocument.Parse(schema);
            return ValidateNode(document.RootElement, arguments, 0, out errorCode);
        }
        catch (JsonException) { errorCode = "invalid_schema"; return false; }
    }

    private static bool ValidateNode(JsonElement schema, JsonElement value, int depth, out string error)
    {
        error = "invalid_parameter";
        if (depth > 8 || schema.ValueKind != JsonValueKind.Object) return false;
        var type = schema.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;
        if (!MatchesType(type, value)) return false;
        if (schema.TryGetProperty("enum", out var choices) && choices.ValueKind == JsonValueKind.Array &&
            !choices.EnumerateArray().Any(choice => JsonElement.DeepEquals(choice, value))) return false;
        if (value.ValueKind == JsonValueKind.String && !ValidateString(schema, value.GetString()!)) return false;
        if (value.ValueKind == JsonValueKind.Number && !ValidateNumber(schema, value)) return false;
        if (value.ValueKind == JsonValueKind.Array)
        {
            if (schema.TryGetProperty("maxItems", out var maximum) && value.GetArrayLength() > maximum.GetInt32()) return false;
            if (schema.TryGetProperty("minItems", out var minimum) && value.GetArrayLength() < minimum.GetInt32()) return false;
            if (schema.TryGetProperty("items", out var itemSchema))
                foreach (var item in value.EnumerateArray()) if (!ValidateNode(itemSchema, item, depth + 1, out error)) return false;
        }
        if (value.ValueKind != JsonValueKind.Object) { error = string.Empty; return true; }
        var properties = schema.TryGetProperty("properties", out var propertyNode) && propertyNode.ValueKind == JsonValueKind.Object
            ? propertyNode : default;
        if (schema.TryGetProperty("required", out var required) && required.ValueKind == JsonValueKind.Array)
            foreach (var name in required.EnumerateArray().Select(item => item.GetString()).Where(item => item is not null))
                if (!value.TryGetProperty(name!, out _)) return false;
        var allowAdditional = !schema.TryGetProperty("additionalProperties", out var additional) || additional.ValueKind != JsonValueKind.False;
        foreach (var property in value.EnumerateObject())
        {
            if (properties.ValueKind == JsonValueKind.Object && properties.TryGetProperty(property.Name, out var child))
            { if (!ValidateNode(child, property.Value, depth + 1, out error)) return false; }
            else if (!allowAdditional) return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool MatchesType(string? type, JsonElement value) => type switch
    {
        null => true,
        "object" => value.ValueKind == JsonValueKind.Object,
        "array" => value.ValueKind == JsonValueKind.Array,
        "string" => value.ValueKind == JsonValueKind.String,
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
        "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == JsonValueKind.Number,
        "null" => value.ValueKind == JsonValueKind.Null,
        _ => false
    };
    private static bool ValidateString(JsonElement schema, string value)
    {
        if (schema.TryGetProperty("minLength", out var minimum) && value.Length < minimum.GetInt32()) return false;
        if (schema.TryGetProperty("maxLength", out var maximum) && value.Length > maximum.GetInt32()) return false;
        if (!schema.TryGetProperty("format", out var format)) return true;
        return format.GetString() switch
        {
            "uuid" => Guid.TryParse(value, out _),
            "date-time" => DateTimeOffset.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out _),
            _ => true
        };
    }
    private static bool ValidateNumber(JsonElement schema, JsonElement value)
    {
        var number = value.GetDouble();
        return (!schema.TryGetProperty("minimum", out var minimum) || number >= minimum.GetDouble()) &&
            (!schema.TryGetProperty("maximum", out var maximum) || number <= maximum.GetDouble());
    }
}
