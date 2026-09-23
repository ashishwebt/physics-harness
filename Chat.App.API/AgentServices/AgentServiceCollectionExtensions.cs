
using System.ClientModel;
using Chat.App.API.AgentServices.HistoryProvider;
using Chat.App.API.Services;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;

namespace Chat.App.API.AgentServices;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddAgentServices(
        this IServiceCollection services,
        string apiKey,
        string model,
        string name,
        string systemPrompt,
        string connectionStringMessages)
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
            throw new ArgumentException("Agent name is required.", nameof(name));
        }

        if (string.IsNullOrWhiteSpace(systemPrompt))
        {
            throw new ArgumentException("System prompt is required.", nameof(systemPrompt));
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
            ChatClientAgentOptions options = new()
            {
                ChatHistoryProvider = chatHistoryProvider,
                Name = name,
                ChatOptions = new()
                {
                    MaxOutputTokens = 10_000,
                    Instructions = systemPrompt,
                }
            };
            var openRouterClient = new ChatClient(
                model: model,
                credential: new ApiKeyCredential(apiKey),
                options: new OpenAIClientOptions
                {
                    Endpoint = new Uri("https://openrouter.ai/api/v1")
                });

            ChatClientAgent agent = new(
                options: options,
                chatClient: openRouterClient.AsIChatClient()
                );

            return new AgentService(agent, dbFactory);
        });

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ChatHistoryDbContext>();
        db.Database.EnsureCreated();


        return services;
    }
}
