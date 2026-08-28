using DesktopPet.Application.Configuration;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Runtime;

public sealed class BehaviorCatalog(RuntimePolicy policy)
{
    public IReadOnlyList<BehaviorDefinition> Build(CharacterBehaviorProfile profile, RuntimePreferences preferences)
    {
        return policy.Defaults().Select(basis =>
        {
            var recommended = profile.Behaviors.FirstOrDefault(item => item.Animation == basis.Semantic.Value);
            var user = preferences.Behaviors?.FirstOrDefault(item => item is not null && item.Behavior == basis.Id);
            var semantic = basis.Semantic;
            // Phase 2 emotion profiles contain semantic mappings, not numeric emotional tendencies.
            if (basis.Id != BehaviorId.Idle && profile.EmotionAnimations.TryGetValue(basis.Semantic.Value, out var mapped))
                semantic = new(mapped);
            var seconds = user?.CooldownSeconds ?? recommended?.CooldownSeconds ?? basis.Cooldown.TotalSeconds;
            return basis with
            {
                Semantic = semantic,
                RequiredCapabilities = basis.Id == BehaviorId.Idle ? [] : [semantic],
                BaseWeight = RuntimeLimits.Clamp(user?.Weight ?? basis.BaseWeight, 0, policy.MaxWeight),
                CharacterModifier = user?.Weight is not null ? 1 : RuntimeLimits.Factor(recommended?.Weight ?? 1),
                Cooldown = basis.Id == BehaviorId.Idle ? TimeSpan.Zero :
                    TimeSpan.FromSeconds(RuntimeLimits.Clamp(seconds, policy.MinActionCooldown.TotalSeconds, policy.MaxCooldown.TotalSeconds)),
                Enabled = basis.Id == BehaviorId.Idle || (user?.Enabled ?? true)
            };
        }).ToArray();
    }
}
