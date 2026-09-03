using DesktopPet.AI.Contracts;
using DesktopPet.AI.Providers;
using DesktopPet.AI.Security;
using DesktopPet.AI.Services;
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
        services.AddSingleton<IAiCredentialVault, AiCredentialVault>();
        services.AddSingleton<IAiProviderService, AiProviderService>();
        services.AddSingleton<IMemoryService, MemoryService>();
        services.AddSingleton<IAiContextBuilder, AiContextBuilder>();
        services.AddSingleton<IResponseInterpreter, ResponseInterpreter>();
        services.AddSingleton<IAiChatService, AiChatService>();
        return services;
    }
}
