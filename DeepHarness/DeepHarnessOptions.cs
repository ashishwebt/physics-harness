using Microsoft.EntityFrameworkCore;

namespace DeepHarness;

public sealed class DeepHarnessOptions
{
    public string ConnectionStringMessages { get; init; } = string.Empty;

    public Action<DbContextOptionsBuilder>? ConfigureHistoryDb { get; init; }

    public Action<DbContextOptionsBuilder>? ConfigureStoreDb { get; init; }

    public string AgentName { get; init; } = DeepAgentFactory.DefaultAgentName;

    public string? SystemPrompt { get; init; }

    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = string.Empty;

    public string Provider { get; init; } = "google";

    public string? Endpoint { get; init; }

    public bool EnsureDatabaseCreatedOnStartup { get; init; } = true;
}
