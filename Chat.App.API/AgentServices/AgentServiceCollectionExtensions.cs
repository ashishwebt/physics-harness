
using Chat.App.API.Services;
using DeepHarness;
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
        string providerName = "openai")
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

        services.AddPooledDbContextFactory<ChatHistoryDbContext>(options =>
        {
            options.UseSqlite(connectionStringMessages);
        });

        services.AddSingleton<SqliteChatHistoryProvider>();
        services.AddSingleton<IAgentService>(provider =>
        {
            var chatHistoryProvider = provider.GetRequiredService<SqliteChatHistoryProvider>();
            var dbFactory = provider.GetRequiredService<IDbContextFactory<ChatHistoryDbContext>>();

            var chatClient = ChatClientFactory.Create(providerName, apiKey, model);

            return new AgentService(
                chatClient,
                dbFactory,
                chatHistoryProvider,
                name,
                systemPrompt);
        });

        var provider = services.BuildServiceProvider();
        var environment = provider.GetRequiredService<IHostEnvironment>();

        if (environment.IsDevelopment())
        {
            using var scope = provider.CreateScope();
            var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ChatHistoryDbContext>>();
            using var dbContext = dbFactory.CreateDbContext();

            if (dbContext.Database.GetMigrations().Any())
            {
                dbContext.Database.Migrate();
            }
            else
            {
                dbContext.Database.EnsureCreated();
            }
        }

        return services;
    }
}
