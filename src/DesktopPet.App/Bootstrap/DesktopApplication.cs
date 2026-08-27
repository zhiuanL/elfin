using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Startup;
using DesktopPet.App.ViewModels;
using DesktopPet.App.Views;

namespace DesktopPet.App.Bootstrap;

public sealed class DesktopApplication(IRecoveryCoordinator recovery, MainWindow window,
    MainWindowViewModel viewModel, ITextLocalizer text, IAppLogger logger, TimeProvider timeProvider)
{
    public MainWindow Window => window;
    public async Task StartAsync(CancellationToken ct)
    {
        logger.Write(new(AppEvent.Starting, timeProvider.GetUtcNow()));
        var result = await recovery.InitializeAsync(ct);
        ct.ThrowIfCancellationRequested();
        System.Globalization.CultureInfo.CurrentCulture = text.Culture;
        System.Globalization.CultureInfo.CurrentUICulture = text.Culture;
        viewModel.Initialize(result);
        window.Show();
        logger.Write(new(AppEvent.Started, timeProvider.GetUtcNow()));
    }
}
