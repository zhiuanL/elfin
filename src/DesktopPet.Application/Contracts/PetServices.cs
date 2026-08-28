using DesktopPet.CharacterSdk;
using DesktopPet.Application.Characters;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Platform;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Contracts;

public interface IPetRuntime : IAsyncDisposable
{
    PetInstanceId InstanceId { get; }
    PetSnapshot Snapshot { get; }
    Task StartAsync(CancellationToken ct);
    Task StopAsync(CancellationToken ct);
}
public interface IPetHost
{
    IReadOnlyCollection<IPetRuntime> Instances { get; }
}
public sealed record EnvironmentContext(TimeOnly LocalTime, TimeSpan UserIdleDuration,
    PomodoroPhase? PomodoroPhase, ForegroundWindowState ForegroundWindow, SessionState Session);
public sealed record BehaviorDecision(AnimationSemantic Semantic, BehaviorPriority Priority);
public interface IBehaviorDecisionEngine
{
    BehaviorDecision Decide(PetSnapshot pet, EnvironmentContext context);
}
public interface IEmotionService
{
    EmotionState Current { get; }
}
public interface IMovementController
{
    Task MoveAsync(PixelPoint target, CancellationToken ct);
    void Stop();
}
public interface ICharacterPackageService
{
    Task<CharacterDiscovery> DiscoverAsync(CancellationToken ct);
    Task<ValidationResult> ValidateAsync(string sourcePath, CancellationToken ct);
    Task<CharacterOperationResult> ImportAsync(string sourcePath, CancellationToken ct);
    Task<CharacterOperationResult> InstallAsync(string sourcePath, CancellationToken ct);
    Task<IReadOnlyList<CharacterPackage>> ListAsync(CancellationToken ct);
    Task<CharacterOperationResult> GetAsync(CharacterId characterId, CancellationToken ct);
    Task<ValidationResult> RemoveAsync(CharacterId characterId, CancellationToken ct);
    Task<CharacterOperationResult> ActivateAsync(CharacterId characterId, CancellationToken ct);
}
public sealed record PerformanceContext(bool IsVisible, SessionState Session, bool IsActive);
public sealed record PerformanceBudget(int FramesPerSecond, TimeSpan BehaviorInterval);
public interface IPerformancePolicy
{
    PerformanceBudget GetBudget(PerformanceMode mode, PerformanceContext context);
}
public interface IDialogueProvider
{
    Task<string?> GetDialogueAsync(CharacterId characterId, AnimationSemantic context, string locale, CancellationToken ct);
}
