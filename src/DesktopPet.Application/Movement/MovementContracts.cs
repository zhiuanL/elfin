using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Application.Movement;

public sealed record MovementSurfaceSnapshot(PixelRect Bounds, DpiScale Dpi, bool IsVisible, bool IsUserOwned);
public interface IMovementSurface
{
    Task<MovementSurfaceSnapshot> ReadAsync(CancellationToken ct);
    Task<bool> TryMoveAsync(PixelPoint origin, CancellationToken ct);
    Task RecoverAsync(PixelPoint origin, CancellationToken ct);
}
public interface IPetMovementPort
{
    bool IsUserOwned { get; }
    bool TryMoveAutonomously(PixelPoint origin);
    void SetClickThrough(bool enabled);
}
public interface ICharacterVisualSurface
{
    Task SetMirroredAsync(bool mirrored, CancellationToken ct);
}
public interface IMouseInteractionService
{
    MouseInteractionMode Mode { get; }
    Task SetModeAsync(MouseInteractionMode mode, CancellationToken ct);
    Task ToggleAsync(CancellationToken ct);
    Task ResetAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
public interface IBehaviorActionExecutor
{
    bool CanExecute(BehaviorId behavior);
    Task ExecuteAsync(BehaviorDefinition behavior, Action<AnimationSemantic> resolved, CancellationToken ct);
}
public interface IMovementService
{
    MovementDiagnostic Diagnostic { get; }
    MotionProfile Motion { get; }
    void Configure(CharacterPackage package);
    void RecordInteraction();
    Task ReconcileAsync(bool updateHome, CancellationToken ct);
    Task<MovementPlan?> PlanAsync(CancellationToken ct);
    Task ExecuteAsync(MovementPlan plan, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
