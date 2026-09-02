using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Startup;
using DesktopPet.App.ViewModels;
using DesktopPet.App.Views;
using DesktopPet.Application.Runtime;
using DesktopPet.Application.Movement;
using DesktopPet.Application.Appearance;
using DesktopPet.Application.Hotkeys;

namespace DesktopPet.App.Bootstrap;

public sealed class DesktopApplication(IRecoveryCoordinator recovery, MainWindow window, PetWindow petWindow,
    MainWindowViewModel viewModel, ITextLocalizer text, IAppLogger logger, TimeProvider timeProvider,
    IWindowService windows, WindowEventBridge events, PetHost pets,
    CharacterToolsViewModel characterTools, IExceptionHandler exceptions, IMouseInteractionService input, MovementToolsViewModel movementTools,
    CharacterManagerViewModel characterManager, SettingsViewModel settings, HotkeysViewModel hotkeys,
    IAppearanceService appearance, IHotkeyCoordinator hotkeyCoordinator, ProductivityRuntimeBridge productivityBridge,
    IPomodoroService pomodoro, IReminderScheduler reminderScheduler, PomodoroViewModel pomodoroPage,
    RemindersViewModel remindersPage, StatisticsViewModel statisticsPage, HomeDashboardViewModel home)
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
        await appearance.InitializeAsync(ct);
        viewModel.Initialize(result);
        await pets.Runtime.StartAsync(ct);
        await productivityBridge.StartAsync(ct);
        await characterTools.InitializeAsync();
        window.ContentRendered += OnRendered;
        petWindow.ContentRendered += OnPetRendered;
        petWindow.IsVisibleChanged += OnPetVisibilityChanged;
        events.Attach();
        await windows.InitializeAsync(ct);
        await hotkeyCoordinator.InitializeAsync(ct);
        await pets.Runtime.ReconcileMovementAsync(false, ct);
        movementTools.Initialize();
        settings.Initialize();
        hotkeys.Initialize();
        await characterManager.InitializeAsync();
        await pomodoroPage.InitializeAsync();
        await remindersPage.InitializeAsync();
        await statisticsPage.InitializeAsync();
        await home.InitializeAsync(ct);
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
    private async void OnPetVisibilityChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        try
        {
            if (!petWindow.IsVisible) await input.ResetAsync(CancellationToken.None);
            await pets.Runtime.SetVisibleAsync(petWindow.IsVisible, CancellationToken.None);
        }
        catch (Exception exception) { exceptions.Report(exception, ErrorCode.CommandFailed, ErrorOrigin.Command); }
    }
    public async Task StopAsync(CancellationToken ct)
    {
        logger.Write(new(AppEvent.Stopping, timeProvider.GetUtcNow()));
        window.ContentRendered -= OnRendered;
        petWindow.ContentRendered -= OnPetRendered;
        petWindow.IsVisibleChanged -= OnPetVisibilityChanged;
        events.Dispose();
        try
        {
            await productivityBridge.StopAsync(ct);
            await reminderScheduler.StopAsync(ct);
            await pomodoro.StopSchedulerAsync(ct);
            await pomodoroPage.StopAsync(); remindersPage.Dispose();
            await hotkeys.StopAsync(); await characterManager.StopAsync(); await settings.StopAsync();
            await movementTools.StopAsync(); await characterTools.StopAsync();
            await hotkeyCoordinator.StopAsync(ct); await pets.Runtime.StopAsync(ct); await input.StopAsync(ct);
        }
        finally { await windows.StopAsync(ct); }
    }
}
