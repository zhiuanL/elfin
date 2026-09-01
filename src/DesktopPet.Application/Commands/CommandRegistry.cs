namespace DesktopPet.Application.Commands;

public enum CommandId { OpenControlCenter, TogglePetVisibility, OpenAi, TogglePomodoro, TemporaryClickThrough, ToggleSilentMode, Exit,
    ShowPet, HidePet, CloseControlCenter, SetInteractive, SetClickThrough, ToggleClickThrough, EnableTopmost, DisableTopmost }
public enum CommandStatus { Completed, Unavailable, Cancelled }
public sealed record CommandResult(CommandStatus Status);
public interface IAppCommand
{
    CommandId Id { get; }
    Task<CommandResult> ExecuteAsync(CancellationToken ct);
}
public interface ICommandRegistry
{
    IReadOnlyCollection<CommandId> RegisteredCommands { get; }
    Task<CommandResult> ExecuteAsync(CommandId id, CancellationToken ct);
}
public sealed class CommandRegistry : ICommandRegistry
{
    private readonly IReadOnlyDictionary<CommandId, IAppCommand> _commands;
    public CommandRegistry(IEnumerable<IAppCommand> commands)
    {
        _commands = commands.ToDictionary(command => command.Id);
    }
    public IReadOnlyCollection<CommandId> RegisteredCommands => _commands.Keys.ToArray();
    public Task<CommandResult> ExecuteAsync(CommandId id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return _commands.TryGetValue(id, out var command)
            ? command.ExecuteAsync(ct)
            : Task.FromResult(new CommandResult(CommandStatus.Unavailable));
    }
}
