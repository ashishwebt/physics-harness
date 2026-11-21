using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace DeepHarness.Agents;

// ════════════════════════════════════════════════════════════════════
//  State pattern for the agent loop
//
//   ┌────────────────┐ response ┌─────────────────┐
//   │ CallModel /    │ ───────► │ InspectResponse │
//   │ StreamModel    │          └───┬─────┬─────┬─┘
//   └───────▲────────┘   no tools   │     │     │ round limit
//           │              ┌────────┘     │     └──────► Failed (throws)
//           │              ▼              │ tool calls
//           │        ┌──────────┐         ▼
//           │        │ Finalize │   ┌──────────────┐
//           │        └────┬─────┘   │ ExecuteTools │
//           │             ▼         └──────┬───────┘
//           │        ┌─────────┐           ▼
//           └─────── │ Persist │ ◄─────────┘   (saves this round's call/result pairs)
//                    └─────────┘
//   After Finalize, Persist saves the final response and the machine ends.
// ════════════════════════════════════════════════════════════════════

/// <summary>
/// Shared, mutable data that flows through the states of one agent run.
/// </summary>
internal sealed class AgentLoopContext
{
    public const int MaxToolRounds = 10;

    public required IChatClient ChatClient { get; init; }
    public required ILogger Logger { get; init; }
    public required string? AgentName { get; init; }
    public required bool Streaming { get; init; }

    /// <summary>Full prompt history sent to the model (grows as tools run).</summary>
    public required List<ChatMessage> History { get; init; }

    public ChatOptions? ChatOptions { get; init; }

    /// <summary>The messages the caller passed in for this run (stored once, with the first flush).</summary>
    public required IReadOnlyCollection<ChatMessage> InputMessages { get; init; }

    /// <summary>Writes (request, response) messages to the history provider.</summary>
    public required Func<IEnumerable<ChatMessage>, IEnumerable<ChatMessage>, CancellationToken, Task> Store { get; init; }

    /// <summary>Messages produced since the last flush, waiting to be persisted.</summary>
    public List<ChatMessage> Pending { get; } = [];

    private bool _inputsPersisted;

    /// <summary>
    /// Persists <see cref="Pending"/> now. The input messages go out with the first flush only,
    /// so later flushes don't store them twice.
    /// </summary>
    public async Task FlushAsync(CancellationToken cancellationToken)
    {
        if (_inputsPersisted && Pending.Count == 0)
            return;

        IEnumerable<ChatMessage> request = _inputsPersisted ? [] : InputMessages;
        var response = Pending.ToList();

        await Store(request, response, cancellationToken).ConfigureAwait(false);

        _inputsPersisted = true;
        Pending.Clear();
    }

    /// <summary>The most recent model response (set by the model-call states).</summary>
    public ChatResponse? Response { get; set; }

    /// <summary>Tool calls extracted by <see cref="InspectResponseState"/>.</summary>
    public List<FunctionCallContent> PendingToolCalls { get; set; } = [];

    public int ToolRound { get; set; }

    public AgentLoopState CreateModelState() =>
        Streaming ? new StreamModelState() : new CallModelState();

    public AIFunction? FindTool(string name) =>
        ChatOptions?.Tools?
            .OfType<AIFunction>()
            .FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
}

/// <summary>
/// A state in the agent loop. A state does its work (optionally emitting streaming updates)
/// and then publishes the state that should run next via <see cref="Next"/>.
/// A <c>null</c> <see cref="Next"/> means the machine has finished.
/// </summary>
internal abstract class AgentLoopState
{
    public AgentLoopState? Next { get; protected set; }

    public abstract IAsyncEnumerable<ChatResponseUpdate> RunAsync(
        AgentLoopContext ctx,
        CancellationToken cancellationToken);
}

/// <summary>
/// Convenience base for states that never emit streaming updates.
/// </summary>
internal abstract class SilentState : AgentLoopState
{
    protected abstract ValueTask ExecuteAsync(AgentLoopContext ctx, CancellationToken cancellationToken);

    public sealed override async IAsyncEnumerable<ChatResponseUpdate> RunAsync(
        AgentLoopContext ctx,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        yield break;
    }
}

/// <summary>Non-streaming model call.</summary>
internal sealed class CallModelState : SilentState
{
    protected override async ValueTask ExecuteAsync(AgentLoopContext ctx, CancellationToken cancellationToken)
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

        Next = new InspectResponseState();
    }
}

/// <summary>Streaming model call: forwards updates to the caller while collecting them.</summary>
internal sealed class StreamModelState : AgentLoopState
{
    public override async IAsyncEnumerable<ChatResponseUpdate> RunAsync(
        AgentLoopContext ctx,
        [EnumeratorCancellation] CancellationToken cancellationToken)
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
        Next = new InspectResponseState();
    }
}

/// <summary>Decides whether the model asked for tools or produced a final answer.</summary>
internal sealed class InspectResponseState : SilentState
{
    protected override ValueTask ExecuteAsync(AgentLoopContext ctx, CancellationToken cancellationToken)
    {
        ctx.PendingToolCalls = ctx.Response!.Messages
            .SelectMany(m => m.Contents)
            .OfType<FunctionCallContent>()
            .ToList();

        Next = ctx.PendingToolCalls.Count switch
        {
            0 => new FinalizeState(),
            _ when ctx.ToolRound >= AgentLoopContext.MaxToolRounds =>
                new FailedState(new InvalidOperationException("The tool-calling loop exceeded the maximum allowed rounds.")),
            _ => new ExecuteToolsState(),
        };

        return ValueTask.CompletedTask;
    }
}

/// <summary>Invokes the requested tools and appends the call/result pairs to history.</summary>
internal sealed class ExecuteToolsState : SilentState
{
    protected override async ValueTask ExecuteAsync(AgentLoopContext ctx, CancellationToken cancellationToken)
    {
        var hadToolExecution = false;

        foreach (var call in ctx.PendingToolCalls)
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

            var assistantToolMessage = new ChatMessage(ChatRole.Assistant, [call]);
            var toolResultMessage = new ChatMessage(ChatRole.Tool,
            [
                new FunctionResultContent(call.CallId, result ?? string.Empty)
            ]);

            ctx.History.Add(assistantToolMessage);
            ctx.History.Add(toolResultMessage);
            ctx.Pending.Add(assistantToolMessage);
            ctx.Pending.Add(toolResultMessage);
            hadToolExecution = true;
        }

        ctx.PendingToolCalls = [];

        if (!hadToolExecution)
        {
            // Nothing we could run → treat the current response as final.
            Next = new FinalizeState();
            return;
        }

        ctx.ToolRound++;

        // Save this round's call/result pairs before asking the model again.
        Next = new PersistState(next: ctx.CreateModelState());
    }
}

/// <summary>
/// Flushes everything queued in <see cref="AgentLoopContext.Pending"/> to the history provider,
/// then continues with <paramref name="next"/> (or ends the run when it is null).
/// </summary>
internal sealed class PersistState(AgentLoopState? next) : SilentState
{
    protected override async ValueTask ExecuteAsync(AgentLoopContext ctx, CancellationToken cancellationToken)
    {
        await ctx.FlushAsync(cancellationToken).ConfigureAwait(false);
        Next = next;
    }
}

/// <summary>
/// The model produced a final answer: stamp the author name, queue the response
/// messages, and hand over to a terminal <see cref="PersistState"/>.
/// </summary>
internal sealed class FinalizeState : SilentState
{
    protected override ValueTask ExecuteAsync(AgentLoopContext ctx, CancellationToken cancellationToken)
    {
        foreach (var msg in ctx.Response!.Messages)
        {
            msg.AuthorName ??= ctx.AgentName;
            ctx.Pending.Add(msg);
        }

        Next = new PersistState(next: null);
        return ValueTask.CompletedTask;
    }
}

/// <summary>Terminal state: surfaces a failure.</summary>
internal sealed class FailedState(Exception error) : SilentState
{
    protected override ValueTask ExecuteAsync(AgentLoopContext ctx, CancellationToken cancellationToken)
        => throw error;
}