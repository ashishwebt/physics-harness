
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using DeepHarness.Backend;
using DeepHarness;
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
    private readonly IChatClient _chatClient;
    private readonly IDbContextFactory<ChatHistoryDbContext> _dbFactory;
    private readonly ChatHistoryProvider _chatHistoryProvider;

    public AgentService(
        IChatClient chatClient,
        IDbContextFactory<ChatHistoryDbContext> dbFactory,
        ChatHistoryProvider chatHistoryProvider)
    {
        _chatClient = chatClient;
        _dbFactory = dbFactory;
        _chatHistoryProvider = chatHistoryProvider;
    }

    public async IAsyncEnumerable<AgentResponseUpdate> StreamAsync(
        string conversationId, ChatMessage message,
        [EnumeratorCancellation]
     CancellationToken ct = default)
    {

        var store = new InMemoryFileStore();
        var backend = new CompositeBackend(
            defaultBackend: new FilesystemBackend(Path.GetFullPath("./Physics", AppContext.BaseDirectory)),
            routes: new Dictionary<string, IBackend>
            {
                ["/memories/"] = new StoreBackend(store, namespaceKey: "memories")
            });

        var agent = DeepAgentFactory.Create(_chatClient, backend, _chatHistoryProvider);

        AgentSession session = await agent.CreateSessionAsync(ct);
        session.StateBag.SetValue("SqliteChatHistoryProvider.ConversationId", conversationId);
        await foreach (var update in agent.RunStreamingAsync(message, session, cancellationToken: ct))
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