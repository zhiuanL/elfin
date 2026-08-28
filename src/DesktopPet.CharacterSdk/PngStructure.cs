using System.Buffers.Binary;

namespace DesktopPet.CharacterSdk;

public static class PngStructure
{
    private static readonly uint[] CrcTable = CreateTable();
    public static PngInfo Inspect(Stream stream, int maximumDimension)
    {
        Span<byte> signature = stackalloc byte[8];
        stream.ReadExactly(signature);
        if (!signature.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return new(0, 0, false);
        var first = true;
        var hasData = false;
        var width = 0;
        var height = 0;
        Span<byte> header = stackalloc byte[8];
        Span<byte> checksum = stackalloc byte[4];
        while (stream.Position < stream.Length)
        {
            stream.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header[..4]);
            if (length > int.MaxValue || length > stream.Length - stream.Position - 4) return new(width, height, false);
            var type = System.Text.Encoding.ASCII.GetString(header[4..]);
            if (first && (type != "IHDR" || length != 13)) return new(0, 0, false);
            var data = new byte[(int)length];
            stream.ReadExactly(data);
            stream.ReadExactly(checksum);
            var crc = uint.MaxValue;
            foreach (var b in header[4..]) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8);
            foreach (var b in data) crc = CrcTable[(crc ^ b) & 255] ^ (crc >> 8);
            if (~crc != BinaryPrimitives.ReadUInt32BigEndian(checksum)) return new(width, height, false);
            if (type == "IHDR")
            {
                if (!first) return new(width, height, false);
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0, 4)));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4, 4)));
                if (width <= 0 || height <= 0 || width > maximumDimension || height > maximumDimension) return new(width, height, false);
            }
            first = false;
            if (type == "acTL") return new(width, height, false); // APNG is not a PNG sequence package.
            if (type == "IDAT") hasData = true;
            if (type == "IEND") return new(width, height, hasData && length == 0 && stream.Position == stream.Length);
        }
        return new(width, height, false);
    }
    private static uint[] CreateTable()
    {
        var result = new uint[256];
        for (uint i = 0; i < result.Length; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) == 0 ? value >> 1 : 0xedb88320U ^ (value >> 1);
            result[i] = value;
        }
        return result;
    }
}
