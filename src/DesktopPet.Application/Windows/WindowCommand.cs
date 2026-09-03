using DesktopPet.Application.Commands;
using DesktopPet.Application.Contracts;

namespace DesktopPet.Application.Windows;

public sealed class WindowCommand : IAppCommand
{
    private readonly Func<IWindowService> _windows;
    public WindowCommand(CommandId id, IWindowService windows) : this(id, () => windows) { }
    public WindowCommand(CommandId id, Func<IWindowService> windows) { Id = id; _windows = windows; }
    public CommandId Id { get; }
    public async Task<CommandResult> ExecuteAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var windows = _windows();
        await (Id switch
        {
            CommandId.ShowPet => windows.ShowPetAsync(ct),
            CommandId.HidePet => windows.HidePetAsync(ct),
            CommandId.TogglePetVisibility => windows.TogglePetAsync(ct),
            CommandId.OpenControlCenter => windows.ShowControlCenterAsync(ct),
            CommandId.CloseControlCenter => windows.CloseControlCenterAsync(ct),
            CommandId.EnableTopmost => windows.SetTopmostAsync(true, ct),
            CommandId.DisableTopmost => windows.SetTopmostAsync(false, ct),
            CommandId.Exit => windows.ExitAsync(ct),
            _ => throw new ArgumentOutOfRangeException(nameof(Id))
        });
        return new(CommandStatus.Completed);
    }
}
