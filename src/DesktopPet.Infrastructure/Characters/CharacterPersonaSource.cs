using System.Text.Json;
using DesktopPet.AI.Contracts;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Localization;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Infrastructure.Characters;

public sealed class CharacterPersonaSource(ICharacterPackageStore packages, ITextLocalizer text) : ICharacterPersonaSource
{
    public async Task<string?> GetPersonaAsync(CharacterId characterId, CancellationToken ct)
    {
        var package = (await packages.GetAsync(characterId, ct)).Package;
        if (package is null || package.Definition.Manifest.Profiles.Persona.Count == 0) return null;
        var references = package.Definition.Manifest.Profiles.Persona;
        var locale = text.Culture.Name;
        var relative = references.GetValueOrDefault(locale) ?? references.GetValueOrDefault(package.Definition.Manifest.DefaultLocale)
            ?? references.Values.FirstOrDefault();
        if (relative is null) return null;
        var path = PackagePath.Resolve(package.InstalledDirectory, relative);
        var json = await File.ReadAllTextAsync(path, ct);
        var profile = JsonSerializer.Deserialize<PersonaProfile>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        return profile is null ? null : $"You are this desktop companion. Persona: {profile.Summary}\nTraits: {string.Join(", ", profile.Traits)}";
    }
}
