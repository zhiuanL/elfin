using DesktopPet.Application.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Runtime;

public interface ICharacterPresentation
{
    CharacterPackage? Current { get; }
    Task<CharacterOperationResult> ActivateAsync(CharacterId id, CancellationToken ct);
    Task PlayAsync(AnimationSemantic semantic, CancellationToken ct);
}
public interface IBehaviorAnimationPlayer
{
    Task PlayBehaviorAsync(AnimationSemantic semantic, TimeSpan minimum, TimeSpan maximum, bool repeat,
        Action<AnimationSemantic> resolved, CancellationToken ct);
}
public sealed record CharacterBehaviorProfile(IReadOnlyList<BehaviorRecommendation> Behaviors,
    IReadOnlyDictionary<string, string> EmotionAnimations)
{
    public static CharacterBehaviorProfile Empty => new([], new Dictionary<string, string>());
}
public interface ICharacterBehaviorProfileReader
{
    Task<CharacterBehaviorProfile> ReadAsync(CharacterPackage package, CancellationToken ct);
}
public sealed record RuntimeDiagnostic(PetState State, EmotionState Emotion, IReadOnlyList<UtilityScore> Scores,
    RecentBehaviorContext Recent, bool IsRunning, bool IsVisible, bool IsInteracting, int InteractionCount, DateTimeOffset? LastInteractionUtc);
public enum PetInteractionKind { PointerPressed, Click, DragEnded }
public sealed class PetInteractionEventArgs(PetInteractionKind kind) : EventArgs
{
    public PetInteractionKind Kind { get; } = kind;
}
public interface IPetInteractionSource { event EventHandler<PetInteractionEventArgs>? Interaction; }
public sealed class LocalBehaviorDecisionEngine(RuntimePolicy policy, IRandomSource random) : IBehaviorDecisionEngine
{
    private readonly UtilityDecisionEngine _engine = new(policy, random);
    public BehaviorDecision Decide(BehaviorContext context, IReadOnlyList<BehaviorDefinition> behaviors) => _engine.Decide(context, behaviors);
}
public sealed class RuntimeEmotionService(RuntimePolicy policy) : IEmotionService
{
    private readonly EmotionModel _model = new(policy);
    public EmotionState Current => _model.Current;
    public void Restore(EmotionState state) => _model.Restore(state);
    public void Elapse(TimeSpan elapsed, PetPrimaryState state) => _model.Elapse(elapsed, state);
    public void Interact() => _model.Interact();
    public void Complete(BehaviorId behavior) => _model.Complete(behavior);
}
