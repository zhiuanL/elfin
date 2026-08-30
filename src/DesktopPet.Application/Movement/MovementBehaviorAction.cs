using DesktopPet.Application.Characters;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Movement;
using DesktopPet.Domain.Pets;

namespace DesktopPet.Application.Movement;

// Runtime-side adapter: coordinates never enter Utility or animation providers.
public sealed class MovementBehaviorAction(IMovementService movement, CharacterPresentationService presentation) : IBehaviorActionExecutor
{
    public bool CanExecute(BehaviorId behavior) => behavior == BehaviorId.Move;
    public async Task ExecuteAsync(BehaviorDefinition behavior, Action<AnimationSemantic> resolved, CancellationToken ct)
    {
        var plan = await movement.PlanAsync(ct);
        if (plan is null) return; // Scheduler immediately returns to its normal idle action.
        var package = presentation.Current!;
        var animation = MovementAnimationResolver.Resolve(package.Definition.Animations.Keys.Select(s => new AnimationSemantic(s)).ToHashSet(),
            package.Definition.Manifest.SupportsMirroring, plan.Facing);
        using var active = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await presentation.SetMirroredAsync(animation.Mirrored, ct);
        var playback = presentation.PlayBehaviorAsync(animation.Semantic, MotionPolicy.MaxMovementDuration,
            MotionPolicy.MaxMovementDuration, true, resolved, active.Token);
        var moving = movement.ExecuteAsync(plan, active.Token);
        try
        {
            var first = await Task.WhenAny(playback, moving);
            await first; // Animation failure also cancels movement.
            if (first == playback && !moving.IsCompleted) active.Cancel();
            await moving;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { /* Platform ownership/long frame gap: end this action and let the scheduler reconsider. */ }
        finally
        {
            active.Cancel();
            try { await Task.WhenAll(playback, moving); }
            catch (OperationCanceledException) when (active.IsCancellationRequested) { }
            finally { await presentation.SetMirroredAsync(false, CancellationToken.None); }
        }
    }
}
