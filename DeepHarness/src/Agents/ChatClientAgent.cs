using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepHarness.Agents;

/// <summary>
/// A lightweight agent that wraps an <see cref="IChatClient"/> with always-on
/// chat history provider support.  Built only on Microsoft.Extensions.AI — no
/// dependency on Microsoft.Agents.AI.
/// The model/tool loop is implemented as a state machine (see AgentLoopStates.cs).
/// </summary>
public sealed class ChatClientAgent
{
    private readonly ChatClientAgentOptions _options;
    private readonly ChatHistoryProvider _historyProvider;
    private readonly IChatClient _chatClient;
    private readonly string? _name;
    private readonly ILogger _logger;

    /// <summary>
    /// Creates a new agent from explicit parameters (convenience overload).
    /// </summary>
    public ChatClientAgent(
        IChatClient chatClient,
        ChatHistoryProvider chatHistoryProvider,
        string? name = null,
        string? instructions = null,
        IList<AITool>? tools = null,
        ILoggerFactory? loggerFactory = null)
        : this(chatClient, new ChatClientAgentOptions
        {
            ChatHistoryProvider = chatHistoryProvider,
            Name = name,
            ChatOptions = (tools is null && string.IsNullOrWhiteSpace(instructions)) ? null : new ChatOptions
            {
                Instructions = instructions,
                Tools = tools
            }
        }, loggerFactory)
    {
    }

    /// <summary>
    /// Creates a new agent from <see cref="ChatClientAgentOptions"/>.
    /// </summary>
    public ChatClientAgent(IChatClient chatClient, ChatClientAgentOptions options, ILoggerFactory? loggerFactory = null)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(options);

        _chatClient = chatClient;
        _options = options;
        _historyProvider = options.ChatHistoryProvider;
        _name = options.Name;
        _logger = (loggerFactory ?? NullLoggerFactory.Instance).CreateLogger<ChatClientAgent>();
    }

    /// <summary>
    /// Creates a new session for an existing conversation ID.
    /// </summary>
    public ValueTask<AgentSession> CreateSessionAsync(string conversationId, CancellationToken cancellationToken = default)
    {
        return new(new AgentSession { ConversationId = conversationId });
    }

    /// <summary>
    /// Runs the agent non-streaming: loads history, calls the chat client, persists
    /// the new messages, and returns the <see cref="ChatResponse"/>.
    /// </summary>
    public async Task<ChatResponse> RunAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession session,
        ChatOptions? runOptions = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(session);

        var inputList = messages as IReadOnlyCollection<ChatMessage> ?? messages.ToList();
        var ctx = await CreateLoopContextAsync(session, inputList, runOptions, streaming: false, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Agent {Name} invoking chat client with {Count} messages", _name, ctx.History.Count);

        // Non-streaming runs produce no updates; drive the loop to completion.
        await foreach (var _ in AgentLoopState.RunAsync(ctx, cancellationToken).ConfigureAwait(false))
        {
        }

        // History was already persisted incrementally by the loop.
        return ctx.Response!;
    }

    /// <summary>
    /// Runs the agent in streaming mode: loads history, streams updates from the
    /// chat client, and persists the complete messages after the stream ends.
    /// </summary>
    public async IAsyncEnumerable<AgentResponseUpdate> RunStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession session,
        ChatOptions? runOptions = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);
        ArgumentNullException.ThrowIfNull(session);

        var inputList = messages as IReadOnlyCollection<ChatMessage> ?? messages.ToList();
        var ctx = await CreateLoopContextAsync(session, inputList, runOptions, streaming: true, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Agent {Name} invoking streaming chat client with {Count} messages", _name, ctx.History.Count);

        await foreach (var update in AgentLoopState.RunAsync(ctx, cancellationToken).ConfigureAwait(false))
        {
            yield return new AgentResponseUpdate(update, _name);
        }
    }

    // ──────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────

    private async Task<AgentLoopContext> CreateLoopContextAsync(
        AgentSession session,
        IReadOnlyCollection<ChatMessage> inputMessages,
        ChatOptions? runOptions,
        bool streaming,
        CancellationToken cancellationToken)
    {
        var history = await LoadHistoryAsync(session, inputMessages, cancellationToken).ConfigureAwait(false);

        return new AgentLoopContext
        {
            ChatClient = _chatClient,
            Logger = _logger,
            AgentName = _name,
            Streaming = streaming,
            History = history,
            InputMessages = inputMessages,
            Store = (request, response, ct) => StoreHistoryAsync(session, request, response, ct),
            ChatOptions = MergeChatOptions(runOptions),
        };
    }

    private async Task<List<ChatMessage>> LoadHistoryAsync(
        AgentSession session,
        IReadOnlyCollection<ChatMessage> inputMessages,
        CancellationToken cancellationToken)
    {
        var invokingContext = new InvokingContext(this, session, inputMessages);
        var history = await _historyProvider.InvokingAsync(invokingContext, cancellationToken).ConfigureAwait(false);

        // Prepend history, then append the new input messages.
        var result = new List<ChatMessage>();
        if (history is not null)
            result.AddRange(history);
        result.AddRange(inputMessages);
        return result;
    }

    private async Task StoreHistoryAsync(
        AgentSession session,
        IEnumerable<ChatMessage> requestMessages,
        IEnumerable<ChatMessage> responseMessages,
        CancellationToken cancellationToken)
    {
        var invokedContext = new InvokedContext(this, session, requestMessages, responseMessages);
        await _historyProvider.InvokedAsync(invokedContext, cancellationToken).ConfigureAwait(false);
    }

    private ChatOptions? MergeChatOptions(ChatOptions? runOptions)
    {
        var defaultOptions = _options.ChatOptions;
        if (defaultOptions is null)
            return runOptions;

        if (runOptions is null)
            return defaultOptions;

        // Clone the default options and override with run-specific values.
        var merged = new ChatOptions
        {
            Instructions = !string.IsNullOrWhiteSpace(runOptions.Instructions)
                ? runOptions.Instructions
                : defaultOptions.Instructions,
            ModelId = runOptions.ModelId ?? defaultOptions.ModelId,
            Temperature = runOptions.Temperature ?? defaultOptions.Temperature,
            TopP = runOptions.TopP ?? defaultOptions.TopP,
            TopK = runOptions.TopK ?? defaultOptions.TopK,
            MaxOutputTokens = runOptions.MaxOutputTokens ?? defaultOptions.MaxOutputTokens,
            FrequencyPenalty = runOptions.FrequencyPenalty ?? defaultOptions.FrequencyPenalty,
            PresencePenalty = runOptions.PresencePenalty ?? defaultOptions.PresencePenalty,
            ResponseFormat = runOptions.ResponseFormat ?? defaultOptions.ResponseFormat,
            ToolMode = runOptions.ToolMode ?? defaultOptions.ToolMode,
            AllowMultipleToolCalls = runOptions.AllowMultipleToolCalls ?? defaultOptions.AllowMultipleToolCalls,
            Seed = runOptions.Seed ?? defaultOptions.Seed,
        };

        // Merge tools: prefer run options if provided, else defaults.
        merged.Tools = runOptions.Tools is { Count: > 0 } ? runOptions.Tools : defaultOptions.Tools;

        // Merge stop sequences.
        if (defaultOptions.StopSequences is { Count: > 0 })
        {
            merged.StopSequences = [.. defaultOptions.StopSequences];
            if (runOptions.StopSequences is { Count: > 0 })
            {
                foreach (var s in runOptions.StopSequences)
                    merged.StopSequences.Add(s);
            }
        }
        else if (runOptions.StopSequences is { Count: > 0 })
        {
            merged.StopSequences = [.. runOptions.StopSequences];
        }

        // Merge additional properties.
        if (defaultOptions.AdditionalProperties is { Count: > 0 })
        {
            merged.AdditionalProperties = new AdditionalPropertiesDictionary(defaultOptions.AdditionalProperties);
            if (runOptions.AdditionalProperties is { Count: > 0 })
            {
                foreach (var kvp in runOptions.AdditionalProperties)
                    merged.AdditionalProperties.TryAdd(kvp.Key, kvp.Value);
            }
        }
        else if (runOptions.AdditionalProperties is { Count: > 0 })
        {
            merged.AdditionalProperties = new AdditionalPropertiesDictionary(runOptions.AdditionalProperties);
        }

        return merged;
    }
}
