using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Navigation;
using DesktopPet.Application.Runtime;
using DesktopPet.Application.Windows;

namespace DesktopPet.App.ViewModels;

public sealed class HomeDashboardViewModel : ObservableViewModel, IDisposable
{
    private readonly PetHost _pets;
    private readonly ISettingsService _settings;
    private readonly ITextLocalizer _text;
    private readonly INavigationService _navigation;

    public HomeDashboardViewModel(PetHost pets, ISettingsService settings, ITextLocalizer text,
        INavigationService navigation)
    {
        _pets = pets; _settings = settings; _text = text; _navigation = navigation;
        ShowCommand = Command(CommandId.ShowPet);
        HideCommand = Command(CommandId.HidePet);
        ToggleClickThroughCommand = Command(CommandId.ToggleClickThrough);
        OpenCharactersCommand = new RelayCommand(() => _navigation.Navigate(AppPage.Characters));
        OpenSettingsCommand = new RelayCommand(() => _navigation.Navigate(AppPage.Settings));
        _pets.Runtime.Changed += OnChanged;
        _text.CultureChanged += OnChanged;
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
    public RelayCommand ShowCommand { get; }
    public RelayCommand HideCommand { get; }
    public RelayCommand ToggleClickThroughCommand { get; }
    public RelayCommand OpenCharactersCommand { get; }
    public RelayCommand OpenSettingsCommand { get; }
    public event EventHandler<WindowCommandEventArgs>? CommandRequested;
    private RelayCommand Command(CommandId id) => new(() => CommandRequested?.Invoke(this, new(id)));
    private void OnChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);
    public void Dispose()
    {
        _pets.Runtime.Changed -= OnChanged;
        _text.CultureChanged -= OnChanged;
    }
}
