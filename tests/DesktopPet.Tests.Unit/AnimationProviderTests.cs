using DesktopPet.Application.Characters;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Tests.Unit;

public sealed class AnimationProviderTests
{
    [Fact]
    public async Task StaticFrameNeedsNoPlaybackTimerAndNonLoopSequenceKeepsItsOrder()
    {
        var surface = new RecordingSurface();
        var staticProvider = new StaticPngAnimationProvider(surface, TimeProvider.System);
        await staticProvider.PreloadAsync(new() { Type = AnimationFormat.StaticPng, Frames = [new("one.png", 1)] }, default);
        await staticProvider.PlayAsync(new(new(Guid.NewGuid()), new("idle"), true), default);
        Assert.Equal(new[] { "one.png" }, surface.Presented);
        surface.Presented.Clear();
        var sequence = new PngSequenceAnimationProvider(surface, TimeProvider.System);
        await sequence.PreloadAsync(new() { Type = AnimationFormat.PngSequence, Loop = false,
            Frames = [new("third.png", 1), new("first.png", 2), new("second.png", 3)] }, default);
        await sequence.PlayAsync(new(new(Guid.NewGuid()), new("happy"), true), default);
        Assert.Equal(new[] { "third.png", "first.png", "second.png" }, surface.Presented);
    }
    [Fact]
    public async Task LoopCanBeStoppedAndCancellationPropagates()
    {
        using var stop = new CancellationTokenSource();
        var surface = new RecordingSurface { AfterPresent = count => { if (count == 5) stop.Cancel(); } };
        var provider = new PngSequenceAnimationProvider(surface, TimeProvider.System);
        await provider.PreloadAsync(new() { Type = AnimationFormat.PngSequence, Loop = true, Frames = [new("a.png", 1), new("b.png", 1)] }, default);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.PlayAsync(new(new(Guid.NewGuid()), new("idle"), true), stop.Token));
        Assert.Equal(5, surface.Presented.Count);
        surface.AfterPresent = _ => provider.Stop();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.PlayAsync(new(new(Guid.NewGuid()), new("idle"), true), default));
    }
    [Fact]
    public async Task RequestCanDisableAnAuthoredLoopAndProvidersRejectWrongFormats()
    {
        var surface = new RecordingSurface();
        var provider = new PngSequenceAnimationProvider(surface, TimeProvider.System);
        var animation = new AnimationDefinition { Type = AnimationFormat.PngSequence, Frames = [new("a.png", 1)], Loop = true };
        await provider.PreloadAsync(animation, default);
        await provider.PlayAsync(new(new(Guid.NewGuid()), new("idle"), false), default);
        Assert.Single(surface.Presented);
        await Assert.ThrowsAsync<ArgumentException>(() => new StaticPngAnimationProvider(surface, TimeProvider.System).PreloadAsync(animation, default));
    }
    internal sealed class RecordingSurface : IAnimationSurface
    {
        public List<string> Presented { get; } = [];
        public Action<int>? AfterPresent { get; set; }
        public Task SetPackageAsync(CharacterPackage package, CancellationToken ct) => Task.CompletedTask;
        public Task PreloadAsync(string resourcePath, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task PresentAsync(string resourcePath, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            Presented.Add(resourcePath);
            AfterPresent?.Invoke(Presented.Count);
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken ct) { Presented.Clear(); return Task.CompletedTask; }
    }
}
