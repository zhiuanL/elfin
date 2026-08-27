using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Contracts;

public sealed record VoiceProfile(string ProviderId, string VoiceId, double Speed, double Volume);
public sealed record SpeechRequest(PetInstanceId InstanceId, string Text, string Locale, VoiceProfile Voice);
public interface ITtsProvider
{
    string ProviderId { get; }
    Task SpeakAsync(SpeechRequest request, CancellationToken ct);
}
public sealed record LipSyncFrame(TimeSpan Offset, double MouthOpenness, string? Viseme);
public interface ILipSyncProvider
{
    IAsyncEnumerable<LipSyncFrame> AnalyzeAsync(Stream audio, CancellationToken ct);
}
public interface ISpeechToTextProvider
{
    Task<string> TranscribeAsync(Stream audio, string locale, CancellationToken ct);
}
