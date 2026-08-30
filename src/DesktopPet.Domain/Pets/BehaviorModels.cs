namespace DesktopPet.Domain.Pets;

public enum BehaviorId { Idle, Blink, Happy, Rest, Talking, Interacting, Move }
public enum EmotionAxis { Mood, Energy, Boredom, Affinity }
public enum ModifierDirection { High, Low }
public enum CandidateFilter { None, Hidden, Interacting, MissingCapability, Cooldown, RecentRepeat, Disabled }
public sealed record EmotionModifier(EmotionAxis Axis, ModifierDirection Direction, double Strength)
{
    public double Evaluate(EmotionState emotion)
    {
        var value = Axis switch { EmotionAxis.Mood => emotion.Mood.Value, EmotionAxis.Energy => emotion.Energy.Value,
            EmotionAxis.Boredom => emotion.Boredom.Value, _ => emotion.Affinity.Value };
        var normalized = Direction == ModifierDirection.High ? value / 100.0 : 1 - value / 100.0;
        return RuntimeLimits.Factor(1 + normalized * RuntimeLimits.Clamp(Strength, 0, 5));
    }
}
public sealed record BehaviorDefinition(BehaviorId Id, AnimationSemantic Semantic, double BaseWeight, TimeSpan Cooldown,
    TimeSpan MinDuration, TimeSpan MaxDuration, BehaviorPriority Priority, bool Interruptible,
    IReadOnlyList<AnimationSemantic> RequiredCapabilities, IReadOnlyList<EmotionModifier> EmotionModifiers,
    double CharacterModifier = 1, double UserModifier = 1, bool Enabled = true)
{
    public bool IsValid => Enum.IsDefined(Id) && Enum.IsDefined(Priority) && !string.IsNullOrWhiteSpace(Semantic.Value) &&
        double.IsFinite(BaseWeight) && BaseWeight >= 0 && Cooldown >= TimeSpan.Zero &&
        MinDuration > TimeSpan.Zero && MaxDuration >= MinDuration && MaxDuration <= TimeSpan.FromMinutes(1);
}
public sealed record BehaviorExecution(BehaviorId Behavior, DateTimeOffset StartedAtUtc);
public sealed record RecentBehaviorContext(IReadOnlyList<BehaviorExecution> RecentBehaviors,
    IReadOnlyDictionary<BehaviorId, DateTimeOffset> LastExecutionTime)
{
    public BehaviorId? LastBehavior => RecentBehaviors.LastOrDefault()?.Behavior;
    public int ExecutionCountInWindow(BehaviorId behavior) => RecentBehaviors.Count(item => item.Behavior == behavior);
}
public sealed record BehaviorContext(DateTimeOffset Now, PetPrimaryState CurrentState, EmotionState EmotionState,
    RecentBehaviorContext RecentBehaviors, IReadOnlySet<AnimationSemantic> CurrentCharacterCapabilities,
    DateTimeOffset? LastInteractionTime, bool IsPetVisible, bool IsUserInteracting, TimeOnly TimeOfDay);
public sealed record UtilityScore(BehaviorId Behavior, double BaseWeight, double CharacterModifier, double EmotionModifier,
    double ContextModifier, double UserModifier, double RecentModifier, double FinalScore, CandidateFilter Filter, TimeSpan CooldownRemaining);
public sealed record BehaviorDecision(BehaviorDefinition Behavior, IReadOnlyList<UtilityScore> Scores, bool UsedFallback);
public interface IRandomSource { double NextUnit(); }
public sealed class SeededRandomSource(int seed) : IRandomSource
{
    private readonly Random _random = new(seed);
    public double NextUnit() => _random.NextDouble();
}
public static class RuntimeLimits
{
    public static double Clamp(double value, double min, double max) => double.IsFinite(value) ? Math.Clamp(value, min, max) : min;
    public static double Factor(double value) => Clamp(value, 0, 10);
    public static Percentage Percentage(double value) => new((int)Math.Round(Clamp(value, 0, 100)));
}
