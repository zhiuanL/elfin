using System.Windows;
using System.Windows.Threading;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Storage;
using DesktopPet.App.Bootstrap;
using DesktopPet.Infrastructure.Diagnostics;
using DesktopPet.Infrastructure.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace DesktopPet.App;

public partial class App : System.Windows.Application, IAppLifetime
{
    private readonly CancellationTokenSource _lifetime = new();
    private IHost? _host;
    private IExceptionHandler? _exceptions;
    private ITextLocalizer? _text;
    private IAppDataDirectories? _directories;
    private bool _smokeTest;
    public bool IsShuttingDown { get; private set; }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;
        TaskScheduler.UnobservedTaskException += OnTaskException;
        try
        {
            var options = StartupOptions.Parse(e.Args);
            _smokeTest = options.SmokeTest;
            _directories = AppBootstrapper.ResolveDirectories(options);
            _host = AppBootstrapper.Build(_directories, this);
            _exceptions = _host.Services.GetRequiredService<IExceptionHandler>();
            _text = _host.Services.GetRequiredService<ITextLocalizer>();
            var desktop = _host.Services.GetRequiredService<DesktopApplication>();
            if (_smokeTest) desktop.Window.ContentRendered += (_, _) => RequestShutdown();
            await _host.StartAsync(_lifetime.Token);
            await desktop.StartAsync(_lifetime.Token);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { RequestShutdown(); }
        catch (Exception exception)
        {
            ReportFailure(exception, ErrorCode.StartupFailed, ErrorOrigin.Startup);
            RequestShutdown(1);
        }
    }

    public async void RequestShutdown(int exitCode = 0)
    {
        if (IsShuttingDown) return;
        IsShuttingDown = true;
        _lifetime.Cancel();
        try
        {
            if (_host is not null) await _host.StopAsync(TimeSpan.FromSeconds(5));
        }
        catch (Exception exception)
        {
            _exceptions?.Report(exception, ErrorCode.UnhandledException, ErrorOrigin.BackgroundTask);
            exitCode = 1;
        }
        finally { Shutdown(exitCode); }
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
        // Safe bootstrap fallback also works if DI/configuration construction itself fails.
        var defaults = Options.Create(new DesktopPet.Application.Configuration.AppSettings());
        _directories ??= AppBootstrapper.ResolveDirectories(new(DeploymentMode.Installed, false, null));
        var logger = new RollingFileAppLogger(_directories, defaults);
        _exceptions ??= new ExceptionHandler(logger, TimeProvider.System);
        _text ??= new ResourceTextLocalizer("zh-CN");
        var failure = _exceptions.Report(exception, code, origin);
        if (!_smokeTest)
            MessageBox.Show($"{_text.Get(origin == ErrorOrigin.Startup ? TextKey.StartupError : TextKey.FatalError)}\n\n{_text.Get(TextKey.LogLocation)}: {_directories.Logs}\n{_text.Get(TextKey.BackupLocation)}: {_directories.Backups}\n\n{failure.CorrelationId}",
                _text.Get(TextKey.AppTitle), MessageBoxButton.OK, MessageBoxImage.Error);
    }
    protected override void OnExit(ExitEventArgs e)
    {
        DispatcherUnhandledException -= OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException -= OnDomainException;
        TaskScheduler.UnobservedTaskException -= OnTaskException;
        _host?.Dispose();
        _lifetime.Dispose();
        base.OnExit(e);
    }
}
