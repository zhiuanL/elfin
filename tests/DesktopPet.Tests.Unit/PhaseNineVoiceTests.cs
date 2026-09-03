using System.Net;
using System.Text;
using System.Text.Json;
using DesktopPet.AI.Contracts;
using DesktopPet.AI.Providers;
using DesktopPet.AI.Services;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Runtime;
using DesktopPet.Application.Voice;
using DesktopPet.Application.Commands;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Tests.Unit;

public sealed class PhaseNineVoiceTests
{
    [Fact]
    public void VoicePolicyAppliesUserThenCharacterThenSafeDefaults()
    {
        var policy = new VoiceProfilePolicy();
        var catalogs = Catalogs();
        var recommended = policy.Resolve(new VoiceSettings(), new("openai", "coral", .8, .7), catalogs, "zh-CN");
        Assert.Equal(VoiceProviderIds.OpenAI, recommended.ProviderId);
        Assert.Equal("coral", recommended.VoiceId);
        Assert.Equal(.8, recommended.Speed);
        var user = policy.Resolve(new VoiceSettings { Provider = TtsProviderKind.Windows, ProviderUserOverride = true,
            VoiceId = "en", VoiceUserOverride = true, Speed = 1.4, SpeedUserOverride = true }, recommendation: new("openai", "coral", .8, .7), catalogs, "zh-CN");
        Assert.Equal(VoiceProviderIds.Windows, user.ProviderId);
        Assert.Equal("en", user.VoiceId);
        Assert.Equal(1.4, user.Speed);
    }

    [Fact]
    public void InvalidCharacterProviderAndVoiceFallBackWithoutRejectingLocale()
    {
        var policy = new VoiceProfilePolicy();
        var result = policy.Resolve(new VoiceSettings(), new("untrusted", "missing", 99, -4), Catalogs(), "zh-CN");
        Assert.Equal(VoiceProviderIds.Windows, result.ProviderId);
        Assert.Equal("zh", result.VoiceId);
        Assert.Equal(1, result.Speed);
        Assert.Equal(1, result.Volume);
    }

    [Fact]
    public async Task AmplitudeLipSyncEmitsClosedOpenClosedAndNoVisemes()
    {
        var frames = new List<LipSyncFrame>();
        await foreach (var frame in new AmplitudeLipSyncProvider().AnalyzeAsync(
            new(CreateWave(), SpeechAudioFormat.Wave, VoiceProviderIds.Windows, "test"), default)) frames.Add(frame);
        Assert.False(frames[0].MouthOpen);
        Assert.Contains(frames, item => item.MouthOpen);
        Assert.False(frames[^1].MouthOpen);
    }

    [Fact]
    public async Task DisabledAndSilentAutomaticSpeechAreSuppressedBeforeSynthesis()
    {
        var provider = new FakeTtsProvider(VoiceProviderIds.Windows, false);
        using var service = Service(provider, new VoiceSettings { Enabled = false }, out _);
        Assert.Equal(SpeechStatus.Suppressed, (await service.SpeakAsync(Request(), default)).Status);
        Assert.Equal(0, provider.Calls);
        using var silent = Service(provider, new VoiceSettings { Enabled = true, AutoReadAi = true, SilentMode = true }, out _);
        Assert.Equal(SpeechStatus.Suppressed, (await silent.SpeakAsync(Request(SpeechOrigin.AiAutomatic), default)).Status);
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public async Task HiddenLockedOrFocusEnvironmentSuppressesAutomaticSpeechAndSilentCommandUsesRegistry()
    {
        var provider = new FakeTtsProvider(VoiceProviderIds.Windows, false);
        using var service = Service(provider, new VoiceSettings { AutoReadAi = true }, out var visual);
        visual.IsSpeechEnvironmentAvailable = false;
        Assert.Equal(SpeechStatus.Suppressed, (await service.SpeakAsync(Request(SpeechOrigin.AiAutomatic), default)).Status);
        Assert.Equal(0, provider.Calls);
        visual.IsSpeechEnvironmentAvailable = true;
        visual.IsFocusActive = true;
        Assert.Equal(SpeechStatus.Suppressed, (await service.SpeakAsync(Request(SpeechOrigin.AiAutomatic), default)).Status);
        Assert.Equal(0, provider.Calls);

        var settings = new TestSettingsService();
        var registry = new CommandRegistry([new VoiceSettingsCommand(settings)]);
        Assert.Equal(CommandStatus.Completed, (await registry.ExecuteAsync(CommandId.ToggleSilentMode, default)).Status);
        Assert.True(settings.Current.Voice.SilentMode);
    }

    [Fact]
    public async Task OnlineFailureFallsBackToLocalAndTalkingAlwaysExits()
    {
        var online = new FakeTtsProvider(VoiceProviderIds.OpenAI, true) { Failure = new TtsProviderException("http_500") };
        var local = new FakeTtsProvider(VoiceProviderIds.Windows, false);
        var settings = new VoiceSettings { Provider = TtsProviderKind.OpenAI, ProviderUserOverride = true, OnlineFallback = true };
        using var service = Service([online, local], settings, out var visual);
        var result = await service.SpeakAsync(Request(), default);
        Assert.Equal(SpeechStatus.Completed, result.Status);
        Assert.Equal(1, online.Calls); Assert.Equal(1, local.Calls);
        Assert.Equal(1, visual.EnterCount); Assert.Equal(1, visual.ExitCount);
        Assert.False(service.IsSpeaking);
    }

    [Fact]
    public async Task StopCancelsPlaybackAndPreventsOverlap()
    {
        var provider = new FakeTtsProvider(VoiceProviderIds.Windows, false);
        var playback = new BlockingPlayback();
        using var service = Service([provider], new VoiceSettings(), out var visual, playback);
        var first = service.SpeakAsync(Request(), default);
        await visual.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        visual.Interrupt();
        Assert.Equal(SpeechStatus.Cancelled, (await first).Status);
        var second = service.SpeakAsync(Request(), default);
        await playback.SecondStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await service.StopAsync(default);
        Assert.Equal(SpeechStatus.Cancelled, (await second).Status);
        Assert.Equal(1, playback.MaximumConcurrent);
        Assert.Equal(2, visual.ExitCount);
    }

    [Fact]
    public async Task PlaybackFailureRecoversTalkingAndLogsNoSpeechText()
    {
        var provider = new FakeTtsProvider(VoiceProviderIds.Windows, false);
        var logger = new RecordingLogger(); var visual = new FakeVisual();
        using var service = new SpeechService([provider], new TestSettingsService(), new NullCharacter(),
            new NullVoiceProfileReader(), new VoiceProfilePolicy(), new FakeLipSync(), new FailingPlayback(), visual, logger, TimeProvider.System);
        var result = await service.SpeakAsync(Request(), default);
        Assert.Equal(SpeechStatus.Failed, result.Status);
        Assert.Equal(1, visual.EnterCount); Assert.Equal(1, visual.ExitCount);
        Assert.DoesNotContain("hello", JsonSerializer.Serialize(logger.Entries), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OpenAiTtsUsesSpeechEndpointWavAndCredentialWithoutLeakingText()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new ByteArrayContent(CreateWave()) });
        var provider = OpenAi(handler, out var delay);
        var audio = await provider.SynthesizeAsync(Synthesis(), default);
        Assert.Equal(SpeechAudioFormat.Wave, audio.Format);
        Assert.Equal("Bearer secret", handler.Authorization);
        Assert.EndsWith("/v1/audio/speech", handler.Uri!.AbsoluteUri);
        using var json = JsonDocument.Parse(handler.Body!);
        Assert.Equal("gpt-4o-mini-tts", json.RootElement.GetProperty("model").GetString());
        Assert.Equal("wav", json.RootElement.GetProperty("response_format").GetString());
        Assert.Equal("private words", json.RootElement.GetProperty("input").GetString());
        Assert.Empty(delay.Delays);
    }

    [Fact]
    public async Task OpenAiTtsDoesNotRetryForbiddenAndHonorsTimeoutAndCancellation()
    {
        var forbiddenHandler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.Forbidden));
        var forbidden = OpenAi(forbiddenHandler, out var delays);
        var error = await Assert.ThrowsAsync<TtsProviderException>(() => forbidden.SynthesizeAsync(Synthesis(), default));
        Assert.Equal("http_403", error.Category); Assert.Empty(delays.Delays); Assert.Equal(1, forbiddenHandler.Calls);

        var timeoutHandler = new RecordingHandler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return new(); });
        var timeout = OpenAi(timeoutHandler, out _);
        var timedOut = await Assert.ThrowsAsync<TtsProviderException>(() => timeout.SynthesizeAsync(Synthesis(TimeSpan.FromMilliseconds(20)), default));
        Assert.Equal("timeout", timedOut.Category);

        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => timeout.SynthesizeAsync(Synthesis(), cancelled.Token));
    }

    [Fact]
    public async Task OpenAiTimeoutCoversSuccessfulHeadersAndStalledAudioBody()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StreamContent(new BlockingReadStream()) });
        var provider = OpenAi(handler, out _);
        var error = await Assert.ThrowsAsync<TtsProviderException>(() =>
            provider.SynthesizeAsync(Synthesis(TimeSpan.FromMilliseconds(20)), default));
        Assert.Equal("timeout", error.Category);
    }

    [Fact]
    public async Task OpenAiRetryableFailureUsesRepositoryBackoffSequence()
    {
        var handler = new RecordingHandler((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var provider = OpenAi(handler, out var delay);
        var error = await Assert.ThrowsAsync<TtsProviderException>(() => provider.SynthesizeAsync(Synthesis(), default));
        Assert.Equal("http_500", error.Category);
        Assert.Equal([1, 3, 7, 15], delay.Delays.Select(item => (int)item.TotalSeconds));
        Assert.Equal(5, handler.Calls);
    }

    [Fact]
    public void ResponseInterpreterAllowsOnlyControlledTtsHints()
    {
        var interpreter = new ResponseInterpreter(new NullCharacter());
        Assert.Equal("calm", interpreter.Interpret("ok<pet-hint>{\"ttsPreference\":\"calm\"}</pet-hint>").Hint!.TtsPreference);
        Assert.Null(interpreter.Interpret("ok<pet-hint>{\"ttsPreference\":\"openai-url\"}</pet-hint>").Hint);
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<TtsVoice>> Catalogs() => new Dictionary<string, IReadOnlyList<TtsVoice>>(StringComparer.OrdinalIgnoreCase)
    {
        [VoiceProviderIds.Windows] = [new("en", "English", "en-US", true), new("zh", "Chinese", "zh-CN")],
        [VoiceProviderIds.OpenAI] = [new("alloy", "alloy", "mul", true), new("coral", "coral", "mul")]
    };
    private static SpeechRequest Request(SpeechOrigin origin = SpeechOrigin.Manual) => new(new(Guid.NewGuid()), "hello", "en-US", origin);
    private static SpeechSynthesisRequest Synthesis(TimeSpan? timeout = null) => new("private words", "en-US",
        new(VoiceProviderIds.OpenAI, "alloy", 1, 1, "gpt-4o-mini-tts"), timeout ?? TimeSpan.FromSeconds(2));
    private static SpeechService Service(FakeTtsProvider provider, VoiceSettings settings, out FakeVisual visual) => Service([provider], settings, out visual);
    private static SpeechService Service(IEnumerable<ITtsProvider> providers, VoiceSettings voice, out FakeVisual visual, IAudioPlaybackService? playback = null)
    {
        visual = new();
        var settings = new TestSettingsService { Current = new AppSettings { Voice = voice } };
        return new(providers, settings, new NullCharacter(), new NullVoiceProfileReader(), new VoiceProfilePolicy(),
            new FakeLipSync(), playback ?? new ImmediatePlayback(), visual, new RecordingLogger(), TimeProvider.System);
    }
    private static OpenAiTtsProvider OpenAi(RecordingHandler handler, out RecordingDelay delay)
    {
        delay = new();
        var profile = new AiProviderProfile(Guid.NewGuid(), AiProviderType.OpenAI, "OpenAI", new("https://api.openai.com/v1/"),
            "chat", TimeSpan.FromSeconds(5), new("saved:test"), true, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        return new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, new FakeVault(), new FakeProfiles(profile), delay);
    }
    private static byte[] CreateWave()
    {
        const int rate = 1000; const int samples = 240; var data = new byte[samples * 2];
        for (var index = 80; index < 160; index++) BitConverter.GetBytes((short)12000).CopyTo(data, index * 2);
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.ASCII, true);
        writer.Write("RIFF"u8); writer.Write(36 + data.Length); writer.Write("WAVEfmt "u8); writer.Write(16); writer.Write((short)1);
        writer.Write((short)1); writer.Write(rate); writer.Write(rate * 2); writer.Write((short)2); writer.Write((short)16);
        writer.Write("data"u8); writer.Write(data.Length); writer.Write(data); return stream.ToArray();
    }

    private sealed class FakeTtsProvider(string id, bool online) : ITtsProvider
    {
        public string ProviderId => id; public bool IsOnline => online; public int Calls { get; private set; } public Exception? Failure { get; init; }
        public Task<IReadOnlyList<TtsVoice>> GetVoicesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<TtsVoice>>(
            id == VoiceProviderIds.OpenAI ? [new("alloy", "alloy", "mul", true)] : [new("local", "Local", "en-US", true)]);
        public Task<SynthesizedSpeech> SynthesizeAsync(SpeechSynthesisRequest request, CancellationToken ct)
        { Calls++; if (Failure is not null) throw Failure; return Task.FromResult(new SynthesizedSpeech(CreateWave(), SpeechAudioFormat.Wave, id, request.Voice.VoiceId)); }
    }
    private sealed class NullCharacter : ICharacterPresentation
    { public CharacterPackage? Current => null; public Task<CharacterOperationResult> ActivateAsync(CharacterId id, CancellationToken ct) => throw new NotSupportedException(); public Task PlayAsync(AnimationSemantic semantic, CancellationToken ct) => Task.CompletedTask; }
    private sealed class NullVoiceProfileReader : ICharacterVoiceProfileReader
    { public Task<CharacterVoiceProfile?> ReadAsync(CharacterPackage package, CancellationToken ct) => Task.FromResult<CharacterVoiceProfile?>(null); }
    private sealed class FakeLipSync : ILipSyncProvider
    { public async IAsyncEnumerable<LipSyncFrame> AnalyzeAsync(SynthesizedSpeech audio, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct) { await Task.CompletedTask; yield return new(TimeSpan.Zero, true, 1); } }
    private sealed class FakeVisual : ISpeechVisualController
    {
        public bool IsSpeechEnvironmentAvailable { get; set; } = true; public bool IsFocusActive { get; set; } public int EnterCount { get; private set; } public int ExitCount { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public event EventHandler? SpeechInterruptionRequested;
        public Task<SpeechVisualMode> EnterTalkingAsync(CancellationToken ct) { EnterCount++; Entered.TrySetResult(); return Task.FromResult(SpeechVisualMode.Talking); }
        public Task ApplyMouthFrameAsync(bool open, CancellationToken ct) => Task.CompletedTask;
        public Task ExitTalkingAsync(CancellationToken ct) { ExitCount++; return Task.CompletedTask; }
        public void Interrupt() => SpeechInterruptionRequested?.Invoke(this, EventArgs.Empty);
    }
    private class ImmediatePlayback : IAudioPlaybackService
    { public virtual async Task PlayAsync(SynthesizedSpeech audio, double volume, Func<CancellationToken, Task> start, CancellationToken ct) => await start(ct); public Task StopAsync(CancellationToken ct) => Task.CompletedTask; public void Dispose() { } }
    private sealed class BlockingPlayback : ImmediatePlayback
    {
        private int _concurrent, _starts; public int MaximumConcurrent { get; private set; }
        public TaskCompletionSource SecondStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task PlayAsync(SynthesizedSpeech audio, double volume, Func<CancellationToken, Task> start, CancellationToken ct)
        { var current = Interlocked.Increment(ref _concurrent); MaximumConcurrent = Math.Max(MaximumConcurrent, current); try { await start(ct); if (Interlocked.Increment(ref _starts) == 2) SecondStarted.TrySetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, ct); } finally { Interlocked.Decrement(ref _concurrent); } }
    }
    private sealed class FailingPlayback : ImmediatePlayback
    { public override async Task PlayAsync(SynthesizedSpeech audio, double volume, Func<CancellationToken, Task> start, CancellationToken ct) { await start(ct); throw new InvalidOperationException("device failed"); } }
    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        public int Calls { get; private set; } public Uri? Uri { get; private set; } public string? Authorization { get; private set; } public byte[]? Body { get; private set; }
        public RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> send) : this((r, c) => Task.FromResult(send(r, c))) { }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        { Calls++; Uri = request.RequestUri; Authorization = request.Headers.Authorization?.ToString(); Body = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct); return await send(request, ct); }
    }
    private sealed class RecordingDelay : IAiRetryDelay
    { public List<TimeSpan> Delays { get; } = []; public Task DelayAsync(TimeSpan delay, CancellationToken ct) { Delays.Add(delay); return Task.CompletedTask; } }
    private sealed class FakeVault : IAiCredentialVault
    {
        public Task<SecretReference> StoreAsync(Guid id, ReadOnlyMemory<char> key, CredentialPersistence persistence, CancellationToken ct) => throw new NotSupportedException();
        public Task<byte[]?> ReadAsync(SecretReference reference, CancellationToken ct) => Task.FromResult<byte[]?>(Encoding.UTF8.GetBytes("secret"));
        public Task DeleteAsync(SecretReference reference, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class FakeProfiles(AiProviderProfile profile) : IAiProviderProfileRepository
    {
        public Task<IReadOnlyList<AiProviderProfile>> ListAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<AiProviderProfile>>([profile]);
        public Task<AiProviderProfile?> GetAsync(Guid id, CancellationToken ct) => Task.FromResult<AiProviderProfile?>(profile);
        public Task SaveAsync(AiProviderProfile value, CancellationToken ct) => Task.CompletedTask; public Task SetActiveAsync(Guid id, CancellationToken ct) => Task.CompletedTask; public Task DeleteAsync(Guid id, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return 0; }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
