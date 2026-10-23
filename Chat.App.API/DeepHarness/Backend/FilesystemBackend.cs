using System.Text;
using System.Text.RegularExpressions;

namespace DeepHarness.Backend;

/// <summary>
/// Reads and writes files beneath a configured directory. Virtual mode presents stable absolute paths
/// such as <c>/docs/readme.md</c> while keeping all physical I/O under the configured root.
/// </summary>
public sealed class FilesystemBackend : IBackend
{
    private readonly string _root;

    /// <param name="rootDirectory">Physical directory used as the virtual root.</param>
    public FilesystemBackend(string rootDirectory)
    {
        _root = System.IO.Path.GetFullPath(rootDirectory);
        Directory.CreateDirectory(_root);
    }

    public IReadOnlyList<FileInfoEntry> List(string path)
    {
        var physical = VirtualPath.UnderRoot(_root, path);
        if (!Directory.Exists(physical)) throw new DirectoryNotFoundException($"Directory not found: {path}");
        return Directory.EnumerateFileSystemEntries(physical).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).Select(p =>
        {
            var isDirectory = Directory.Exists(p);
            var info = isDirectory ? (FileSystemInfo)new DirectoryInfo(p) : new FileInfo(p);
            return new FileInfoEntry(VirtualPath.FromPhysical(_root, p) + (isDirectory ? "/" : ""), isDirectory,
                isDirectory ? 0 : ((FileInfo)info).Length, info.LastWriteTimeUtc);
        }).ToArray();
    }

    public BackendReadResult Read(string path, int offset = 0, int limit = 2000)
    {
        if (offset < 0) return new("offset must be non-negative", null);
        if (limit < 0) return new("limit must be non-negative", null);
        var physical = VirtualPath.UnderRoot(_root, path);
        if (!File.Exists(physical)) return new($"File '{path}' not found", null);
        var lines = File.ReadAllLines(physical);
        var take = Math.Max(0, Math.Min(limit, lines.Length - Math.Min(offset, lines.Length)));
        var start = Math.Min(offset, lines.Length);
        var selected = lines.Skip(start).Take(take).ToArray();
        return new(null, string.Join(Environment.NewLine, selected), lines.Length,
            selected.Length == 0 ? null : start + 1, selected.Length == 0 ? null : start + selected.Length,
            start + selected.Length < lines.Length ? start + selected.Length : null);
    }

    public BackendWriteResult Write(string path, string content)
    {
        var physical = VirtualPath.UnderRoot(_root, path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(physical)!);
        File.WriteAllText(physical, content, new UTF8Encoding(false));
        return new(null, VirtualPath.Normalize(path));
    }

    public BackendWriteResult Edit(string path, string oldText, string newText, bool replaceAll = false)
    {
        var physical = VirtualPath.UnderRoot(_root, path);
        if (!File.Exists(physical)) return new($"File '{path}' not found", null);
        var content = File.ReadAllText(physical);
        var occurrences = CountOccurrences(content, oldText);
        if (occurrences == 0) return new("old text was not found", null);
        if (!replaceAll && occurrences != 1) return new($"old text occurs {occurrences} times; provide a unique string or enable replaceAll", null);
        File.WriteAllText(physical, replaceAll ? content.Replace(oldText, newText, StringComparison.Ordinal) : content.Replace(oldText, newText, StringComparison.Ordinal), new UTF8Encoding(false));
        return new(null, VirtualPath.Normalize(path), replaceAll ? occurrences : 1);
    }

    public BackendWriteResult Delete(string path)
    {
        var normalized = VirtualPath.Normalize(path);
        var physical = Path.GetFullPath(Path.Combine(_root, normalized.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        VirtualPath.EnsureContained(_root, physical, path);
        var info = Directory.Exists(physical) ? (FileSystemInfo)new DirectoryInfo(physical) : new FileInfo(physical);
        if (!info.Exists) return new($"File or directory '{path}' not found", null);
        if (info is DirectoryInfo) Directory.Delete(physical, recursive: info.LinkTarget is null);
        else File.Delete(physical);
        return new(null, VirtualPath.Normalize(path));
    }

    public IReadOnlyList<FileInfoEntry> Glob(string pattern, string? path = null)
    {
        var root = VirtualPath.UnderRoot(_root, path ?? "/");
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Directory not found: {path ?? "/"}");
        var regex = GlobRegex(pattern);
        var matchBasename = !pattern.StartsWith('/') && !pattern.Contains('/') && !pattern.Contains('\\');
        return EnumerateFiles(root)
            .Select(p => new FileInfo(p))
            .Where(info => regex.IsMatch(matchBasename ? info.Name : Path.GetRelativePath(root, info.FullName).Replace('\\', '/')))
            .Select(info => new FileInfoEntry(VirtualPath.FromPhysical(_root, info.FullName), false, info.Length, info.LastWriteTimeUtc))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<GrepMatch> Grep(string pattern, string? path = null, string? glob = null, int? maxCount = null)
    {
        if (maxCount is <= 0) return [];
        var root = VirtualPath.UnderRoot(_root, path ?? "/");
        var regex = glob is null ? null : GlobRegex(glob);
        var matchBasename = glob is not null && !glob.Contains('/') && !glob.Contains('\\');
        var matches = new List<GrepMatch>();
        foreach (var file in Directory.Exists(root) ? EnumerateFiles(root) : File.Exists(root) ? [root] : [])
        {
            if (regex is not null && !regex.IsMatch(matchBasename ? Path.GetFileName(file) : Path.GetRelativePath(root, file).Replace('\\', '/'))) continue;
            try
            {
                var lines = File.ReadLines(file);
                var lineNo = 0;
                foreach (var line in lines)
                {
                    lineNo++;
                    if (line.Contains(pattern, StringComparison.Ordinal)) matches.Add(new(VirtualPath.FromPhysical(_root, file), lineNo, line));
                    if (maxCount is not null && matches.Count >= maxCount) return matches;
                }
            }
            catch (DecoderFallbackException) { /* Binary file: skip it. */ }
        }
        return matches;
    }

    public IReadOnlyList<FileDownloadResult> DownloadFiles(IEnumerable<string> paths) => paths.Select(path =>
    {
        var physical = VirtualPath.UnderRoot(_root, path);
        if (Directory.Exists(physical)) return new FileDownloadResult(path, null, "is_directory");
        return File.Exists(physical) ? new FileDownloadResult(path, File.ReadAllBytes(physical), null) : new FileDownloadResult(path, null, "file_not_found");
    }).ToArray();

    public IReadOnlyList<FileUploadResult> UploadFiles(IEnumerable<UploadedFile> files) => files.Select(file =>
    {
        var physical = VirtualPath.UnderRoot(_root, file.Path);
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        File.WriteAllBytes(physical, file.Content);
        return new FileUploadResult(file.Path, null);
    }).ToArray();

    internal static Regex GlobRegex(string pattern)
    {
        var normalized = pattern.Replace('\\', '/').TrimStart('/');
        var builder = new StringBuilder("^");
        for (var i = 0; i < normalized.Length; i++)
        {
            if (normalized[i] == '*')
            {
                if (i + 1 < normalized.Length && normalized[i + 1] == '*')
                {
                    i++;
                    if (i + 1 < normalized.Length && normalized[i + 1] == '/') { builder.Append("(?:.*/)?"); i++; }
                    else builder.Append(".*");
                }
                else builder.Append("[^/]*");
            }
            else if (normalized[i] == '?') builder.Append("[^/]");
            else builder.Append(Regex.Escape(normalized[i].ToString()));
        }
        return new Regex(builder.Append('$').ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static int CountOccurrences(string source, string value)
    {
        if (value.Length == 0) return 0;
        var count = 0;
        for (var start = 0; (start = source.IndexOf(value, start, StringComparison.Ordinal)) >= 0; start += value.Length) count++;
        return count;
    }

    private static IEnumerable<string> EnumerateFiles(string root) => Directory.EnumerateFiles(root, "*", new EnumerationOptions
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true
    });
}
