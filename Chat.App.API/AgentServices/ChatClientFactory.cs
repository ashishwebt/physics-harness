using System.ClientModel;
using Microsoft.Extensions.AI;

namespace Chat.App.API.AgentServices;

/// <summary>Creates provider-specific chat clients for DeepHarness agents.</summary>
public static class ChatClientFactory
{
    public static IChatClient Create(string provider, string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        IChatClient chatClient =
                new OpenAI.Chat.ChatClient(
                    model,
                    new ApiKeyCredential(apiKey),
                    new OpenAI.OpenAIClientOptions 
                    {
                        Endpoint = new Uri("https://openrouter.ai/api/v1/")
                    })
                .AsIChatClient();

        return provider.Trim().ToLowerInvariant() switch
        {
            "openai" => chatClient,
            "google" or "gemini" => new Google.GenAI.Client(apiKey: apiKey).AsIChatClient(model),
            _ => throw new ArgumentException($"Unsupported agent provider '{provider}'. Supported providers are 'openai' and 'google'.", nameof(provider))
        };
    }
}
