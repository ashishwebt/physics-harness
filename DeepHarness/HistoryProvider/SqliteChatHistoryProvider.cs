using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace DeepHarness.HistoryProvider;

/// <summary>
/// SQLite-backed chat history provider.  Stores messages per conversation
/// in a <see cref="ChatHistoryDbContext"/>.
/// </summary>
public sealed class SqliteChatHistoryProvider(IDbContextFactory<ChatHistoryDbContext> dbFactory) : ChatHistoryProvider
{
    private const string ConversationIdKey = "SqliteChatHistoryProvider.ConversationId";

    /// <inheritdoc/>
    public override IReadOnlyList<string> StateKeys => [ConversationIdKey];

    /// <inheritdoc/>
    public override async ValueTask<IEnumerable<ChatMessage>> InvokingAsync(
        InvokingContext context,
        CancellationToken cancellationToken = default)
    {
        var conversationId = GetConversationId(context);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(cancellationToken);

        return rows.Select(x =>
            JsonSerializer.Deserialize<ChatMessage>(x.MessageJson)!
        );
    }

    /// <inheritdoc/>
    public override async ValueTask InvokedAsync(
        InvokedContext context,
        CancellationToken cancellationToken = default)
    {
        var conversationId = GetConversationId(context);

        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);

        var messages = context.RequestMessages
            .Concat(context.ResponseMessages ?? [])
            .ToList();

        if (messages.Count == 0)
            return;

        var lastSequence = await db.Messages
            .Where(x => x.ConversationId == conversationId)
            .Select(x => (int?)x.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        var sequence = lastSequence;

        foreach (var message in messages)
        {
            db.Messages.Add(new ChatMessageEntity
            {
                ConversationId = conversationId,
                Sequence = ++sequence,
                MessageJson = JsonSerializer.Serialize(message),
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static string GetConversationId(InvokingContext context)
    {
        if (context.Session is null)
            throw new InvalidOperationException("A session is required for SQLite chat history.");

        if (string.IsNullOrWhiteSpace(context.Session.ConversationId))
        {
            context.Session.ConversationId = Guid.NewGuid().ToString();
        }
        return context.Session.ConversationId;
    }

    private static string GetConversationId(InvokedContext context)
    {
        if (context.Session is null)
            throw new InvalidOperationException("A session is required for SQLite chat history.");

        if (string.IsNullOrWhiteSpace(context.Session.ConversationId))
        {
            throw new InvalidOperationException("Conversation ID was not initialized.");
        }
        return context.Session.ConversationId;

    }
}
