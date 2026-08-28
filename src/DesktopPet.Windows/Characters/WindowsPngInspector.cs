using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;
using DesktopPet.CharacterSdk;

namespace DesktopPet.Windows.Characters;

public sealed class WindowsPngInspector : IPngInspector
{
    public PngInfo Inspect(Stream stream, int maxDimension)
    {
        try
        {
            var origin = stream.Position;
            var header = PngStructure.Inspect(stream, maxDimension);
            if (!header.IsValid) return header;
            stream.Position = origin;
            var decoder = new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1) return header with { IsValid = false };
            var frame = decoder.Frames[0];
            if (frame.PixelWidth != header.Width || frame.PixelHeight != header.Height) return header with { IsValid = false };
            // Force decoding every row, not just accepting a plausible PNG header.
            var stride = checked((frame.PixelWidth * frame.Format.BitsPerPixel + 7) / 8);
            var row = new byte[stride];
            for (var y = 0; y < frame.PixelHeight; y++) frame.CopyPixels(new Int32Rect(0, y, frame.PixelWidth, 1), row, stride, 0);
            return header;
        }
        catch (Exception e) when (e is IOException or FormatException or NotSupportedException or ArgumentException or OverflowException or COMException)
        { return new(0, 0, false); }
    }
}
