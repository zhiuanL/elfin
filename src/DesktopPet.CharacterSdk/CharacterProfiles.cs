namespace DesktopPet.CharacterSdk;

// Declarative content only. Runtime consumers remain in their owning application modules.
public sealed record PersonaProfile(string Summary, IReadOnlyList<string> Traits);
public sealed record DialogueProfile(IReadOnlyDictionary<string, IReadOnlyList<string>> Lines);
public sealed record EmotionProfile(IReadOnlyDictionary<string, string> Animations);
public sealed record BehaviorRecommendation(string Animation, double Weight, double CooldownSeconds);
public sealed record BehaviorProfile(IReadOnlyList<BehaviorRecommendation> Behaviors);
public sealed record HitAreaDefinition(string Id, double X, double Y, double Width, double Height);
public sealed record HitAreaProfile(IReadOnlyList<HitAreaDefinition> Areas);
public sealed record CharacterVoiceProfile(string Provider, string Voice, double Speed = 1, double Volume = 1);
