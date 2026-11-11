using System.Text.Json;
using DeepHarness.Agents;
using Microsoft.Extensions.AI;

namespace DeepHarness;

/// <summary>
/// A lightweight agent session that carries a conversation ID and a state bag
/// for provider-scoped state (e.g. the SQLite history provider stores its
/// conversation-id key here).
/// </summary>
public sealed class AgentSession
{
    /// <summary>Unique identifier for the conversation this session belongs to.</summary>
    public string? ConversationId { get; set; }

    /// <summary>
    /// Shared state bag that providers can read from and write to.
    /// Keys are provider-defined strings (see <see cref="ChatHistoryProvider.StateKeys"/>).
    /// </summary>
    public Dictionary<string, object?> StateBag { get; } = new(StringComparer.Ordinal);
}

/// <summary>
/// Context passed to <see cref="ChatHistoryProvider.InvokingAsync"/> before the
/// agent calls the underlying chat client.
/// </summary>
public sealed class InvokingContext(
    ChatClientAgent agent,
    AgentSession session,
    IEnumerable<ChatMessage> requestMessages)
{
    /// <summary>The agent that is about to invoke the chat client.</summary>
    public ChatClientAgent Agent { get; } = agent;

    /// <summary>The session for this run.</summary>
    public AgentSession Session { get; } = session;

    /// <summary>The messages that will be sent to the chat client (after history merge).</summary>
    public IEnumerable<ChatMessage> RequestMessages { get; } = requestMessages;
}

/// <summary>
/// Context passed to <see cref="ChatHistoryProvider.InvokedAsync"/> after the
/// agent receives a response from the chat client.
/// </summary>
public sealed class InvokedContext(
    ChatClientAgent agent,
    AgentSession session,
    IEnumerable<ChatMessage> requestMessages,
    IEnumerable<ChatMessage>? responseMessages)
{
    /// <summary>The agent that invoked the chat client.</summary>
    public ChatClientAgent Agent { get; } = agent;

    /// <summary>The session for this run.</summary>
    public AgentSession Session { get; } = session;

    /// <summary>The messages sent to the chat client.</summary>
    public IEnumerable<ChatMessage> RequestMessages { get; } = requestMessages;

    /// <summary>The messages returned by the chat client, if any.</summary>
    public IEnumerable<ChatMessage>? ResponseMessages { get; } = responseMessages;
}

/// <summary>
/// Abstract base for chat history providers.  The agent calls
/// <see cref="InvokingAsync"/> before each run to load history, and
/// <see cref="InvokedAsync"/> after each run to persist new messages.
/// </summary>
public abstract class ChatHistoryProvider
{
    /// <summary>
    /// State keys this provider reads from / writes to in the session state bag.
    /// Used by the caller to seed the session with a conversation ID.
    /// </summary>
    public abstract IReadOnlyList<string> StateKeys { get; }

    /// <summary>
    /// Called before the agent invokes the chat client.  Should return the
    /// full message history (oldest first) to prepend to the user's input.
    /// </summary>
    public abstract ValueTask<IEnumerable<ChatMessage>> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Called after the agent receives a response.  Should persist the new
    /// messages (request + response) to the backing store.
    /// </summary>
    public abstract ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory history provider used as a fallback when no provider is configured.
/// </summary>
public sealed class InMemoryChatHistoryProvider : ChatHistoryProvider
{
    private readonly List<ChatMessage> _messages = [];

    public override IReadOnlyList<string> StateKeys => [nameof(InMemoryChatHistoryProvider)];

    public override ValueTask<IEnumerable<ChatMessage>> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        // Return a snapshot so the caller can't mutate our internal list.
        return new(_messages.ToList());
    }

    public override ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.ResponseMessages is not null)
        {
            _messages.AddRange(context.RequestMessages.Concat(context.ResponseMessages));
        }
        return default;
    }
}

/// <summary>
/// Options for configuring a <see cref="ChatClientAgent"/>.
/// </summary>
public sealed class ChatClientAgentOptions
{
    /// <summary>Provider for loading and persisting chat history. Always used.</summary>
    public required ChatHistoryProvider ChatHistoryProvider { get; init; }

    /// <summary>Display name for the agent (used for logging and AuthorName).</summary>
    public string? Name { get; init; }

    /// <summary>Default chat options (instructions, tools, modelId, etc.).</summary>
    public ChatOptions? ChatOptions { get; init; }
}

/// <summary>
/// A single streaming update from the agent, wrapping <see cref="ChatResponseUpdate"/>
/// with the agent's name for authorship attribution.
/// </summary>
public sealed class AgentResponseUpdate
{
    internal AgentResponseUpdate(ChatResponseUpdate inner, string? authorName)
    {
        inner.AuthorName ??= authorName;
        Inner = inner;
    }

    internal ChatResponseUpdate Inner { get; }

    /// <summary>Text content of this update, if any.</summary>
    public string? Text => Inner.Text;

    /// <summary>Author name set on the underlying update.</summary>
    public string? AuthorName => Inner.AuthorName;

    /// <summary>The role of this update (typically assistant).</summary>
    public ChatRole? Role => Inner.Role;

    /// <summary>Additional contents (tool calls, function results, etc.).</summary>
    public IReadOnlyList<AIContent> Contents => Inner.Contents.AsReadOnly();

    /// <summary>Implicit conversion to the underlying update for compatibility.</summary>
    public static implicit operator ChatResponseUpdate(AgentResponseUpdate update) => update.Inner;
}
