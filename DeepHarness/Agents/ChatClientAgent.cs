using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace DeepHarness.Agents;

/// <summary>
/// A lightweight agent that wraps an <see cref="IChatClient"/> with always-on
/// chat history provider support.  Built only on Microsoft.Extensions.AI — no
/// dependency on Microsoft.Agents.AI.
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

        // Merge history with input messages.
        var history = await LoadHistoryAsync(session, inputList, cancellationToken).ConfigureAwait(false);
        var chatOptions = MergeChatOptions(runOptions);

        _logger.LogDebug("Agent {Name} invoking chat client with {Count} messages", _name, history.Count);

        ChatResponse response;
        List<ChatMessage> persistedMessages;
        try
        {
            response = await _chatClient.GetResponseAsync(history, chatOptions, cancellationToken).ConfigureAwait(false);
            (response, persistedMessages) = await ExecuteToolCallsAsync(history, response, chatOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Chat client invocation failed for agent {Name}", _name);
            throw;
        }

        // Set author name on response messages.
        foreach (var msg in response.Messages)
        {
            msg.AuthorName ??= _name;
        }

        // Persist the full message chain, including any assistant/tool round-trips.
        await StoreHistoryAsync(session, inputList, persistedMessages.Concat(response.Messages), cancellationToken).ConfigureAwait(false);

        return response;
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

        // Merge history with input messages.
        var history = await LoadHistoryAsync(session, inputList, cancellationToken).ConfigureAwait(false);
        var chatOptions = MergeChatOptions(runOptions);

        _logger.LogDebug("Agent {Name} invoking streaming chat client with {Count} messages", _name, history.Count);

        var persistedMessages = new List<ChatMessage>();
        List<ChatResponseUpdate> finalRoundUpdates = [];

        const int maxToolRounds = 10;
        for (var round = 0; round < maxToolRounds; round++)
        {
            IAsyncEnumerator<ChatResponseUpdate> enumerator;
            try
            {
                enumerator = _chatClient.GetStreamingResponseAsync(history, chatOptions, cancellationToken).GetAsyncEnumerator(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Streaming chat client invocation failed for agent {Name}", _name);
                throw;
            }

            var roundUpdates = new List<ChatResponseUpdate>();
            try
            {
                while (true)
                {
                    bool hasMore;
                    try
                    {
                        hasMore = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Streaming failed for agent {Name}", _name);
                        throw;
                    }

                    if (!hasMore)
                        break;

                    var update = enumerator.Current;
                    if (update is null)
                        continue;

                    roundUpdates.Add(update);
                    yield return new AgentResponseUpdate(update, _name);
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            finalRoundUpdates = roundUpdates;

            var toolCalls = roundUpdates
                .SelectMany(u => u.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (toolCalls.Count == 0)
                break;

            var hadToolExecution = false;
            foreach (var call in toolCalls)
            {
                var tool = chatOptions?.Tools?
                    .OfType<AIFunction>()
                    .FirstOrDefault(t => string.Equals(t.Name, call.Name, StringComparison.Ordinal));

                if (tool is null)
                {
                    _logger.LogWarning("Tool '{ToolName}' was requested but is not available to the agent.", call.Name);
                    continue;
                }

                var result = await tool.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken).ConfigureAwait(false);
                var resultText = result?.ToString() ?? string.Empty;

                var assistantToolMessage = new ChatMessage(ChatRole.Assistant, [call]);
                var toolResultMessage = new ChatMessage(ChatRole.Tool, resultText);

                history.Add(assistantToolMessage);
                history.Add(toolResultMessage);
                persistedMessages.Add(assistantToolMessage);
                persistedMessages.Add(toolResultMessage);
                hadToolExecution = true;
            }

            if (!hadToolExecution)
                break;
        }

        // Build the response messages from the final round and persist the full chain.
        var responseMessages = finalRoundUpdates.ToChatResponse().Messages;
        await StoreHistoryAsync(session, inputList, persistedMessages.Concat(responseMessages), cancellationToken).ConfigureAwait(false);
    }

    // ──────────────────────────────────────────────────────
    //  Private helpers
    // ──────────────────────────────────────────────────────

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

    private async Task<(ChatResponse Response, List<ChatMessage> PersistedMessages)> ExecuteToolCallsAsync(
        IList<ChatMessage> history,
        ChatResponse response,
        ChatOptions? chatOptions,
        CancellationToken cancellationToken)
    {
        const int maxToolRounds = 10;
        var persistedMessages = new List<ChatMessage>();

        for (var round = 0; round < maxToolRounds; round++)
        {
            var toolCalls = response.Messages
                .SelectMany(m => m.Contents)
                .OfType<FunctionCallContent>()
                .ToList();

            if (toolCalls.Count == 0)
            {
                return (response, persistedMessages);
            }

            var hadToolExecution = false;

            foreach (var call in toolCalls)
            {
                var tool = chatOptions?.Tools?
                    .OfType<AIFunction>()
                    .FirstOrDefault(t => string.Equals(t.Name, call.Name, StringComparison.Ordinal));

                if (tool is null)
                {
                    _logger.LogWarning("Tool '{ToolName}' was requested but is not available to the agent.", call.Name);
                    continue;
                }

                var result = await tool.InvokeAsync(new AIFunctionArguments(call.Arguments), cancellationToken).ConfigureAwait(false);
                var resultText = result?.ToString() ?? string.Empty;

                var assistantToolMessage = new ChatMessage(ChatRole.Assistant, [call]);
                var toolResultMessage = new ChatMessage(ChatRole.Tool, resultText);

                history.Add(assistantToolMessage);
                history.Add(toolResultMessage);
                persistedMessages.Add(assistantToolMessage);
                persistedMessages.Add(toolResultMessage);
                hadToolExecution = true;
            }

            if (!hadToolExecution)
            {
                return (response, persistedMessages);
            }

            response = await _chatClient.GetResponseAsync(history, chatOptions, cancellationToken).ConfigureAwait(false);
        }

        throw new InvalidOperationException("The tool-calling loop exceeded the maximum allowed rounds.");
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

