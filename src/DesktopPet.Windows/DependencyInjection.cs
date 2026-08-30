using DesktopPet.Application.Contracts;
using DesktopPet.Application.Characters;
using DesktopPet.CharacterSdk;
using DesktopPet.Windows.Characters;
using DesktopPet.Application.Windows;
using DesktopPet.Application.Movement;
using DesktopPet.Windows.Security;
using DesktopPet.Windows.Windowing;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopPet.Windows;

public static class DependencyInjection
{
    public static IServiceCollection AddWindowsPlatform(this IServiceCollection services)
    {
        services.AddSingleton<IDataProtectionService, DpapiDataProtectionService>();
        services.AddSingleton<WindowsDisplayService>();
        services.AddSingleton<IDisplayService>(provider => provider.GetRequiredService<WindowsDisplayService>());
        services.AddSingleton<IDisplayTopologyService>(provider => provider.GetRequiredService<WindowsDisplayService>());
        services.AddSingleton<IMovementSurface, WindowsMovementSurface>();
        services.AddSingleton<ITrayService, WindowsTrayService>();
        services.AddSingleton<IPngInspector, WindowsPngInspector>();
        services.AddSingleton<ICharacterPackagePicker, WindowsCharacterPackagePicker>();
        services.AddSingleton<WpfAnimationSurface>();
        services.AddSingleton<IAnimationSurface>(provider => provider.GetRequiredService<WpfAnimationSurface>());
        services.AddSingleton<ICharacterImageSource>(provider => provider.GetRequiredService<WpfAnimationSurface>());
        return services;
    }
}
