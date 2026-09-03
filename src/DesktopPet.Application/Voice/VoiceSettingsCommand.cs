using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;

namespace DesktopPet.Application.Voice;

public sealed class VoiceSettingsCommand(ISettingsService settings) : IAppCommand
{
    public CommandId Id => CommandId.ToggleSilentMode;
    public async Task<CommandResult> ExecuteAsync(CancellationToken ct)
    {
        await settings.UpdateAsync(current => current with
        { Voice = current.Voice with { SilentMode = !current.Voice.SilentMode } }, ct);
        return new(CommandStatus.Completed);
    }
}
