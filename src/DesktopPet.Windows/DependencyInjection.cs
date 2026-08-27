using DesktopPet.Application.Contracts;
using DesktopPet.Application.Windows;
using DesktopPet.Windows.Security;
using DesktopPet.Windows.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopPet.Windows;

public static class DependencyInjection
{
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IDataProtectionService, DpapiDataProtectionService>();
        services.AddSingleton<IDisplayService, WindowsDisplayService>();
        services.AddSingleton<ITrayService, WindowsTrayService>();
        return services;
    }
}
