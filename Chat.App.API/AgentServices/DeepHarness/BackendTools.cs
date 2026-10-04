using System.ComponentModel;
using DeepHarness.Backend;
using Microsoft.Extensions.AI;

namespace DeepHarness;

/// <summary>Agent-callable adapters over an <see cref="IBackend"/>.</summary>
internal sealed class BackendTools(IBackend backend, IChatClient chatClient)
{
    private readonly List<string> _todos = [];

    [Description("List files and directories at an absolute virtual path. Use `/` for the workspace root.")]
    public string List([Description("Absolute virtual directory path, for example `/` or `/artifacts/`.")] string path = "/") =>
        Safe(() => string.Join('\n', backend.List(path).Select(entry => $"{entry.Path} ({entry.Size} bytes)")));

    [Description("Read a UTF-8 text file. Use offset and limit to page through large files.")]
    public string Read([Description("Absolute virtual file path.")] string filePath, [Description("Zero-based line offset.")] int offset = 0, [Description("Maximum number of lines to return.")] int limit = 2000) => Safe(() =>
    {
        var result = backend.Read(filePath, offset, limit);
        if (result.Error is not null) return $"Error: {result.Error}";
        var header = result.StartLine is null ? "" : $"Lines {result.StartLine}-{result.EndLine} of {result.TotalLines}:\n";
        var next = result.NextOffset is null ? "" : $"\n[More lines available; next offset: {result.NextOffset}]";
        return header + result.Content + next;
    });

    [Description("Write or overwrite a UTF-8 text file at an absolute virtual path.")]
    public string Write([Description("Absolute virtual file path.")] string filePath, [Description("Complete text content to write.")] string content) => Safe(() => Format(backend.Write(filePath, content)));

    [Description("Replace exact text in a file. By default the old text must occur exactly once.")]
    public string Edit([Description("Absolute virtual file path.")] string filePath, [Description("Exact existing text to replace.")] string oldText, [Description("Replacement text.")] string newText, [Description("Replace every occurrence instead of requiring one unique occurrence.")] bool replaceAll = false) => Safe(() => Format(backend.Edit(filePath, oldText, newText, replaceAll)));

    [Description("Delete a file or directory at an absolute virtual path.")]
    public string Delete([Description("Absolute virtual file or directory path.")] string filePath) => Safe(() => Format(backend.Delete(filePath)));

    [Description("Find files by glob pattern, for example `**/*.md`.")]
    public string Glob([Description("Glob pattern to match against virtual paths.")] string pattern, [Description("Optional absolute directory to search under.")] string? path = null) => Safe(() => string.Join('\n', backend.Glob(pattern, path).Select(entry => entry.Path)));

    [Description("Search file text for a literal string and return matching lines.")]
    public string Grep([Description("Literal text to search for.")] string pattern, [Description("Optional absolute directory to search under.")] string? path = null, [Description("Optional file glob filter such as `**/*.cs`.")] string? glob = null, [Description("Optional maximum number of matching lines.")] int? maxCount = null) => Safe(() => string.Join('\n', backend.Grep(pattern, path, glob, maxCount).Select(match => $"{match.Path}:{match.Line}: {match.Text}")));

    [Description("Replace the current task plan with ordered TODO items. Include completion state in each item, such as [ ] or [x].")]
    public string UpdateTodos([Description("Complete current TODO list, one item per line.")] string todos) => Safe(() =>
    {
        _todos.Clear();
        _todos.AddRange(todos.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return _todos.Count == 0 ? "Todo list cleared." : string.Join('\n', _todos);
    });

    [Description("Delegate one focused, read-only research task to an isolated subagent. The subagent cannot delegate further.")]
    public async Task<string> Task([Description("A focused research task for the subagent.")] string task)
    {
        var subagent = DeepAgentFactory.CreateResearchAgent(backend, chatClient);
        var response = await subagent.RunAsync(task, await subagent.CreateSessionAsync());
        return response.ToString();
    }

    private static string Safe(Func<string> operation)
    {
        try { return operation(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { return $"Error: {ex.Message} Check the virtual path and backend access, then retry."; }
    }

    private static string Format(BackendWriteResult result) => result.Error is null ? $"Success: {result.Path}" : $"Error: {result.Error}";
}
