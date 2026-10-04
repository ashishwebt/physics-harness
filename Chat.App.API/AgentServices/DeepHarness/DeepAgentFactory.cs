using DeepHarness.Backend;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DeepHarness;

/// <summary>
/// Creates a ready-to-run DeepAgent with built-in file tools over the supplied backend.
/// All implementation remains in the console project for now,
/// so this API can later move into a class library without changing the app entry point.
/// </summary>
public static class DeepAgentFactory
{
    public const string DefaultAgentName = "DeepAgent";

    private const string DefaultSystemPrompt = """
        You are a capable AI agent that can use tools to inspect, search, read, create, update, and verify information before answering.
        Be concise, direct, and evidence-based. Prioritize correctness over agreement, and do not invent details.
        Use the minimum valid action needed to complete a task, then verify the result before concluding.
        Ask only for the minimum clarification required when the request is underspecified.

        Tool usage guidance:
        - Use the right tool for the job rather than guessing or relying on memory alone.
        - Treat tool output as the source of truth and ground your answer in it.
        - Respect backend boundaries and do not access data outside the allowed environment.
        """;

    public static string BuildSystemPrompt(string? systemPrompt = null)
    {
        var sections = new List<string> { DefaultSystemPrompt.Trim() };

        if (!string.IsNullOrWhiteSpace(systemPrompt))
        {
            sections.Add(systemPrompt.Trim());
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    /// <summary>
    /// Creates an agent with the standard tool suite over a caller-configured backend.
    /// </summary>
    /// <param name="chatClient">Provider-specific Microsoft.Extensions.AI chat client.</param>
    /// <param name="backend">Configured backend (for example a CompositeBackend).</param>
    public static ChatClientAgent Create(
        IChatClient chatClient,
        IBackend backend,
        ChatHistoryProvider chatHistoryProvider,
        string agentName = DefaultAgentName,
        string? systemPrompt = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(backend);

        var tools = new BackendTools(backend, chatClient);
        var agent = chatClient.AsAIAgent(
            new ChatClientAgentOptions
            {
                ChatHistoryProvider = chatHistoryProvider,
                Name = agentName,
                ChatOptions = new ChatOptions
                {
                    Instructions = BuildSystemPrompt(systemPrompt),
                    MaxOutputTokens = 4096,
                    Tools = [
                        AIFunctionFactory.Create(tools.List, "ls"),
                        AIFunctionFactory.Create(tools.Read, "read_file"),
                        AIFunctionFactory.Create(tools.Write, "write_file"),
                        AIFunctionFactory.Create(tools.Edit, "edit_file"),
                        AIFunctionFactory.Create(tools.Delete, "delete_file"),
                        AIFunctionFactory.Create(tools.Glob, "glob"),
                        AIFunctionFactory.Create(tools.Grep, "grep"),
                        AIFunctionFactory.Create(tools.UpdateTodos, "write_todos"),
                        AIFunctionFactory.Create(tools.Task, "task")
                    ]
                },
            });

        return agent;
    }

    internal static ChatClientAgent CreateResearchAgent(
        IBackend backend,
        IChatClient chatClient,
        string agentName = "DeepAgentResearch",
        string? systemPrompt = null)
    {
        var tools = new BackendTools(backend, chatClient);
        var instructions = BuildSystemPrompt(
            systemPrompt ?? "You are a read-only research subagent. Complete only the delegated task. Use discovery, reading, and search tools; do not modify files or delegate further.");

        return new ChatClientAgent(
            chatClient,
            name: agentName,
            instructions: instructions,
            tools: [AIFunctionFactory.Create(tools.List, "ls"), AIFunctionFactory.Create(tools.Read, "read_file"), AIFunctionFactory.Create(tools.Glob, "glob"), AIFunctionFactory.Create(tools.Grep, "grep")]);
    }
}

