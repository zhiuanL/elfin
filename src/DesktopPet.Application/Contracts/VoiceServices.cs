using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Contracts;

public static class VoiceProviderIds
{
    public const string Windows = "windows";
    public const string OpenAI = "openai";
}

public enum SpeechOrigin { Manual, AiAutomatic, SystemAutomatic }
public enum SpeechStatus { Completed, Cancelled, Suppressed, Failed }
public enum SpeechAudioFormat { Wave }
public enum SpeechVisualMode { Compatible, Talking, MouthFrames }

public sealed record TtsVoice(string Id, string DisplayName, string Locale, bool IsDefault = false);
public sealed record VoiceProfile(string ProviderId, string VoiceId, double Speed, double Volume, string Model);
public sealed record SpeechRequest(PetInstanceId InstanceId, string Text, string Locale,
    SpeechOrigin Origin = SpeechOrigin.Manual, string? Preference = null, VoiceProfile? ExplicitVoice = null);
public sealed record SpeechSynthesisRequest(string Text, string Locale, VoiceProfile Voice, TimeSpan Timeout);
public sealed record SynthesizedSpeech(ReadOnlyMemory<byte> Audio, SpeechAudioFormat Format,
    string ProviderId, string VoiceId, TimeSpan? Duration = null);
public sealed record SpeechResult(SpeechStatus Status, string? ProviderId = null, string? ErrorCategory = null);
public sealed class TtsProviderException(string category, Exception? inner = null) : Exception(category, inner)
{ public string Category { get; } = category; }

public interface ITtsProvider
{
    string ProviderId { get; }
    bool IsOnline { get; }
    Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken ct);
    Task<SynthesizedSpeech> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken ct);
}

public sealed record LipSyncFrame(TimeSpan Offset, bool MouthOpen, double Amplitude);
public interface ILipSyncProvider
{
    IAsyncEnumerable<LipSyncFrame> AnalyzeAsync(SynthesizedSpeech audio, CancellationToken ct);
}

public interface IAudioPlaybackService : IDisposable
{
    Task PlayAsync(SynthesizedSpeech audio, double volume,
        Func<CancellationToken, Task> onPlaybackStarting, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

public interface ICharacterVoiceProfileReader
{
    Task<CharacterVoiceProfile?> ReadAsync(CharacterPackage package, CancellationToken ct);
}

public interface ISpeechVisualController
{
    bool IsSpeechEnvironmentAvailable { get; }
    bool IsFocusActive { get; }
    event EventHandler? SpeechInterruptionRequested;
    Task<SpeechVisualMode> EnterTalkingAsync(CancellationToken ct);
    Task ApplyMouthFrameAsync(bool open, CancellationToken ct);
    Task ExitTalkingAsync(CancellationToken ct);
}

public interface ISpeechService
{
    bool IsSpeaking { get; }
    event EventHandler? StateChanged;
    IReadOnlyList<string> ProviderIds { get; }
    Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(string providerId, CancellationToken ct);
    Task<VoiceProfile> ResolveVoiceAsync(CancellationToken ct);
    Task<SpeechResult> SpeakAsync(SpeechRequest request, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}

// Phase 9 reserves only the boundary. No microphone, recording or recognition implementation is registered.
public interface ISpeechToTextProvider
{
    Task<string> TranscribeAsync(Stream audio, string locale, CancellationToken ct);
}
