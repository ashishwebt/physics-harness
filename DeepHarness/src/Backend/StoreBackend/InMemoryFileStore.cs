namespace DeepHarness.Backend;

/// <summary>Thread-safe process-local store matching the lifecycle of LangGraph's InMemoryStore.</summary>
public sealed class InMemoryFileStore : IFileStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Dictionary<string, byte[]>> _namespaces = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> List(string namespaceKey)
    {
        lock (_gate) return _namespaces.TryGetValue(namespaceKey, out var entries)
            ? entries.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal)
            : new Dictionary<string, byte[]>(StringComparer.Ordinal);
    }

    public byte[]? Get(string namespaceKey, string key)
    {
        lock (_gate) return _namespaces.TryGetValue(namespaceKey, out var entries) && entries.TryGetValue(key, out var value) ? value.ToArray() : null;
    }

    public void Put(string namespaceKey, string key, byte[] value)
    {
        lock (_gate)
        {
            if (!_namespaces.TryGetValue(namespaceKey, out var entries)) _namespaces[namespaceKey] = entries = new(StringComparer.Ordinal);
            entries[key] = value.ToArray();
        }
    }

    public bool Remove(string namespaceKey, string key)
    {
        lock (_gate) return _namespaces.TryGetValue(namespaceKey, out var entries) && entries.Remove(key);
    }
}
