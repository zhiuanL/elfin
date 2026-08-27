using System.IO;
using DesktopPet.Application;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Storage;
using DesktopPet.App.ViewModels;
using DesktopPet.App.Views;
using DesktopPet.Infrastructure;
using DesktopPet.Infrastructure.Storage;
using DesktopPet.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DesktopPet.App.Bootstrap;

// The only composition root. Resolving the root object here is not a service locator in business code.
public static class AppBootstrapper
{
public static IHost Build(IAppDataDirectories directories, IAppLifetime lifetime)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        }));
        builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false);
        builder.Services.AddOptions<AppSettings>()
            .Bind(builder.Configuration.GetSection("DesktopPet"), binder => binder.ErrorOnUnknownConfiguration = true)
            .Validate(settings => settings.IsValid(), "Invalid DesktopPet configuration.")
            .ValidateOnStart();
        builder.Services.AddApplication().AddInfrastructure(directories).AddWindowsPlatform();
        builder.Services.AddSingleton(lifetime);
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<DesktopApplication>();
        return builder.Build();
    }

    public static IAppDataDirectories ResolveDirectories(StartupOptions options) =>
        options.SmokeDataRoot is { } root ? new AppDataDirectories(root) :
        AppDataDirectories.Resolve(options.Mode, AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
