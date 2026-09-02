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
    SetInteractive, ToggleClickThrough, TemporaryClickThrough, MovementSummary,
    NavHome, NavCharacters, NavSettings, NavHotkeys, NavDiagnostics,
    HomeTitle, HomeSubtitle, CurrentCharacterCard, PetStatusCard, MovementModeCard, QuickActionsCard,
    NoCharacter, CharacterActiveBadge, RuntimeRunning, RuntimeStopped, SessionSummary, OpenCharacters, OpenSettings,
    CharactersTitle, CharactersSubtitle, CharacterPreview, CharacterDescription, CharacterPackageVersion,
    CharacterCapabilities, CharacterValidationResults, CharacterRemoveConfirm, CharacterRemoveProtected,
    CharacterImportAndActivate, CharacterImportSucceeded, ConfirmTitle, Yes, No,
    SettingsTitle, SettingsSubtitle, GeneralSettings, PetSettings, MovementSettingsLabel, InteractionSettings,
    AppearanceSettings, AdvancedSettings, Language, CloseBehavior, CloseHideToTray, CloseExit,
    PetVisible, AlwaysOnTop, UpdateHomeOnDrag, HybridStrategy, SelectedDisplays, CurrentMouseMode,
    ApplyGeneral, ApplyMovement, ApplyInteraction, ApplyAppearance, SettingsSaved, SettingsFailed,
    ThemeSystem, ThemeLight, ThemeDark, AppVersion, SettingsSchema,
    HotkeysTitle, HotkeysSubtitle, HotkeyEnabled, HotkeyCommand, HotkeyModifiers, HotkeyKey,
    HotkeyApply, HotkeyReset, HotkeySaved, HotkeyConflict, HotkeyInvalid,
    HotkeyShowPet, HotkeyHidePet, HotkeyOpenControlCenter, HotkeyTogglePet, HotkeyToggleClickThrough,
    HotkeyTemporaryClickThrough, HotkeyStartPausePomodoro, DiagnosticsTitle, DiagnosticsSubtitle, OfflineCoreStatus,
    NavPomodoro, NavReminders, NavStatistics,
    PomodoroTitle, PomodoroSubtitle, PomodoroPhaseLabel, PomodoroRemaining, PomodoroStart, PomodoroStartPause,
    PomodoroPause, PomodoroResume, PomodoroStop, PomodoroTask, PomodoroTags, PomodoroConsecutive,
    PomodoroTodayFocus, PomodoroSettings, PomodoroSaveSettings, PomodoroAddTask, PomodoroArchiveTask,
    PomodoroAddTag, PomodoroAssignTag, PomodoroNoTask, PomodoroIdle,
    RemindersTitle, RemindersSubtitle, ReminderAdd, ReminderUpdate, ReminderDelete, ReminderEnable,
    ReminderDisable, ReminderScheduleType, ReminderDue, ReminderRecurrence, ReminderTimeZone,
    ReminderNextTrigger, ReminderChannelsLabel, ReminderEmpty, ReminderDeleteConfirm,
    StatisticsTitle, StatisticsSubtitle, StatisticsTodayFocus, StatisticsTodayCompleted,
    StatisticsStreak, StatisticsDaily, StatisticsWeekly, StatisticsMonthly, StatisticsTaskSummary,
    StatisticsTagSummary, HomePomodoroCard, HomeTodayFocusCard, HomeRecentReminderCard,
    OpenPomodoro, OpenReminders, FocusDuration, ShortBreakDuration, LongBreakDuration,
    LongBreakIntervalLabel, AutoStartNextPhase, ReminderTitleLabel, ReminderDescriptionLabel,
    ReminderRelativeMinutes, ReminderAbsoluteLocal, ReminderRecurringTime, ReminderIntervalDays,
    ReminderWeekdays, ChannelPetBubble, ChannelPetAction, ChannelWindows, ChannelSound }
public interface ITextLocalizer
{
    CultureInfo Culture { get; }
    event EventHandler? CultureChanged;
    string Get(TextKey key);
    Task SetCultureAsync(string culture, CancellationToken ct);
}
