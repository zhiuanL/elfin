using System.Globalization;

namespace DesktopPet.Application.Localization;

public enum TextKey { AppTitle, BootstrapTitle, BootstrapDescription, OfflineReady, AiStorageUnavailable,
    SettingsRecovered, StartupError, FatalError, LogLocation, BackupLocation, Close }
public interface ITextLocalizer
{
    CultureInfo Culture { get; }
    string Get(TextKey key);
}
