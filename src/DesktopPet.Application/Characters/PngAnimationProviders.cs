using DesktopPet.CharacterSdk;

namespace DesktopPet.Application.Characters;

public abstract class PngAnimationProvider(IAnimationSurface surface, TimeProvider clock) : IAnimationProvider
{
    private AnimationDefinition? _prepared;
    private CancellationTokenSource? _playing;
    public abstract bool CanRender(AnimationDefinition definition);
    public async Task PreloadAsync(AnimationDefinition definition, CancellationToken ct)
    {
        if (!CanRender(definition) || definition.Frames is not { Count: > 0 }) throw new ArgumentException("Unsupported or unresolved animation.", nameof(definition));
        _prepared = definition;
        // Decode lazily beyond the first frame; the platform owns a bounded per-character cache.
        await surface.PreloadAsync(definition.Frames[0].Path, ct);
    }
    public async Task PlayAsync(AnimationRequest request, CancellationToken ct)
    {
        var animation = _prepared ?? throw new InvalidOperationException("Preload a validated animation before playback.");
        Stop();
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _playing = lifetime;
        try
        {
            var token = lifetime.Token;
            if (animation.Type == AnimationFormat.StaticPng)
            {
                await surface.PresentAsync(animation.Frames![0].Path, token);
                return;
            }
            var started = clock.GetTimestamp();
            var deadline = TimeSpan.Zero;
            do
            {
                foreach (var frame in animation.Frames!)
                {
                    token.ThrowIfCancellationRequested();
                    await surface.PresentAsync(frame.Path, token);
                    deadline += TimeSpan.FromMilliseconds(frame.DurationMs!.Value);
                    var remaining = deadline - clock.GetElapsedTime(started);
                    if (remaining > TimeSpan.Zero) await Task.Delay(remaining, clock, token);
                    else deadline = clock.GetElapsedTime(started); // Do not replay an unbounded backlog after suspension.
                }
            } while (animation.Loop && request.Loop);
        }
        finally { if (ReferenceEquals(_playing, lifetime)) _playing = null; }
    }
    public void Stop() => _playing?.Cancel();
}
public sealed class StaticPngAnimationProvider(IAnimationSurface surface, TimeProvider clock) : PngAnimationProvider(surface, clock)
{
    public override bool CanRender(AnimationDefinition definition) => definition.Type == AnimationFormat.StaticPng;
}
public sealed class PngSequenceAnimationProvider(IAnimationSurface surface, TimeProvider clock) : PngAnimationProvider(surface, clock)
{
    public override bool CanRender(AnimationDefinition definition) => definition.Type == AnimationFormat.PngSequence;
}
