using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;

namespace FileBackendMaker;

public static class MarkdownDocumentBuilder
{
    public static string Build(string pdfText)
    {
        ArgumentNullException.ThrowIfNull(pdfText);

        var normalized = pdfText
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace('\u00A0', ' ')
            .Replace('\t', ' ')
            .Trim();

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var blocks = Regex.Split(normalized, @"\n\s*\n+")
            .Select(block => Regex.Replace(block, @"\s+", " ").Trim())
            .Where(block => !string.IsNullOrWhiteSpace(block))
            .ToList();

        if (blocks.Count == 0)
        {
            return string.Empty;
        }

        var title = blocks[0];
        var bodyBlocks = blocks.Skip(1).Where(block => !string.IsNullOrWhiteSpace(block)).ToList();

        if (bodyBlocks.Count == 0)
        {
            return $"# {title}";
        }

        var body = string.Join("\n\n", bodyBlocks);
        return $"# {title}\n\n{body}";
    }
}

public static class PdfToMarkdownConverter
{
    public static int ConvertDirectory(string inputDirectory, string outputDirectory, bool recursive = true)
    {
        if (!Directory.Exists(inputDirectory))
        {
            throw new DirectoryNotFoundException($"Input directory '{inputDirectory}' does not exist.");
        }

        var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var pdfFiles = Directory
            .EnumerateFiles(inputDirectory, "*.pdf", searchOption)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        Directory.CreateDirectory(outputDirectory);

        foreach (var pdfFile in pdfFiles)
        {
            var relativePath = Path.GetRelativePath(inputDirectory, pdfFile);
            var outputPath = Path.Combine(outputDirectory, Path.ChangeExtension(relativePath, ".md"));
            var outputFolder = Path.GetDirectoryName(outputPath);

            if (!string.IsNullOrEmpty(outputFolder))
            {
                Directory.CreateDirectory(outputFolder);
            }

            File.WriteAllText(outputPath, ConvertFile(pdfFile), Encoding.UTF8);
        }

        return pdfFiles.Count;
    }

    public static string ConvertFile(string pdfPath)
    {
        if (!File.Exists(pdfPath))
        {
            throw new FileNotFoundException($"PDF file '{pdfPath}' was not found.", pdfPath);
        }

        using var stream = File.OpenRead(pdfPath);
        using var document = PdfDocument.Open(stream);

        var builder = new StringBuilder();

        foreach (var page in document.GetPages())
        {
            var words = page.GetWords();
            var pageText = string.Join(" ", words.Select(word => word.Text));

            if (!string.IsNullOrWhiteSpace(pageText))
            {
                builder.AppendLine(pageText);
                builder.AppendLine();
            }
        }

        var text = builder.ToString();
        return MarkdownDocumentBuilder.Build(text);
    }
}
