using DesktopPet.Application.Configuration;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Runtime;
using DesktopPet.CharacterSdk;
using System.Text.Json;

namespace DesktopPet.Infrastructure.Characters;

public sealed class CharacterBehaviorProfileReader(ISettingsService settings, IExceptionHandler exceptions) : ICharacterBehaviorProfileReader
{
    public async Task<CharacterBehaviorProfile> ReadAsync(CharacterPackage package, CancellationToken ct)
    {
        var profile = package.Definition.Manifest.Profiles;
        async Task<T?> Read<T>(string? relative, CharacterCapability capability)
        {
            if (relative is null || package.Definition.Metadata.MissingCapabilities.Contains(capability)) return default;
            try
            {
                var path = PackagePath.Resolve(package.InstalledDirectory, relative);
                PackageFiles.RejectLinks(path);
                if (new FileInfo(path).Length > settings.Current.Security.MaxManifestBytes) throw new InvalidDataException("Profile is too large.");
                return CharacterSchema.Read<T>(await File.ReadAllTextAsync(path, ct));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
            {
                exceptions.Report(e, ErrorCode.CommandFailed, ErrorOrigin.BackgroundTask);
                return default;
            }
        }
        var behavior = await Read<BehaviorProfile>(profile.BehaviorProfile, CharacterCapability.BehaviorProfile);
        var emotion = await Read<EmotionProfile>(profile.EmotionMap, CharacterCapability.EmotionMap);
        return new(behavior?.Behaviors?.Where(item => item is not null).ToArray() ?? [],
            emotion?.Animations?.Where(pair => CharacterSchema.IsSemantic(pair.Key) && pair.Value is not null &&
                package.Definition.Animations.ContainsKey(pair.Value)).ToDictionary(pair => pair.Key, pair => pair.Value)
            ?? new Dictionary<string, string>());
    }
}
