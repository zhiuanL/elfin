using System.Windows.Input;

namespace DesktopPet.App.ViewModels;

public sealed class AsyncActionCommand(Func<Task> action, Action<Exception> onError) : ICommand
{
    private bool _running;
    public Task Completion { get; private set; } = Task.CompletedTask;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_running;
    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        Completion = RunAsync();
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
    private async Task RunAsync()
    {
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception exception) { onError(exception); }
        finally { _running = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
