using System.Windows;
using System.Windows.Threading;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Storage;
using DesktopPet.Application.Navigation;
using DesktopPet.Infrastructure.Diagnostics;
using DesktopPet.Infrastructure.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DesktopPet.App.Bootstrap;

/// <summary>Process-level startup, exception and shutdown boundary, separate from window policy.</summary>
public sealed class ApplicationHostController(System.Windows.Application app) : IAppLifetime, IDisposable
{
    private readonly CancellationTokenSource _lifetime = new();
    private IHost? _host;
    private DesktopApplication? _desktop;
    private IExceptionHandler? _exceptions;
    private ITextLocalizer? _text;
    private IAppDataDirectories? _directories;
    private bool _smokeTest;
    public bool IsShuttingDown { get; private set; }

    public async Task StartAsync(string[] args)
    {
        app.DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
        try
        {
            var options = StartupOptions.Parse(args);
            _smokeTest = options.SmokeTest;
            _directories = AppBootstrapper.ResolveDirectories(options);
            _host = AppBootstrapper.Build(_directories, this);
            _exceptions = _host.Services.GetRequiredService<IExceptionHandler>();
            _text = _host.Services.GetRequiredService<ITextLocalizer>();
            _desktop = _host.Services.GetRequiredService<DesktopApplication>();
            await _host.StartAsync(_lifetime.Token);
            await _desktop.StartAsync(_lifetime.Token);
            if (_smokeTest)
            {
                await ExerciseControlCenterPagesAsync();
                await _desktop.WaitForRenderAsync(_lifetime.Token);
                if (options.SmokeDurationSeconds > 0)
                    await Task.Delay(TimeSpan.FromSeconds(options.SmokeDurationSeconds), _lifetime.Token);
                RequestShutdown();
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { RequestShutdown(); }
        catch (Exception exception)
        {
            ReportFailure(exception, ErrorCode.StartupFailed, ErrorOrigin.Startup);
            RequestShutdown(1);
        }
    }

    private async Task ExerciseControlCenterPagesAsync()
    {
        var navigation = _host!.Services.GetRequiredService<INavigationService>();
        foreach (var page in new[] { AppPage.Home, AppPage.AI, AppPage.Pomodoro, AppPage.Reminders, AppPage.Statistics,
                     AppPage.Characters, AppPage.Settings, AppPage.Hotkeys, AppPage.Diagnostics })
        {
            navigation.Navigate(page);
            await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
        }
        navigation.Navigate(AppPage.Home);
        await app.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Loaded);
    }

    public async void RequestShutdown(int exitCode = 0)
    {
        app.Dispatcher.VerifyAccess();
        if (IsShuttingDown) return;
        IsShuttingDown = true;
        _lifetime.Cancel();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { if (_desktop is not null) await _desktop.StopAsync(timeout.Token); }
            finally { if (_host is not null) await _host.StopAsync(timeout.Token); }
        }
        catch (Exception exception)
        {
            ReportFailure(exception, ErrorCode.UnhandledException, ErrorOrigin.BackgroundTask);
            exitCode = 1;
        }
        finally { app.Shutdown(exitCode); }
    }
    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportFailure(e.Exception, ErrorCode.UnhandledException, ErrorOrigin.Dispatcher);
        e.Handled = true;
        RequestShutdown(1);
    }
    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            _exceptions?.Report(exception, ErrorCode.UnhandledException, ErrorOrigin.AppDomain);
    }
    private void OnTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _exceptions?.Report(e.Exception, ErrorCode.UnhandledException, ErrorOrigin.BackgroundTask);
        e.SetObserved();
    }
    private void ReportFailure(Exception exception, ErrorCode code, ErrorOrigin origin)
    {
        var defaults = Options.Create(new DesktopPet.Application.Configuration.AppSettings());
        _directories ??= AppBootstrapper.ResolveDirectories(new(DeploymentMode.Installed, false, null));
        _exceptions ??= new ExceptionHandler(new RollingFileAppLogger(_directories, defaults), TimeProvider.System);
        _text ??= new ResourceTextLocalizer("zh-CN");
        var failure = _exceptions.Report(exception, code, origin);
        if (!_smokeTest)
            MessageBox.Show($"{_text.Get(origin == ErrorOrigin.Startup ? TextKey.StartupError : TextKey.FatalError)}\n\n{_text.Get(TextKey.LogLocation)}: {_directories.Logs}\n{_text.Get(TextKey.BackupLocation)}: {_directories.Backups}\n\n{failure.CorrelationId}",
                _text.Get(TextKey.AppTitle), MessageBoxButton.OK, MessageBoxImage.Error);
    }
    public void Dispose()
    {
        app.DispatcherUnhandledException -= OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
        TaskScheduler.UnobservedTaskException -= OnTaskException;
        _host?.Dispose();
        _lifetime.Dispose();
    }
}
