using System.ComponentModel;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Windows;
using DesktopPet.CharacterSdk;

namespace DesktopPet.Windows.Characters;

public interface ICharacterImageSource : INotifyPropertyChanged { BitmapSource? Frame { get; } }
public sealed class WpfAnimationSurface(IUiDispatcher dispatcher, ISettingsService settings, IPngInspector inspector)
    : IAnimationSurface, ICharacterImageSource
{
    public const long CacheLimitBytes = 64 * 1024 * 1024;
    private readonly Dictionary<string, BitmapSource> _cache = new(StringComparer.Ordinal);
    private readonly LinkedList<string> _recent = new();
    private string? _root;
    private long _bytes;
    public BitmapSource? Frame { get; private set; }
    public long CachedBytes => _bytes;
    public event PropertyChangedEventHandler? PropertyChanged;
    public Task SetPackageAsync(CharacterPackage package, CancellationToken ct) => dispatcher.InvokeAsync(() =>
    {
        _root = package.InstalledDirectory;
        ClearCache();
        return Task.CompletedTask;
    }, ct);
    public async Task PreloadAsync(string resourcePath, CancellationToken ct) => _ = await LoadAsync(resourcePath, ct);
    public async Task PresentAsync(string resourcePath, CancellationToken ct)
    {
        var frame = await LoadAsync(resourcePath, ct);
        await dispatcher.InvokeAsync(() =>
        {
            Frame = frame;
            PropertyChanged?.Invoke(this, new(nameof(Frame)));
            return Task.CompletedTask;
        }, ct);
    }
    private async Task<BitmapSource> LoadAsync(string relative, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (_cache.TryGetValue(relative, out var cached))
        {
            _recent.Remove(relative);
            _recent.AddFirst(relative);
            return cached;
        }
        var root = _root ?? throw new InvalidOperationException("No character package selected.");
        var bitmap = await Task.Run(() => Decode(root, relative, ct), ct);
        ct.ThrowIfCancellationRequested();
        var bytes = (long)bitmap.PixelWidth * bitmap.PixelHeight * 4;
        while (_bytes + bytes > CacheLimitBytes && _recent.Last is { } oldest)
        {
            var item = _cache[oldest.Value];
            _bytes -= (long)item.PixelWidth * item.PixelHeight * 4;
            _cache.Remove(oldest.Value);
            _recent.RemoveLast();
        }
        if (bytes <= CacheLimitBytes) { _cache.Add(relative, bitmap); _recent.AddFirst(relative); _bytes += bytes; }
        return bitmap;
    }
    private BitmapSource Decode(string root, string relative, CancellationToken ct)
    {
        try
        {
            var path = PackagePath.Resolve(root, relative);
            var cursor = path;
            while (true)
            {
                if ((File.GetAttributes(cursor) & FileAttributes.ReparsePoint) != 0) throw new IOException("Resource link rejected.");
                if (cursor == Path.GetFullPath(root)) break;
                cursor = Path.GetDirectoryName(cursor) ?? throw new IOException("Invalid resource root.");
            }
            using var stream = File.OpenRead(path);
            if (stream.Length > settings.Current.Security.MaxFileBytes || !inspector.Inspect(stream, settings.Current.Security.MaxImageDimension).IsValid)
                throw new IOException("Invalid PNG resource.");
            ct.ThrowIfCancellationRequested();
            stream.Position = 0;
            var source = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
            var stride = checked(converted.PixelWidth * 4);
            var pixels = new byte[checked(stride * converted.PixelHeight)];
            converted.CopyPixels(pixels, stride, 0);
            // Normalize image metadata only; window DPI remains managed by the Phase 1 platform adapter.
            var result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, stride);
            result.Freeze();
            return result;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or FormatException or OverflowException or System.Runtime.InteropServices.COMException)
        { throw new CharacterAssetException("Character frame is unavailable.", e); }
    }
    public Task ClearAsync(CancellationToken ct) => dispatcher.InvokeAsync(() =>
    {
        ClearCache();
        _root = null;
        Frame = null;
        PropertyChanged?.Invoke(this, new(nameof(Frame)));
        return Task.CompletedTask;
    }, ct);
    private void ClearCache() { _cache.Clear(); _recent.Clear(); _bytes = 0; }
}
