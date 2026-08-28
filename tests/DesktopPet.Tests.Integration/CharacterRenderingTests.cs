using DesktopPet.Application.Characters;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;
using DesktopPet.Infrastructure.Characters;
using DesktopPet.Windows.Characters;

namespace DesktopPet.Tests.Integration;

public sealed class CharacterRenderingTests
{
    [Fact]
    public async Task RealPngIsFrozenTransparentAndDoesNotLockFilesOrLeakCacheAcrossSwitches()
    {
        using var context = new CharacterTestContext();
        var package = (await context.Manager.ImportAsync(context.CopyFixture(), default)).Package!;
        var surface = Surface(context);
        await surface.SetPackageAsync(package, default);
        await surface.PresentAsync("images/idle.png", default);
        Assert.NotNull(surface.Frame);
        Assert.True(surface.Frame.IsFrozen);
        Assert.Equal(256, surface.Frame.PixelWidth);
        var pixel = new byte[4];
        surface.Frame.CopyPixels(new System.Windows.Int32Rect(0, 0, 1, 1), pixel, 4, 0);
        Assert.Equal(0, pixel[3]);
        Assert.Equal(256 * 256 * 4, surface.CachedBytes);
        var first = surface.Frame;
        await surface.PresentAsync("images/idle.png", default);
        Assert.Same(first, surface.Frame);
        using (new FileStream(Path.Combine(package.InstalledDirectory, "images", "idle.png"), FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        var second = (await context.Manager.ImportAsync(context.CopyFixture("dev-standard"), default)).Package!;
        await surface.SetPackageAsync(second, default);
        Assert.Equal(0, surface.CachedBytes);
        await surface.PresentAsync("images/01.png", default);
        Assert.NotSame(first, surface.Frame);
        Assert.InRange(surface.CachedBytes, 1, WpfAnimationSurface.CacheLimitBytes);
        await surface.ClearAsync(default);
        Assert.Null(surface.Frame);
        Assert.Equal(0, surface.CachedBytes);
    }
    [Fact]
    public async Task FrameCacheHasAnEnforcedBoundAndEvictsLeastRecentlyUsedFrames()
    {
        using var context = new CharacterTestContext();
        var package = (await context.Manager.ImportAsync(context.CopyFixture(), default)).Package!;
        var surface = Surface(context);
        await surface.SetPackageAsync(package, default);
        await surface.PresentAsync("images/idle.png", default);
        var first = surface.Frame;
        for (var i = 0; i < 257; i++)
        {
            var name = $"images/cache-{i:D3}.png";
            File.Copy(Path.Combine(package.InstalledDirectory, "images", "idle.png"), Path.Combine(package.InstalledDirectory, name));
            await surface.PreloadAsync(name, default);
            Assert.InRange(surface.CachedBytes, 0, WpfAnimationSurface.CacheLimitBytes);
        }
        await surface.PresentAsync("images/idle.png", default);
        Assert.NotSame(first, surface.Frame);
        await surface.ClearAsync(default);
    }
    [Theory]
    [InlineData("truncate")]
    [InlineData("crc")]
    [InlineData("signature")]
    [InlineData("dimensions")]
    public void PngValidationReadsActualDataNotOnlyExtension(string damage)
    {
        var bytes = File.ReadAllBytes(Path.Combine(CharacterTestContext.FixtureRoot, "dev-basic", "images", "idle.png"));
        if (damage == "truncate") bytes = bytes[..^5];
        if (damage == "crc") bytes[bytes.Length / 2] ^= 0xFF;
        if (damage == "signature") bytes[0] = 0;
        using var stream = new MemoryStream(bytes);
        Assert.False(new WindowsPngInspector().Inspect(stream, damage == "dimensions" ? 64 : 4096).IsValid);
    }
    [Fact]
    public async Task StartupSwitchHideShowStopAndSelectionRestoreUseRealPackages()
    {
        using var context = new CharacterTestContext();
        await context.Settings.LoadAsync(default);
        var surface = Surface(context);
        using (var presentation = Presentation(context, surface))
        {
            await presentation.InitializeAsync(default);
            Assert.NotNull(surface.Frame);
            Assert.Equal("dev.elfin.basic", presentation.Current!.Definition.Id.Value);
            Assert.Equal(2, (await context.Manager.ListAsync(default)).Count);
            Assert.True((await presentation.ActivateAsync(new("dev.elfin.standard"), default)).Succeeded);
            await presentation.PlayAsync(new("happy"), default);
            await presentation.SetVisibleAsync(false, default);
            var paused = surface.Frame;
            await Task.Delay(80);
            Assert.Same(paused, surface.Frame);
            await presentation.SetVisibleAsync(true, default);
            Assert.NotNull(surface.Frame);
            Assert.Equal("dev.elfin.standard", context.Settings.Current.ActiveCharacterId);
            await presentation.StopAsync(default);
            Assert.Null(surface.Frame);
            Assert.Equal(0, surface.CachedBytes);
        }
        using var restarted = Presentation(context, surface);
        await restarted.InitializeAsync(default);
        Assert.Equal("dev.elfin.standard", restarted.Current!.Definition.Id.Value);
        await restarted.StopAsync(default);
    }
    [Fact]
    public async Task CorruptActiveInstallationRecoversWithoutReplacingUserFiles()
    {
        using var context = new CharacterTestContext();
        await context.Settings.LoadAsync(default);
        var installed = (await context.Manager.ImportAsync(context.CopyFixture(), default)).Package!;
        await context.Manager.ActivateAsync(installed.Definition.Id, default);
        var manifest = Path.Combine(installed.InstalledDirectory, "manifest.json");
        await File.WriteAllTextAsync(manifest, "broken-user-installation");
        var surface = Surface(context);
        using var presentation = Presentation(context, surface);
        await presentation.InitializeAsync(default);
        Assert.NotNull(surface.Frame);
        Assert.Equal("dev.elfin.standard", presentation.Current!.Definition.Id.Value);
        Assert.Equal("broken-user-installation", await File.ReadAllTextAsync(manifest));
        await presentation.StopAsync(default);
    }
    [Fact]
    public async Task ReadOnlyShippedFallbackStillWorksWhenBothInstalledPackagesAreCorrupt()
    {
        using var context = new CharacterTestContext();
        foreach (var name in new[] { "dev-basic", "dev-standard" })
        {
            var package = (await context.Manager.ImportAsync(context.CopyFixture(name), default)).Package!;
            await File.WriteAllTextAsync(Path.Combine(package.InstalledDirectory, "manifest.json"), "corrupt");
        }
        var surface = Surface(context);
        using var presentation = Presentation(context, surface);
        await presentation.InitializeAsync(default);
        Assert.NotNull(surface.Frame);
        Assert.StartsWith(CharacterTestContext.FixtureRoot, presentation.Current!.InstalledDirectory);
        Assert.Empty(await context.Manager.ListAsync(default));
        await presentation.StopAsync(default);
    }
    [Fact]
    public async Task RuntimeMissingRequestedAndIdleResourcesFallBackToFallbackPng()
    {
        using var context = new CharacterTestContext();
        var surface = new FailingSurface();
        using var presentation = new CharacterPresentationService(context.Manager,
            new DirectoryCharacterSeedSource(CharacterTestContext.FixtureRoot), context.Settings, surface, context.Exceptions, TimeProvider.System);
        await presentation.InitializeAsync(default);
        Assert.Equal("fallback.png", surface.Last);
        await presentation.PlayAsync(new("unknown"), default);
        Assert.Equal("fallback.png", surface.Last);
        surface.FailEverything = true;
        await Assert.ThrowsAsync<CharacterAssetException>(() => presentation.PlayAsync(AnimationSemantic.Idle, default));
        await presentation.StopAsync(default);
    }
    private static WpfAnimationSurface Surface(CharacterTestContext context) =>
        new(new CharacterTestContext.InlineDispatcher(), context.Settings, new WindowsPngInspector());
    private static CharacterPresentationService Presentation(CharacterTestContext context, IAnimationSurface surface) =>
        new(context.Manager, new DirectoryCharacterSeedSource(CharacterTestContext.FixtureRoot), context.Settings, surface, context.Exceptions, TimeProvider.System);
    private sealed class FailingSurface : IAnimationSurface
    {
        public string? Last { get; private set; }
        public bool FailEverything { get; set; }
        public Task SetPackageAsync(CharacterPackage package, CancellationToken ct) => Task.CompletedTask;
        public Task PreloadAsync(string path, CancellationToken ct) => Check(path);
        public Task PresentAsync(string path, CancellationToken ct) { Check(path); Last = path; return Task.CompletedTask; }
        private Task Check(string path)
        {
            if (FailEverything || path != "fallback.png") throw new CharacterAssetException("Unavailable test frame.");
            return Task.CompletedTask;
        }
        public Task ClearAsync(CancellationToken ct) { Last = null; return Task.CompletedTask; }
    }
}
