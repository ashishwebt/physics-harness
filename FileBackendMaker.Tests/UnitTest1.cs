using FileBackendMaker;

namespace FileBackendMaker.Tests;

public class UnitTest1
{
    [Fact]
    public void Build_FormatsTitleAndParagraphs()
    {
        var markdown = MarkdownDocumentBuilder.Build("Sample Title\n\nThis is the first paragraph.\n\nThis is the second.");

        Assert.Equal("# Sample Title\n\nThis is the first paragraph.\n\nThis is the second.", markdown);
    }
}
