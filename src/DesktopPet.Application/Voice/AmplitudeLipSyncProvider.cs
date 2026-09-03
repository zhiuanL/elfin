using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using DesktopPet.Application.Contracts;

namespace DesktopPet.Application.Voice;

public sealed class AmplitudeLipSyncProvider : ILipSyncProvider
{
    private const int FrameMilliseconds = 80;
    public async IAsyncEnumerable<LipSyncFrame> AnalyzeAsync(SynthesizedSpeech audio,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!TryReadPcm(audio.Audio.Span, out var pcm)) yield break;
        var samplesPerFrame = Math.Max(1, pcm.SampleRate * pcm.Channels * FrameMilliseconds / 1000);
        var bytesPerSample = pcm.BitsPerSample / 8;
        var frameBytes = samplesPerFrame * bytesPerSample;
        var open = false;
        for (var offset = 0; offset < pcm.Data.Length; offset += frameBytes)
        {
            ct.ThrowIfCancellationRequested();
            var frame = pcm.Data.Slice(offset, Math.Min(frameBytes, pcm.Data.Length - offset));
            var amplitude = AverageAmplitude(frame, pcm.BitsPerSample);
            var next = amplitude >= .035;
            if (next != open || offset == 0)
            {
                open = next;
                yield return new(TimeSpan.FromMilliseconds((double)offset / frameBytes * FrameMilliseconds), open, amplitude);
            }
            await Task.Yield();
        }
        if (open) yield return new(TimeSpan.FromMilliseconds((double)pcm.Data.Length / frameBytes * FrameMilliseconds), false, 0);
    }

    private static bool TryReadPcm(ReadOnlySpan<byte> wave, out PcmData pcm)
    {
        pcm = default;
        if (wave.Length < 44 || !wave[..4].SequenceEqual("RIFF"u8) || !wave.Slice(8, 4).SequenceEqual("WAVE"u8)) return false;
        ushort format = 0, channels = 0, bits = 0; var rate = 0; ReadOnlySpan<byte> data = default;
        for (var cursor = 12; cursor + 8 <= wave.Length;)
        {
            var size = BinaryPrimitives.ReadInt32LittleEndian(wave.Slice(cursor + 4, 4));
            if (size < 0 || cursor + 8L + size > wave.Length) return false;
            var chunk = wave.Slice(cursor + 8, size);
            if (wave.Slice(cursor, 4).SequenceEqual("fmt "u8) && chunk.Length >= 16)
            {
                format = BinaryPrimitives.ReadUInt16LittleEndian(chunk[..2]);
                channels = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(2, 2));
                rate = BinaryPrimitives.ReadInt32LittleEndian(chunk.Slice(4, 4));
                bits = BinaryPrimitives.ReadUInt16LittleEndian(chunk.Slice(14, 2));
            }
            else if (wave.Slice(cursor, 4).SequenceEqual("data"u8)) data = chunk;
            cursor += 8 + size + (size & 1);
        }
        if (format != 1 || channels == 0 || rate <= 0 || bits is not (8 or 16) || data.IsEmpty) return false;
        pcm = new(data.ToArray(), rate, channels, bits);
        return true;
    }

    private static double AverageAmplitude(ReadOnlySpan<byte> data, ushort bits)
    {
        if (data.IsEmpty) return 0;
        double total = 0; var samples = 0;
        if (bits == 8) foreach (var value in data) { total += Math.Abs(value - 128) / 128d; samples++; }
        else for (var index = 0; index + 1 < data.Length; index += 2)
        { total += Math.Abs(BinaryPrimitives.ReadInt16LittleEndian(data.Slice(index, 2)) / 32768d); samples++; }
        return samples == 0 ? 0 : total / samples;
    }

    private readonly record struct PcmData(ReadOnlyMemory<byte> Bytes, int SampleRate, ushort Channels, ushort BitsPerSample)
    { public ReadOnlySpan<byte> Data => Bytes.Span; }
}
