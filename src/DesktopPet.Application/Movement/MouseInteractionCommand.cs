using DesktopPet.Application.Commands;
using DesktopPet.Domain.Movement;

namespace DesktopPet.Application.Movement;

public sealed class MouseInteractionCommand(CommandId id, IMouseInteractionService input) : IAppCommand
{
    public CommandId Id => id;
    public async Task<CommandResult> ExecuteAsync(CancellationToken ct)
    {
        await (id switch
        {
            CommandId.SetInteractive => input.SetModeAsync(MouseInteractionMode.Interactive, ct),
            CommandId.SetClickThrough => input.SetModeAsync(MouseInteractionMode.ClickThrough, ct),
            CommandId.ToggleClickThrough => input.ToggleAsync(ct),
            CommandId.TemporaryClickThrough => input.SetModeAsync(MouseInteractionMode.TemporaryPassThrough, ct),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        });
        return new(CommandStatus.Completed);
    }
}
