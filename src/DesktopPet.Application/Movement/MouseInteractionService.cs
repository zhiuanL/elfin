using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Windows;
using DesktopPet.Domain.Movement;

namespace DesktopPet.Application.Movement;

public sealed class MouseInteractionService(IPetWindow window, IUiDispatcher dispatcher, TimeProvider clock,
    IExceptionHandler exceptions) : IMouseInteractionService, IDisposable
{
    private CancellationTokenSource? _temporary;
    private Task _expiry = Task.CompletedTask;
    private bool _stopped, _disposed;
    public MouseInteractionMode Mode { get; private set; }
    public Task ToggleAsync(CancellationToken ct) => SetModeAsync(Mode == MouseInteractionMode.Interactive ?
        MouseInteractionMode.ClickThrough : MouseInteractionMode.Interactive, ct);
    public Task SetModeAsync(MouseInteractionMode mode, CancellationToken ct) => dispatcher.InvokeAsync(() =>
    {
        if (_stopped) return Task.CompletedTask;
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        CancelTemporary();
        Apply(window.IsVisible ? mode : MouseInteractionMode.Interactive);
        if (Mode == MouseInteractionMode.TemporaryPassThrough)
        {
            _temporary = new();
            _expiry = ExpireAsync(_temporary.Token);
        }
        return Task.CompletedTask;
    }, ct);
    private void Apply(MouseInteractionMode mode)
    {
        if (window is not IPetMovementPort port) throw new NotSupportedException("Window input mode is unavailable.");
        port.SetClickThrough(mode != MouseInteractionMode.Interactive);
        Mode = mode;
    }
    private async Task ExpireAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(MotionPolicy.TemporaryPassThroughDuration, clock, ct);
            await dispatcher.InvokeAsync(() =>
            {
                ct.ThrowIfCancellationRequested();
                if (!_stopped) Apply(MouseInteractionMode.Interactive);
                return Task.CompletedTask;
            }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception e) { exceptions.Report(e, ErrorCode.CommandFailed, ErrorOrigin.BackgroundTask); }
    }
    public Task ResetAsync(CancellationToken ct) => SetModeAsync(MouseInteractionMode.Interactive, ct);
    public async Task StopAsync(CancellationToken ct)
    {
        if (_stopped) return;
        await dispatcher.InvokeAsync(() => { CancelTemporary(); Apply(MouseInteractionMode.Interactive); _stopped = true; return Task.CompletedTask; }, ct);
        await _expiry;
    }
    private void CancelTemporary() { _temporary?.Cancel(); _temporary?.Dispose(); _temporary = null; }
    public void Dispose() { if (_disposed) return; _disposed = true; CancelTemporary(); }
}
