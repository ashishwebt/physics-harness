namespace DeepHarness.Backend;

/// <summary>Small namespaced key/value file store. Implementations can replace the in-memory store for persistence.</summary>
public interface IFileStore
{
    IReadOnlyDictionary<string, byte[]> List(string namespaceKey);
    byte[]? Get(string namespaceKey, string key);
    void Put(string namespaceKey, string key, byte[] value);
    bool Remove(string namespaceKey, string key);
}
