using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Movement;
using DesktopPet.Application.Runtime;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;
using DesktopPet.Infrastructure.Characters;
using DesktopPet.Infrastructure.Configuration;
using DesktopPet.Tests.Shared;
using Microsoft.Extensions.Options;

namespace DesktopPet.Tests.Integration;

public sealed class MovementIntegrationTests
{
    [Fact]
    public async Task HomeRecoveryDragUpdateAndDiskPersistenceUseSettingsService()
    {
        using var context = new CharacterTestContext();
        var package = (await context.Manager.ImportAsync(context.CopyFixture(), default)).Package!;
        var surface = new Surface();
        var topology = new Displays();
        await using var movement = new MovementController(surface, topology, new CharacterTestContext.InlineDispatcher(),
            context.Settings, new ManualTimeProvider(), new SeededRandomSource(4), context.Environment.Logger);
        movement.Configure(package);
        await movement.ReconcileAsync(false, default);
        var first = context.Settings.Current.Movement.Home!;
        Assert.True(first.Position.X < 0);
        await surface.RecoverAsync(new(-800, -300), default);
        await movement.ReconcileAsync(true, default);
        Assert.NotEqual(first, context.Settings.Current.Movement.Home);
        var saved = context.Settings.Current;
        using var reloaded = new JsonSettingsService(context.Environment.Directories, Options.Create(new AppSettings()), context.Environment.Logger, TimeProvider.System);
        Assert.Equal(saved, (await reloaded.LoadAsync(default)).Settings);
        topology.Items = [new("primary", new(0, 0, 1920, 1080), new(0, 0, 1920, 1040), new(1.25, 1.25), true)];
        await movement.ReconcileAsync(false, default);
        Assert.Equal("primary", context.Settings.Current.Movement.Home!.DisplayId);
        Assert.True(MovementGeometry.Contains(surface.Origin, new(220, 220), topology.Items[0].WorkingArea));
    }
    [Fact]
    public async Task SchemaFourMigrationPreservesNegativePositionEmotionAndMovementPreferences()
    {
        using var context = new CharacterTestContext();
        var path = Path.Combine(context.Environment.Directories.Config, "settings.json");
        const string original = """{"schemaVersion":4,"movementMode":"Fixed","motionStyle":"Quiet","petWindow":{"position":{"origin":{"x":-600,"y":-300},"displayId":"left"}},"emotions":[{"characterId":"dev.elfin.basic","mood":77,"energy":64,"affinity":40}]}""";
        await File.WriteAllTextAsync(path, original);
        var result = await context.Settings.LoadAsync(default);
        Assert.Equal(SettingsLoadStatus.Migrated, result.Status); Assert.Equal(5, result.Settings.SchemaVersion);
        Assert.Equal(MovementMode.Fixed, result.Settings.MovementMode); Assert.Equal(MotionStyle.Quiet, result.Settings.MotionStyle);
        Assert.Equal(-600, result.Settings.PetWindow.Position!.Origin.X); Assert.Equal(77, result.Settings.Emotions.Single().Mood);
        Assert.Equal(original, await File.ReadAllTextAsync(path + ".bak"));
    }
    [Theory]
    [InlineData("hide")]
    [InlineData("drag")]
    [InlineData("switch")]
    [InlineData("display")]
    [InlineData("exit")]
    public async Task RuntimeCancelsMovingActionBeforeLifecycleOwnershipChanges(string operation)
    {
        using var context = new CharacterTestContext();
        var clock = new ManualTimeProvider();
        var movement = new WaitingMovement(clock);
        // A one-frame legacy package makes missing-walk fallback and preload ordering explicit.
        await context.Manager.ImportAsync(context.CopyFixture(), default);
        var animation = new AnimationSurface();
        await context.Settings.UpdateAsync(s => s with { Runtime = new() { Behaviors = new RuntimePolicy().Defaults()
            .Select(b => new BehaviorOverride { Behavior = b.Id, Weight = 0 }).ToArray() } }, default);
        using var presentation = new CharacterPresentationService(context.Manager, new DirectoryCharacterSeedSource(CharacterTestContext.FixtureRoot),
            context.Settings, animation, context.Exceptions, clock);
        await using var runtime = new PetRuntime(presentation, context.Settings, new CharacterBehaviorProfileReader(context.Settings, context.Exceptions),
            clock, new(), new SeededRandomSource(4), context.Exceptions, context.Environment.Logger, movement);
        await runtime.StartAsync(default);
        // The first preload belongs to presentation startup; the second follows the scheduler's deadline registration.
        // Wait for that real synchronization point before advancing, never race the thread pool with repeated Yield.
        await animation.ScheduledAnimationReady.Task.WaitAsync(TimeSpan.FromSeconds(4));
        clock.Advance(TimeSpan.FromSeconds(7));
        await movement.Started.Task.WaitAsync(TimeSpan.FromSeconds(4));
        Assert.Equal(PetPrimaryState.Moving, runtime.Snapshot.State); // Missing walk falls back visually, but movement remains a moving state.
        switch (operation)
        {
            case "hide": await runtime.SetVisibleAsync(false, default); break;
            case "drag": await runtime.InteractAsync(PetInteractionKind.PointerPressed, default); break;
            case "switch": await runtime.ActivateAsync(new("dev.elfin.basic"), default); break;
            case "display": await runtime.ReconcileMovementAsync(false, default); break;
            default: await runtime.StopAsync(default); break;
        }
        Assert.False(movement.Running); Assert.Equal(1, movement.Cancelled);
        if (operation == "hide") { await runtime.SetVisibleAsync(true, default); Assert.True(runtime.Diagnostic.IsRunning); }
        if (operation == "drag") await runtime.InteractAsync(PetInteractionKind.DragEnded, default);
        await runtime.StopAsync(default);
        Assert.False(runtime.Diagnostic.IsRunning);
    }
    private sealed class WaitingMovement(TimeProvider clock) : IMovementService
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Running { get; private set; }
        public int Cancelled { get; private set; }
        public MotionProfile Motion => MotionPolicy.Preset(MotionStyle.Natural);
        public MovementDiagnostic Diagnostic => new(Running, null, null, FacingDirection.Left, MovementMode.Hybrid, DisplayPolicy.LockedCurrent);
        public void Configure(CharacterPackage package) { Assert.False(Running); }
        public void RecordInteraction() { }
        public Task ReconcileAsync(bool updateHome, CancellationToken ct) { Assert.False(Running); return Task.CompletedTask; }
        public Task<MovementPlan?> PlanAsync(CancellationToken ct) => Task.FromResult<MovementPlan?>(new(new(0, 0), new(100, 0),
            new(-500, -500, 1920, 1080), new(220, 220), new(1, 1), Motion, "screen", FacingDirection.Right));
        public async Task ExecuteAsync(MovementPlan plan, CancellationToken ct)
        {
            Running = true; Started.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, clock, ct); }
            catch (OperationCanceledException) { Cancelled++; throw; }
            finally { Running = false; }
        }
        public Task StopAsync(CancellationToken ct) { Assert.False(Running); return Task.CompletedTask; }
    }
    private sealed class AnimationSurface : IAnimationSurface
    {
        private int _preloads;
        public TaskCompletionSource ScheduledAnimationReady { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SetPackageAsync(CharacterPackage package, CancellationToken ct) => Task.CompletedTask;
        public Task PreloadAsync(string path, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (Interlocked.Increment(ref _preloads) == 2) ScheduledAnimationReady.TrySetResult();
            return Task.CompletedTask;
        }
        public Task PresentAsync(string path, CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Task.CompletedTask; }
        public Task ClearAsync(CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class Surface : IMovementSurface
    {
        public PixelPoint Origin { get; private set; } = new(-500, -200);
        public Task<MovementSurfaceSnapshot> ReadAsync(CancellationToken ct) => Task.FromResult(new MovementSurfaceSnapshot(new(Origin.X, Origin.Y, 220, 220), new(1, 1), true, false));
        public Task<bool> TryMoveAsync(PixelPoint origin, CancellationToken ct) { Origin = origin; return Task.FromResult(true); }
        public Task RecoverAsync(PixelPoint origin, CancellationToken ct) { Origin = origin; return Task.CompletedTask; }
    }
    private sealed class Displays : IDisplayTopologyService
    {
        public IReadOnlyList<DisplayInfo> Items { get; set; } = [new("left", new(-1920, -500, 1920, 1080), new(-1920, -500, 1920, 1040), new(1, 1), false)];
        public DisplayTopology GetTopology() => new(Items, []);
        public event EventHandler? TopologyChanged { add { } remove { } }
    }
}
