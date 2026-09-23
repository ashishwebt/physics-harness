using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Chat.App.API.AgentServices.HistoryProvider;

public sealed class SqliteChatHistoryProvider(
    IDbContextFactory<ChatHistoryDbContext> dbFactory)
    : ChatHistoryProvider
{
    private const string ConversationIdKey = "SqliteChatHistoryProvider.ConversationId";

    public override IReadOnlyList<string> StateKeys =>
        [ConversationIdKey];

    protected override async ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
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

    protected override async ValueTask StoreChatHistoryAsync(
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
            throw new InvalidOperationException(
                "A session is required for SQLite chat history.");

        if (!context.Session.StateBag.TryGetValue<string>(
                ConversationIdKey,
                out var value))
        {
            value = Guid.NewGuid().ToString("N");

            context.Session.StateBag.SetValue<string>(ConversationIdKey, value);
        }

        return value!;
    }

    private static string GetConversationId(InvokedContext context)
    {
        if (context.Session is null)
            throw new InvalidOperationException(
                "A session is required for SQLite chat history.");

        if (!context.Session.StateBag.TryGetValue<string>(
                ConversationIdKey,
                out var value))
        {
            throw new InvalidOperationException(
                "Conversation ID was not initialized.");
        }

        return (string)value!;
    }
}