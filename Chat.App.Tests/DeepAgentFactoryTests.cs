using DeepHarness;
using DeepHarness.Backend;
using Xunit;

namespace Chat.App.Tests;

public class DeepAgentFactoryTests
{
    [Fact]
    public void BuildSystemPrompt_IncludesGenericToolGuidanceAndOptionalSpecificPrompt()
    {
        var prompt = DeepAgentFactory.BuildSystemPrompt("Focus on code review tasks.");

        Assert.Contains("tool", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Focus on code review tasks.", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void DefaultAgentName_IsUsedWhenNoNameIsProvided()
    {
        Assert.Equal("DeepAgent", DeepAgentFactory.DefaultAgentName);
    }
}

public class FilesystemBackendTests
{
    [Fact]
    public void WriteReadAndEdit_ApplyVirtualPathOperations()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deepagent-backend-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var backend = new FilesystemBackend(root);

            var write = backend.Write("/docs/notes.txt", "alpha\nbeta\n");
            Assert.Null(write.Error);

            var readBefore = backend.Read("/docs/notes.txt");
            Assert.Null(readBefore.Error);
            Assert.Equal($"alpha{Environment.NewLine}beta", readBefore.Content);

            var edit = backend.Edit("/docs/notes.txt", "alpha", "gamma");
            Assert.Null(edit.Error);

            var readAfter = backend.Read("/docs/notes.txt");
            Assert.Null(readAfter.Error);
            Assert.Equal($"gamma{Environment.NewLine}beta", readAfter.Content);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Edit_WithDuplicateMatches_RequiresUniqueReplacementOrReplaceAll()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deepagent-backend-duplicate-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var backend = new FilesystemBackend(root);
            backend.Write("/dup.txt", "one\none");

            var ambiguous = backend.Edit("/dup.txt", "one", "two");
            Assert.Equal("old text occurs 2 times; provide a unique string or enable replaceAll", ambiguous.Error);

            var replaced = backend.Edit("/dup.txt", "one", "two", replaceAll: true);
            Assert.Null(replaced.Error);
            Assert.Equal(2, replaced.Occurrences);

            var read = backend.Read("/dup.txt");
            Assert.Equal($"two{Environment.NewLine}two", read.Content);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
