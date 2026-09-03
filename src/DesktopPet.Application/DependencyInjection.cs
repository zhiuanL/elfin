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
using DesktopPet.Application.Navigation;
using DesktopPet.Application.Hotkeys;
using DesktopPet.Application.Productivity;

namespace DesktopPet.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton<IExceptionHandler, ExceptionHandler>();
        services.AddSingleton<IRecoveryCoordinator, RecoveryCoordinator>();
        services.AddSingleton<ICommandRegistry, CommandRegistry>();
        services.AddSingleton<INavigationService, ControlCenterNavigationService>();
        services.AddSingleton<IHotkeyCoordinator, HotkeyCoordinator>();
        services.AddSingleton<ProductivityEventHub>();
        services.AddSingleton<IProductivityEventPublisher>(provider => provider.GetRequiredService<ProductivityEventHub>());
        services.AddSingleton<IPomodoroService, PomodoroService>();
        services.AddSingleton<ITaskService, TaskService>();
        services.AddSingleton<ITagService, TagService>();
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddSingleton<IStatisticsExporter, CsvStatisticsExporter>();
        services.AddSingleton<IReminderService, ReminderService>();
        services.AddSingleton<IReminderOccurrenceProcessor, ReminderOccurrenceProcessor>();
        services.AddSingleton<IMissedReminderResolver, MissedReminderResolver>();
        services.AddSingleton<IReminderScheduler, ReminderScheduler>();
        services.AddSingleton<IProductivityRecoveryService, ProductivityRecoveryService>();
        services.AddSingleton<IReminderNotificationChannel, NoOpSoundReminderChannel>();
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
            CommandId.OpenControlCenter, CommandId.CloseControlCenter, CommandId.Exit,
            CommandId.EnableTopmost, CommandId.DisableTopmost })
            services.AddSingleton<IAppCommand>(provider => new WindowCommand(id, () => provider.GetRequiredService<IWindowService>()));
        services.AddSingleton<IAppCommand, PomodoroToggleCommand>();
        services.AddSingleton<IAppCommand>(provider => new ProductivityNavigationCommand(CommandId.OpenPomodoro, AppPage.Pomodoro,
            provider.GetRequiredService<INavigationService>(), () => provider.GetRequiredService<IWindowService>()));
        services.AddSingleton<IAppCommand>(provider => new ProductivityNavigationCommand(CommandId.OpenReminders, AppPage.Reminders,
            provider.GetRequiredService<INavigationService>(), () => provider.GetRequiredService<IWindowService>()));
        foreach (var item in new[] { (CommandId.OpenHome, AppPage.Home), (CommandId.OpenAi, AppPage.AI),
            (CommandId.OpenStatistics, AppPage.Statistics), (CommandId.OpenCharacters, AppPage.Characters),
            (CommandId.OpenSettings, AppPage.Settings) })
            services.AddSingleton<IAppCommand>(provider => new ProductivityNavigationCommand(item.Item1, item.Item2,
                provider.GetRequiredService<INavigationService>(), () => provider.GetRequiredService<IWindowService>()));
        return services;
    }
}
