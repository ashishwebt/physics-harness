using FileBackendMaker;

var app = new PdfMarkdownCli();
return app.Run(args);

internal sealed class PdfMarkdownCli
{
    public int Run(string[] args)
    {
        if (args.Any(arg => arg is "--help" or "-h" or "/?"))
        {
            PrintUsage();
            return 0;
        }

        var inputDirectory = GetArgument(args, "--input", "-i") ?? ResolveDefaultInputDirectory();
        var outputDirectory = GetArgument(args, "--output", "-o") ?? ResolveDefaultOutputDirectory(inputDirectory);
        var recursive = !HasArgument(args, "--non-recursive") && !HasArgument(args, "--no-recursive");

        try
        {
            var convertedCount = PdfToMarkdownConverter.ConvertDirectory(inputDirectory, outputDirectory, recursive);
            Console.WriteLine($"Converted {convertedCount} PDF file(s) to Markdown.");
            Console.WriteLine($"Input: {inputDirectory}");
            Console.WriteLine($"Output: {outputDirectory}");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string? GetArgument(string[] args, params string[] names)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (names.Contains(arg, StringComparer.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
    }

    private static bool HasArgument(string[] args, params string[] names)
    {
        return args.Any(arg => names.Contains(arg, StringComparer.OrdinalIgnoreCase));
    }

    private static string ResolveDefaultInputDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.CurrentDirectory, "FileBackendMaker", "Files"),
            Path.Combine(Environment.CurrentDirectory, "Files"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Files"),
            Path.Combine(AppContext.BaseDirectory, "Files")
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var defaultPath = Path.Combine(Environment.CurrentDirectory, "FileBackendMaker", "Files");
        Directory.CreateDirectory(defaultPath);
        return Path.GetFullPath(defaultPath);
    }

    private static string ResolveDefaultOutputDirectory(string inputDirectory)
    {
        var inputRoot = Path.GetDirectoryName(inputDirectory) ?? inputDirectory;
        var outputPath = Path.Combine(inputRoot, "Markdown");
        Directory.CreateDirectory(outputPath);
        return Path.GetFullPath(outputPath);
    }

    private static void PrintUsage()
    {
        Console.WriteLine("PDF to Markdown converter");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project FileBackendMaker -- --input <folder> --output <folder>");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --input, -i      folder containing PDF files (default: FileBackendMaker/Files)");
        Console.WriteLine("  --output, -o     folder where markdown files are written (default: input-folder/Markdown)");
        Console.WriteLine("  --non-recursive  process only the top-level PDFs in the input folder");
    }
}
