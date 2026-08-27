using System.Windows.Input;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Startup;
using DesktopPet.Application.Windows;

namespace DesktopPet.App.ViewModels;

public sealed class MainWindowViewModel : ObservableViewModel
{
    private readonly ITextLocalizer _text;
    private StartupResult? _startup;
    private bool _commandFailed;
    public MainWindowViewModel(ITextLocalizer text)
    {
        _text = text;
        CloseCommand = CreateCommand(CommandId.CloseControlCenter);
        ShowCommand = CreateCommand(CommandId.ShowPet);
        HideCommand = CreateCommand(CommandId.HidePet);
        ToggleCommand = CreateCommand(CommandId.TogglePetVisibility);
        ExitCommand = CreateCommand(CommandId.Exit);
    }
    public string Title => _text.Get(TextKey.AppTitle);
    public string Heading => _text.Get(TextKey.BootstrapTitle);
    public string Description => _text.Get(TextKey.BootstrapDescription);
    public string Status => _text.Get(TextKey.OfflineReady);
    public string Notice => _commandFailed ? _text.Get(TextKey.CommandFailed) :
        _startup?.AiStorageAvailable == false ? _text.Get(TextKey.AiStorageUnavailable) :
        _startup?.SettingsStatus == SettingsLoadStatus.RecoveredInvalid ? _text.Get(TextKey.SettingsRecovered) : string.Empty;
    public string CloseText => _text.Get(TextKey.Close);
    public string ShowText => _text.Get(TextKey.ShowPet);
    public string HideText => _text.Get(TextKey.HidePet);
    public string ToggleText => _text.Get(TextKey.TogglePet);
    public string ExitText => _text.Get(TextKey.ExitApplication);
    public ICommand CloseCommand { get; }
    public ICommand ShowCommand { get; }
    public ICommand HideCommand { get; }
    public ICommand ToggleCommand { get; }
    public ICommand ExitCommand { get; }
    public event EventHandler<WindowCommandEventArgs>? CommandRequested;
    private ICommand CreateCommand(CommandId id) => new RelayCommand(() => CommandRequested?.Invoke(this, new(id)));
    public void ReportCommandFailure() { _commandFailed = true; OnPropertyChanged(nameof(Notice)); }
    public void Initialize(StartupResult result)
    {
        _startup = result;
        OnPropertyChanged(string.Empty);
    }
}
