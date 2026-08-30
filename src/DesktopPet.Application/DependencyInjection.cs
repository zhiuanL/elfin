using DesktopPet.Application.Commands;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Windows;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Startup;
using Microsoft.Extensions.DependencyInjection;
using DesktopPet.Application.Runtime;
using DesktopPet.Domain.Pets;
using DesktopPet.Application.Movement;

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
        services.AddSingleton<RuntimePolicy>();
        services.AddTransient<IRandomSource>(_ => new SeededRandomSource(System.Security.Cryptography.RandomNumberGenerator.GetInt32(int.MaxValue)));
        services.AddTransient<PetRuntime>();
        services.AddTransient<IMovementService, MovementController>();
        services.AddSingleton<IMouseInteractionService, MouseInteractionService>();
        services.AddSingleton<PetHost>();
        services.AddSingleton<IPetHost>(provider => provider.GetRequiredService<PetHost>());
        services.AddSingleton<ICharacterPresentation>(provider => provider.GetRequiredService<PetHost>().Runtime);
        return services;
    }

    public static IServiceCollection AddWindowApplication(this IServiceCollection services)
    {
        services.AddSingleton<WindowPlacementPolicy>();
        services.AddSingleton<IWindowService, WindowLifecycleService>();
        foreach (var id in new[] { CommandId.SetInteractive, CommandId.SetClickThrough, CommandId.ToggleClickThrough, CommandId.TemporaryClickThrough })
            services.AddSingleton<IAppCommand>(provider => new MouseInteractionCommand(id, provider.GetRequiredService<IMouseInteractionService>()));
        foreach (var id in new[] { CommandId.ShowPet, CommandId.HidePet, CommandId.TogglePetVisibility,
            CommandId.OpenControlCenter, CommandId.CloseControlCenter, CommandId.Exit })
            services.AddSingleton<IAppCommand>(provider => new WindowCommand(id, provider.GetRequiredService<IWindowService>()));
        return services;
    }
}
