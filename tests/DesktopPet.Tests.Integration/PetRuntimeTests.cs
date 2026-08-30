using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Runtime;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;
using DesktopPet.Infrastructure.Characters;
using DesktopPet.Infrastructure.Configuration;
using DesktopPet.Tests.Shared;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

public sealed class PetRuntimeTests
{
    [Fact]
    public async Task RuntimeStartsOnceHidesResumesAndStopsWithoutOutstandingAnimation()
    {
        using var fixture = new RuntimeFixture();
        await fixture.Context.Settings.LoadAsync(default);
        await fixture.Runtime.StartAsync(default);
        Assert.True(fixture.Runtime.Diagnostic.IsRunning);
        Assert.Equal("dev.elfin.standard", fixture.Runtime.Current!.Definition.Id.Value);
        var count = fixture.Surface.Count;
        await fixture.Runtime.StartAsync(default);
        Assert.Equal(count, fixture.Surface.Count);
        await fixture.Runtime.SetVisibleAsync(false, default);
        Assert.False(fixture.Runtime.Diagnostic.IsRunning);
        count = fixture.Surface.Count;
        fixture.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(count, fixture.Surface.Count);
        await fixture.Runtime.SetVisibleAsync(true, default);
        Assert.True(fixture.Runtime.Diagnostic.IsRunning);
        await fixture.Runtime.StopAsync(default);
        Assert.False(fixture.Runtime.Diagnostic.IsRunning);
        Assert.True(fixture.Surface.Cleared);
        await fixture.Runtime.SetVisibleAsync(true, default);
        Assert.False(fixture.Runtime.Diagnostic.IsRunning);
    }
    [Fact]
    public async Task ClickFeedbackUpdatesEmotionAndDraggingPausesResumesWithoutClickReward()
    {
        using var fixture = new RuntimeFixture();
        await fixture.Runtime.StartAsync(default);
        await fixture.Runtime.InteractAsync(PetInteractionKind.PointerPressed, default);
        Assert.Equal(PetPrimaryState.Dragging, fixture.Runtime.Snapshot.State);
        Assert.False(fixture.Runtime.Diagnostic.IsRunning);
        var before = fixture.Runtime.Snapshot.Emotion;
        await fixture.Runtime.InteractAsync(PetInteractionKind.DragEnded, default);
        Assert.Equal(before, fixture.Runtime.Snapshot.Emotion);
        await fixture.Runtime.InteractAsync(PetInteractionKind.Click, default);
        Assert.True(fixture.Runtime.Snapshot.Emotion.Mood.Value > before.Mood.Value);
        Assert.Equal(BehaviorId.Interacting, fixture.Runtime.Diagnostic.State.Behavior);
        Assert.Equal("happy", fixture.Runtime.Diagnostic.State.Semantic.Value);
        await fixture.Runtime.StopAsync(default);
    }
    [Fact]
    public async Task CharacterSwitchCancelsOldFramesClearsMemoryAndRestoresPerCharacterEmotion()
    {
        using var fixture = new RuntimeFixture();
        await fixture.Runtime.StartAsync(default);
        await fixture.Runtime.InteractAsync(PetInteractionKind.Click, default);
        var standardMood = fixture.Runtime.Snapshot.Emotion.Mood;
        Assert.True((await fixture.Runtime.ActivateAsync(new("dev.elfin.basic"), default)).Succeeded);
        Assert.Equal(EmotionState.Initial, fixture.Runtime.Snapshot.Emotion);
        Assert.DoesNotContain(fixture.Runtime.Diagnostic.Recent.RecentBehaviors, item => item.Behavior == BehaviorId.Interacting);
        var afterSwitch = fixture.Surface.Count;
        fixture.Clock.Advance(TimeSpan.FromSeconds(1));
        Assert.All(fixture.Surface.Frames.Skip(afterSwitch), frame => Assert.Equal("dev.elfin.basic", frame.Character));
        await fixture.Runtime.InteractAsync(PetInteractionKind.Click, default);
        Assert.Equal(PetPrimaryState.Idle, fixture.Runtime.Snapshot.State); // Missing happy resolves to idle, not a false Happy state.
        Assert.True((await fixture.Runtime.ActivateAsync(new("dev.elfin.standard"), default)).Succeeded);
        Assert.Equal(standardMood, fixture.Runtime.Snapshot.Emotion.Mood);
        await fixture.Runtime.StopAsync(default);
        Assert.Equal(2, fixture.Context.Settings.Current.Emotions.Count);
    }
    [Fact]
    public async Task ConcurrentLifecycleRequestsLeaveOnlyLatestCharacterAndOneRunningScheduler()
    {
        using var fixture = new RuntimeFixture();
        await fixture.Runtime.StartAsync(default);
        await Task.WhenAll(fixture.Runtime.ActivateAsync(new("dev.elfin.basic"), default),
            fixture.Runtime.SetVisibleAsync(false, default), fixture.Runtime.ActivateAsync(new("dev.elfin.standard"), default),
            fixture.Runtime.SetVisibleAsync(true, default));
        Assert.Equal("dev.elfin.standard", fixture.Runtime.Current!.Definition.Id.Value);
        Assert.True(fixture.Runtime.Diagnostic.IsRunning);
        await fixture.Runtime.StopAsync(default);
        var count = fixture.Surface.Count;
        fixture.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(count, fixture.Surface.Count);
    }
    [Fact]
    public async Task StableEmotionPersistsAcrossFreshRuntimeWhileTransientContextDoesNot()
    {
        using var context = new CharacterTestContext();
        using (var fixture = new RuntimeFixture(context))
        {
            await fixture.Runtime.StartAsync(default);
            await fixture.Runtime.InteractAsync(PetInteractionKind.Click, default);
            await fixture.Runtime.StopAsync(default);
        }
        using var reloaded = new JsonSettingsService(context.Environment.Directories, Options.Create(new AppSettings()), context.Environment.Logger, TimeProvider.System);
        var saved = await reloaded.LoadAsync(default);
        Assert.True(saved.Settings.Emotions.Single().Mood > 60);
        using var restarted = new RuntimeFixture(context, reloaded);
        await restarted.Runtime.StartAsync(default);
        Assert.Equal((int)saved.Settings.Emotions.Single().Mood, restarted.Runtime.Snapshot.Emotion.Mood.Value);
        Assert.Equal(20, restarted.Runtime.Snapshot.Emotion.Boredom.Value);
        Assert.Null(restarted.Runtime.Diagnostic.LastInteractionUtc);
        Assert.DoesNotContain(restarted.Runtime.Diagnostic.Recent.RecentBehaviors, item => item.Behavior == BehaviorId.Interacting);
        await restarted.Runtime.StopAsync(default);
    }
    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task OldSettingsMigratePreservingWindowAndSelection(int schema)
    {
        using var context = new CharacterTestContext();
        var path = Path.Combine(context.Environment.Directories.Config, "settings.json");
        var json = $$$"""{"schemaVersion":{{{schema}}},"culture":"en-US","activeCharacterId":"custom.pet","petWindow":{"isVisible":false,"topmost":false}}""";
        await File.WriteAllTextAsync(path, json);
        var loaded = await context.Settings.LoadAsync(default);
        Assert.Equal(SettingsLoadStatus.Migrated, loaded.Status);
        Assert.Equal(AppSettings.CurrentSchemaVersion, loaded.Settings.SchemaVersion);
        Assert.False(loaded.Settings.PetWindow.IsVisible);
        Assert.Equal("custom.pet", loaded.Settings.ActiveCharacterId);
        Assert.Equal(json, await File.ReadAllTextAsync(path + ".bak"));
        Assert.Empty(loaded.Settings.Emotions);
    }
    [Fact]
    public async Task CorruptSettingsArePreservedAndExtremeEmotionIsClampedOnRestore()
    {
        using var context = new CharacterTestContext();
        var path = Path.Combine(context.Environment.Directories.Config, "settings.json");
        await File.WriteAllTextAsync(path, "{broken-emotion-configuration");
        Assert.Equal(SettingsLoadStatus.RecoveredInvalid, (await context.Settings.LoadAsync(default)).Status);
        Assert.Single(Directory.GetFiles(context.Environment.Directories.Config, "*.invalid-*"));
        await context.Settings.UpdateAsync(s => s with { Emotions = [new() { CharacterId = "dev.elfin.standard", Mood = -500, Energy = 99999, Affinity = 99999 }] }, default);
        using var fixture = new RuntimeFixture(context);
        await fixture.Runtime.StartAsync(default);
        Assert.Equal(0, fixture.Runtime.Snapshot.Emotion.Mood.Value);
        Assert.Equal(100, fixture.Runtime.Snapshot.Emotion.Energy.Value);
        await fixture.Runtime.StopAsync(default);
    }
    [Fact]
    public async Task ProfileReaderConsumesOnlyValidatedCurrentPhaseProfilesAndDefaultsForMissing()
    {
        using var context = new CharacterTestContext();
        var package = (await context.Manager.ImportAsync(context.CopyFixture(), default)).Package!;
        var reader = new CharacterBehaviorProfileReader(context.Settings, context.Exceptions);
        Assert.Empty((await reader.ReadAsync(package, default)).Behaviors);
        var source = context.CopyFixture("dev-standard");
        await File.WriteAllTextAsync(Path.Combine(source, "behavior.json"),
            """{"behaviors":[{"animation":"happy","weight":3,"cooldownSeconds":9}]}""");
        var result = await context.Manager.ImportAsync(source, default);
        Assert.True(result.Succeeded);
        var profile = await reader.ReadAsync(result.Package!, default);
        Assert.Equal(3, profile.Behaviors.Single().Weight);
        Assert.NotEmpty(profile.EmotionAnimations);
        var catalog = new BehaviorCatalog(new()).Build(profile, new());
        Assert.Equal(3, catalog.Single(item => item.Id == BehaviorId.Happy).CharacterModifier);
    }
    [Fact]
    public async Task NonemptyRuntimeSettingsRoundTripWithValueEqualityAndOverrides()
    {
        using var context = new CharacterTestContext();
        await context.Settings.UpdateAsync(s => s with
        {
            Runtime = new() { Behaviors = [new() { Behavior = BehaviorId.Rest, Enabled = false, Weight = 2.5 }] },
            Emotions = [new() { CharacterId = "dev.elfin.standard", Mood = 81, Energy = 37, Affinity = 42 }]
        }, default);
        var original = context.Settings.Current;
        using var reloaded = new JsonSettingsService(context.Environment.Directories, Options.Create(new AppSettings()), context.Environment.Logger, TimeProvider.System);
        var restored = (await reloaded.LoadAsync(default)).Settings;
        Assert.Equal(original, restored);
        Assert.Equal(original.GetHashCode(), restored.GetHashCode());
        Assert.False(new BehaviorCatalog(new()).Build(CharacterBehaviorProfile.Empty, restored.Runtime)[3].Enabled);
    }

    [Fact]
    public async Task RuntimeDisposeIsIdempotentAfterAnimationHasStarted()
    {
        using var fixture = new RuntimeFixture();
        await fixture.Runtime.StartAsync(default);
        await fixture.Runtime.DisposeAsync();
        await fixture.Runtime.DisposeAsync();
        fixture.Runtime.Dispose();
        Assert.False(fixture.Runtime.Diagnostic.IsRunning);
        Assert.True(fixture.Surface.Cleared);
    }

    private sealed class RuntimeFixture : IDisposable
    {
        private readonly bool _ownsContext;
        public CharacterTestContext Context { get; }
        public ManualTimeProvider Clock { get; } = new();
        public RecordingSurface Surface { get; } = new();
        public CharacterPresentationService Presentation { get; }
        public PetRuntime Runtime { get; }
        public RuntimeFixture(CharacterTestContext? context = null, ISettingsService? settings = null)
        {
            _ownsContext = context is null;
            Context = context ?? new();
            settings ??= Context.Settings;
            Presentation = new(Context.Manager, new DirectoryCharacterSeedSource(CharacterTestContext.FixtureRoot),
                settings, Surface, Context.Exceptions, Clock);
            Runtime = new(Presentation, settings, new CharacterBehaviorProfileReader(settings, Context.Exceptions),
                Clock, new(), new SeededRandomSource(4), Context.Exceptions, Context.Environment.Logger);
        }
        public void Dispose()
        {
            Runtime.Dispose(); Presentation.Dispose();
            if (_ownsContext) Context.Dispose();
        }
    }
    private sealed class RecordingSurface : IAnimationSurface
    {
        private readonly List<(string Character, string Path)> _frames = [];
        private string _character = "";
        public (string Character, string Path)[] Frames { get { lock (_frames) return _frames.ToArray(); } }
        public int Count { get { lock (_frames) return _frames.Count; } }
        public bool Cleared { get; private set; }
        public Task SetPackageAsync(CharacterPackage package, CancellationToken ct) { ct.ThrowIfCancellationRequested(); _character = package.Definition.Id.Value; return Task.CompletedTask; }
        public Task PreloadAsync(string path, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task PresentAsync(string path, CancellationToken ct) { ct.ThrowIfCancellationRequested(); lock (_frames) _frames.Add((_character, path)); return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken ct) { Cleared = true; return Task.CompletedTask; }
    }
}
