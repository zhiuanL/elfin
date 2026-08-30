using System.Globalization;

namespace DesktopPet.Application.Localization;

public enum TextKey { AppTitle, BootstrapTitle, BootstrapDescription, OfflineReady, AiStorageUnavailable,
    SettingsRecovered, StartupError, FatalError, LogLocation, BackupLocation, Close,
    ShowPet, HidePet, TogglePet, OpenControlCenter, ExitApplication, PetTitle, PetHint, CommandFailed,
    CharacterTools, CharacterSource, CharacterRefresh, CharacterValidate, CharacterImport, CharacterActivate,
    CharacterRemove, CharacterPlay, CharacterAccepted, CharacterRejected, CharacterInvalidSemantic,
    CharacterTargetTier, CharacterActualTier, CharacterMissingCapabilities,
    CharacterBrowseZip, CharacterBrowseFolder, CharacterZipFilter,
    RuntimeDiagnostics, RuntimeSummary, RuntimeScore, RuntimeRecent,
    MovementTools, MovementHint, MovementApply, MovementFixed, MovementLocal, MovementDesktop, MovementHybrid,
    DisplayPrimary, DisplayCurrent, DisplaySelected, DisplayAll, MotionQuiet, MotionNatural, MotionLively,
    SetInteractive, ToggleClickThrough, TemporaryClickThrough, MovementSummary }
public interface ITextLocalizer
{
    CultureInfo Culture { get; }
    string Get(TextKey key);
}
