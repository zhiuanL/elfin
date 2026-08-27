using DesktopPet.Domain.Pets;

namespace DesktopPet.CharacterSdk;

public enum CharacterTier { Basic, Standard, Full }
public enum AnimationFormat { StaticPng, PngSequence, Layered2D, Live2D }
public sealed record CharacterAssets(string Preview, string Fallback);
public sealed record CharacterCapabilities(bool HitAreas, bool Persona, bool BehaviorProfile,
    bool TtsProfile, bool LipSync);
public sealed record AnimationDefinition(AnimationFormat Type, string Path, int FramesPerSecond, bool Loop);
public sealed record LocalizedCharacterText(string Name, string Description);
public sealed record CharacterDefinition(int SchemaVersion, string PackageVersion, string MinimumAppVersion,
    CharacterId Id, string DefaultLocale, CharacterTier TargetTier, CharacterAssets Assets,
    IReadOnlyDictionary<string, AnimationDefinition> Animations, CharacterCapabilities Capabilities,
    IReadOnlyDictionary<string, LocalizedCharacterText> Locales);
public sealed record CharacterPackage(CharacterDefinition Definition, string InstalledDirectory);
public sealed record AnimationRequest(PetInstanceId InstanceId, AnimationSemantic Semantic, bool Loop);

public interface IAnimationProvider
{
    bool CanRender(AnimationDefinition definition);
    Task PreloadAsync(AnimationDefinition definition, CancellationToken ct);
    Task PlayAsync(AnimationRequest request, CancellationToken ct);
    void Stop();
}

public enum ValidationSeverity { Fatal, Error, Warning }
public sealed record CharacterDiagnostic(string ErrorCode, string JsonPath, ValidationSeverity Severity,
    string Expected, string Actual, string Suggestion);
public sealed record CharacterValidationResult(bool CanInstall, CharacterTier ActualTier,
    int CompletenessPercent, IReadOnlyList<CharacterDiagnostic> Diagnostics);
public interface ICharacterPackageValidator
{
    Task<CharacterValidationResult> ValidateAsync(string stagingDirectory, CancellationToken ct);
}
