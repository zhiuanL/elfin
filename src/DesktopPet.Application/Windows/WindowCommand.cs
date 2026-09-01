using DesktopPet.Application.Commands;
using DesktopPet.Application.Contracts;

namespace DesktopPet.Application.Windows;

public sealed class WindowCommand(CommandId id, IWindowService windows) : IAppCommand
{
    public CommandId Id => id;
    public async Task<CommandResult> ExecuteAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        await (id switch
        {
            CommandId.ShowPet => windows.ShowPetAsync(ct),
            CommandId.HidePet => windows.HidePetAsync(ct),
            CommandId.TogglePetVisibility => windows.TogglePetAsync(ct),
            CommandId.OpenControlCenter => windows.ShowControlCenterAsync(ct),
            CommandId.CloseControlCenter => windows.CloseControlCenterAsync(ct),
            CommandId.EnableTopmost => windows.SetTopmostAsync(true, ct),
            CommandId.DisableTopmost => windows.SetTopmostAsync(false, ct),
            CommandId.Exit => windows.ExitAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(id))
        });
        return new(CommandStatus.Completed);
    }
}
