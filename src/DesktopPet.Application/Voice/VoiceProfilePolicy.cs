using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.CharacterSdk;

namespace DesktopPet.Application.Voice;

public sealed class VoiceProfilePolicy
{
    public VoiceProfile Resolve(VoiceSettings settings, CharacterVoiceProfile? recommendation,
        IReadOnlyDictionary<string, IReadOnlyList<TtsVoice>> catalogs, string locale)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(catalogs);
        var recommendedProvider = Normalize(recommendation?.Provider);
        var requestedProvider = settings.ProviderUserOverride ? ToId(settings.Provider) : recommendedProvider;
        var provider = IsAvailable(requestedProvider, catalogs) ? requestedProvider! :
            IsAvailable(VoiceProviderIds.Windows, catalogs) ? VoiceProviderIds.Windows :
            catalogs.FirstOrDefault(pair => pair.Value.Count > 0).Key ?? throw new TtsProviderException("no_tts_provider");
        var voices = catalogs[provider];
        var requestedVoice = settings.VoiceUserOverride ? settings.VoiceId :
            recommendedProvider == provider ? recommendation?.Voice : null;
        var voice = voices.FirstOrDefault(item => item.Id.Equals(requestedVoice, StringComparison.OrdinalIgnoreCase)) ??
            voices.FirstOrDefault(item => LocaleMatches(item.Locale, locale)) ?? voices.FirstOrDefault(item => item.IsDefault) ?? voices[0];
        var speed = settings.SpeedUserOverride ? settings.Speed : ValidSpeed(recommendation?.Speed) ?? 1;
        var volume = settings.VolumeUserOverride ? settings.Volume : ValidVolume(recommendation?.Volume) ?? 1;
        return new(provider, voice.Id, Math.Clamp(speed, .5, 2), Math.Clamp(volume, 0, 1), settings.OpenAiModel);
    }

    public static string ToId(TtsProviderKind provider) => provider switch
    {
        TtsProviderKind.Windows => VoiceProviderIds.Windows,
        TtsProviderKind.OpenAI => VoiceProviderIds.OpenAI,
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    private static string? Normalize(string? provider) => provider?.Trim().ToLowerInvariant() switch
    {
        "windows" or "windows-local" or "local" => VoiceProviderIds.Windows,
        "openai" => VoiceProviderIds.OpenAI,
        _ => null
    };
    private static bool IsAvailable(string? provider, IReadOnlyDictionary<string, IReadOnlyList<TtsVoice>> catalogs) =>
        provider is not null && catalogs.TryGetValue(provider, out var voices) && voices.Count > 0;
    private static bool LocaleMatches(string candidate, string requested) =>
        candidate.Equals(requested, StringComparison.OrdinalIgnoreCase) ||
        candidate.Split('-')[0].Equals(requested.Split('-')[0], StringComparison.OrdinalIgnoreCase);
    private static double? ValidSpeed(double? value) => value is { } number && double.IsFinite(number) && number is >= .5 and <= 2 ? number : null;
    private static double? ValidVolume(double? value) => value is { } number && double.IsFinite(number) && number is >= 0 and <= 1 ? number : null;
}
