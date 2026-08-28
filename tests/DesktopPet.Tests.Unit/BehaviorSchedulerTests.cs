using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;
using DesktopPet.Tests.Shared;

namespace DesktopPet.Tests.Unit;

public sealed class BehaviorSchedulerTests
{
    [Fact]
    public async Task SchedulerUsesVirtualTimeRejectsDuplicateRunCancelsAndResumes()
    {
        var clock = new ManualTimeProvider();
        var policy = new RuntimePolicy();
        var random = new SeededRandomSource(5);
        var player = new WaitingPlayer(clock);
        var scheduler = new BehaviorScheduler(clock, random, policy, new LocalBehaviorDecisionEngine(policy, random), player, new RecordingLogger());
        scheduler.Configure(policy.Defaults(), new HashSet<AnimationSemantic>([new("idle"), new("blink")]), EmotionState.Initial);
        scheduler.IsVisible = true;
        using var cancellation = new CancellationTokenSource();
        var loop = scheduler.RunAsync(null, cancellation.Token);
        Assert.True(scheduler.IsRunning);
        Assert.Equal(1, player.Calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => scheduler.RunAsync(null, cancellation.Token));
        clock.Advance(TimeSpan.FromSeconds(7));
        await player.Second.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(player.Calls >= 2);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
        Assert.False(scheduler.IsRunning);
        scheduler.IsVisible = false;
        var before = player.Calls;
        await scheduler.RunAsync(null, default);
        Assert.Equal(before, player.Calls);
        scheduler.IsVisible = true;
        using var resumed = new CancellationTokenSource();
        var again = scheduler.RunAsync(null, resumed.Token);
        Assert.Equal(before + 1, player.Calls);
        resumed.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => again);
    }
    [Fact]
    public void InteractionIsDebouncedAndRecentContextResetsOnCharacterChange()
    {
        var clock = new ManualTimeProvider();
        var policy = new RuntimePolicy();
        var random = new SeededRandomSource(2);
        var scheduler = new BehaviorScheduler(clock, random, policy, new LocalBehaviorDecisionEngine(policy, random), new WaitingPlayer(clock), new RecordingLogger());
        Assert.True(scheduler.Interact());
        var mood = scheduler.Emotion.Current.Mood;
        Assert.False(scheduler.Interact());
        Assert.Equal(mood, scheduler.Emotion.Current.Mood);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(scheduler.Interact());
        Assert.Equal(3, scheduler.InteractionCount);
        scheduler.Memory.Record(BehaviorId.Happy, clock.GetUtcNow());
        scheduler.Configure(policy.Defaults(), new HashSet<AnimationSemantic>(), EmotionState.Initial);
        Assert.Empty(scheduler.Diagnostic.Recent.RecentBehaviors);
        Assert.Null(scheduler.LastInteractionUtc);
    }
    [Fact]
    public async Task CheckpointUsesInjectedTimeAndOccursOnlyAfterInterval()
    {
        var clock = new ManualTimeProvider();
        var policy = new RuntimePolicy();
        var random = new SeededRandomSource(5);
        var player = new WaitingPlayer(clock);
        var scheduler = new BehaviorScheduler(clock, random, policy, new LocalBehaviorDecisionEngine(policy, random), player, new RecordingLogger());
        scheduler.Configure(policy.Defaults(), new HashSet<AnimationSemantic>([AnimationSemantic.Idle]), EmotionState.Initial);
        scheduler.IsVisible = true;
        var persisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var checkpoints = 0;
        scheduler.CheckpointAsync = _ => { checkpoints++; persisted.TrySetResult(); return Task.CompletedTask; };
        using var cancellation = new CancellationTokenSource();
        var loop = scheduler.RunAsync(null, cancellation.Token);
        clock.Advance(TimeSpan.FromSeconds(7));
        await player.Second.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(0, checkpoints);
        clock.Advance(policy.CheckpointInterval);
        await persisted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
        Assert.Equal(1, checkpoints);
    }

    private sealed class WaitingPlayer(TimeProvider clock) : IBehaviorAnimationPlayer
    {
        public int Calls { get; private set; }
        public TaskCompletionSource Second { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task PlayBehaviorAsync(AnimationSemantic semantic, TimeSpan minimum, TimeSpan maximum, bool repeat,
            Action<AnimationSemantic> resolved, CancellationToken ct)
        {
            Calls++;
            resolved(semantic);
            var completion = Task.Delay(maximum, clock, ct);
            if (Calls == 2) Second.TrySetResult();
            await completion;
        }
    }
}
