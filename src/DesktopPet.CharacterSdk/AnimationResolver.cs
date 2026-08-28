using DesktopPet.Domain.Pets;

namespace DesktopPet.CharacterSdk;

public sealed record ResolvedAnimation(AnimationSemantic Semantic, AnimationDefinition Definition);
public sealed class AnimationResolver
{
    public IReadOnlyList<ResolvedAnimation> Candidates(CharacterDefinition character, AnimationSemantic requested)
    {
        var result = new List<ResolvedAnimation>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string semantic)
        {
            // Untrusted author chains must not grow the UI thread's call stack.
            while (seen.Add(semantic))
            {
                if (character.Animations.TryGetValue(semantic, out var animation)) result.Add(new(new(semantic), animation));
                if (!character.Manifest.Animations.TryGetValue(semantic, out var declared) || declared?.Fallback is not { } fallback) break;
                semantic = fallback;
            }
        }
        Visit(requested.Value ?? "idle");
        var category = requested.Value switch
        {
            "mouth-open" or "mouth-closed" => "talking",
            "sleep" or "sleeping" => "rest",
            "celebrate" or "excited" => "happy",
            _ => "idle"
        };
        Visit(category);
        Visit("idle");
        result.Add(new(new("fallback"), new AnimationDefinition
        {
            Type = AnimationFormat.StaticPng, Path = character.Assets.Fallback,
            Frames = [new(character.Assets.Fallback, 1000)], Loop = false
        }));
        return result.AsReadOnly();
    }
}
