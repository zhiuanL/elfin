using System.Globalization;

namespace DesktopPet.Application.Localization;

public enum TextKey { AppTitle, BootstrapTitle, BootstrapDescription, OfflineReady, AiStorageUnavailable,
    SettingsRecovered, StartupError, FatalError, LogLocation, BackupLocation, Close,
    ShowPet, HidePet, TogglePet, OpenControlCenter, ExitApplication, PetTitle, PetHint, CommandFailed }
public interface ITextLocalizer
{
    CultureInfo Culture { get; }
    string Get(TextKey key);
}
