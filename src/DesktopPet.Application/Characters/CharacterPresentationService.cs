using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;
using DesktopPet.Application.Runtime;
using DesktopPet.Application.Movement;

namespace DesktopPet.Application.Characters;

// A single window's presentation coordinator, NOT a behavior runtime or global pet state.
public sealed class CharacterPresentationService(ICharacterPackageService characters, ICharacterSeedSource seeds,
    ISettingsService settings, IAnimationSurface surface, IExceptionHandler exceptions, TimeProvider clock) : IDisposable, ICharacterPresentation, IBehaviorAnimationPlayer
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly AnimationResolver _resolver = new();
    private CancellationTokenSource? _playToken;
    private Task _playback = Task.CompletedTask;
    private bool _visible = true;
    private bool _stopped;
    private readonly PetInstanceId _instance = new(Guid.NewGuid());
    public CharacterPackage? Current { get; private set; }
    public event EventHandler? Changed;
    public Task SetMirroredAsync(bool mirrored, CancellationToken ct) =>
        surface is ICharacterVisualSurface visual ? visual.SetMirroredAsync(mirrored, ct) : Task.CompletedTask;

    public async Task InitializeAsync(CancellationToken ct, bool preferBehaviorReady = false)
    {
        _visible = settings.Current.PetWindow.IsVisible;
        var available = await characters.ListAsync(ct);
        if (available.Count == 0)
        {
            foreach (var path in seeds.GetDirectories()) await characters.ImportAsync(path, ct);
            available = await characters.ListAsync(ct);
        }
        var chosen = available.FirstOrDefault(package => package.Definition.Id.Value == settings.Current.ActiveCharacterId) ??
            (preferBehaviorReady ? available.OrderByDescending(package => package.Definition.Animations.Count).FirstOrDefault() : available.FirstOrDefault());
        if (chosen is not null) { await ActivateAsync(chosen.Definition.Id, ct); return; }
        // Corrupted user installations must not prevent a validated shipped fallback from displaying.
        foreach (var path in seeds.GetDirectories())
        {
            var result = await characters.ValidateAsync(path, ct);
            if (result is { CanInstall: true, Definition: not null })
            {
                await SwitchAsync(new(result.Definition, path), ct);
                return;
            }
        }
        throw new CharacterAssetException("No valid installed or shipped development character is available.");
    }
    public async Task<CharacterOperationResult> ActivateAsync(CharacterId id, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var result = await characters.ActivateAsync(id, ct);
            if (result.Package is { } package) await SwitchCoreAsync(package, ct);
            return result;
        }
        finally { _gate.Release(); }
    }
    private async Task SwitchAsync(CharacterPackage package, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { await SwitchCoreAsync(package, ct); }
        finally { _gate.Release(); }
    }
    private async Task SwitchCoreAsync(CharacterPackage package, CancellationToken ct)
    {
        await StopPlaybackAsync();
        await surface.SetPackageAsync(package, ct);
        Current = package;
        _stopped = false;
        await BeginAsync(AnimationSemantic.Idle, ct);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public async Task PlayAsync(AnimationSemantic semantic, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_stopped || Current is null) return;
            await StopPlaybackAsync();
            await BeginAsync(semantic, ct);
        }
        finally { _gate.Release(); }
    }
    public async Task PlayBehaviorAsync(AnimationSemantic semantic, TimeSpan minimum, TimeSpan maximum, bool repeat,
        Action<AnimationSemantic> resolved, CancellationToken ct)
    {
        if (minimum <= TimeSpan.Zero || maximum < minimum || maximum > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(maximum));
        await _gate.WaitAsync(ct);
        try
        {
            if (_stopped || !_visible || Current is null) { ct.ThrowIfCancellationRequested(); return; }
            await StopPlaybackAsync();
            using var deadline = new CancellationTokenSource(maximum, clock);
            using var active = CancellationTokenSource.CreateLinkedTokenSource(ct, deadline.Token);
            var started = clock.GetTimestamp();
            foreach (var candidate in _resolver.Candidates(Current.Definition, semantic))
            {
                try
                {
                    IAnimationProvider provider = candidate.Definition.Type == AnimationFormat.StaticPng
                        ? new StaticPngAnimationProvider(surface, clock) : new PngSequenceAnimationProvider(surface, clock);
                    await provider.PreloadAsync(candidate.Definition, active.Token);
                    ct.ThrowIfCancellationRequested();
                    resolved(candidate.Semantic);
                    try { await provider.PlayAsync(new(_instance, candidate.Semantic, repeat), active.Token); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested) { }
                    ct.ThrowIfCancellationRequested();
                    var remaining = minimum - clock.GetElapsedTime(started);
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining, clock, ct);
                    return;
                }
                catch (CharacterAssetException e) { exceptions.Report(e, ErrorCode.CommandFailed, ErrorOrigin.BackgroundTask); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested && deadline.IsCancellationRequested) { return; }
            }
            throw new CharacterAssetException("Every behavior animation fallback is unavailable.");
        }
        finally { _gate.Release(); }
    }
    private async Task BeginAsync(AnimationSemantic semantic, CancellationToken ct)
    {
        if (Current is null) return;
        var candidates = _resolver.Candidates(Current.Definition, semantic);
        _playToken = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // Ensure an actual first image is ready before startup/activation completes.
        var presented = false;
        foreach (var candidate in candidates)
        {
            try { await surface.PresentAsync(candidate.Definition.Frames![0].Path, ct); presented = true; break; }
            catch (CharacterAssetException e) { exceptions.Report(e, ErrorCode.CommandFailed, ErrorOrigin.Command); }
        }
        if (!presented) throw new CharacterAssetException("Every animation fallback is unavailable; revalidate or reinstall the character.");
        if (_visible) _playback = RunAsync(candidates, _playToken.Token);
    }
    private async Task RunAsync(IReadOnlyList<ResolvedAnimation> candidates, CancellationToken ct)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                IAnimationProvider provider = candidate.Definition.Type == AnimationFormat.StaticPng
                    ? new StaticPngAnimationProvider(surface, clock) : new PngSequenceAnimationProvider(surface, clock);
                await provider.PreloadAsync(candidate.Definition, ct);
                await provider.PlayAsync(new(_instance, candidate.Semantic, true), ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (CharacterAssetException e) { exceptions.Report(e, ErrorCode.CommandFailed, ErrorOrigin.Command); }
        }
    }
    public async Task SetVisibleAsync(bool visible, CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_visible == visible || _stopped) return;
            _visible = visible;
            await StopPlaybackAsync();
            if (visible) await BeginAsync(AnimationSemantic.Idle, ct);
        }
        finally { _gate.Release(); }
    }
    private async Task StopPlaybackAsync()
    {
        _playToken?.Cancel();
        try { await _playback; }
        finally
        {
            _playToken?.Dispose();
            _playToken = null;
            _playback = Task.CompletedTask;
        }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try { _stopped = true; await StopPlaybackAsync(); await surface.ClearAsync(ct); }
        finally { _gate.Release(); }
    }
    public void Dispose() { _playToken?.Cancel(); _playToken?.Dispose(); _gate.Dispose(); }
}
