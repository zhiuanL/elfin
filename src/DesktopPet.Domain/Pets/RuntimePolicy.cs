namespace DesktopPet.Domain.Pets;

// Engine defaults and hard safety limits live together, never in UI/character-name branches.
public sealed record RuntimePolicy
{
    public TimeSpan RecentWindow { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan RepeatWindow { get; init; } = TimeSpan.FromSeconds(12);
    public TimeSpan InteractionWindow { get; init; } = TimeSpan.FromSeconds(15);
    public TimeSpan InteractionDebounce { get; init; } = TimeSpan.FromMilliseconds(500);
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan LogInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan MaxElapsedEmotion { get; init; } = TimeSpan.FromMinutes(5);
    public int RecentCapacity { get; init; } = 64;
    public double RepeatPenalty { get; init; } = .45;
    public double SignificantEmotionChange { get; init; } = 10;
    public int MaxInteractionCount { get; init; } = 1_000_000;
    public double RecentInteractionBoost { get; init; } = 3;
    public double NightRestBoost { get; init; } = 1.5;
    public TimeOnly NightStarts { get; init; } = new(22, 0);
    public TimeOnly NightEnds { get; init; } = new(6, 0);
    public double MaxWeight { get; init; } = 10;
    public TimeSpan MinActionCooldown { get; init; } = TimeSpan.FromSeconds(2);
    public TimeSpan MaxCooldown { get; init; } = TimeSpan.FromMinutes(5);
    public EmotionDelta IdlePerMinute { get; init; } = new(-.1, -.3, 1, 0);
    public EmotionDelta RestPerMinute { get; init; } = new(.1, 4, -.3, 0);
    public EmotionDelta InteractionDelta { get; init; } = new(4, -.2, -12, .2);
    public EmotionDelta ActiveBehaviorDelta { get; init; } = new(.5, -.3, -1, 0);
    public IReadOnlyList<BehaviorDefinition> Defaults() =>
    [
        new(BehaviorId.Idle, AnimationSemantic.Idle, 2, TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(6),
            BehaviorPriority.Low, true, [], []),
        new(BehaviorId.Blink, new("blink"), 6, TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(.2), TimeSpan.FromSeconds(.5),
            BehaviorPriority.Low, true, [new("blink")], []),
        new(BehaviorId.Happy, new("happy"), 1.2, TimeSpan.FromSeconds(18), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3),
            BehaviorPriority.Low, true, [new("happy")], [new(EmotionAxis.Mood, ModifierDirection.High, 2),
                new(EmotionAxis.Boredom, ModifierDirection.High, 2), new(EmotionAxis.Affinity, ModifierDirection.High, .2)]),
        new(BehaviorId.Rest, new("rest"), .8, TimeSpan.FromSeconds(40), TimeSpan.FromSeconds(6), TimeSpan.FromSeconds(12),
            BehaviorPriority.Low, true, [new("rest")], [new(EmotionAxis.Energy, ModifierDirection.Low, 5)])
    ];
}
public sealed record EmotionDelta(double Mood, double Energy, double Boredom, double Affinity);
