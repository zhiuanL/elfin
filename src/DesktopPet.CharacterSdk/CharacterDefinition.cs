using System.Text.Json.Serialization;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Movement;

namespace DesktopPet.CharacterSdk;

public enum CharacterTier { Basic, Standard, Full }
public enum AnimationFormat
{
    [JsonStringEnumMemberName("static")] StaticPng,
    [JsonStringEnumMemberName("sequence")] PngSequence,
    [JsonStringEnumMemberName("layered2d")] Layered2D,
    [JsonStringEnumMemberName("live2d")] Live2D
}
public enum CharacterCapability { Idle, Blink, Happy, Rest, Talking, Persona, Dialogue, EmotionMap, HitAreas, BehaviorProfile, TtsProfile, LipSync }
public sealed record CharacterAssets(string Preview, string Fallback);
public sealed record CharacterCapabilities
{
    public bool HitAreas { get; init; }
    public bool Persona { get; init; }
    public bool Dialogue { get; init; }
    public bool EmotionMap { get; init; }
    public bool BehaviorProfile { get; init; }
    public bool TtsProfile { get; init; }
    public bool LipSync { get; init; }
    public bool Layered2D { get; init; }
    public bool Live2D { get; init; }
}
public sealed record AnimationFrameDefinition(string Path, int? DurationMs = null);
public sealed record AnimationDefinition
{
    public required AnimationFormat Type { get; init; }
    public string? Path { get; init; }
    public IReadOnlyList<AnimationFrameDefinition>? Frames { get; init; }
    public int Fps { get; init; } = 12;
    public bool Loop { get; init; } = true;
    public string? Fallback { get; init; }
}
public sealed record LocalizedCharacterText(string Name, string Description);
public sealed record CharacterProfileReferences
{
    public IReadOnlyDictionary<string, string> Persona { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> Dialogue { get; init; } = new Dictionary<string, string>();
    public string? EmotionMap { get; init; }
    public string? HitAreas { get; init; }
    public string? BehaviorProfile { get; init; }
    public string? Voice { get; init; }
}
public sealed record CharacterManifest
{
    public required int SchemaVersion { get; init; }
    public required string PackageVersion { get; init; }
    public required string MinimumAppVersion { get; init; }
    [JsonPropertyName("id")] public required string CharacterId { get; init; }
    public string DefaultLocale { get; init; } = "zh-CN";
    public CharacterTier TargetTier { get; init; } = CharacterTier.Basic;
    public required CharacterAssets Assets { get; init; }
    public required IReadOnlyDictionary<string, AnimationDefinition> Animations { get; init; }
    public CharacterCapabilities Capabilities { get; init; } = new();
    public IReadOnlyDictionary<string, LocalizedCharacterText> Locales { get; init; } = new Dictionary<string, LocalizedCharacterText>();
    public CharacterProfileReferences Profiles { get; init; } = new();
    public bool IsDevelopmentFixture { get; init; }
    public VisualAnchor? VisualAnchor { get; init; }
    public bool SupportsMirroring { get; init; }
    public MotionOverrides? Movement { get; init; }
}
public sealed record CharacterPackageMetadata(CharacterTier TargetTier, CharacterTier ActualLevel,
    int CompletenessPercentage, IReadOnlyList<CharacterCapability> MissingCapabilities, IReadOnlyList<ValidationIssue> Warnings);
public sealed record CharacterDefinition(CharacterManifest Manifest,
    IReadOnlyDictionary<string, AnimationDefinition> Animations, CharacterPackageMetadata Metadata)
{
    public CharacterId Id => new(Manifest.CharacterId);
    public CharacterAssets Assets => Manifest.Assets;
    public LocalizedCharacterText Localize(string locale) =>
        Manifest.Locales.GetValueOrDefault(locale) ?? Manifest.Locales.GetValueOrDefault("en-US") ??
        Manifest.Locales.GetValueOrDefault(Manifest.DefaultLocale) ?? new(Manifest.CharacterId, string.Empty);
}
public sealed record CharacterPackage(CharacterDefinition Definition, string InstalledDirectory);
public sealed record AnimationRequest(PetInstanceId InstanceId, AnimationSemantic Semantic, bool Loop);

public interface IAnimationProvider
{
    bool CanRender(AnimationDefinition definition);
    Task PreloadAsync(AnimationDefinition definition, CancellationToken ct);
    Task PlayAsync(AnimationRequest request, CancellationToken ct);
    void Stop();
}
public interface ICharacterSchemaMigration
{
    int FromVersion { get; }
    int ToVersion { get; }
    string Migrate(string manifestJson);
}
