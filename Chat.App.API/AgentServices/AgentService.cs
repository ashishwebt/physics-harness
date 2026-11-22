
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Chat.App.API.Models;
using DeepHarness;
using DeepHarness.Agents;
using DeepHarness.Backend;
using DeepHarness.HistoryProvider;
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

    Task<IReadOnlyList<ConversationMemoryFile>> GetMemoryFilesAsync(
        string conversationId,
        CancellationToken ct = default);
}

public sealed class AgentService : IAgentService
{
    private const string MemoryNamespaceKey = "artifacts";

    private readonly IChatClient _chatClient;
    private readonly IDbContextFactory<ChatHistoryDbContext> _historyDbFactory;
    private readonly IDbContextFactory<SqlStoreDbContext> _storeDbFactory;
    private readonly ChatHistoryProvider _chatHistoryProvider;
    private readonly string _agentName;
    private readonly string? _systemPrompt;

    public AgentService(
        IChatClient chatClient,
        IDbContextFactory<ChatHistoryDbContext> historyDbFactory,
        IDbContextFactory<SqlStoreDbContext> storeDbFactory,
        ChatHistoryProvider chatHistoryProvider,
        string agentName = DeepAgentFactory.DefaultAgentName,
        string? systemPrompt = null)
    {
        _chatClient = chatClient;
        _historyDbFactory = historyDbFactory;
        _storeDbFactory = storeDbFactory;
        _chatHistoryProvider = chatHistoryProvider;
        _agentName = string.IsNullOrWhiteSpace(agentName) ? DeepAgentFactory.DefaultAgentName : agentName;
        _systemPrompt = systemPrompt;
    }

    public async IAsyncEnumerable<AgentResponseUpdate> StreamAsync(
        string conversationId, ChatMessage message,
        [EnumeratorCancellation]
     CancellationToken ct = default)
    {
        var store = new SqlFileStore(_storeDbFactory, conversationId, MemoryNamespaceKey);
        var backend = new CompositeBackend(
            defaultBackend: new FilesystemBackend(Path.GetFullPath("./Physics", AppContext.BaseDirectory)),
            routes: new Dictionary<string, IBackend>
            {
                ["/artifacts/"] = new StoreBackend(store, namespaceKey: MemoryNamespaceKey)
            });

        var agent = DeepAgentFactory.Create(
            _chatClient,
            backend,
            _chatHistoryProvider,
            _agentName,
            _systemPrompt);

        AgentSession session =  ChatClientAgent.CreateSessionAsync(conversationId);

        await foreach (var update in agent.RunStreamingAsync([message], session, cancellationToken: ct))
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


        await using var db = await _historyDbFactory.CreateDbContextAsync(ct);

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

    public async Task<IReadOnlyList<ConversationMemoryFile>> GetMemoryFilesAsync(
        string conversationId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new ArgumentException("Conversation ID is required.", nameof(conversationId));
        }

        var rows = await GetMemoryFileRowsAsync(conversationId, ct);

        return rows
            .Select(row => new ConversationMemoryFile(
                row.Path,
                Encoding.UTF8.GetString(row.Content),
                row.Content.Length,
                row.UpdatedAt.UtcDateTime))
            .ToArray();
    }

    private async Task<List<ChatMemoryFileEntity>> GetMemoryFileRowsAsync(
        string conversationId,
        CancellationToken ct)
    {
        await using var db = await _storeDbFactory.CreateDbContextAsync(ct);

        return await db.MemoryFiles
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.NamespaceKey == MemoryNamespaceKey)
            .OrderBy(x => x.Path)
            .ToListAsync(ct);
    }
}