using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Runtime;
using DesktopPet.CharacterSdk;

namespace DesktopPet.Application.Voice;

public sealed class SpeechService : ISpeechService, IDisposable
{
    private const int MaximumTextLength = 4096;
    private const int MaximumAudioBytes = 16 * 1024 * 1024;
    private readonly IReadOnlyDictionary<string, ITtsProvider> _providers;
    private readonly ISettingsService _settings;
    private readonly ICharacterPresentation _character;
    private readonly ICharacterVoiceProfileReader _profiles;
    private readonly VoiceProfilePolicy _policy;
    private readonly ILipSyncProvider _lipSync;
    private readonly IAudioPlaybackService _playback;
    private readonly ISpeechVisualController _visual;
    private readonly IAppLogger _logger;
    private readonly TimeProvider _clock;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _sync = new();
    private CancellationTokenSource? _active;
    private Task<SpeechResult> _current = Task.FromResult(new SpeechResult(SpeechStatus.Completed));
    private bool _isSpeaking;
    private bool _disposed;

    public SpeechService(IEnumerable<ITtsProvider> providers, ISettingsService settings, ICharacterPresentation character,
        ICharacterVoiceProfileReader profiles, VoiceProfilePolicy policy, ILipSyncProvider lipSync,
        IAudioPlaybackService playback, ISpeechVisualController visual, IAppLogger logger, TimeProvider clock)
    {
        _providers = providers.ToDictionary(item => item.ProviderId, StringComparer.OrdinalIgnoreCase);
        _settings = settings; _character = character; _profiles = profiles; _policy = policy;
        _lipSync = lipSync; _playback = playback; _visual = visual; _logger = logger; _clock = clock;
        _visual.SpeechInterruptionRequested += OnInterruptionRequested;
    }

    public bool IsSpeaking { get { lock (_sync) return _isSpeaking; } }
    public event EventHandler? StateChanged;
    public IReadOnlyList<string> ProviderIds => _providers.Keys.Order(StringComparer.Ordinal).ToArray();

    public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(string providerId, CancellationToken ct) =>
        _providers.TryGetValue(providerId, out var provider) ? provider.GetVoicesAsync(ct) :
        Task.FromResult<IReadOnlyList<TtsVoice>>([]);

    public async Task<VoiceProfile> ResolveVoiceAsync(CancellationToken ct)
    {
        var catalogs = new Dictionary<string, IReadOnlyList<TtsVoice>>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in _providers.Values)
        {
            try { catalogs[provider.ProviderId] = await provider.GetVoicesAsync(ct); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            { catalogs[provider.ProviderId] = []; }
        }
        CharacterVoiceProfile? recommendation = null;
        if (_character.Current is { } package) recommendation = await _profiles.ReadAsync(package, ct);
        return _policy.Resolve(_settings.Current.Voice, recommendation, catalogs, _settings.Current.Culture);
    }

    public Task<SpeechResult> SpeakAsync(SpeechRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Text) || request.Text.Length > MaximumTextLength)
            return Task.FromResult(new SpeechResult(SpeechStatus.Failed, ErrorCategory: "invalid_text"));
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _active?.Cancel();
            var previous = _current;
            var active = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            _active = active;
            _current = RunQueuedAsync(previous, request with { Text = request.Text.Trim() }, active);
            return _current;
        }
    }

    private async Task<SpeechResult> RunQueuedAsync(Task<SpeechResult> previous, SpeechRequest request, CancellationTokenSource active)
    {
        try { await previous.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
        catch { }
        try { return await RunAsync(request, active.Token).ConfigureAwait(false); }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_active, active)) _active = null;
            }
            active.Dispose();
        }
    }

    private async Task<SpeechResult> RunAsync(SpeechRequest request, CancellationToken ct)
    {
        var settings = _settings.Current.Voice;
        if (request.Locale is not ("zh-CN" or "en-US")) request = request with { Locale = _settings.Current.Culture };
        if (!settings.Enabled || request.Preference?.Equals("silent", StringComparison.OrdinalIgnoreCase) == true ||
            request.Origin != SpeechOrigin.Manual && (settings.SilentMode || !settings.AutoReadAi ||
                !_visual.IsSpeechEnvironmentAvailable || settings.SuppressDuringFocus && _visual.IsFocusActive))
            return new(SpeechStatus.Suppressed);

        try
        {
            var voice = request.ExplicitVoice ?? await ResolveVoiceAsync(ct).ConfigureAwait(false);
            if (!_providers.TryGetValue(voice.ProviderId, out var provider))
                throw new TtsProviderException("provider_unavailable");
            var synthesis = new SpeechSynthesisRequest(request.Text, request.Locale, voice,
                TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
            SynthesizedSpeech audio;
            try { audio = await provider.SynthesizeAsync(synthesis, ct).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException && provider.IsOnline && settings.OnlineFallback &&
                _providers.TryGetValue(VoiceProviderIds.Windows, out _))
            {
                var local = _providers[VoiceProviderIds.Windows];
                var localVoices = await local.GetVoicesAsync(ct).ConfigureAwait(false);
                if (localVoices.Count == 0) throw;
                var localVoice = localVoices.FirstOrDefault(item => item.Locale.Equals(request.Locale, StringComparison.OrdinalIgnoreCase)) ??
                    localVoices.FirstOrDefault(item => item.IsDefault) ?? localVoices[0];
                var fallback = voice with { ProviderId = VoiceProviderIds.Windows, VoiceId = localVoice.Id };
                audio = await local.SynthesizeAsync(synthesis with { Voice = fallback }, ct).ConfigureAwait(false);
            }
            if (audio.Audio.IsEmpty || audio.Audio.Length > MaximumAudioBytes) throw new TtsProviderException("invalid_audio");

            var entered = false;
            using var lipToken = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Task lipTask = Task.CompletedTask;
            try
            {
                await _playback.PlayAsync(audio, voice.Volume, async startedToken =>
                {
                    var visualMode = await _visual.EnterTalkingAsync(startedToken).ConfigureAwait(false);
                    entered = true;
                    SetSpeaking(true);
                    _logger.Write(new(AppEvent.SpeechStarted, _clock.GetUtcNow(), State: DesktopPet.Domain.Pets.PetPrimaryState.Talking));
                    if (visualMode == SpeechVisualMode.MouthFrames)
                        lipTask = DriveLipSyncAsync(audio, lipToken.Token);
                }, ct).ConfigureAwait(false);
                return new(SpeechStatus.Completed, audio.ProviderId);
            }
            finally
            {
                lipToken.Cancel();
                try { await lipTask.ConfigureAwait(false); } catch (OperationCanceledException) when (lipToken.IsCancellationRequested) { }
                if (entered) await _visual.ExitTalkingAsync(CancellationToken.None).ConfigureAwait(false);
                SetSpeaking(false);
                if (entered) _logger.Write(new(AppEvent.SpeechStopped, _clock.GetUtcNow()));
            }
        }
        catch (OperationCanceledException) { return new(SpeechStatus.Cancelled); }
        catch (TtsProviderException exception)
        {
            _logger.Write(new(AppEvent.SpeechFailed, _clock.GetUtcNow()));
            return new(SpeechStatus.Failed, ErrorCategory: exception.Category);
        }
        catch
        {
            _logger.Write(new(AppEvent.SpeechFailed, _clock.GetUtcNow()));
            return new(SpeechStatus.Failed, ErrorCategory: "speech_failed");
        }
    }

    private async Task DriveLipSyncAsync(SynthesizedSpeech audio, CancellationToken ct)
    {
        var started = _clock.GetTimestamp();
        await foreach (var frame in _lipSync.AnalyzeAsync(audio, ct).ConfigureAwait(false))
        {
            var delay = frame.Offset - _clock.GetElapsedTime(started);
            if (delay > TimeSpan.Zero) await Task.Delay(delay, _clock, ct).ConfigureAwait(false);
            await _visual.ApplyMouthFrameAsync(frame.MouthOpen, ct).ConfigureAwait(false);
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        Task current;
        lock (_sync) { _active?.Cancel(); current = _current; }
        await _playback.StopAsync(ct).ConfigureAwait(false);
        try { await current.WaitAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
    }

    private void OnInterruptionRequested(object? sender, EventArgs e) { lock (_sync) _active?.Cancel(); }
    private void SetSpeaking(bool value)
    {
        lock (_sync) { if (_isSpeaking == value) return; _isSpeaking = value; }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true; _lifetime.Cancel(); _active?.Cancel();
        }
        _visual.SpeechInterruptionRequested -= OnInterruptionRequested;
        _playback.Dispose(); _lifetime.Dispose();
    }
}
