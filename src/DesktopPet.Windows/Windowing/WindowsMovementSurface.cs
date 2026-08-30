using DesktopPet.Application.Movement;
using DesktopPet.Application.Windows;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsMovementSurface(IPetWindow window, IUiDispatcher dispatcher) : IMovementSurface
{
    private IPetMovementPort Port => window as IPetMovementPort ?? throw new NotSupportedException("Autonomous window movement is unavailable.");
    public async Task<MovementSurfaceSnapshot> ReadAsync(CancellationToken ct)
    {
        MovementSurfaceSnapshot? result = null;
        await dispatcher.InvokeAsync(() =>
        {
            result = new(window.Bounds, window.Dpi, window.IsVisible, Port.IsUserOwned);
            return Task.CompletedTask;
        }, ct);
        return result!;
    }
    public async Task<bool> TryMoveAsync(PixelPoint origin, CancellationToken ct)
    {
        var moved = false;
        await dispatcher.InvokeAsync(() => { ct.ThrowIfCancellationRequested(); moved = Port.TryMoveAutonomously(origin); return Task.CompletedTask; }, ct);
        return moved;
    }
    public Task RecoverAsync(PixelPoint origin, CancellationToken ct) => dispatcher.InvokeAsync(() =>
    {
        ct.ThrowIfCancellationRequested();
        if (!Port.IsUserOwned) window.MoveTo(origin);
        return Task.CompletedTask;
    }, ct);
}
