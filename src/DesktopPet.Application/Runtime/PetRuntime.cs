using DesktopPet.Application.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.CharacterSdk;
using DesktopPet.Domain.Pets;
using DesktopPet.Domain.Movement;
using DesktopPet.Application.Movement;

namespace DesktopPet.Application.Runtime;

// Instance-scoped mutable state; PetHost owns one instance in V1.
public sealed class PetRuntime : IPetRuntime, ICharacterPresentation, ISpeechVisualController, IDisposable
{
    private readonly CharacterPresentationService _presentation;
    private readonly ISettingsService _settings;
    private readonly ICharacterBehaviorProfileReader _profiles;
    private readonly TimeProvider _clock;
    private readonly RuntimePolicy _policy;
    private readonly IExceptionHandler _exceptions;
    private readonly IAppLogger _logger;
    private readonly IMovementService? _movement;
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly BehaviorScheduler _scheduler;
    private CancellationTokenSource? _lifetime, _run;
    private Task _loop = Task.CompletedTask;
    private bool _started, _stopped, _disposed;
    private bool _sessionSuspended;
    private SpeechVisualMode _speechVisualMode;
    private RuntimeDiagnostic _diagnostic;
    public PetRuntime(CharacterPresentationService presentation, ISettingsService settings, ICharacterBehaviorProfileReader profiles,
        TimeProvider clock, RuntimePolicy policy, IRandomSource random, IExceptionHandler exceptions, IAppLogger logger,
        IMovementService? movement = null)
    {
        _presentation = presentation; _settings = settings; _profiles = profiles; _clock = clock;
        _policy = policy; _exceptions = exceptions; _logger = logger;
        _movement = movement;
        _scheduler = new(clock, random, policy, new LocalBehaviorDecisionEngine(policy, random), presentation, logger,
            movement is null ? null : new MovementBehaviorAction(movement, presentation));
        _scheduler.CheckpointAsync = SaveEmotionAsync;
        _scheduler.Changed += OnChanged;
        _diagnostic = _scheduler.Diagnostic;
    }
    public PetInstanceId InstanceId { get; } = new(Guid.NewGuid());
    public CharacterPackage? Current => _presentation.Current;
    public RuntimeDiagnostic Diagnostic => _diagnostic;
    public MovementDiagnostic? Movement => _movement?.Diagnostic;
    public PetSnapshot Snapshot => new(InstanceId, Current?.Definition.Id ?? new(string.Empty), _diagnostic.State.Primary, _diagnostic.Emotion);
    public event EventHandler? Changed;
    public event EventHandler? SpeechInterruptionRequested;
    public bool IsSpeechEnvironmentAvailable => _started && !_stopped && !_sessionSuspended &&
        _scheduler.IsVisible && !_scheduler.IsInteracting;
    public bool IsFocusActive => _scheduler.FocusMode;
    private void OnChanged(object? sender, EventArgs e) { _diagnostic = _scheduler.Diagnostic; Changed?.Invoke(this, EventArgs.Empty); }
    public async Task StartAsync(CancellationToken ct)
    {
        await _operations.WaitAsync(ct);
        try
        {
            if (_started || _stopped) return;
            _lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await _presentation.InitializeAsync(ct, preferBehaviorReady: true);
            await ConfigureAsync(ct);
            _scheduler.IsVisible = _settings.Current.PetWindow.IsVisible;
            _started = true;
            Resume();
        }
        finally { _operations.Release(); }
    }
    private async Task ConfigureAsync(CancellationToken ct)
    {
        var package = Current ?? throw new InvalidOperationException("A character must be loaded first.");
        var profile = await _profiles.ReadAsync(package, ct);
        var checkpoint = _settings.Current.Emotions.FirstOrDefault(item => item is not null && item.CharacterId == package.Definition.Id.Value);
        _movement?.Configure(package);
        var behaviors = new BehaviorCatalog(_policy).Build(profile, _settings.Current.Runtime).ToList();
        if (_movement is not null) behaviors.Add(MoveBehavior());
        _scheduler.Configure(behaviors,
            package.Definition.Animations.Keys.Select(key => new AnimationSemantic(key)).ToHashSet(),
            checkpoint?.Restore() ?? EmotionState.Initial);
        _logger.Write(new(AppEvent.CharacterSwitched, _clock.GetUtcNow()));
    }
    private BehaviorDefinition MoveBehavior() => new(BehaviorId.Move, new("walk"), 1.5,
        _movement!.Motion.MovementInterval, TimeSpan.FromSeconds(1), MotionPolicy.MaxMovementDuration,
        BehaviorPriority.Low, true, [], [new(EmotionAxis.Boredom, ModifierDirection.High, 1)],
        Enabled: _settings.Current.MovementMode != MovementMode.Fixed);

    public async Task ReconcileMovementAsync(bool updateHome, CancellationToken ct)
    {
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped || !_started || _movement is null) return;
            await CancelLoopAsync();
            await _movement.ReconcileAsync(updateHome, ct);
        }
        finally { Resume(); _operations.Release(); }
    }
    public async Task ApplyMovementSettingsAsync(MovementMode mode, HybridMovementStrategy hybrid, DisplayPolicy displays,
        MotionStyle style, IReadOnlyList<string> selected, CancellationToken ct)
    {
        if (!Enum.IsDefined(mode) || !Enum.IsDefined(hybrid) || !Enum.IsDefined(displays) || !Enum.IsDefined(style))
            throw new ArgumentException("Invalid movement setting.");
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped || !_started) return;
            await CancelLoopAsync();
            await SaveEmotionAsync(ct);
            await _settings.UpdateAsync(s => s with { MovementMode = mode, HybridStrategy = hybrid, DisplayPolicy = displays,
                MotionStyle = style, Movement = s.Movement with { UserMotionStyle = style, SelectedDisplays = selected.ToArray() } }, ct);
            await ConfigureAsync(ct);
            if (_movement is not null) await _movement.ReconcileAsync(false, ct);
        }
        finally { Resume(); _operations.Release(); }
    }
    public async Task<CharacterOperationResult> ActivateAsync(CharacterId id, CancellationToken ct)
    {
        SpeechInterruptionRequested?.Invoke(this, EventArgs.Empty);
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped) return new(ValidationResult.Reject(CharacterErrorCode.StorageFailure, null, "Runtime is stopped."));
            await CancelLoopAsync();
            await SaveEmotionAsync(ct);
            var result = await _presentation.ActivateAsync(id, ct);
            if (result.Succeeded)
            {
                await ConfigureAsync(ct);
                if (_movement is not null) await _movement.ReconcileAsync(false, ct);
            }
            return result;
        }
        finally { Resume(); _operations.Release(); }
    }
    public async Task SetVisibleAsync(bool visible, CancellationToken ct)
    {
        if (!visible) SpeechInterruptionRequested?.Invoke(this, EventArgs.Empty);
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped || !_started || _scheduler.IsVisible == visible) return;
            await CancelLoopAsync();
            _scheduler.IsVisible = visible;
            _scheduler.IsInteracting = false;
            await _presentation.SetVisibleAsync(visible, ct);
            if (visible && _movement is not null) await _movement.ReconcileAsync(false, ct);
            if (!visible) await SaveEmotionAsync(ct);
            _scheduler.Publish();
        }
        finally { Resume(); _operations.Release(); }
    }
    public async Task InteractAsync(PetInteractionKind kind, CancellationToken ct)
    {
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind == PetInteractionKind.PointerPressed) SpeechInterruptionRequested?.Invoke(this, EventArgs.Empty);
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped || !_started || !_scheduler.IsVisible) return;
            await CancelLoopAsync(); // Pointer ownership is a high-priority lifecycle boundary.
            _scheduler.IsInteracting = kind == PetInteractionKind.PointerPressed;
            if (_scheduler.IsInteracting) _scheduler.State.BeginInteraction();
            else
            {
                _scheduler.State.Complete();
                _movement?.RecordInteraction();
                if (kind == PetInteractionKind.DragEnded && _movement is not null) await _movement.ReconcileAsync(true, ct);
                var feedback = kind == PetInteractionKind.Click && _scheduler.Interact();
                if (feedback) Resume(InteractionBehavior());
            }
            _scheduler.Publish();
        }
        finally { Resume(); _operations.Release(); }
    }
    private BehaviorDefinition InteractionBehavior() => _policy.Defaults().First(item => item.Id == BehaviorId.Happy) with
    {
        Id = BehaviorId.Interacting, Priority = BehaviorPriority.Medium, RequiredCapabilities = []
    };
    public async Task PlayAsync(AnimationSemantic semantic, CancellationToken ct)
    {
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped || !_started || !_scheduler.IsVisible) return;
            await CancelLoopAsync();
            _scheduler.State.Complete();
            Resume(InteractionBehavior() with { Semantic = semantic });
        }
        finally { _operations.Release(); }
    }
    public async Task SetProductivityContextAsync(DesktopPet.Domain.Productivity.PomodoroPhase? phase, bool focusMode, CancellationToken ct)
    {
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped || !_started) return;
            await CancelLoopAsync();
            _scheduler.PomodoroPhase = phase;
            _scheduler.FocusMode = focusMode;
            _scheduler.Publish();
        }
        finally { Resume(); _operations.Release(); }
    }
    public async Task SetSessionSuspendedAsync(bool suspended, CancellationToken ct)
    {
        if (suspended) SpeechInterruptionRequested?.Invoke(this, EventArgs.Empty);
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped || !_started || _sessionSuspended == suspended) return;
            _sessionSuspended = suspended;
            await CancelLoopAsync();
            if (suspended)
            {
                _scheduler.IsVisible = false;
                if (_movement is not null) await _movement.StopAsync(ct);
            }
            else _scheduler.IsVisible = _settings.Current.PetWindow.IsVisible;
            _scheduler.Publish();
        }
        finally { Resume(); _operations.Release(); }
    }
    private void Resume(BehaviorDefinition? first = null)
    {
        if (!_started || _stopped || _sessionSuspended || !_scheduler.IsVisible || _scheduler.IsInteracting || !_loop.IsCompleted || _lifetime?.IsCancellationRequested != false) return;
        _run?.Dispose();
        _run = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _loop = RunObservedAsync(first, _run.Token);
    }
    private async Task RunObservedAsync(BehaviorDefinition? first, CancellationToken ct)
    {
        try { await _scheduler.RunAsync(first, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) { _exceptions.Report(e, ErrorCode.CommandFailed, ErrorOrigin.BackgroundTask); }
    }
    private async Task CancelLoopAsync()
    {
        _run?.Cancel();
        await _loop;
        _run?.Dispose(); _run = null; _loop = Task.CompletedTask;
    }
    private Task SaveEmotionAsync(CancellationToken ct)
    {
        if (Current is null) return Task.CompletedTask;
        var saved = EmotionCheckpoint.From(Current.Definition.Id.Value, _scheduler.Emotion.Current, _clock.GetUtcNow());
        return _settings.UpdateAsync(settings => settings with
        {
            Emotions = settings.Emotions.Where(item => item is not null && item.CharacterId != saved.CharacterId)
                .TakeLast(AppSettings.MaxEmotionCheckpoints - 1).Append(saved).ToArray()
        }, ct);
    }
    public async Task StopAsync(CancellationToken ct)
    {
        if (_disposed) return;
        SpeechInterruptionRequested?.Invoke(this, EventArgs.Empty);
        await _operations.WaitAsync(CancellationToken.None);
        try
        {
            if (_stopped) return;
            _stopped = true;
            _lifetime?.Cancel();
            await CancelLoopAsync();
            try { if (_started) await SaveEmotionAsync(ct); }
            finally
            {
                _scheduler.IsVisible = false; _scheduler.IsInteracting = false; _scheduler.Publish();
                if (_movement is not null) await _movement.StopAsync(CancellationToken.None);
                await _presentation.StopAsync(CancellationToken.None);
            }
        }
        finally { _operations.Release(); }
    }
    public async Task<SpeechVisualMode> EnterTalkingAsync(CancellationToken ct)
    {
        await _operations.WaitAsync(ct);
        var entered = false;
        try
        {
            if (_stopped || !_started || _scheduler.IsInteracting) throw new TtsProviderException("runtime_busy");
            await CancelLoopAsync();
            var animations = Current?.Definition.Animations;
            _speechVisualMode = animations?.ContainsKey("mouth-open") == true && animations.ContainsKey("mouth-closed")
                ? SpeechVisualMode.MouthFrames : animations?.ContainsKey("talking") == true
                    ? SpeechVisualMode.Talking : SpeechVisualMode.Compatible;
            var behavior = new BehaviorDefinition(BehaviorId.Talking, new("talking"), 1, TimeSpan.Zero,
                TimeSpan.FromMilliseconds(1), TimeSpan.FromMinutes(1), BehaviorPriority.High, true, [], []);
            if (!_scheduler.State.TryEnter(behavior, _clock.GetUtcNow())) throw new TtsProviderException("runtime_busy");
            entered = true;
            await _presentation.PlayAsync(_speechVisualMode == SpeechVisualMode.MouthFrames ? new("mouth-closed") : new("talking"), ct);
            _scheduler.Publish();
            return _speechVisualMode;
        }
        catch
        {
            if (entered && _scheduler.State.Current.Primary == PetPrimaryState.Talking)
            {
                _speechVisualMode = SpeechVisualMode.Compatible;
                _scheduler.State.Complete();
                _scheduler.Publish();
            }
            Resume();
            throw;
        }
        finally { _operations.Release(); }
    }
    public async Task ApplyMouthFrameAsync(bool open, CancellationToken ct)
    {
        await _operations.WaitAsync(ct);
        try
        {
            if (_stopped || _speechVisualMode != SpeechVisualMode.MouthFrames || _scheduler.State.Current.Primary != PetPrimaryState.Talking) return;
            await _presentation.PlayAsync(new(open ? "mouth-open" : "mouth-closed"), ct);
        }
        finally { _operations.Release(); }
    }
    public async Task ExitTalkingAsync(CancellationToken ct)
    {
        await _operations.WaitAsync(ct);
        try
        {
            if (_scheduler.State.Current.Primary == PetPrimaryState.Talking)
            {
                _speechVisualMode = SpeechVisualMode.Compatible;
                _scheduler.State.Complete();
                if (!_stopped && _started && _scheduler.IsVisible) await _presentation.PlayAsync(AnimationSemantic.Idle, ct);
                _scheduler.Publish();
            }
        }
        finally { Resume(); _operations.Release(); }
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync(CancellationToken.None);
        Dispose();
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime?.Cancel(); _run?.Cancel();
        _scheduler.Changed -= OnChanged;
        _run?.Dispose(); _lifetime?.Dispose(); _operations.Dispose();
    }
}
public sealed class PetHost(PetRuntime runtime) : IPetHost
{
    public PetRuntime Runtime { get; } = runtime;
    public IReadOnlyCollection<IPetRuntime> Instances { get; } = Array.AsReadOnly<IPetRuntime>([runtime]);
}
