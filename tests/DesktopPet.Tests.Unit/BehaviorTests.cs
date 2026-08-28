using DesktopPet.Application.Configuration;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Tests.Unit;

public sealed class BehaviorTests
{
    private readonly RuntimePolicy _policy = new();
    private static DateTimeOffset Now => new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);
    private BehaviorContext Context(EmotionState? emotion = null, RecentBehaviorContext? recent = null) =>
        new(Now, PetPrimaryState.Idle, emotion ?? EmotionState.Initial, recent ?? new([], new Dictionary<BehaviorId, DateTimeOffset>()),
            new HashSet<AnimationSemantic>([new("idle"), new("blink"), new("happy"), new("rest")]), null, true, false, new(12, 0));
    [Theory]
    [InlineData(BehaviorId.Idle, PetPrimaryState.Idle)]
    [InlineData(BehaviorId.Blink, PetPrimaryState.Acting)]
    [InlineData(BehaviorId.Happy, PetPrimaryState.Acting)]
    [InlineData(BehaviorId.Rest, PetPrimaryState.Resting)]
    public void StateEntryCompletionAndSemanticFallback(BehaviorId id, PetPrimaryState expected)
    {
        var machine = new PetStateMachine();
        var transitions = 0;
        machine.Changed += (_, _) => transitions++;
        Assert.True(machine.TryEnter(_policy.Defaults().First(item => item.Id == id), Now));
        Assert.Equal(expected, machine.Current.Primary);
        machine.ResolveAnimation(AnimationSemantic.Idle);
        Assert.Equal(PetPrimaryState.Idle, machine.Current.Primary);
        Assert.Equal(id, machine.Current.Behavior);
        machine.Complete();
        Assert.Equal(BehaviorId.Idle, machine.Current.Behavior);
        Assert.Equal(3, transitions);
    }
    [Fact]
    public void InvalidTransitionsMinDurationPriorityAndCriticalInterruptAreEnforced()
    {
        var machine = new PetStateMachine();
        var happy = _policy.Defaults()[2];
        Assert.False(machine.TryEnter(happy with { Id = (BehaviorId)999 }, Now));
        Assert.False(machine.TryEnter(happy with { MinDuration = TimeSpan.Zero }, Now));
        Assert.True(machine.TryEnter(happy, Now));
        Assert.False(machine.TryEnter(happy, Now.AddSeconds(10)));
        Assert.False(machine.CanInterrupt(BehaviorPriority.High, Now));
        Assert.True(machine.CanInterrupt(BehaviorPriority.Medium, Now.AddSeconds(2)));
        machine.Complete();
        Assert.True(machine.TryEnter(happy with { Interruptible = false }, Now));
        Assert.False(machine.CanInterrupt(BehaviorPriority.High, Now.AddMinutes(1)));
        Assert.True(machine.CanInterrupt(BehaviorPriority.Critical, Now));
        machine.BeginInteraction();
        Assert.Equal(PetPrimaryState.Dragging, machine.Current.Primary);
    }
    [Theory]
    [InlineData(-500, 0)]
    [InlineData(99999, 100)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void EmotionAndRestoredValuesAreCentrallyClamped(double value, int expected)
    {
        var model = new EmotionModel(_policy);
        model.Apply(new(value, value, value, value), 1000);
        Assert.InRange(model.Current.Mood.Value, 0, 100);
        var restored = new EmotionCheckpoint { Mood = value, Energy = value, Affinity = value }.Restore();
        Assert.Equal(expected, restored.Mood.Value);
        Assert.Equal(20, restored.Boredom.Value);
    }
    [Fact]
    public void ElapsedTimeIsCappedRestRecoversAndInteractionImprovesEmotion()
    {
        var a = new EmotionModel(_policy);
        var b = new EmotionModel(_policy);
        a.Elapse(TimeSpan.FromDays(100), PetPrimaryState.Idle);
        b.Elapse(_policy.MaxElapsedEmotion, PetPrimaryState.Idle);
        Assert.Equal(a.Current, b.Current);
        Assert.True(a.Current.Boredom.Value > 20);
        var before = a.Current;
        a.Interact();
        Assert.True(a.Current.Mood.Value > before.Mood.Value);
        Assert.True(a.Current.Boredom.Value < before.Boredom.Value);
        a.Elapse(TimeSpan.FromMinutes(2), PetPrimaryState.Resting);
        Assert.True(a.Current.Energy.Value > before.Energy.Value);
    }
    [Fact]
    public void FractionalElapsedUpdatesAccumulateWithoutTickRoundingLoss()
    {
        var model = new EmotionModel(_policy);
        for (var i = 0; i < 60; i++) model.Elapse(TimeSpan.FromSeconds(1), PetPrimaryState.Idle);
        Assert.Equal(21, model.Current.Boredom.Value);
    }
    [Fact]
    public void UtilityRespondsToEnergyMoodBoredomAndInteraction()
    {
        var engine = new UtilityDecisionEngine(_policy, new SeededRandomSource(17));
        var happy = _policy.Defaults()[2];
        var rest = _policy.Defaults()[3];
        Assert.True(engine.Score(rest, Context(new(new(60), new(10), new(20), new(20)))).FinalScore >
            engine.Score(rest, Context(new(new(60), new(90), new(20), new(20)))).FinalScore);
        Assert.True(engine.Score(happy, Context(new(new(90), new(70), new(90), new(20)))).FinalScore >
            engine.Score(happy, Context(new(new(10), new(70), new(10), new(20)))).FinalScore);
        Assert.True(engine.Score(happy, Context() with { LastInteractionTime = Now }).FinalScore > engine.Score(happy, Context()).FinalScore);
    }
    [Theory]
    [InlineData(CandidateFilter.Hidden)]
    [InlineData(CandidateFilter.Interacting)]
    [InlineData(CandidateFilter.MissingCapability)]
    [InlineData(CandidateFilter.Cooldown)]
    [InlineData(CandidateFilter.RecentRepeat)]
    [InlineData(CandidateFilter.Disabled)]
    public void IneligibleBehaviorsAreFilteredBeforeScoring(CandidateFilter reason)
    {
        var context = Context();
        var behavior = _policy.Defaults()[1];
        context = reason switch
        {
            CandidateFilter.Hidden => context with { IsPetVisible = false },
            CandidateFilter.Interacting => context with { IsUserInteracting = true },
            CandidateFilter.MissingCapability => context with { CurrentCharacterCapabilities = new HashSet<AnimationSemantic>() },
            CandidateFilter.Cooldown => context with { RecentBehaviors = new([], new Dictionary<BehaviorId, DateTimeOffset> { [BehaviorId.Blink] = Now }) },
            CandidateFilter.RecentRepeat => context with { RecentBehaviors = new([new(BehaviorId.Blink, Now.AddSeconds(-5))], new Dictionary<BehaviorId, DateTimeOffset>()) },
            _ => context
        };
        if (reason == CandidateFilter.Disabled) behavior = behavior with { Enabled = false };
        var score = new UtilityDecisionEngine(_policy, new SeededRandomSource(1)).Score(behavior, context);
        Assert.Equal(reason, score.Filter);
        Assert.Equal(0, score.FinalScore);
    }
    [Fact]
    public void ZeroScoresAlwaysFallBackToSafeIdleAndSeededChoicesRepeat()
    {
        var zero = _policy.Defaults().Select(item => item with { BaseWeight = 0 }).ToArray();
        var engine = new UtilityDecisionEngine(_policy, new SeededRandomSource(1));
        var result = engine.Decide(Context(), zero);
        Assert.True(result.UsedFallback);
        Assert.Equal(BehaviorId.Idle, result.Behavior.Id);
        var first = new UtilityDecisionEngine(_policy, new SeededRandomSource(42));
        var second = new UtilityDecisionEngine(_policy, new SeededRandomSource(42));
        var choices = Enumerable.Range(0, 100).Select(_ => first.Decide(Context(), _policy.Defaults()).Behavior.Id).ToArray();
        Assert.Equal(choices, Enumerable.Range(0, 100).Select(_ => second.Decide(Context(), _policy.Defaults()).Behavior.Id));
        Assert.True(choices.Distinct().Count() > 2);
        var weighted = _policy.Defaults().Select(item => item with { BaseWeight = item.Id == BehaviorId.Happy ? 1 : 0 }).ToArray();
        Assert.All(Enumerable.Range(0, 20), _ => Assert.Equal(BehaviorId.Happy, first.Decide(Context(), weighted).Behavior.Id));
    }
    [Fact]
    public void RecentMemoryIsBoundedPrunesCountsButRetainsIndependentCooldowns()
    {
        var memory = new RecentBehaviorMemory(_policy);
        for (var i = 0; i < 100; i++) memory.Record(BehaviorId.Blink, Now.AddSeconds(i));
        var current = memory.Snapshot(Now.AddSeconds(100));
        Assert.Equal(64, current.ExecutionCountInWindow(BehaviorId.Blink));
        Assert.Equal(BehaviorId.Blink, current.LastBehavior);
        Assert.Empty(memory.Snapshot(Now.AddMinutes(10)).RecentBehaviors);
        Assert.Equal(Now.AddSeconds(99), memory.Snapshot(Now.AddMinutes(10)).LastExecutionTime[BehaviorId.Blink]);
        memory.Clear();
        Assert.Empty(memory.Snapshot(Now).LastExecutionTime);
    }
    [Fact]
    public void SystemLimitsOverrideUserWhoOverridesCharacterAndDefaults()
    {
        var profile = new CharacterBehaviorProfile([new("happy", 9, 99999)], new Dictionary<string, string>());
        var catalog = new BehaviorCatalog(_policy);
        var authored = catalog.Build(profile, new())[2];
        Assert.Equal(9, authored.CharacterModifier);
        Assert.Equal(_policy.MaxCooldown, authored.Cooldown);
        var user = catalog.Build(profile, new() { Behaviors = [new() { Behavior = BehaviorId.Happy, Weight = 99999, CooldownSeconds = -100 }] })[2];
        Assert.Equal(10, user.BaseWeight);
        Assert.Equal(1, user.CharacterModifier);
        Assert.Equal(_policy.MinActionCooldown, user.Cooldown);
        Assert.True(catalog.Build(profile, new() { Behaviors = [new() { Behavior = BehaviorId.Idle, Enabled = false }] })[0].Enabled);
        Assert.Equivalent(_policy.Defaults()[1], catalog.Build(CharacterBehaviorProfile.Empty, new())[1]);
    }

    [Fact]
    public void ProfilesCannotRemapSafeIdleAndAffinityHasOnlyALightInfluence()
    {
        var profile = new CharacterBehaviorProfile([], new Dictionary<string, string> { ["idle"] = "rest", ["happy"] = "blink" });
        var catalog = new BehaviorCatalog(_policy).Build(profile, new());
        Assert.Equal(AnimationSemantic.Idle, catalog[0].Semantic);
        Assert.Equal(new AnimationSemantic("blink"), catalog[2].Semantic);
        var engine = new UtilityDecisionEngine(_policy, new SeededRandomSource(1));
        var low = engine.Score(_policy.Defaults()[2], Context(new(new(60), new(70), new(20), new(0)))).FinalScore;
        var high = engine.Score(_policy.Defaults()[2], Context(new(new(60), new(70), new(20), new(100)))).FinalScore;
        Assert.InRange(high / low, 1.19, 1.21);
    }
}
