using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Startup;
using DesktopPet.App.ViewModels;
using DesktopPet.App.Views;

namespace DesktopPet.App.Bootstrap;

public sealed class DesktopApplication(IRecoveryCoordinator recovery, MainWindow window, PetWindow petWindow,
    MainWindowViewModel viewModel, ITextLocalizer text, IAppLogger logger, TimeProvider timeProvider,
    IWindowService windows, WindowEventBridge events)
{
    private readonly TaskCompletionSource _rendered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _petRendered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WaitForRenderAsync(CancellationToken ct) =>
        Task.WhenAll(_rendered.Task, petWindow.IsVisible ? _petRendered.Task : Task.CompletedTask).WaitAsync(ct);
    public async Task StartAsync(CancellationToken ct)
    {
        logger.Write(new(AppEvent.Starting, timeProvider.GetUtcNow()));
        var result = await recovery.InitializeAsync(ct);
        ct.ThrowIfCancellationRequested();
        System.Globalization.CultureInfo.CurrentCulture = text.Culture;
        System.Globalization.CultureInfo.CurrentUICulture = text.Culture;
        viewModel.Initialize(result);
        window.ContentRendered += OnRendered;
        petWindow.ContentRendered += OnPetRendered;
        events.Attach();
        await windows.InitializeAsync(ct);
        logger.Write(new(AppEvent.Started, timeProvider.GetUtcNow()));
    }
    private void OnRendered(object? sender, EventArgs e)
    {
        window.ContentRendered -= OnRendered;
        _rendered.TrySetResult();
    }
    private void OnPetRendered(object? sender, EventArgs e)
    {
        petWindow.ContentRendered -= OnPetRendered;
        _petRendered.TrySetResult();
    }
    public Task StopAsync(CancellationToken ct)
    {
        logger.Write(new(AppEvent.Stopping, timeProvider.GetUtcNow()));
        window.ContentRendered -= OnRendered;
        petWindow.ContentRendered -= OnPetRendered;
        events.Dispose();
        return windows.StopAsync(ct);
    }
}
