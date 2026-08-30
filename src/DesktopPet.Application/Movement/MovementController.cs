using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Windows;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Application.Movement;

// The runtime serializes calls. Policy selects destinations, engine only interpolates safe segments.
public sealed class MovementController(IMovementSurface surface, IDisplayTopologyService displays, IUiDispatcher dispatcher,
    ISettingsService settings, TimeProvider clock, IRandomSource random, IAppLogger logger) : IMovementService, IAsyncDisposable, IDisposable
{
    private readonly MovementEngine _engine = new(surface, clock);
    private readonly DisplayMovementPolicy _displayPolicy = new();
    private CharacterPackage? _character;
    private HomePosition? _home;
    private PixelPoint? _target;
    private FacingDirection _facing = FacingDirection.Right;
    private long _interactionAt = clock.GetTimestamp();
    private bool _returnHome;
    private bool _stopped;
    public MotionProfile Motion => new MotionPolicy().Resolve(settings.Current.Movement.UserMotionStyle ??
        (settings.Current.MotionStyle == MotionStyle.Natural ? null : settings.Current.MotionStyle),
        settings.Current.Movement.Overrides, _character?.Definition.Manifest.Movement);
    public MovementDiagnostic Diagnostic => new(_engine.IsMoving, _home, _target, _facing, settings.Current.MovementMode, settings.Current.DisplayPolicy);
    public void Configure(CharacterPackage package) { _character = package; _home = settings.Current.Movement.Home; }
    public void RecordInteraction() { _interactionAt = clock.GetTimestamp(); _returnHome = true; }
    private async Task<DisplayTopology> TopologyAsync(CancellationToken ct)
    {
        DisplayTopology? topology = null;
        await dispatcher.InvokeAsync(() => { topology = displays.GetTopology(); return Task.CompletedTask; }, ct);
        return topology!;
    }
    public async Task ReconcileAsync(bool updateHome, CancellationToken ct)
    {
        if (_stopped || _character is null) return;
        await _engine.StopAsync();
        var snapshot = await surface.ReadAsync(ct);
        if (snapshot.IsUserOwned) return;
        var topology = await TopologyAsync(ct);
        var origin = new PixelPoint(snapshot.Bounds.X, snapshot.Bounds.Y);
        var size = new PixelSize(snapshot.Bounds.Width, snapshot.Bounds.Height);
        var current = MovementGeometry.Nearest(new(origin.X + size.Width / 2, origin.Y + size.Height / 2), topology.Displays);
        var allowed = _displayPolicy.Allowed(topology, settings.Current.DisplayPolicy, settings.Current.Movement.SelectedDisplays, current.Id);
        // No selected screen connected: recover visibly, but PlanAsync will not roam on an unapproved screen.
        var recovery = allowed.Count == 0 ? topology.Displays : allowed;
        var chosen = recovery.FirstOrDefault(d => d.Id == current.Id) ?? MovementGeometry.Nearest(origin, recovery);
        var safe = MovementGeometry.Clamp(origin, size, chosen.WorkingArea);
        if (origin != safe) await surface.RecoverAsync(safe, ct);
        snapshot = await surface.ReadAsync(ct); // A cross-DPI recovery may resize the actual HWND.
        size = new(snapshot.Bounds.Width, snapshot.Bounds.Height);
        safe = MovementGeometry.Clamp(new(snapshot.Bounds.X, snapshot.Bounds.Y), size, chosen.WorkingArea);
        if (safe.X != snapshot.Bounds.X || safe.Y != snapshot.Bounds.Y) await surface.RecoverAsync(safe, ct);
        var anchor = _character.Definition.Manifest.VisualAnchor ?? new();
        _home = _displayPolicy.RestoreHome(updateHome && settings.Current.Movement.UpdateHomeOnDrag ? null : _home, safe, size, anchor, recovery);
        await settings.UpdateAsync(s => s with
        {
            Movement = s.Movement with { Home = _home },
            PetWindow = s.PetWindow with { Position = new(safe, chosen.Id) }
        }, ct);
    }
    public async Task<MovementPlan?> PlanAsync(CancellationToken ct)
    {
        if (_stopped || _character is null || settings.Current.MovementMode == MovementMode.Fixed) return null;
        var snapshot = await surface.ReadAsync(ct);
        if (!snapshot.IsVisible || snapshot.IsUserOwned) return null;
        if (_home is null) { await ReconcileAsync(false, ct); snapshot = await surface.ReadAsync(ct); }
        var topology = await TopologyAsync(ct);
        var origin = new PixelPoint(snapshot.Bounds.X, snapshot.Bounds.Y);
        var current = MovementGeometry.Nearest(new(origin.X + snapshot.Bounds.Width / 2, origin.Y + snapshot.Bounds.Height / 2), topology.Displays);
        var context = new MovementContext(new(topology, current.Id, new(snapshot.Bounds.Width, snapshot.Bounds.Height), snapshot.Dpi),
            origin, new(_home!, _character.Definition.Manifest.VisualAnchor ?? new()), clock.GetElapsedTime(_interactionAt), _returnHome);
        return new MovementTargetPolicy(_displayPolicy, random).Choose(context, settings.Current.MovementMode,
            settings.Current.HybridStrategy, settings.Current.DisplayPolicy, settings.Current.Movement.SelectedDisplays, Motion);
    }
    public async Task ExecuteAsync(MovementPlan plan, CancellationToken ct)
    {
        _target = plan.Target; _facing = plan.Facing;
        logger.Write(new(AppEvent.MovementStarted, clock.GetUtcNow(), Behavior: BehaviorId.Move, State: PetPrimaryState.Moving));
        try
        {
            await _engine.MoveAsync(plan, ct);
            _returnHome = false;
            var snapshot = await surface.ReadAsync(ct);
            await settings.UpdateAsync(s => s with { PetWindow = s.PetWindow with
                { Position = new(new(snapshot.Bounds.X, snapshot.Bounds.Y), plan.TargetDisplayId) } }, ct);
        }
        finally
        {
            _target = null;
            logger.Write(new(AppEvent.MovementStopped, clock.GetUtcNow(), Behavior: BehaviorId.Move));
        }
    }
    public async Task StopAsync(CancellationToken ct) { _stopped = true; await _engine.StopAsync(); }
    public async ValueTask DisposeAsync() { await StopAsync(default); await _engine.DisposeAsync(); }
    public void Dispose() => _engine.Dispose();
}
