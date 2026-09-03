using System.Windows.Media;
using System.IO;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Windows;

namespace DesktopPet.Windows.Voice;

public sealed class WindowsAudioPlaybackService : IAudioPlaybackService
{
    private readonly IUiDispatcher _dispatcher;
    private readonly string _tempRoot;
    private readonly object _sync = new();
    private MediaPlayer? _active;
    private string? _activePath;
    private bool _disposed;

    public WindowsAudioPlaybackService(IUiDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _tempRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "DesktopPet", "voice"));
        Directory.CreateDirectory(_tempRoot);
        foreach (var file in Directory.EnumerateFiles(_tempRoot, "speech-*.wav", SearchOption.TopDirectoryOnly))
            try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public async Task PlayAsync(SynthesizedSpeech audio, double volume,
        Func<CancellationToken, Task> onPlaybackStarting, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (audio.Format != SpeechAudioFormat.Wave) throw new TtsProviderException("unsupported_audio_format");
        var path = Path.Combine(_tempRoot, $"speech-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(path, audio.Audio.ToArray(), ct);
        try
        {
            await _dispatcher.InvokeAsync(async () =>
            {
                var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var player = new MediaPlayer { Volume = Math.Clamp(volume, 0, 1) };
                lock (_sync) { _active = player; _activePath = path; }
                async void Opened(object? sender, EventArgs e)
                {
                    try { await onPlaybackStarting(ct); ct.ThrowIfCancellationRequested(); player.Play(); }
                    catch (OperationCanceledException) { completed.TrySetCanceled(ct); }
                    catch (Exception exception) { completed.TrySetException(exception); }
                }
                void Ended(object? sender, EventArgs e) => completed.TrySetResult();
                void Failed(object? sender, ExceptionEventArgs e) => completed.TrySetException(
                    new TtsProviderException("audio_playback_failed", e.ErrorException));
                player.MediaOpened += Opened; player.MediaEnded += Ended; player.MediaFailed += Failed;
                using var registration = ct.Register(() => _ = _dispatcher.InvokeAsync(() =>
                { player.Stop(); completed.TrySetCanceled(ct); return Task.CompletedTask; }, CancellationToken.None));
                try { player.Open(new System.Uri(path, System.UriKind.Absolute)); await completed.Task; }
                finally
                {
                    player.MediaOpened -= Opened; player.MediaEnded -= Ended; player.MediaFailed -= Failed;
                    player.Stop(); player.Close();
                    lock (_sync) { if (ReferenceEquals(_active, player)) { _active = null; _activePath = null; } }
                }
            }, ct);
        }
        finally { TryDelete(path); }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        MediaPlayer? player;
        lock (_sync) player = _active;
        if (player is not null) await _dispatcher.InvokeAsync(() => { player.Stop(); player.Close(); return Task.CompletedTask; }, ct);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        MediaPlayer? player; string? path;
        lock (_sync) { player = _active; path = _activePath; _active = null; _activePath = null; }
        if (player is not null) _dispatcher.InvokeAsync(() => { player.Stop(); player.Close(); return Task.CompletedTask; }, CancellationToken.None).GetAwaiter().GetResult();
        if (path is not null) TryDelete(path);
    }
}
