using DesktopPet.Application.Commands;
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
        return services;
    }
}
