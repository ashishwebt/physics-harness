namespace DeepHarness.Backend;

/// <summary>
/// Routes file operations to a backend by longest matching virtual path prefix.
/// Unmatched paths use the default backend; root listings also expose configured route directories.
/// </summary>
public sealed class CompositeBackend : IBackend
{
    private readonly KeyValuePair<string, IBackend>[] _routes;

    /// <param name="defaultBackend">Backend used for paths without a configured route.</param>
    /// <param name="routes">Virtual path prefixes mapped to backends, for example <c>/memories/</c>.</param>
    public CompositeBackend(IBackend defaultBackend, IReadOnlyDictionary<string, IBackend> routes)
    {
        Default = defaultBackend;
        _routes = routes.Select(pair => new KeyValuePair<string, IBackend>(NormalizePrefix(pair.Key), pair.Value))
            .OrderByDescending(pair => pair.Key.Length).ToArray();
    }

    public IBackend Default { get; }

    public IReadOnlyList<FileInfoEntry> List(string path)
    {
        var normalized = VirtualPath.Normalize(path);
        if (normalized == "/")
        {
            var entries = Default.List("/").ToDictionary(e => e.Path, StringComparer.Ordinal);
            foreach (var route in _routes)
            {
                var routeName = "/" + route.Key.Trim('/').Split('/')[0] + "/";
                entries.TryAdd(routeName, new(routeName, true));
            }
            return entries.Values.OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
        }
        var (backend, innerPath, prefix) = Route(normalized);
        var listed = backend.List(innerPath);
        return prefix is null ? listed : listed.Select(entry => entry with { Path = prefix.TrimEnd('/') + entry.Path }).ToArray();
    }

    public BackendReadResult Read(string path, int offset = 0, int limit = 2000) { var (b, p, _) = Route(path); return b.Read(p, offset, limit); }
    public BackendWriteResult Write(string path, string content) { var (b, p, prefix) = Route(path); var result = b.Write(p, content); return result.Error is null && prefix is not null ? result with { Path = prefix.TrimEnd('/') + result.Path } : result; }
    public BackendWriteResult Edit(string path, string oldText, string newText, bool replaceAll = false) { var (b, p, prefix) = Route(path); var result = b.Edit(p, oldText, newText, replaceAll); return result.Error is null && prefix is not null ? result with { Path = prefix.TrimEnd('/') + result.Path } : result; }
    public BackendWriteResult Delete(string path) { var (b, p, prefix) = Route(path); var result = b.Delete(p); return result.Error is null && prefix is not null ? result with { Path = prefix.TrimEnd('/') + result.Path } : result; }

    public IReadOnlyList<FileInfoEntry> Glob(string pattern, string? path = null)
    {
        if (path is not null)
        {
            var (backend, inner, prefix) = Route(path);
            var matches = backend.Glob(StripRoutePattern(pattern, prefix) ?? pattern, inner);
            return prefix is null ? matches : matches.Select(entry => entry with { Path = prefix.TrimEnd('/') + entry.Path }).ToArray();
        }
        var results = Default.Glob(pattern).ToList();
        foreach (var (prefix, backend) in _routes)
        {
            var routedPattern = RoutePattern(pattern, prefix);
            if (routedPattern is null) continue;
            results.AddRange(backend.Glob(routedPattern).Select(entry => entry with { Path = prefix.TrimEnd('/') + entry.Path }));
        }
        return results.GroupBy(e => e.Path, StringComparer.Ordinal).Select(g => g.First()).OrderBy(e => e.Path, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<GrepMatch> Grep(string pattern, string? path = null, string? glob = null, int? maxCount = null)
    {
        if (path is not null)
        {
            var (backend, inner, prefix) = Route(path);
            var matches = backend.Grep(pattern, inner, StripRoutePattern(glob, prefix), maxCount);
            return prefix is null ? matches : matches.Select(m => m with { Path = prefix.TrimEnd('/') + m.Path }).ToArray();
        }
        var results = Default.Grep(pattern, null, glob, maxCount).ToList();
        foreach (var (prefix, backend) in _routes)
        {
            var routedGlob = RoutePattern(glob, prefix);
            if (glob?.StartsWith('/') == true && routedGlob is null) continue;
            int? remaining = maxCount is null ? null : Math.Max(0, maxCount.Value - results.Count);
            if (remaining == 0) break;
            results.AddRange(backend.Grep(pattern, null, routedGlob, remaining).Select(m => m with { Path = prefix.TrimEnd('/') + m.Path }));
        }
        return results;
    }

    public IReadOnlyList<FileDownloadResult> DownloadFiles(IEnumerable<string> paths) => paths.Select(path =>
    {
        var (backend, inner, _) = Route(path);
        return backend.DownloadFiles([inner]).Single();
    }).ToArray();

    public IReadOnlyList<FileUploadResult> UploadFiles(IEnumerable<UploadedFile> files) => files.Select(file =>
    {
        var (backend, inner, _) = Route(file.Path);
        var result = backend.UploadFiles([file with { Path = inner }]).Single();
        return result with { Path = file.Path };
    }).ToArray();

    private (IBackend Backend, string InnerPath, string? Prefix) Route(string path)
    {
        var normalized = VirtualPath.Normalize(path);
        foreach (var (prefix, backend) in _routes)
            if (normalized.StartsWith(prefix, StringComparison.Ordinal))
                return (backend, "/" + normalized[prefix.Length..], prefix);
        return (Default, normalized, null);
    }

    private static string NormalizePrefix(string prefix)
    {
        var normalized = VirtualPath.Normalize(prefix);
        return normalized.TrimEnd('/') + "/";
    }

    private static string? StripRoutePattern(string? pattern, string? prefix)
    {
        if (pattern is null || prefix is null) return pattern;
        var barePattern = pattern.TrimStart('/');
        var barePrefix = prefix.Trim('/');
        return barePattern.StartsWith(barePrefix + "/", StringComparison.Ordinal) ? "/" + barePattern[(barePrefix.Length + 1)..] : pattern;
    }

    private static string? RoutePattern(string? pattern, string prefix)
    {
        if (pattern is null) return null;
        var rewritten = StripRoutePattern(pattern, prefix);
        return rewritten == pattern && pattern.StartsWith('/') ? null : rewritten;
    }
}
