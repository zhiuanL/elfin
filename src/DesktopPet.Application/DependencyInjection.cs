using DesktopPet.Application.Commands;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Windows;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Startup;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopPet.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IExceptionHandler, ExceptionHandler>();
        services.AddSingleton<IRecoveryCoordinator, RecoveryCoordinator>();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<ICharacterPackageService, CharacterManager>();
        services.AddSingleton<CharacterPresentationService>();
        return services;
    }

    public static IServiceCollection AddWindowApplication(this IServiceCollection services)
    {
        services.AddSingleton<WindowPlacementPolicy>();
        services.AddSingleton<IWindowService, WindowLifecycleService>();
        foreach (var id in new[] { CommandId.ShowPet, CommandId.HidePet, CommandId.TogglePetVisibility,
            CommandId.OpenControlCenter, CommandId.CloseControlCenter, CommandId.Exit })
            services.AddSingleton<IAppCommand>(provider => new WindowCommand(id, provider.GetRequiredService<IWindowService>()));
        return services;
    }
}
