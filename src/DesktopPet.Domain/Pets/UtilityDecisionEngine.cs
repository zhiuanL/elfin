namespace DesktopPet.Domain.Pets;

public sealed class UtilityDecisionEngine(RuntimePolicy policy, IRandomSource random)
{
    public BehaviorDecision Decide(BehaviorContext context, IReadOnlyList<BehaviorDefinition> behaviors)
    {
        var scores = behaviors.Select(behavior => Score(behavior, context)).ToArray();
        var total = scores.Sum(score => score.FinalScore);
        if (total <= 0)
            return new(policy.Defaults()[0], Array.AsReadOnly(scores), true);
        var sample = RuntimeLimits.Clamp(random.NextUnit(), 0, Math.BitDecrement(1.0)) * total;
        for (var i = 0; i < scores.Length; i++)
        {
            if (scores[i].FinalScore <= 0) continue;
            sample -= scores[i].FinalScore;
            if (sample < 0) return new(behaviors[i], Array.AsReadOnly(scores), false);
        }
        return new(policy.Defaults()[0], Array.AsReadOnly(scores), true);
    }
    public UtilityScore Score(BehaviorDefinition behavior, BehaviorContext context)
    {
        var recent = context.RecentBehaviors;
        var remaining = TimeSpan.Zero;
        if (recent.LastExecutionTime.TryGetValue(behavior.Id, out var last))
            remaining = behavior.Cooldown - (context.Now - last);
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        var lastAction = recent.RecentBehaviors.LastOrDefault(item => item.Behavior != BehaviorId.Idle);
        var filter = !context.IsPetVisible ? CandidateFilter.Hidden : context.IsUserInteracting ? CandidateFilter.Interacting :
            !behavior.Enabled || !behavior.IsValid ? CandidateFilter.Disabled :
            behavior.RequiredCapabilities.Any(capability => !context.CurrentCharacterCapabilities.Contains(capability)) ? CandidateFilter.MissingCapability :
            behavior.Id != BehaviorId.Idle && remaining > TimeSpan.Zero ? CandidateFilter.Cooldown :
            behavior.Id != BehaviorId.Idle && lastAction?.Behavior == behavior.Id && context.Now - lastAction.StartedAtUtc < policy.RepeatWindow ?
                CandidateFilter.RecentRepeat : CandidateFilter.None;
        var emotion = behavior.EmotionModifiers.Aggregate(1.0, (product, modifier) => RuntimeLimits.Factor(product * modifier.Evaluate(context.EmotionState)));
        var interaction = context.LastInteractionTime is { } at && context.Now >= at && context.Now - at < policy.InteractionWindow;
        var environment = behavior.Id == BehaviorId.Happy && interaction ? policy.RecentInteractionBoost :
            behavior.Id == BehaviorId.Rest && (context.TimeOfDay >= policy.NightStarts || context.TimeOfDay < policy.NightEnds) ? policy.NightRestBoost : 1;
        var suppression = behavior.Id == BehaviorId.Idle ? 1 : Math.Pow(policy.RepeatPenalty, recent.ExecutionCountInWindow(behavior.Id));
        var basis = RuntimeLimits.Clamp(behavior.BaseWeight, 0, policy.MaxWeight);
        var character = RuntimeLimits.Factor(behavior.CharacterModifier);
        var user = RuntimeLimits.Factor(behavior.UserModifier);
        var final = filter == CandidateFilter.None ? basis * character * emotion * environment * user * suppression : 0;
        return new(behavior.Id, basis, character, emotion, environment, user, suppression, final, filter, remaining);
    }
}
