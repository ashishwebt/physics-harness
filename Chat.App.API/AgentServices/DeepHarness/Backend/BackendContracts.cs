namespace DeepHarness.Backend;

/// <summary>Common file operations exposed by a DeepAgent backend.</summary>
public interface IBackend
{
    IReadOnlyList<FileInfoEntry> List(string path);
    BackendReadResult Read(string path, int offset = 0, int limit = 2000);
    BackendWriteResult Write(string path, string content);
    BackendWriteResult Edit(string path, string oldText, string newText, bool replaceAll = false);
    BackendWriteResult Delete(string path);
    IReadOnlyList<FileInfoEntry> Glob(string pattern, string? path = null);
    IReadOnlyList<GrepMatch> Grep(string pattern, string? path = null, string? glob = null, int? maxCount = null);
    IReadOnlyList<FileDownloadResult> DownloadFiles(IEnumerable<string> paths);
    IReadOnlyList<FileUploadResult> UploadFiles(IEnumerable<UploadedFile> files);
}

public sealed record FileInfoEntry(string Path, bool IsDirectory = false, long Size = 0, DateTimeOffset? ModifiedAt = null);
public sealed record BackendReadResult(string? Error, string? Content, int? TotalLines = null, int? StartLine = null, int? EndLine = null, int? NextOffset = null);
public sealed record BackendWriteResult(string? Error, string? Path, int? Occurrences = null);
public sealed record GrepMatch(string Path, int Line, string Text);
public sealed record FileDownloadResult(string Path, byte[]? Content, string? Error);
public sealed record FileUploadResult(string Path, string? Error);
public sealed record UploadedFile(string Path, byte[] Content);

/// <summary>Throws when a virtual path is not absolute, contains traversal, or escapes the configured root.</summary>
internal static class VirtualPath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith('/')) normalized = "/" + normalized;
        if (normalized.Split('/').Any(part => part is ".." or ".") || normalized.StartsWith("/~", StringComparison.Ordinal))
            throw new ArgumentException("Path traversal is not allowed.", nameof(path));
        return normalized;
    }

    public static string UnderRoot(string root, string path)
    {
        var normalized = Normalize(path);
        var fullRoot = System.IO.Path.GetFullPath(root);
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(fullRoot, normalized.TrimStart('/').Replace('/', System.IO.Path.DirectorySeparatorChar)));
        EnsureContained(fullRoot, fullPath, path);

        // Resolve every existing symlink in the path so a link inside the workspace
        // cannot redirect a read or write outside the configured root.
        var current = fullRoot;
        var relativePath = System.IO.Path.GetRelativePath(fullRoot, fullPath);
        foreach (var component in relativePath.Split(System.IO.Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = System.IO.Path.Combine(current, component);
            FileSystemInfo info = Directory.Exists(current) ? new DirectoryInfo(current) : new FileInfo(current);
            if (info.LinkTarget is null) continue;
            current = info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? current;
            EnsureContained(fullRoot, current, path);
        }
        return current;
    }

    public static void EnsureContained(string root, string fullPath, string requestedPath)
    {
        var relative = System.IO.Path.GetRelativePath(root, fullPath);
        if (relative == ".." || relative.StartsWith(".." + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal) || System.IO.Path.IsPathRooted(relative))
            throw new ArgumentException($"Path '{requestedPath}' escapes the backend root.", nameof(requestedPath));
    }

    public static string FromPhysical(string root, string path)
    {
        var relative = System.IO.Path.GetRelativePath(root, path).Replace('\\', '/');
        return relative == "." ? "/" : "/" + relative;
    }
}
