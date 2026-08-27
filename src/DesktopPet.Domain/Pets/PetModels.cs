namespace DesktopPet.Domain.Pets;

public readonly record struct PetInstanceId(Guid Value);
public readonly record struct CharacterId(string Value);
public readonly record struct AnimationSemantic(string Value)
{
    public static AnimationSemantic Idle => new("idle");
}

public enum PetPrimaryState { Idle, Acting, Moving, Dragging, Resting, Sleeping, Talking, Focus, Notification }
public enum PetTransientState { Blink, Happy, Surprised, Bored, Stretch, LookAround }
public enum BehaviorPriority { Low, Medium, High, Critical }
public enum MovementMode { Fixed, Local, Desktop, Hybrid }
public enum HybridMovementStrategy { Anchor, Roaming, Scenario, SmartHybrid }
public enum DisplayPolicy { PrimaryOnly, LockedCurrent, SelectedMonitors, AllMonitors }
public enum MotionStyle { Quiet, Natural, Lively }
public enum PerformanceMode { Auto, PowerSaver, Balanced, HighQuality }

public readonly record struct Percentage
{
    public int Value { get; }
    public Percentage(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 100);
        Value = value;
    }
}

public sealed record EmotionState(Percentage Mood, Percentage Energy, Percentage Boredom, Percentage Affinity)
{
    public static EmotionState Initial => new(new(60), new(70), new(20), new(20));
}

public sealed record PetSnapshot(PetInstanceId InstanceId, CharacterId CharacterId,
    PetPrimaryState State, EmotionState Emotion);
