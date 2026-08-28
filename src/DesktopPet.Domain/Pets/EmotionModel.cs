namespace DesktopPet.Domain.Pets;

public sealed class EmotionModel(RuntimePolicy policy)
{
    private double _mood = 60, _energy = 70, _boredom = 20, _affinity = 20;
    public EmotionState Current => new(RuntimeLimits.Percentage(_mood), RuntimeLimits.Percentage(_energy),
        RuntimeLimits.Percentage(_boredom), RuntimeLimits.Percentage(_affinity));
    public void Restore(EmotionState state)
    {
        _mood = state.Mood.Value; _energy = state.Energy.Value; _boredom = state.Boredom.Value; _affinity = state.Affinity.Value;
    }
    public void Apply(EmotionDelta delta, double multiplier = 1)
    {
        _mood = RuntimeLimits.Clamp(_mood + delta.Mood * multiplier, 0, 100);
        _energy = RuntimeLimits.Clamp(_energy + delta.Energy * multiplier, 0, 100);
        _boredom = RuntimeLimits.Clamp(_boredom + delta.Boredom * multiplier, 0, 100);
        _affinity = RuntimeLimits.Clamp(_affinity + delta.Affinity * multiplier, 0, 100);
    }
    public void Elapse(TimeSpan elapsed, PetPrimaryState state) =>
        Apply(state == PetPrimaryState.Resting ? policy.RestPerMinute : policy.IdlePerMinute,
            RuntimeLimits.Clamp(elapsed.TotalMinutes, 0, policy.MaxElapsedEmotion.TotalMinutes));
    public void Interact() => Apply(policy.InteractionDelta);
    public void Complete(BehaviorId behavior)
    {
        if (behavior is BehaviorId.Happy or BehaviorId.Interacting) Apply(policy.ActiveBehaviorDelta);
    }
}
