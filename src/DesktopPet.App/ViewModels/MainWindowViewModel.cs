using System.Collections.ObjectModel;
using System.Windows.Input;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Navigation;
using DesktopPet.Application.Startup;
using DesktopPet.Application.Windows;

namespace DesktopPet.App.ViewModels;

public sealed class NavigationItemViewModel(AppPage page, TextKey labelKey, ITextLocalizer text, Action<AppPage> navigate) : ObservableViewModel
{
    public AppPage Page { get; } = page;
    public string Label => text.Get(labelKey);
    public ICommand Command { get; } = new RelayCommand(() => navigate(page));
    public bool IsSelected { get; private set; }
    public void SetSelected(bool selected) { if (IsSelected == selected) return; IsSelected = selected; OnPropertyChanged(nameof(IsSelected)); }
    public void RefreshText() => OnPropertyChanged(nameof(Label));
}

public sealed class MainWindowViewModel : ObservableViewModel, IDisposable
{
    private readonly ITextLocalizer _text;
    private readonly INavigationService _navigation;
    private readonly IReadOnlyDictionary<AppPage, object> _pages;
    private StartupResult? _startup;
    private bool _commandFailed;
    private HomeDashboardViewModel? _home;
    private SettingsViewModel? _settings;

    public MainWindowViewModel(ITextLocalizer text, INavigationService navigation, HomeDashboardViewModel home,
        CharacterManagerViewModel characters, SettingsViewModel settings, HotkeysViewModel hotkeys,
        DiagnosticsPageViewModel diagnostics)
    {
        _text = text; _navigation = navigation;
        _home = home;
        _settings = settings;
        _pages = new Dictionary<AppPage, object>
        {
            [AppPage.Home] = home, [AppPage.Characters] = characters, [AppPage.Settings] = settings,
            [AppPage.Hotkeys] = hotkeys, [AppPage.Diagnostics] = diagnostics
        };
        NavigationItems =
        [
            Item(AppPage.Home, TextKey.NavHome), Item(AppPage.Characters, TextKey.NavCharacters),
            Item(AppPage.Settings, TextKey.NavSettings), Item(AppPage.Hotkeys, TextKey.NavHotkeys),
            Item(AppPage.Diagnostics, TextKey.NavDiagnostics)
        ];
        CloseCommand = CreateCommand(CommandId.CloseControlCenter);
        ExitCommand = CreateCommand(CommandId.Exit);
        ToggleCommand = CreateCommand(CommandId.TogglePetVisibility);
        _navigation.Changed += OnNavigationChanged;
        _text.CultureChanged += OnCultureChanged;
        _home.CommandRequested += OnPageCommand;
        _settings.CommandRequested += OnPageCommand;
        RefreshSelection();
    }

    // Narrow compatibility constructor used by platform-boundary tests; production DI uses the complete page constructor.
    public MainWindowViewModel(ITextLocalizer text)
    {
        _text = text; _navigation = new ControlCenterNavigationService(); _pages = new Dictionary<AppPage, object>();
        NavigationItems =
        [
            Item(AppPage.Home, TextKey.NavHome), Item(AppPage.Characters, TextKey.NavCharacters),
            Item(AppPage.Settings, TextKey.NavSettings), Item(AppPage.Hotkeys, TextKey.NavHotkeys),
            Item(AppPage.Diagnostics, TextKey.NavDiagnostics)
        ];
        CloseCommand = CreateCommand(CommandId.CloseControlCenter); ExitCommand = CreateCommand(CommandId.Exit);
        ToggleCommand = CreateCommand(CommandId.TogglePetVisibility);
        _navigation.Changed += OnNavigationChanged; _text.CultureChanged += OnCultureChanged; RefreshSelection();
    }

    private NavigationItemViewModel Item(AppPage page, TextKey key) => new(page, key, _text, _navigation.Navigate);
    public string Title => _text.Get(TextKey.AppTitle);
    public string Notice => _commandFailed ? _text.Get(TextKey.CommandFailed) :
        _startup?.AiStorageAvailable == false ? _text.Get(TextKey.AiStorageUnavailable) :
        _startup?.SettingsStatus == SettingsLoadStatus.RecoveredInvalid ? _text.Get(TextKey.SettingsRecovered) : string.Empty;
    public string CloseText => _text.Get(TextKey.Close);
    public string ExitText => _text.Get(TextKey.ExitApplication);
    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; }
    public object? CurrentPage => _pages.GetValueOrDefault(_navigation.Current);
    public ICommand CloseCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand ToggleCommand { get; }
    public event EventHandler<WindowCommandEventArgs>? CommandRequested;
    private ICommand CreateCommand(CommandId id) => new RelayCommand(() => CommandRequested?.Invoke(this, new(id)));
    public void ReportCommandFailure() { _commandFailed = true; OnPropertyChanged(nameof(Notice)); }
    public void Initialize(StartupResult result) { _startup = result; OnPropertyChanged(string.Empty); }
    private void OnNavigationChanged(object? sender, NavigationChangedEventArgs e) { RefreshSelection(); OnPropertyChanged(nameof(CurrentPage)); }
    private void RefreshSelection() { foreach (var item in NavigationItems) item.SetSelected(item.Page == _navigation.Current); }
    private void OnCultureChanged(object? sender, EventArgs e)
    {
        foreach (var item in NavigationItems) item.RefreshText();
        OnPropertyChanged(string.Empty);
    }
    private void OnPageCommand(object? sender, WindowCommandEventArgs e) => CommandRequested?.Invoke(this, e);
    public void Dispose()
    {
        _navigation.Changed -= OnNavigationChanged; _text.CultureChanged -= OnCultureChanged;
        if (_home is not null) _home.CommandRequested -= OnPageCommand;
        if (_settings is not null) _settings.CommandRequested -= OnPageCommand;
    }
}
