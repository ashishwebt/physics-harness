namespace DeepHarness.Backend;

/// <summary>Small namespaced key/value file store. Implementations can replace the in-memory store for persistence.</summary>
public interface IFileStore
{
    IReadOnlyDictionary<string, byte[]> List(string namespaceKey);
    byte[]? Get(string namespaceKey, string key);
    void Put(string namespaceKey, string key, byte[] value);
    bool Remove(string namespaceKey, string key);
}

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

/// <summary>
/// Stores virtual files in an <see cref="IFileStore"/> namespace instead of the local filesystem.
/// Paths are keys and support nested directories, edits, search, and batch transfers.
/// </summary>
public sealed class StoreBackend(IFileStore store, string namespaceKey = "artifacts") : IBackend
{
    public IFileStore Store { get; } = store;
    public string Namespace { get; } = namespaceKey;

    public IReadOnlyList<FileInfoEntry> List(string path)
    {
        var prefix = VirtualPath.Normalize(path).TrimEnd('/') + "/";
        var files = Store.List(Namespace);
        var entries = new Dictionary<string, FileInfoEntry>(StringComparer.Ordinal);
        foreach (var (key, content) in files)
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var remainder = key[prefix.Length..];
            var slash = remainder.IndexOf('/');
            if (slash >= 0)
            {
                var directory = prefix + remainder[..slash] + "/";
                entries[directory] = new(directory, true);
            }
            else entries[key] = new(key, false, content.Length);
        }
        return entries.Values.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
    }

    public BackendReadResult Read(string path, int offset = 0, int limit = 2000)
    {
        var normalized = VirtualPath.Normalize(path);
        var data = Store.Get(Namespace, normalized);
        if (data is null) return new($"File '{normalized}' not found", null);
        string content;
        try { content = new System.Text.UTF8Encoding(false, true).GetString(data); }
        catch (System.Text.DecoderFallbackException) { return new($"File '{normalized}' is not UTF-8 text", null); }
        if (offset < 0 || limit < 0) return new("offset and limit must be non-negative", null);
        var lines = content.Split('\n');
        var selected = lines.Skip(Math.Min(offset, lines.Length)).Take(limit).Select(line => line.TrimEnd('\r')).ToArray();
        var start = Math.Min(offset, lines.Length);
        return new(null, string.Join(Environment.NewLine, selected), lines.Length,
            selected.Length == 0 ? null : start + 1, selected.Length == 0 ? null : start + selected.Length,
            start + selected.Length < lines.Length ? start + selected.Length : null);
    }

    public BackendWriteResult Write(string path, string content)
    {
        var normalized = VirtualPath.Normalize(path);
        Store.Put(Namespace, normalized, System.Text.Encoding.UTF8.GetBytes(content));
        return new(null, normalized);
    }

    public BackendWriteResult Edit(string path, string oldText, string newText, bool replaceAll = false)
    {
        var normalized = VirtualPath.Normalize(path);
        var data = Store.Get(Namespace, normalized);
        if (data is null) return new($"File '{normalized}' not found", null);
        string content;
        try { content = new System.Text.UTF8Encoding(false, true).GetString(data); }
        catch (System.Text.DecoderFallbackException) { return new($"File '{normalized}' is not UTF-8 text", null); }
        var count = content.Split(oldText, StringSplitOptions.None).Length - 1;
        if (oldText.Length == 0 || count == 0) return new("old text was not found", null);
        if (!replaceAll && count != 1) return new($"old text occurs {count} times; provide a unique string or enable replaceAll", null);
        Store.Put(Namespace, normalized, System.Text.Encoding.UTF8.GetBytes(content.Replace(oldText, newText, StringComparison.Ordinal)));
        return new(null, normalized, replaceAll ? count : 1);
    }

    public BackendWriteResult Delete(string path)
    {
        var normalized = VirtualPath.Normalize(path);
        var keys = Store.List(Namespace).Keys.Where(key => key == normalized || key.StartsWith(normalized.TrimEnd('/') + "/", StringComparison.Ordinal)).ToArray();
        if (keys.Length == 0) return new($"File '{normalized}' not found", null);
        foreach (var key in keys) Store.Remove(Namespace, key);
        return new(null, normalized);
    }

    public IReadOnlyList<FileInfoEntry> Glob(string pattern, string? path = null)
    {
        var prefix = path is null ? "/" : VirtualPath.Normalize(path).TrimEnd('/') + "/";
        var regex = FilesystemBackend.GlobRegex(pattern);
        var matchBasename = !pattern.StartsWith('/') && !pattern.Contains('/') && !pattern.Contains('\\');
        return Store.List(Namespace).Keys.Where(key => key.StartsWith(prefix, StringComparison.Ordinal) &&
                regex.IsMatch(matchBasename ? System.IO.Path.GetFileName(key) : key[prefix.Length..]))
            .OrderBy(key => key, StringComparer.Ordinal).Select(key => new FileInfoEntry(key, false,
                Store.Get(Namespace, key)?.Length ?? 0)).ToArray();
    }

    public IReadOnlyList<GrepMatch> Grep(string pattern, string? path = null, string? glob = null, int? maxCount = null)
    {
        if (maxCount is <= 0) return [];
        var prefix = path is null ? "/" : VirtualPath.Normalize(path).TrimEnd('/') + "/";
        var regex = glob is null ? null : FilesystemBackend.GlobRegex(glob);
        var matchBasename = glob is not null && !glob.Contains('/') && !glob.Contains('\\');
        var results = new List<GrepMatch>();
        foreach (var (key, data) in Store.List(Namespace).OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!key.StartsWith(prefix, StringComparison.Ordinal) || (regex is not null &&
                !regex.IsMatch(matchBasename ? System.IO.Path.GetFileName(key) : key[prefix.Length..]))) continue;
            string content;
            try { content = new System.Text.UTF8Encoding(false, true).GetString(data); }
            catch (System.Text.DecoderFallbackException) { continue; }
            var lines = content.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                if (line.Contains(pattern, StringComparison.Ordinal)) results.Add(new(key, i + 1, line));
                if (maxCount is not null && results.Count >= maxCount) return results;
            }
        }
        return results;
    }

    public IReadOnlyList<FileDownloadResult> DownloadFiles(IEnumerable<string> paths) => paths.Select(path =>
    {
        var value = Store.Get(Namespace, VirtualPath.Normalize(path));
        return value is null ? new FileDownloadResult(path, null, "file_not_found") : new FileDownloadResult(path, value, null);
    }).ToArray();

    public IReadOnlyList<FileUploadResult> UploadFiles(IEnumerable<UploadedFile> files) => files.Select(file =>
    {
        Store.Put(Namespace, VirtualPath.Normalize(file.Path), file.Content);
        return new FileUploadResult(file.Path, null);
    }).ToArray();
}
