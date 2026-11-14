using System.ClientModel;
using Microsoft.Extensions.AI;

namespace Chat.App.API.AgentServices;

/// <summary>Creates provider-specific chat clients for DeepHarness agents.</summary>
public static class ChatClientFactory
{
    public static IChatClient Create(string provider, string apiKey, string model, string? endpoint = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        var openAiOptions = new OpenAI.OpenAIClientOptions();
        if (!string.IsNullOrWhiteSpace(endpoint) && Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri))
        {
            openAiOptions.Endpoint = endpointUri;
        }

        IChatClient chatClient =
            new OpenAI.Chat.ChatClient(
                model,
                new ApiKeyCredential(apiKey),
                openAiOptions)
            .AsIChatClient();

        return provider.Trim().ToLowerInvariant() switch
        {
            "openai" => chatClient,
            "google" or "gemini" => new Google.GenAI.Client(apiKey: apiKey).AsIChatClient(model),
            _ => throw new ArgumentException($"Unsupported agent provider '{provider}'. Supported providers are 'openai' and 'google'.", nameof(provider))
        };
    }
}
