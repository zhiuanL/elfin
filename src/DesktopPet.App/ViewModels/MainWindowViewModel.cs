using System.Windows.Input;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Startup;

namespace DesktopPet.App.ViewModels;

public sealed class MainWindowViewModel(ITextLocalizer text, IAppLifetime lifetime) : ObservableViewModel
{
    private StartupResult? _startup;
    public string Title => text.Get(TextKey.AppTitle);
    public string Heading => text.Get(TextKey.BootstrapTitle);
    public string Description => text.Get(TextKey.BootstrapDescription);
    public string Status => text.Get(TextKey.OfflineReady);
    public string Notice => _startup?.AiStorageAvailable == false ? text.Get(TextKey.AiStorageUnavailable) :
        _startup?.SettingsStatus == SettingsLoadStatus.RecoveredInvalid ? text.Get(TextKey.SettingsRecovered) : string.Empty;
    public string CloseText => text.Get(TextKey.Close);
    public bool IsShuttingDown => lifetime.IsShuttingDown;
    public ICommand CloseCommand { get; } = new RelayCommand(() => lifetime.RequestShutdown());
    public void Initialize(StartupResult result)
    {
        _startup = result;
        OnPropertyChanged(string.Empty);
    }
}
