
using Chat.App.API.Services;
using DeepHarness;
using DeepHarness.Backend;
using DeepHarness.HistoryProvider;
using Microsoft.EntityFrameworkCore;

namespace Chat.App.API.AgentServices;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgentServices(
        this IServiceCollection services,
        string apiKey,
        string model,
        string connectionStringMessages,
        string name = DeepAgentFactory.DefaultAgentName,
        string? systemPrompt = null,
        string providerName = "google",
        string? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("Model is required.", nameof(model));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            name = DeepAgentFactory.DefaultAgentName;
        }

        if (string.IsNullOrWhiteSpace(connectionStringMessages))
        {
            throw new ArgumentException("Connection string messages is required.", nameof(connectionStringMessages));
        }

        services.AddDeepHarness(new DeepHarnessOptions
        {
            ConnectionStringMessages = connectionStringMessages,
            ConfigureHistoryDb = dbContextOptions =>
            {
                dbContextOptions.UseSqlite(connectionStringMessages);
            },
            ConfigureStoreDb = dbContextOptions =>
            {
                dbContextOptions.UseSqlite(connectionStringMessages);
            },
            ApiKey = apiKey,
            Model = model,
            Provider = providerName,
            Endpoint = endpoint,
            AgentName = name,
            SystemPrompt = systemPrompt,
            EnsureDatabaseCreatedOnStartup = true
        });

        services.AddSingleton<IAgentService>(provider =>
        {
            var chatHistoryProvider = provider.GetRequiredService<DbChatHistoryProvider>();
            var historyDbFactory = provider.GetRequiredService<IDbContextFactory<ChatHistoryDbContext>>();
            var storeDbFactory = provider.GetRequiredService<IDbContextFactory<SqlStoreDbContext>>();

            var chatClient = ChatClientFactory.Create(providerName, apiKey, model, endpoint);

            return new AgentService(
                chatClient,
                historyDbFactory,
                storeDbFactory,
                chatHistoryProvider,
                name,
                systemPrompt);
        });

        return services;
    }
}
