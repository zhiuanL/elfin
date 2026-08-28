using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Configuration;

public sealed record BehaviorOverride
{
    public BehaviorId Behavior { get; init; }
    public double? Weight { get; init; }
    public double? CooldownSeconds { get; init; }
    public bool? Enabled { get; init; }
}
public sealed record RuntimePreferences
{
    public IReadOnlyList<BehaviorOverride> Behaviors { get; init; } = [];
    public bool Equals(RuntimePreferences? other) => ReferenceEquals(this, other) || other is not null &&
        (Behaviors is null ? other.Behaviors is null : other.Behaviors is not null && Behaviors.SequenceEqual(other.Behaviors));
    public override int GetHashCode()
    {
        var hash = new HashCode();
        if (Behaviors is not null) foreach (var behavior in Behaviors) hash.Add(behavior);
        return hash.ToHashCode();
    }
}
public sealed record EmotionCheckpoint
{
    public string CharacterId { get; init; } = string.Empty;
    public double Mood { get; init; } = 60;
    public double Energy { get; init; } = 70;
    public double Affinity { get; init; } = 20;
    public DateTimeOffset SavedAtUtc { get; init; }
    public EmotionState Restore() => new(RuntimeLimits.Percentage(Mood), RuntimeLimits.Percentage(Energy), new(20), RuntimeLimits.Percentage(Affinity));
    public static EmotionCheckpoint From(string id, EmotionState emotion, DateTimeOffset now) =>
        new() { CharacterId = id, Mood = emotion.Mood.Value, Energy = emotion.Energy.Value, Affinity = emotion.Affinity.Value, SavedAtUtc = now };
}
