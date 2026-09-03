using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Navigation;
using DesktopPet.Domain.Productivity;

namespace DesktopPet.Application.Productivity;

public sealed class PomodoroToggleCommand(IPomodoroService pomodoro, ISettingsService settings) : IAppCommand
{
    public CommandId Id => CommandId.StartOrPausePomodoro;
    public async Task<CommandResult> ExecuteAsync(CancellationToken ct)
    {
        var snapshot = await pomodoro.GetSnapshotAsync(ct);
        if (snapshot.Status == PomodoroStatus.Running) await pomodoro.PauseAsync(ct);
        else if (snapshot.Status == PomodoroStatus.Paused) await pomodoro.ResumeAsync(ct);
        else
        {
            var minutes = snapshot.SuggestedPhase switch
            {
                PomodoroPhase.Focus => settings.Current.Productivity.Pomodoro.FocusMinutes,
                PomodoroPhase.ShortBreak => settings.Current.Productivity.Pomodoro.ShortBreakMinutes,
                _ => settings.Current.Productivity.Pomodoro.LongBreakMinutes
            };
            await pomodoro.StartAsync(snapshot.SuggestedPhase, TimeSpan.FromMinutes(minutes), null, ct);
        }
        return new(CommandStatus.Completed);
    }
}

public sealed class ProductivityNavigationCommand(CommandId id, AppPage page, INavigationService navigation,
    Func<IWindowService> windows) : IAppCommand
{
    public CommandId Id { get; } = id;
    public async Task<CommandResult> ExecuteAsync(CancellationToken ct)
    {
        navigation.Navigate(page);
        await windows().ShowControlCenterAsync(ct);
        return new(CommandStatus.Completed);
    }
}
