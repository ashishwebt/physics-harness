
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Chat.App.API.Services;

public interface IAgentService
{
    IAsyncEnumerable<AgentResponseUpdate> StreamAsync(
        string conversationId,
        ChatMessage message,
        CancellationToken ct = default);

    Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string conversationId,
        CancellationToken ct = default);
}

public sealed class AgentService : IAgentService
{
    private readonly ChatClientAgent _agent;
    private readonly IDbContextFactory<ChatHistoryDbContext> _dbFactory;

    public AgentService(
        ChatClientAgent agent,
        IDbContextFactory<ChatHistoryDbContext> dbFactory)
    {
        _agent = agent;
        _dbFactory = dbFactory;
    }

    public async IAsyncEnumerable<AgentResponseUpdate> StreamAsync(
        string conversationId, ChatMessage message,
        [EnumeratorCancellation]
     CancellationToken ct = default)
    {
        AgentSession session = await _agent.CreateSessionAsync(ct);
        foreach (var key in _agent.ChatHistoryProvider?.StateKeys ?? [])
        {
            session.StateBag.SetValue(key, conversationId);
        }

        await foreach (var update in _agent.RunStreamingAsync(message, session))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update;
            }
        }
    }

    public async Task<IReadOnlyList<ChatMessage>> GetHistoryAsync(
        string conversationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new ArgumentException("Conversation ID is required.", nameof(conversationId));
        }


        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var rows = await db.Messages
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId)
            .OrderBy(x => x.Sequence)
            .ToListAsync(ct);

        return rows
            .Select(x => JsonSerializer.Deserialize<ChatMessage>(x.MessageJson))
            .Where(x => x is not null)
            .Cast<ChatMessage>()
            .ToList();
    }
}