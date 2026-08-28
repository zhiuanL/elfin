using DesktopPet.Application.Configuration;
using DesktopPet.Application.Characters;
using DesktopPet.CharacterSdk;
using DesktopPet.Infrastructure.Characters;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Diagnostics;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Storage;
using DesktopPet.Infrastructure.Configuration;
using DesktopPet.Infrastructure.Diagnostics;
using DesktopPet.Infrastructure.Localization;
using DesktopPet.Infrastructure.Persistence;
using DesktopPet.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using DesktopPet.Application.Runtime;

namespace DesktopPet.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IAppDataDirectories directories)
    {
        services.AddSingleton(directories);
        services.AddSingleton<IAppLogger, RollingFileAppLogger>();
        services.AddSingleton<ISettingsService, JsonSettingsService>();
        services.AddSingleton<ICharacterPackageValidator, CharacterPackageValidator>();
        services.AddSingleton<ICharacterPackageStore, FileCharacterPackageStore>();
        services.AddSingleton<ICharacterBehaviorProfileReader, CharacterBehaviorProfileReader>();
        services.AddSingleton<ITextLocalizer, ResourceTextLocalizer>();
        services.AddSingleton<ISqliteConnectionFactory, SqliteConnectionFactory>();
        foreach (var migration in InitialMigrations.Create()) services.AddSingleton(migration);
        services.AddSingleton<IDatabaseMigrator, SqliteDatabaseMigrator>();
        services.AddSingleton<IUpdateService, NoOpUpdateService>();
        services.AddSingleton<ISyncService, NoOpSyncService>();
        services.AddSingleton<ICrashReportingService, NoOpCrashReportingService>();
        return services;
    }
}
