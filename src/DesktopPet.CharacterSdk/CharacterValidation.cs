using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace DesktopPet.CharacterSdk;

public enum ValidationSeverity { Fatal, Error, Warning, Info }
public enum CharacterErrorCode
{
    MissingManifest, InvalidJson, DuplicateProperty, InvalidId, UnsupportedSchema, InvalidVersion, AppTooOld,
    MissingResource, InvalidPng, InvalidAnimation, InvalidPath, DuplicateResource, ResourceLimit,
    ForbiddenFile, LinkNotAllowed, CapabilityMismatch, UnsupportedRenderer, InvalidProfile,
    TierMismatch, MissingLocalization, InvalidArchive, StorageFailure, AlreadyInstalled, NotFound, ActiveCharacter, Installed
}
public sealed record ValidationIssue(CharacterErrorCode ErrorCode, ValidationSeverity Severity, string? JsonPath,
    string? ResourcePath, string Expected, string Actual, string Message, string Suggestion);
public sealed record ValidationResult(bool CanInstall, CharacterDefinition? Definition, IReadOnlyList<ValidationIssue> Issues)
{
    public CharacterTier? ActualLevel => Definition?.Metadata.ActualLevel;
    public int CompletenessPercentage => Definition?.Metadata.CompletenessPercentage ?? 0;
    public IReadOnlyList<CharacterCapability> MissingCapabilities => Definition?.Metadata.MissingCapabilities ?? Enum.GetValues<CharacterCapability>();
    public IReadOnlyList<ValidationIssue> Warnings => Issues.Where(issue => issue.Severity == ValidationSeverity.Warning).ToArray();
    public static ValidationResult Reject(CharacterErrorCode code, string? path, string message) =>
        new(false, null, [new(code, ValidationSeverity.Fatal, null, path, "Valid safe package", "Rejected", message, "Correct the package and retry.")]);
}
public sealed record PngInfo(int Width, int Height, bool IsValid);
public sealed record CharacterResource(string Path, long Length, PngInfo? Image = null, string? Json = null);
public sealed record CharacterPackageContent(string ManifestJson, IReadOnlyDictionary<string, CharacterResource> Resources);
public sealed record CharacterValidationLimits(int MaxImageDimension = 4096, int MaxAnimationFrames = 1000);
public interface ICharacterPackageValidator
{
    ValidationResult Validate(CharacterPackageContent content, CharacterValidationLimits limits);
}
public interface IPngInspector
{
    PngInfo Inspect(Stream stream, int maxDimension);
}
public static partial class CharacterSchema
{
    public const int CurrentVersion = 1;
    public static Version AppVersion => new(0, 4, 0);
    [GeneratedRegex(@"^[a-z][a-z0-9]*(?:[.-][a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
    public static bool IsValidId(string? id) => id is { Length: >= 3 and <= 100 } && IdPattern().IsMatch(id);
    [GeneratedRegex(@"^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex SemanticPattern();
    public static bool IsSemantic(string? value) => value is not null && SemanticPattern().IsMatch(value);
    public static bool IsVersion(string? value, out Version version)
    {
        version = new(0, 0, 0);
        if (value is null || value.Split('.').Length != 3 ||
            value.Any(c => !char.IsAsciiDigit(c) && c != '.') || !Version.TryParse(value, out var parsed)) return false;
        version = parsed;
        return true;
    }
    public static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
        MaxDepth = 32,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
    public static T Read<T>(string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
        CheckDuplicates(document.RootElement);
        return JsonSerializer.Deserialize<T>(json, JsonOptions()) ?? throw new JsonException("Null document.");
    }
    private static void CheckDuplicates(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!keys.Add(property.Name)) throw new JsonException("Duplicate property.");
                CheckDuplicates(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CheckDuplicates(item);
    }
}
