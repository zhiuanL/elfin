using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Navigation;
using DesktopPet.Application.Runtime;
using DesktopPet.Application.Windows;
using DesktopPet.Application.Productivity;
using DesktopPet.Domain.Productivity;
using DesktopPet.Application.Diagnostics;

namespace DesktopPet.App.ViewModels;

public sealed class HomeDashboardViewModel : ObservableViewModel, IDisposable
{
    private readonly PetHost _pets;
    private readonly ISettingsService _settings;
    private readonly ITextLocalizer _text;
    private readonly INavigationService _navigation;
    private readonly IPomodoroService _pomodoro;
    private readonly IStatisticsService _statistics;
    private readonly IReminderService _reminders;
    private readonly IProductivityEventPublisher _productivityEvents;
    private PomodoroSnapshot _pomodoroSnapshot = new(null, PomodoroPhase.Focus, 0, DateTimeOffset.UtcNow);
    private ProductivityStatistics? _productivityStatistics;
    private Reminder? _recentReminder;
    private readonly IExceptionHandler _exceptions;

    public HomeDashboardViewModel(PetHost pets, ISettingsService settings, ITextLocalizer text,
        INavigationService navigation, IPomodoroService pomodoro, IStatisticsService statistics,
        IReminderService reminders, IProductivityEventPublisher productivityEvents, IExceptionHandler exceptions)
    {
        _pets = pets; _settings = settings; _text = text; _navigation = navigation;
        _pomodoro = pomodoro; _statistics = statistics; _reminders = reminders; _productivityEvents = productivityEvents;
        _exceptions = exceptions;
        ShowCommand = Command(CommandId.ShowPet);
        HideCommand = Command(CommandId.HidePet);
        ToggleClickThroughCommand = Command(CommandId.ToggleClickThrough);
        OpenCharactersCommand = new RelayCommand(() => _navigation.Navigate(AppPage.Characters));
        OpenSettingsCommand = new RelayCommand(() => _navigation.Navigate(AppPage.Settings));
        StartPausePomodoroCommand = Command(CommandId.StartOrPausePomodoro);
        OpenPomodoroCommand = Command(CommandId.OpenPomodoro);
        OpenRemindersCommand = Command(CommandId.OpenReminders);
        _pets.Runtime.Changed += OnChanged;
        _text.CultureChanged += OnChanged;
        _pomodoro.Changed += OnChanged;
        _reminders.Changed += OnChanged;
        _productivityEvents.Published += OnProductivityEvent;
    }

    public string Title => _text.Get(TextKey.HomeTitle);
    public string Subtitle => _text.Get(TextKey.HomeSubtitle);
    public string CurrentCharacterTitle => _text.Get(TextKey.CurrentCharacterCard);
    public string PetStatusTitle => _text.Get(TextKey.PetStatusCard);
    public string MovementTitle => _text.Get(TextKey.MovementModeCard);
    public string QuickActionsTitle => _text.Get(TextKey.QuickActionsCard);
    public string CharacterName => _pets.Runtime.Current?.Definition.Localize(_text.Culture.Name).Name ?? _text.Get(TextKey.NoCharacter);
    public string CharacterLevel => _pets.Runtime.Current is { } package
        ? $"{package.Definition.Metadata.ActualLevel} · {package.Definition.Metadata.CompletenessPercentage}%" : string.Empty;
    public string PetStatus => _pets.Runtime.Diagnostic.IsRunning ? _text.Get(TextKey.RuntimeRunning) : _text.Get(TextKey.RuntimeStopped);
    public string SessionSummary => string.Format(_text.Culture, _text.Get(TextKey.SessionSummary),
        _pets.Runtime.Diagnostic.State.Primary, _pets.Runtime.Diagnostic.InteractionCount);
    public string MovementMode => $"{_settings.Current.MovementMode} · {_settings.Current.DisplayPolicy} · {_settings.Current.MotionStyle}";
    public string ShowText => _text.Get(TextKey.ShowPet);
    public string HideText => _text.Get(TextKey.HidePet);
    public string ClickThroughText => _text.Get(TextKey.ToggleClickThrough);
    public string OpenCharactersText => _text.Get(TextKey.OpenCharacters);
    public string OpenSettingsText => _text.Get(TextKey.OpenSettings);
    public string PomodoroCardTitle => _text.Get(TextKey.HomePomodoroCard);
    public string TodayFocusCardTitle => _text.Get(TextKey.HomeTodayFocusCard);
    public string RecentReminderCardTitle => _text.Get(TextKey.HomeRecentReminderCard);
    public string PomodoroSummary => $"{_pomodoroSnapshot.Status} · {(int)_pomodoroSnapshot.Remaining.TotalMinutes:00}:{_pomodoroSnapshot.Remaining.Seconds:00}";
    public string TodayFocusSummary => $"{_productivityStatistics?.TodayFocusDuration.TotalMinutes ?? 0:F0} min · {_productivityStatistics?.TodayCompletedPomodoros ?? 0}";
    public string RecentReminderSummary => _recentReminder is null ? _text.Get(TextKey.ReminderEmpty) :
        $"{_recentReminder.Title} · {_recentReminder.NextTriggerAtUtc?.ToLocalTime():g}";
    public string StartPauseText => _text.Get(TextKey.PomodoroStartPause);
    public string OpenPomodoroText => _text.Get(TextKey.OpenPomodoro);
    public string OpenRemindersText => _text.Get(TextKey.OpenReminders);
    public RelayCommand ShowCommand { get; }
    public RelayCommand HideCommand { get; }
    public RelayCommand ToggleClickThroughCommand { get; }
    public RelayCommand OpenCharactersCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public RelayCommand StartPausePomodoroCommand { get; }
    public RelayCommand OpenPomodoroCommand { get; }
    public RelayCommand OpenRemindersCommand { get; }
    public event EventHandler<WindowCommandEventArgs>? CommandRequested;
    private RelayCommand Command(CommandId id) => new(() => CommandRequested?.Invoke(this, new(id)));
    private void OnChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
    private async void OnProductivityEvent(object? sender, ProductivityEvent e)
    {
        try { await RefreshProductivityAsync(CancellationToken.None); }
        catch (Exception exception) { _exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.BackgroundTask); }
    }
    public Task InitializeAsync(CancellationToken ct) => RefreshProductivityAsync(ct);
    private async Task RefreshProductivityAsync(CancellationToken ct)
    {
        _pomodoroSnapshot = await _pomodoro.GetSnapshotAsync(ct);
        _productivityStatistics = await _statistics.GetOverviewAsync(DateOnly.FromDateTime(DateTime.Now), TimeZoneInfo.Local, ct);
        _recentReminder = (await _reminders.ListAsync(ct)).Where(x => x.Enabled)
            .OrderBy(x => x.NextTriggerAtUtc).FirstOrDefault();
        OnPropertyChanged(string.Empty);
    }
    public void Dispose()
    {
        _pets.Runtime.Changed -= OnChanged;
        _text.CultureChanged -= OnChanged;
        _pomodoro.Changed -= OnChanged;
        _reminders.Changed -= OnChanged;
        _productivityEvents.Published -= OnProductivityEvent;
    }
}
