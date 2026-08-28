using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Characters;

public sealed record CharacterOperationResult(ValidationResult Validation, CharacterPackage? Package = null)
{
    public bool Succeeded => Validation.CanInstall && Package is not null;
}
public sealed record CharacterDiscovery(IReadOnlyList<CharacterPackage> Packages, IReadOnlyList<ValidationIssue> Issues);
public interface ICharacterPackageStore
{
    Task<CharacterOperationResult> InspectAsync(string sourcePath, bool install, CancellationToken ct);
    Task<CharacterDiscovery> DiscoverAsync(CancellationToken ct);
    Task<CharacterOperationResult> GetAsync(CharacterId id, CancellationToken ct);
    Task<ValidationResult> RemoveAsync(CharacterId id, CancellationToken ct);
}
public interface ICharacterSeedSource { IReadOnlyList<string> GetDirectories(); }
public interface IAnimationSurface
{
    Task SetPackageAsync(CharacterPackage package, CancellationToken ct);
    Task PreloadAsync(string resourcePath, CancellationToken ct);
    Task PresentAsync(string resourcePath, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}
public sealed class CharacterAssetException(string message, Exception? inner = null) : IOException(message, inner);
