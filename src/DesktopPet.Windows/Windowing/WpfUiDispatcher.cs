using System.Windows.Threading;
using DesktopPet.Application.Windows;

namespace DesktopPet.Windows.Windowing;

public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public Task InvokeAsync(Func<Task> action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return dispatcher.CheckAccess() ? action() : dispatcher.InvokeAsync(action, DispatcherPriority.Normal, ct).Task.Unwrap();
    }
}
