using System.IO;
using DesktopPet.Application;
using DesktopPet.Application.Characters;
using DesktopPet.Infrastructure.Characters;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Storage;
using DesktopPet.Application.Windows;
using DesktopPet.App.ViewModels;
using DesktopPet.App.Views;
using DesktopPet.Infrastructure;
using DesktopPet.Infrastructure.Storage;
using DesktopPet.Windows;
using DesktopPet.Windows.Windowing;
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
        builder.Services.AddApplication().AddWindowApplication().AddInfrastructure(directories).AddWindowsPlatform();
        builder.Services.AddSingleton(lifetime);
        builder.Services.AddSingleton<ICharacterSeedSource>(new DirectoryCharacterSeedSource(Path.Combine(AppContext.BaseDirectory, "DevelopmentCharacters")));
        builder.Services.AddSingleton<CharacterToolsViewModel>();
        builder.Services.AddSingleton<RuntimeDiagnosticsViewModel>();
        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();
        builder.Services.AddSingleton<PetWindowViewModel>();
        builder.Services.AddSingleton<PetWindow>();
        builder.Services.AddSingleton<IUiDispatcher>(_ => new WpfUiDispatcher(System.Windows.Threading.Dispatcher.CurrentDispatcher));
        builder.Services.AddSingleton<IPetWindow>(provider => new WindowsPetWindow(provider.GetRequiredService<PetWindow>()));
        builder.Services.AddSingleton<IControlCenterWindow>(provider => new WindowsControlCenterWindow(provider.GetRequiredService<MainWindow>()));
        builder.Services.AddSingleton<WindowEventBridge>();
        builder.Services.AddSingleton<DesktopApplication>();
        return builder.Build();
    }

    public static IAppDataDirectories ResolveDirectories(StartupOptions options) =>
        options.SmokeDataRoot is { } root ? new AppDataDirectories(root) :
        AppDataDirectories.Resolve(options.Mode, AppContext.BaseDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
}
