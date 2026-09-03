using System.Text.Json;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.CharacterSdk;

namespace DesktopPet.Infrastructure.Characters;

public sealed class CharacterVoiceProfileReader(ISettingsService settings, IExceptionHandler exceptions) : ICharacterVoiceProfileReader
{
    public async Task<CharacterVoiceProfile?> ReadAsync(CharacterPackage package, CancellationToken ct)
    {
        if (package.Definition.Manifest.Profiles.Voice is not { } relative ||
            package.Definition.Metadata.MissingCapabilities.Contains(CharacterCapability.TtsProfile)) return null;
        try
        {
            var path = PackagePath.Resolve(package.InstalledDirectory, relative);
            PackageFiles.RejectLinks(path);
            if (new FileInfo(path).Length > settings.Current.Security.MaxManifestBytes)
                throw new InvalidDataException("Voice profile is too large.");
            return CharacterSchema.Read<CharacterVoiceProfile>(await File.ReadAllTextAsync(path, ct));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.BackgroundTask);
            return null;
        }
    }
}
