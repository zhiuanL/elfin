namespace DesktopPet.Application.Configuration;

public enum TtsProviderKind { Windows, OpenAI }

public sealed record VoiceSettings
{
    public bool Enabled { get; init; } = true;
    public bool AutoReadAi { get; init; }
    public bool SilentMode { get; init; }
    public bool SuppressDuringFocus { get; init; } = true;
    public bool OnlineFallback { get; init; } = true;
    public TtsProviderKind Provider { get; init; } = TtsProviderKind.Windows;
    public string VoiceId { get; init; } = string.Empty;
    public double Speed { get; init; } = 1;
    public double Volume { get; init; } = 1;
    public bool ProviderUserOverride { get; init; }
    public bool VoiceUserOverride { get; init; }
    public bool SpeedUserOverride { get; init; }
    public bool VolumeUserOverride { get; init; }
    public string OpenAiModel { get; init; } = "gpt-4o-mini-tts";
    public int RequestTimeoutSeconds { get; init; } = 30;

    public bool IsValid => Enum.IsDefined(Provider) && VoiceId.Length <= 256 &&
        double.IsFinite(Speed) && Speed is >= .5 and <= 2 &&
        double.IsFinite(Volume) && Volume is >= 0 and <= 1 &&
        !string.IsNullOrWhiteSpace(OpenAiModel) && OpenAiModel.Length <= 128 &&
        RequestTimeoutSeconds is >= 1 and <= 300;
}
