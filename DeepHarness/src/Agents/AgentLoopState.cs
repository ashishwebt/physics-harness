using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DeepHarness.Agents;

/// <summary>Data shared by the steps in one agent run.</summary>
internal sealed class AgentLoopContext
{
    public const int MaxToolRounds = 10;

    public required IChatClient ChatClient { get; init; }
    public required ILogger Logger { get; init; }
    public required string? AgentName { get; init; }
    public required bool Streaming { get; init; }
    public required List<ChatMessage> History { get; init; }
    public required IReadOnlyCollection<ChatMessage> InputMessages { get; init; }
    public required Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>, CancellationToken, Task> Store { get; init; }
    public ChatOptions? ChatOptions { get; init; }
    public ChatResponse? Response { get; set; }
    public List<ChatMessage> Pending { get; } = [];

    private bool _inputsPersisted;

    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_inputsPersisted && Pending.Count == 0)
            return;

        await Store(_inputsPersisted ? [] : InputMessages, Pending.ToList(), cancellationToken).ConfigureAwait(false);
        _inputsPersisted = true;
        Pending.Clear();
    }

    public AIFunction? FindTool(string name) => ChatOptions?.Tools?
        .OfType<AIFunction>()
        .FirstOrDefault(tool => string.Equals(tool.Name, name, StringComparison.Ordinal));
}

/// <summary>Runs model, tool, and persistence steps until the model returns a final response.</summary>
internal static class AgentLoopState
{
    public static async IAsyncEnumerable<ChatResponseUpdate> RunAsync(
        AgentLoopContext ctx,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var toolRound = 0;

        while (true)
        {
            if (ctx.Streaming)
            {
                var updates = new List<ChatResponseUpdate>();
                await using var enumerator = ctx.ChatClient
                    .GetStreamingResponseAsync(ctx.History, ctx.ChatOptions, cancellationToken)
                    .GetAsyncEnumerator(cancellationToken);

                while (true)
                {
                    ChatResponseUpdate? update;
                    try
                    {
                        if (!await enumerator.MoveNextAsync().ConfigureAwait(false))
                            break;
                        update = enumerator.Current;
                    }
                    catch (Exception ex)
                    {
                        ctx.Logger.LogError(ex, "Streaming failed for agent {Name}", ctx.AgentName);
                        throw;
                    }

                    if (update is null)
                        continue;
                    updates.Add(update);
                    yield return update;
                }

                ctx.Response = updates.ToChatResponse();
            }
            else
            {
                try
                {
                    ctx.Response = await ctx.ChatClient
                        .GetResponseAsync(ctx.History, ctx.ChatOptions, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ctx.Logger.LogError(ex, "Chat client invocation failed for agent {Name}", ctx.AgentName);
                    throw;
                }
            }

            var toolCalls = ctx.Response!.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (toolCalls.Count == 0)
                break;
            if (toolRound >= AgentLoopContext.MaxToolRounds)
                throw new InvalidOperationException("The tool-calling loop exceeded the maximum allowed rounds.");

            var executedTool = false;
            foreach (var call in toolCalls)
            {
                var tool = ctx.FindTool(call.Name);
                if (tool is null)
                {
                    ctx.Logger.LogWarning("Tool '{ToolName}' was requested but is not available to the agent.", call.Name);
                    continue;
                }

                var result = await tool
                    .InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken)
                    .ConfigureAwait(false);
                var callMessage = new ChatMessage(ChatRole.Assistant, [call]);
                var resultMessage = new ChatMessage(ChatRole.Tool,
                    [new FunctionResultContent(call.CallId, result ?? string.Empty)]);

                ctx.History.Add(callMessage);
                ctx.History.Add(resultMessage);
                ctx.Pending.Add(callMessage);
                ctx.Pending.Add(resultMessage);
                executedTool = true;
            }

            if (!executedTool)
                break;

            toolRound++;
            await ctx.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach (var message in ctx.Response!.Messages)
        {
            message.AuthorName ??= ctx.AgentName;
            ctx.Pending.Add(message);
        }
        await ctx.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
