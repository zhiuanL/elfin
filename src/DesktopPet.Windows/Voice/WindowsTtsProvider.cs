using System.Globalization;
using System.IO;
using System.Speech.Synthesis;
using DesktopPet.Application.Contracts;

namespace DesktopPet.Windows.Voice;

public sealed class WindowsTtsProvider : ITtsProvider
{
    public string ProviderId => VoiceProviderIds.Windows;
    public bool IsOnline => false;

    public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken ct) => Task.Run<IReadOnlyList<TtsVoice>>(() =>
    {
        ct.ThrowIfCancellationRequested();
        using var synth = new SpeechSynthesizer();
        var defaultName = synth.Voice?.Name;
        return synth.GetInstalledVoices().Where(item => item.Enabled).Select(item => new TtsVoice(
            item.VoiceInfo.Name, item.VoiceInfo.Description, item.VoiceInfo.Culture.Name,
            item.VoiceInfo.Name.Equals(defaultName, StringComparison.OrdinalIgnoreCase))).ToArray();
    }, ct);

    public Task<SynthesizedSpeech> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken ct) => Task.Run(async () =>
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            using var synth = new SpeechSynthesizer();
            var voices = synth.GetInstalledVoices().Where(item => item.Enabled).ToArray();
            if (voices.Length == 0) throw new TtsProviderException("windows_voice_unavailable");
            var selected = voices.FirstOrDefault(item => item.VoiceInfo.Name.Equals(request.Voice.VoiceId, StringComparison.OrdinalIgnoreCase)) ??
                voices.FirstOrDefault(item => item.VoiceInfo.Culture.Name.Equals(request.Locale, StringComparison.OrdinalIgnoreCase)) ??
                voices.FirstOrDefault(item => item.VoiceInfo.Culture.TwoLetterISOLanguageName.Equals(
                    SafeCulture(request.Locale).TwoLetterISOLanguageName, StringComparison.OrdinalIgnoreCase)) ?? voices[0];
            synth.SelectVoice(selected.VoiceInfo.Name);
            synth.Rate = Math.Clamp((int)Math.Round(Math.Log2(request.Voice.Speed) * 5), -10, 10);
            synth.Volume = Math.Clamp((int)Math.Round(request.Voice.Volume * 100), 0, 100);
            using var wave = new MemoryStream();
            synth.SetOutputToWaveStream(wave);
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Completed(object? sender, SpeakCompletedEventArgs args)
            {
                if (args.Cancelled) completion.TrySetCanceled();
                else if (args.Error is not null) completion.TrySetException(args.Error);
                else completion.TrySetResult();
            }
            synth.SpeakCompleted += Completed;
            using var registration = ct.Register(() => { try { synth.SpeakAsyncCancelAll(); } catch (ObjectDisposedException) { } });
            try
            {
                synth.SpeakAsync(request.Text);
                await completion.Task.ConfigureAwait(false);
            }
            finally
            {
                synth.SpeakCompleted -= Completed;
                synth.SetOutputToNull();
            }
            ct.ThrowIfCancellationRequested();
            return new SynthesizedSpeech(wave.ToArray(), SpeechAudioFormat.Wave, ProviderId, selected.VoiceInfo.Name);
        }
        catch (OperationCanceledException) { throw; }
        catch (TtsProviderException) { throw; }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        { throw new TtsProviderException("windows_synthesis_failed", exception); }
    }, ct);

    private static CultureInfo SafeCulture(string locale)
    {
        try { return CultureInfo.GetCultureInfo(locale); }
        catch (CultureNotFoundException) { return CultureInfo.GetCultureInfo("en-US"); }
    }
}
