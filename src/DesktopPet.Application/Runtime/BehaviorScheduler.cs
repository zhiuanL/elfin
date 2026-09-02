using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Domain.Pets;
using DesktopPet.Application.Movement;

namespace DesktopPet.Application.Runtime;

// Owned by one PetRuntime. The loop never acquires the runtime's lifecycle semaphore.
public sealed class BehaviorScheduler(TimeProvider clock, IRandomSource random, RuntimePolicy policy,
    IBehaviorDecisionEngine decisions, IBehaviorAnimationPlayer player, IAppLogger logger, IBehaviorActionExecutor? actions = null)
{
    private int _running;
    private long _updatedAt;
    private long _checkpointAt;
    private readonly Dictionary<AppEvent, DateTimeOffset> _logged = [];
    private IReadOnlyList<BehaviorDefinition> _behaviors = policy.Defaults();
    private IReadOnlySet<AnimationSemantic> _capabilities = new HashSet<AnimationSemantic>();
    private IReadOnlyList<UtilityScore> _scores = [];
    public PetStateMachine State { get; } = new();
    public RuntimeEmotionService Emotion { get; } = new(policy);
    public RecentBehaviorMemory Memory { get; } = new(policy);
    public bool IsRunning => Volatile.Read(ref _running) != 0;
    public bool IsVisible { get; set; }
    public bool IsInteracting { get; set; }
    public int InteractionCount { get; private set; }
    public DateTimeOffset? LastInteractionUtc { get; private set; }
    public bool FocusMode { get; set; }
    public DesktopPet.Domain.Productivity.PomodoroPhase? PomodoroPhase { get; set; }
    public RuntimeDiagnostic Diagnostic => new(State.Current, Emotion.Current, _scores, Memory.Snapshot(clock.GetUtcNow()),
        IsRunning, IsVisible, IsInteracting, InteractionCount, LastInteractionUtc);
    public event EventHandler? Changed;
    public Func<CancellationToken, Task> CheckpointAsync { get; set; } = _ => Task.CompletedTask;
    public void Configure(IReadOnlyList<BehaviorDefinition> behaviors, IReadOnlySet<AnimationSemantic> capabilities, EmotionState emotion)
    {
        if (IsRunning) throw new InvalidOperationException("Stop the scheduler before reconfiguring it.");
        _behaviors = behaviors; _capabilities = capabilities;
        Emotion.Restore(emotion); Memory.Clear(); State.Complete(); _scores = [];
        LastInteractionUtc = null; InteractionCount = 0; IsInteracting = false;
        _checkpointAt = clock.GetTimestamp();
        Publish();
    }
    public bool Interact()
    {
        if (IsRunning) throw new InvalidOperationException("Stop the scheduler before applying an interaction.");
        InteractionCount = Math.Min(InteractionCount + 1, policy.MaxInteractionCount);
        var now = clock.GetUtcNow();
        if (LastInteractionUtc is { } last && now - last < policy.InteractionDebounce) return false;
        LastInteractionUtc = now;
        var before = Emotion.Current;
        Emotion.Interact();
        if (Math.Abs(before.Boredom.Value - Emotion.Current.Boredom.Value) >= policy.SignificantEmotionChange ||
            Math.Abs(before.Mood.Value - Emotion.Current.Mood.Value) >= policy.SignificantEmotionChange) Log(AppEvent.EmotionChanged);
        Publish();
        return true;
    }
    public async Task RunAsync(BehaviorDefinition? first, CancellationToken ct)
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) throw new InvalidOperationException("A scheduler is already running.");
        _updatedAt = clock.GetTimestamp();
        Log(AppEvent.SchedulerStarted);
        Publish();
        try
        {
            var next = first ?? _behaviors.First(item => item.Id == BehaviorId.Idle);
            while (!ct.IsCancellationRequested && IsVisible && !IsInteracting)
            {
                ct.ThrowIfCancellationRequested();
                await ExecuteAsync(next, ct);
                if (clock.GetElapsedTime(_checkpointAt) >= policy.CheckpointInterval)
                {
                    await CheckpointAsync(ct);
                    _checkpointAt = clock.GetTimestamp();
                }
                if (next.Id != BehaviorId.Idle) { next = _behaviors.First(item => item.Id == BehaviorId.Idle); continue; }
                var now = clock.GetUtcNow();
                var context = new BehaviorContext(now, State.Current.Primary, Emotion.Current, Memory.Snapshot(now), _capabilities,
                    LastInteractionUtc, IsVisible, IsInteracting, TimeOnly.FromDateTime(clock.GetLocalNow().DateTime),
                    FocusMode, PomodoroPhase);
                var decision = decisions.Decide(context, _behaviors);
                _scores = decision.Scores;
                if (decision.UsedFallback) Log(AppEvent.DecisionFallback);
                next = decision.Behavior;
                Publish();
            }
        }
        finally
        {
            AdvanceEmotion();
            State.Complete();
            Volatile.Write(ref _running, 0);
            Log(AppEvent.SchedulerStopped);
            Publish();
        }
    }
    private async Task ExecuteAsync(BehaviorDefinition behavior, CancellationToken ct)
    {
        if (!State.TryEnter(behavior, clock.GetUtcNow())) throw new InvalidOperationException("Illegal scheduled transition.");
        Memory.Record(behavior.Id, clock.GetUtcNow());
        Log(AppEvent.BehaviorSelected, behavior.Id);
        var duration = behavior.MinDuration + TimeSpan.FromTicks((long)((behavior.MaxDuration - behavior.MinDuration).Ticks *
            RuntimeLimits.Clamp(random.NextUnit(), 0, 1)));
        Publish();
        var before = Emotion.Current;
        var repeat = behavior.Id is BehaviorId.Idle or BehaviorId.Rest;
        void Resolved(AnimationSemantic semantic)
        {
            if (ct.IsCancellationRequested) return;
            State.ResolveAnimation(semantic);
            if (semantic != behavior.Semantic) Log(AppEvent.DecisionFallback, behavior.Id);
            Log(AppEvent.StateChanged, behavior.Id);
            Publish();
        }
        if (actions?.CanExecute(behavior.Id) == true) await actions.ExecuteAsync(behavior, Resolved, ct);
        else await player.PlayBehaviorAsync(behavior.Semantic, repeat ? duration : behavior.MinDuration, duration, repeat, Resolved, ct);
        AdvanceEmotion();
        Emotion.Complete(behavior.Id);
        if (Math.Abs(before.Energy.Value - Emotion.Current.Energy.Value) >= policy.SignificantEmotionChange ||
            Math.Abs(before.Mood.Value - Emotion.Current.Mood.Value) >= policy.SignificantEmotionChange) Log(AppEvent.EmotionChanged);
        State.Complete();
        Publish();
    }
    private void AdvanceEmotion()
    {
        Emotion.Elapse(clock.GetElapsedTime(_updatedAt), State.Current.Primary);
        _updatedAt = clock.GetTimestamp();
    }
    public void Publish() => Changed?.Invoke(this, EventArgs.Empty);
    private void Log(AppEvent kind, BehaviorId? behavior = null)
    {
        var now = clock.GetUtcNow();
        if (_logged.TryGetValue(kind, out var last) && now - last < policy.LogInterval) return;
        _logged[kind] = now;
        logger.Write(new(kind, now, Behavior: behavior, State: State.Current.Primary));
    }
}
