
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Chat.App.API.Models;
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

    Task<IReadOnlyList<ConversationMemoryFile>> GetMemoryFilesAsync(
        string conversationId,
        CancellationToken ct = default);
}

public sealed class AgentService : IAgentService
{
    private const string MemoryNamespaceKey = "artifacts";

    private readonly IChatClient _chatClient;
    private readonly IDbContextFactory<ChatHistoryDbContext> _dbFactory;
    private readonly ChatHistoryProvider _chatHistoryProvider;
    private readonly string _agentName;
    private readonly string? _systemPrompt;

    public AgentService(
        IChatClient chatClient,
        IDbContextFactory<ChatHistoryDbContext> dbFactory,
        ChatHistoryProvider chatHistoryProvider,
        string agentName = DeepAgentFactory.DefaultAgentName,
        string? systemPrompt = null)
    {
        _chatClient = chatClient;
        _dbFactory = dbFactory;
        _chatHistoryProvider = chatHistoryProvider;
        _agentName = string.IsNullOrWhiteSpace(agentName) ? DeepAgentFactory.DefaultAgentName : agentName;
        _systemPrompt = systemPrompt;
    }

    public async IAsyncEnumerable<AgentResponseUpdate> StreamAsync(
        string conversationId, ChatMessage message,
        [EnumeratorCancellation]
     CancellationToken ct = default)
    {
        var store = await LoadMemoryStoreAsync(conversationId, ct);
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

        AgentSession session = await agent.CreateSessionAsync(ct);
        foreach (var providerKey in _chatHistoryProvider.StateKeys)
        {
            session.StateBag.SetValue(providerKey, conversationId);
        }

        await foreach (var update in agent.RunStreamingAsync(message, session, cancellationToken: ct))
        {
            if (!string.IsNullOrEmpty(update.Text))
            {
                yield return update;
            }
        }

        await SaveMemoryStoreAsync(conversationId, store, ct);
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

    private async Task<InMemoryFileStore> LoadMemoryStoreAsync(
        string conversationId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new ArgumentException("Conversation ID is required.", nameof(conversationId));
        }

        var store = new InMemoryFileStore();
        var rows = await GetMemoryFileRowsAsync(conversationId, ct);

        foreach (var row in rows)
        {
            store.Put(row.NamespaceKey, row.Path, row.Content.ToArray());
        }

        return store;
    }

    private async Task SaveMemoryStoreAsync(
        string conversationId,
        InMemoryFileStore store,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(conversationId))
        {
            throw new ArgumentException("Conversation ID is required.", nameof(conversationId));
        }

        ArgumentNullException.ThrowIfNull(store);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var existing = await db.MemoryFiles
            .Where(x => x.ConversationId == conversationId && x.NamespaceKey == MemoryNamespaceKey)
            .ToListAsync(ct);

        if (existing.Count > 0)
        {
            db.MemoryFiles.RemoveRange(existing);
        }

        foreach (var (path, content) in store.List(MemoryNamespaceKey).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            db.MemoryFiles.Add(new ChatMemoryFileEntity
            {
                ConversationId = conversationId,
                NamespaceKey = MemoryNamespaceKey,
                Path = path,
                Content = content.ToArray(),
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task<List<ChatMemoryFileEntity>> GetMemoryFileRowsAsync(
        string conversationId,
        CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        return await db.MemoryFiles
            .AsNoTracking()
            .Where(x => x.ConversationId == conversationId && x.NamespaceKey == MemoryNamespaceKey)
            .OrderBy(x => x.Path)
            .ToListAsync(ct);
    }
}