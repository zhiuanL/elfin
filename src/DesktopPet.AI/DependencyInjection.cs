using DesktopPet.AI.Contracts;
using DesktopPet.AI.Providers;
using DesktopPet.AI.Security;
using DesktopPet.AI.Services;
using DesktopPet.AI.Tools;
using DesktopPet.Application.Appearance;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Configuration;
using DesktopPet.Application.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace DesktopPet.AI;

public static class DependencyInjection
{
    public static IServiceCollection AddAi(this IServiceCollection services)
    {
        services.AddSingleton(new HttpClient { Timeout = Timeout.InfiniteTimeSpan });
        services.AddSingleton<IAiRetryDelay, AiRetryDelay>();
        foreach (var type in Enum.GetValues<AiProviderType>())
            services.AddSingleton<IChatModelProvider>(provider => new ChatCompletionsProvider(type,
                provider.GetRequiredService<HttpClient>(), provider.GetRequiredService<IAiCredentialVault>(), provider.GetRequiredService<IAiRetryDelay>()));
        services.AddSingleton<ITtsProvider, OpenAiTtsProvider>();
        services.AddSingleton<IAiCredentialVault, AiCredentialVault>();
        services.AddSingleton<IAiProviderService, AiProviderService>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<IAiContextBuilder, AiContextBuilder>();
        services.AddSingleton<IResponseInterpreter, ResponseInterpreter>();
        services.AddSingleton<IAiToolSchemaValidator, AiToolSchemaValidator>();
        foreach (var kind in Enum.GetValues<PomodoroToolKind>())
            services.AddSingleton<IAiTool>(provider => new PomodoroAiTool(kind,
                provider.GetRequiredService<IPomodoroService>(), provider.GetRequiredService<ISettingsService>()));
        foreach (var kind in Enum.GetValues<ReminderToolKind>())
            services.AddSingleton<IAiTool>(provider => new ReminderAiTool(kind,
                provider.GetRequiredService<IReminderService>(), provider.GetRequiredService<TimeProvider>()));
        foreach (var kind in Enum.GetValues<UiToolKind>())
            services.AddSingleton<IAiTool>(provider => new UiAiTool(kind, provider.GetRequiredService<ICommandRegistry>()));
        foreach (var kind in Enum.GetValues<PetToolKind>())
            services.AddSingleton<IAiTool>(provider => new PetAiTool(kind,
                provider.GetRequiredService<ICommandRegistry>(), provider.GetRequiredService<ISettingsService>()));
        services.AddSingleton<IAiTool>(provider => new SettingsAiTool(provider.GetRequiredService<ISettingsService>(),
            provider.GetRequiredService<IAppearanceService>(), provider.GetRequiredService<ICommandRegistry>()));
        services.AddSingleton<IAiToolRegistry, AiToolRegistry>();
        services.AddSingleton<IAiChatService, AiChatService>();
        return services;
    }
}
