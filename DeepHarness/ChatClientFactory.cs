using Google.GenAI;
using Microsoft.Extensions.AI;
using OpenAI;

namespace DeepHarness;

/// <summary>Creates provider-specific chat clients for DeepHarness agents.</summary>
public static class ChatClientFactory
{
    public static IChatClient Create(string provider, string apiKey, string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(provider);
        ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(model);

        return provider.Trim().ToLowerInvariant() switch
        {
            "openai" => new OpenAIClient(apiKey).GetChatClient(model).AsIChatClient(),
            "google" or "gemini" => new Client(apiKey: apiKey).AsIChatClient(model),
            _ => throw new ArgumentException($"Unsupported agent provider '{provider}'. Supported providers are 'openai' and 'google'.", nameof(provider))
        };
    }
}
