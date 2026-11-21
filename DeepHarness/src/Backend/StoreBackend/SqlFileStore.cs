using Microsoft.EntityFrameworkCore;

namespace DeepHarness.Backend;

/// <summary>SQL-backed conversation store for agent memory files. Keeps file state persistent in the chat history database.</summary>
public sealed class SqlFileStore(IDbContextFactory<SqlStoreDbContext> dbFactory, string conversationId, string defaultNamespaceKey = "artifacts") : IFileStore
{
    private readonly IDbContextFactory<SqlStoreDbContext> _dbFactory = dbFactory;
    private readonly string _conversationId = string.IsNullOrWhiteSpace(conversationId) ? throw new ArgumentException("Conversation ID is required.", nameof(conversationId)) : conversationId;
    private readonly string _defaultNamespaceKey = string.IsNullOrWhiteSpace(defaultNamespaceKey) ? "artifacts" : defaultNamespaceKey;

    public IReadOnlyDictionary<string, byte[]> List(string namespaceKey)
    {
        var effectiveNamespace = string.IsNullOrWhiteSpace(namespaceKey) ? _defaultNamespaceKey : namespaceKey;
        using var db = _dbFactory.CreateDbContext();

        return db.MemoryFiles
            .AsNoTracking()
            .Where(x => x.ConversationId == _conversationId && x.NamespaceKey == effectiveNamespace)
            .OrderBy(x => x.Path)
            .ToDictionary(x => x.Path, x => x.Content.ToArray(), StringComparer.Ordinal);
    }

    public byte[]? Get(string namespaceKey, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var effectiveNamespace = string.IsNullOrWhiteSpace(namespaceKey) ? _defaultNamespaceKey : namespaceKey;
        using var db = _dbFactory.CreateDbContext();

        var row = db.MemoryFiles
            .AsNoTracking()
            .SingleOrDefault(x => x.ConversationId == _conversationId && x.NamespaceKey == effectiveNamespace && x.Path == key);

        return row is null ? null : row.Content.ToArray();
    }

    public void Put(string namespaceKey, string key, byte[] value)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("File path is required.", nameof(key));

        var effectiveNamespace = string.IsNullOrWhiteSpace(namespaceKey) ? _defaultNamespaceKey : namespaceKey;
        using var db = _dbFactory.CreateDbContext();

        var row = db.MemoryFiles
            .SingleOrDefault(x => x.ConversationId == _conversationId && x.NamespaceKey == effectiveNamespace && x.Path == key);

        if (row is null)
        {
            db.MemoryFiles.Add(new ChatMemoryFileEntity
            {
                ConversationId = _conversationId,
                NamespaceKey = effectiveNamespace,
                Path = key,
                Content = value.ToArray(),
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }
        else
        {
            row.Content = value.ToArray();
            row.UpdatedAt = DateTimeOffset.UtcNow;
        }

        db.SaveChanges();
    }

    public bool Remove(string namespaceKey, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;

        var effectiveNamespace = string.IsNullOrWhiteSpace(namespaceKey) ? _defaultNamespaceKey : namespaceKey;
        using var db = _dbFactory.CreateDbContext();

        var row = db.MemoryFiles
            .SingleOrDefault(x => x.ConversationId == _conversationId && x.NamespaceKey == effectiveNamespace && x.Path == key);

        if (row is null) return false;

        db.MemoryFiles.Remove(row);
        db.SaveChanges();
        return true;
    }
}
