using DesktopPet.Application.Contracts;
using DesktopPet.Windows.Security;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopPet.Windows;

public static class DependencyInjection
{
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IDataProtectionService, DpapiDataProtectionService>();
        // Display/window/session/hotkey adapters are intentionally not registered before their phases.
        return services;
    }
}
