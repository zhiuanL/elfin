namespace DesktopPet.Domain.Pets;

public sealed record PetState(PetPrimaryState Primary, PetTransientState? Transient, BehaviorId Behavior, AnimationSemantic Semantic);
public sealed class PetStateMachine
{
    private BehaviorDefinition? _active;
    private DateTimeOffset _entered;
    public PetState Current { get; private set; } = IdleState();
    public event EventHandler? Changed;
    public bool CanInterrupt(BehaviorPriority priority, DateTimeOffset now) =>
        priority == BehaviorPriority.Critical || _active is null || Current.Behavior == BehaviorId.Idle ||
        (_active.Interruptible && priority > _active.Priority && now - _entered >= _active.MinDuration);
    public bool TryEnter(BehaviorDefinition behavior, DateTimeOffset now)
    {
        if (!behavior.IsValid || !CanInterrupt(behavior.Priority, now)) return false;
        _active = behavior;
        _entered = now;
        Current = FromSemantic(behavior.Id, behavior.Semantic);
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }
    public void ResolveAnimation(AnimationSemantic semantic)
    {
        if (_active is null) return;
        Current = FromSemantic(_active.Id, semantic);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    public void Complete() { _active = null; Current = IdleState(); Changed?.Invoke(this, EventArgs.Empty); }
    public void BeginInteraction()
    {
        _active = null;
        Current = new(PetPrimaryState.Dragging, null, BehaviorId.Interacting, AnimationSemantic.Idle);
        Changed?.Invoke(this, EventArgs.Empty);
    }
    private static PetState IdleState() => new(PetPrimaryState.Idle, null, BehaviorId.Idle, AnimationSemantic.Idle);
    private static PetState FromSemantic(BehaviorId behavior, AnimationSemantic semantic) =>
        behavior == BehaviorId.Move ? new(PetPrimaryState.Moving, null, behavior, semantic) : semantic.Value switch
    {
        "idle" or "fallback" => new(PetPrimaryState.Idle, null, behavior, semantic),
        "blink" => new(PetPrimaryState.Acting, PetTransientState.Blink, behavior, semantic),
        "happy" => new(PetPrimaryState.Acting, PetTransientState.Happy, behavior, semantic),
        "rest" => new(PetPrimaryState.Resting, null, behavior, semantic),
        "talking" => new(PetPrimaryState.Talking, null, behavior, semantic),
        _ => new(PetPrimaryState.Acting, null, behavior, semantic)
    };
}
