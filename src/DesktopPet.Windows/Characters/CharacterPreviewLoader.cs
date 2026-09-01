using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.Application.Configuration;
using DesktopPet.CharacterSdk;

namespace DesktopPet.Windows.Characters;

public interface ICharacterPreviewLoader
{
    Task<ImageSource?> LoadAsync(CharacterPackage package, CancellationToken ct);
}

public sealed class CharacterPreviewLoader(ISettingsService settings, IPngInspector inspector) : ICharacterPreviewLoader
{
    public Task<ImageSource?> LoadAsync(CharacterPackage package, CancellationToken ct) => Task.Run<ImageSource?>(() =>
    {
        ct.ThrowIfCancellationRequested();
        var path = PackagePath.Resolve(package.InstalledDirectory, package.Definition.Assets.Preview);
        using var stream = File.OpenRead(path);
        if (stream.Length > settings.Current.Security.MaxFileBytes ||
            !inspector.Inspect(stream, settings.Current.Security.MaxImageDimension).IsValid) return null;
        stream.Position = 0;
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var converted = new FormatConvertedBitmap(frame, PixelFormats.Pbgra32, null, 0);
        converted.Freeze();
        return converted;
    }, ct);
}
