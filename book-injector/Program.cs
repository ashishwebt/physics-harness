// fetch-book.cs - downloads the OpenStax "Physics" (high school) textbook (CC BY 4.0)
// and converts it into clean markdown files, one per section, grouped by chapter.
//
// Usage (needs .NET 10 SDK), run from anywhere inside the repo:
//   dotnet run tools/fetch-book.cs
//
// Output goes to Chat.App.API/Physics. Add that folder to .gitignore so the book itself is never committed.

using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

const string ZipUrl = "https://codeload.github.com/openstax/osbooks-physics/zip/refs/heads/main";
const string SourcePage = "https://github.com/openstax/osbooks-physics";

// Find the repo root (the folder that contains Chat.App.API) by walking up from the current directory.
var repoRoot = new DirectoryInfo(Directory.GetCurrentDirectory());
while (repoRoot is not null && !Directory.Exists(Path.Combine(repoRoot.FullName, "Chat.App.API")))
    repoRoot = repoRoot.Parent;
if (repoRoot is null)
{
    Console.Error.WriteLine("Could not find a Chat.App.API folder. Run this from inside your repo.");
    return 1;
}
var outDir = Path.Combine(repoRoot.FullName, "Chat.App.API", "Physics");
var zipUrl = ZipUrl;

// 1) Download the repo zip (it includes images, so it is large - roughly 60 MB).
Console.WriteLine($"Downloading {zipUrl} ...");
using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
http.DefaultRequestHeaders.UserAgent.ParseAdd("physics-harness-book-fetcher/1.0");
var zipBytes = await http.GetByteArrayAsync(zipUrl);
Console.WriteLine($"Downloaded {zipBytes.Length / 1024 / 1024} MB");

// 2) Read only the XML we need (collection + modules). Images are skipped.
using var zip = new ZipArchive(new MemoryStream(zipBytes));
var collectionEntry = zip.Entries.FirstOrDefault(e => e.FullName.Contains("/collections/") && e.Name.EndsWith(".collection.xml"))
    ?? throw new InvalidOperationException("No *.collection.xml found in the archive.");

var modules = new Dictionary<string, XDocument>();
foreach (var entry in zip.Entries.Where(e => e.FullName.Contains("/modules/") && e.Name == "index.cnxml"))
{
    var id = entry.FullName.Split('/')[^2];
    await using var s = entry.Open();
    modules[id] = XDocument.Load(s);
}
XDocument collection;
await using (var cs = collectionEntry.Open()) collection = XDocument.Load(cs);

// 3) Walk the collection tree: top-level modules/subcollections = chapters.
var root = collection.Root!;
var meta = root.Elements().First(e => e.Name.LocalName == "metadata");
var bookTitle = meta.Elements().First(e => e.Name.LocalName == "title").Value.Trim();
var licenseEl = meta.Elements().First(e => e.Name.LocalName == "license");
var licenseUrl = (string?)licenseEl.Attribute("url") ?? "https://creativecommons.org/licenses/by/4.0/";
var authors = ((string?)root.Attribute("authors") ?? "").Replace(" ,", ",");

if (Directory.Exists(outDir)) Directory.Delete(outDir, recursive: true);
Directory.CreateDirectory(outDir);

var manifest = new List<(int Chapter, string Title, List<(string Title, string Path)> Sections)>();
var content = root.Elements().First(e => e.Name.LocalName == "content");
var chapterNo = 0;
var sectionCount = 0;

foreach (var node in content.Elements())
{
    chapterNo++;
    string chapterTitle;
    List<string> moduleIds;

    if (node.Name.LocalName == "subcollection")
    {
        chapterTitle = node.Elements().First(e => e.Name.LocalName == "title").Value.Trim();
        moduleIds = node.Descendants().Where(e => e.Name.LocalName == "module")
            .Select(e => (string)e.Attribute("document")!).ToList();
    }
    else // a loose module at top level (e.g. a preface)
    {
        var id = (string)node.Attribute("document")!;
        chapterTitle = modules.TryGetValue(id, out var d) ? Cnxml.TitleOf(d) : "Front matter";
        moduleIds = [id];
    }

    var chapterDir = Path.Combine(outDir, $"{chapterNo:00}-{Slug(chapterTitle)}");
    Directory.CreateDirectory(chapterDir);
    var files = new List<(string Title, string Path)>();
    var n = 0;

    foreach (var id in moduleIds)
    {
        if (!modules.TryGetValue(id, out var doc)) continue;
        n++;
        var md = Cnxml.ToMarkdown(doc);
        if (string.IsNullOrWhiteSpace(md)) continue;
        var title = Cnxml.TitleOf(doc);
        var fileName = $"{n:00}-{Slug(title)}.md";
        var relPath = Path.Combine(Path.GetFileName(chapterDir), fileName);
        File.WriteAllText(Path.Combine(outDir, relPath), md, new UTF8Encoding(false));
        files.Add((title, relPath.Replace('\\', '/')));
        sectionCount++;
    }
    manifest.Add((chapterNo, chapterTitle, files));
    Console.WriteLine($"  {chapterNo,2}. {chapterTitle} ({files.Count} sections)");
}

// 4) Index + attribution (needed for CC BY compliance).
var index = new StringBuilder($"# {bookTitle} - Table of Contents\n\n");
foreach (var ch in manifest)
{
    index.Append($"## {ch.Chapter}. {ch.Title}\n\n");
    foreach (var sec in ch.Sections) index.Append($"- [{sec.Title}]({sec.Path})\n");
    index.Append('\n');
}
File.WriteAllText(Path.Combine(outDir, "index.md"), index.ToString(), new UTF8Encoding(false));

File.WriteAllText(Path.Combine(outDir, "../", "ATTRIBUTION.md"), $"""
    # Attribution

    This folder(Physics) contains a markdown conversion of **{bookTitle}**, published by OpenStax.

    - Original source: {SourcePage}
    - Website: https://openstax.org
    - License: Creative Commons Attribution 4.0 International (CC BY 4.0) - {licenseUrl}
    - Contributors listed in the source metadata: {authors}
    - Changes made: converted from CNXML to markdown (MathML converted to LaTeX; images, teacher-only notes and answer-key
      commentary omitted).

    This content is NOT covered by the MIT license of the surrounding project. It stays under CC BY 4.0.
    Check https://openstax.org for the authoritative author and citation text for this book.
    """);

Console.WriteLine($"\nDone: {sectionCount} sections written to {outDir}");
return 0;

static string Slug(string s)
{
    var slug = Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "-").Trim('-');
    return slug.Length == 0 ? "untitled" : slug.Length > 60 ? slug[..60].TrimEnd('-') : slug;
}

// ---------------------------------------------------------------------------
// CNXML -> Markdown converter
// ---------------------------------------------------------------------------
static class Cnxml
{
    public static string TitleOf(XDocument d) =>
        Collapse(d.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == "title")?.Value ?? "Untitled");

    public static string ToMarkdown(XDocument doc)
    {
        var sb = new StringBuilder();
        sb.Append("# ").Append(TitleOf(doc)).Append("\n\n");
        var content = doc.Root!.Elements().FirstOrDefault(e => e.Name.LocalName == "content");
        if (content is null) return "";
        foreach (var child in content.Elements()) Block(child, sb, 2);
        return Regex.Replace(sb.ToString(), @"\n{3,}", "\n\n").Trim() + "\n";
    }

    static string Name(XElement e) => e.Name.LocalName;
    static bool HasClass(XElement e, string c) =>
        ((string?)e.Attribute("class") ?? "").Split(' ').Contains(c);
    static string Collapse(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    static void Block(XElement e, StringBuilder sb, int level)
    {
        if (HasClass(e, "os-teacher")) return;

        switch (Name(e))
        {
            case "title":
            case "label":
            case "metadata":
            case "commentary":
            case "caption":
            case "colspec":
            case "spanspec":
                return;

            case "section":
                {
                    var t = e.Elements().FirstOrDefault(x => Name(x) == "title");
                    if (t is not null) sb.Append(new string('#', System.Math.Min(level, 5))).Append(' ').Append(Inline(t)).Append("\n\n");
                    foreach (var c in e.Elements()) Block(c, sb, level + 1);
                    return;
                }

            case "para":
                {
                    var text = Inline(e);
                    if (text.Length > 0) sb.Append(text).Append("\n\n");
                    return;
                }

            case "equation":
                {
                    var text = Inline(e);
                    if (text.Length > 0) sb.Append(text).Append("\n\n");
                    return;
                }

            case "list":
                {
                    var ordered = (string?)e.Attribute("list-type") == "enumerated";
                    var i = 0;
                    foreach (var item in e.Elements().Where(x => Name(x) == "item"))
                    {
                        i++;
                        var text = Inline(item);
                        if (text.Length > 0) sb.Append(ordered ? $"{i}. " : "- ").Append(text).Append('\n');
                    }
                    sb.Append('\n');
                    return;
                }

            case "note":
                {
                    var title = e.Elements().FirstOrDefault(x => Name(x) == "title");
                    var inner = new StringBuilder();
                    foreach (var c in e.Elements()) Block(c, inner, level + 1);
                    var body = inner.ToString().Trim();
                    if (body.Length == 0) return;
                    var head = title is null ? "" : $"**{Inline(title)}**\n\n";
                    var quoted = string.Join("\n", (head + body).Split('\n').Select(l => l.Length == 0 ? ">" : "> " + l));
                    sb.Append(quoted).Append("\n\n");
                    return;
                }

            case "exercise":
                {
                    foreach (var c in e.Elements())
                    {
                        var tag = Name(c);
                        if (tag is "problem" or "solution")
                        {
                            var inner = new StringBuilder();
                            foreach (var cc in c.Elements()) Block(cc, inner, level + 1);
                            var body = inner.ToString().Trim();
                            if (body.Length > 0)
                                sb.Append(tag == "problem" ? "**Problem:** " : "**Solution:** ").Append(body).Append("\n\n");
                        }
                    }
                    return;
                }

            case "figure":
                {
                    var cap = e.Elements().FirstOrDefault(x => Name(x) == "caption");
                    var alt = e.Descendants().Where(x => Name(x) == "image").Select(x => (string?)x.Attribute("alt")).FirstOrDefault(a => !string.IsNullOrWhiteSpace(a));
                    var text = cap is not null ? Inline(cap) : alt is not null ? Collapse(alt) : "";
                    if (text.Length > 0) sb.Append("*[Figure: ").Append(text).Append("]*\n\n");
                    return;
                }

            case "media":
                {
                    var alt = (string?)e.Attribute("alt");
                    if (!string.IsNullOrWhiteSpace(alt)) sb.Append("*[Figure: ").Append(Collapse(alt)).Append("]*\n\n");
                    return;
                }

            case "table":
                Table(e, sb);
                return;

            case "definition":
                {
                    var term = e.Elements().FirstOrDefault(x => Name(x) == "term");
                    var meaning = e.Elements().FirstOrDefault(x => Name(x) == "meaning");
                    if (term is not null && meaning is not null)
                        sb.Append("**").Append(Inline(term)).Append("**: ").Append(Inline(meaning)).Append("\n\n");
                    return;
                }

            default:
                foreach (var c in e.Elements()) Block(c, sb, level);
                return;
        }
    }

    static void Table(XElement table, StringBuilder sb)
    {
        var rows = table.Descendants().Where(x => Name(x) == "row")
            .Select(r => r.Elements().Where(c => Name(c) == "entry").Select(c => Inline(c).Replace("|", "\\|").Replace("\n", " ")).ToList())
            .Where(r => r.Count > 0).ToList();
        if (rows.Count == 0) return;

        var width = rows.Max(r => r.Count);
        foreach (var r in rows) while (r.Count < width) r.Add("");

        sb.Append("| ").Append(string.Join(" | ", rows[0])).Append(" |\n");
        sb.Append("|").Append(string.Concat(Enumerable.Repeat(" --- |", width))).Append('\n');
        foreach (var r in rows.Skip(1)) sb.Append("| ").Append(string.Join(" | ", r)).Append(" |\n");
        sb.Append('\n');
    }

    // ---- inline content ----
    static string Inline(XElement e)
    {
        var sb = new StringBuilder();
        foreach (var n in e.Nodes()) InlineNode(n, sb);
        return Collapse(sb.ToString()).Replace(" ,", ",").Replace(" .", ".");
    }

    static void InlineNode(XNode n, StringBuilder sb)
    {
        if (n is XText t) { sb.Append(t.Value); return; }
        if (n is not XElement e) return;
        if (HasClass(e, "os-teacher")) return;

        switch (Name(e))
        {
            case "math": sb.Append(" $").Append(Tex(e)).Append("$ "); break;
            case "emphasis":
                {
                    var inner = InlineChildren(e);
                    if (inner.Length == 0) break;
                    var effect = (string?)e.Attribute("effect");
                    sb.Append(effect == "italics" ? $"*{inner}*" : $"**{inner}**");
                    break;
                }
            case "term":
                {
                    var inner = InlineChildren(e);
                    if (inner.Length > 0) sb.Append($"**{inner}**");
                    break;
                }
            case "sub": sb.Append("<sub>").Append(InlineChildren(e)).Append("</sub>"); break;
            case "sup": sb.Append("<sup>").Append(InlineChildren(e)).Append("</sup>"); break;
            case "newline": sb.Append(' '); break;
            case "link":
                {
                    var inner = InlineChildren(e);
                    var url = (string?)e.Attribute("url");
                    if (inner.Length == 0) break;
                    sb.Append(url is not null ? $"[{inner}]({url})" : inner);
                    break;
                }
            case "media": case "image": case "label": break;
            case "problem":
            case "solution":
            case "para":
            case "item":
            case "span":
            case "title":
            case "equation":
            case "cite":
            case "quote":
            case "code":
            case "meaning":
            case "definition":
                sb.Append(' ').Append(InlineChildren(e)).Append(' ');
                break;
            default: sb.Append(InlineChildren(e)); break;
        }
    }

    static string InlineChildren(XElement e)
    {
        var sb = new StringBuilder();
        foreach (var n in e.Nodes()) InlineNode(n, sb);
        return sb.ToString();
    }

    // ---- MathML -> LaTeX (covers the elements this book uses) ----
    static string Tex(XElement e)
    {
        string K() => string.Concat(e.Elements().Select(Tex));
        var c = e.Elements().ToList();
        string A(int i) => i < c.Count ? Tex(c[i]) : "";

        return Name(e) switch
        {
            "math" or "mrow" or "mstyle" or "mpadded" or "semantics" or "mphantom" => K(),
            "mi" or "mn" => e.Value.Trim(),
            "mo" => Op(e.Value.Trim()),
            "mtext" => e.Value.Trim() is { Length: > 0 } t ? $"\\text{{{t}}}" : "",
            "mspace" => "\\,",
            "mfrac" => $"\\frac{{{A(0)}}}{{{A(1)}}}",
            "msub" => $"{{{A(0)}}}_{{{A(1)}}}",
            "msup" => $"{{{A(0)}}}^{{{A(1)}}}",
            "msubsup" => $"{{{A(0)}}}_{{{A(1)}}}^{{{A(2)}}}",
            "msqrt" => $"\\sqrt{{{K()}}}",
            "mroot" => $"\\sqrt[{A(1)}]{{{A(0)}}}",
            "mover" => c.Count > 1 ? Over(A(0), c[1].Value.Trim()) : A(0),
            "munder" => $"\\underset{{{A(1)}}}{{{A(0)}}}",
            "munderover" => $"{{{A(0)}}}_{{{A(1)}}}^{{{A(2)}}}",
            "mfenced" => $"{(string?)e.Attribute("open") ?? "("}{string.Join(",", c.Select(Tex))}{(string?)e.Attribute("close") ?? ")"}",
            "mtable" => $"\\begin{{matrix}} {string.Join(" \\\\ ", c.Select(Tex))} \\end{{matrix}}",
            "mtr" => string.Join(" & ", c.Select(Tex)),
            "mtd" => K(),
            _ => K(),
        };
    }

    static string Over(string baseTex, string accent) => accent switch
    {
        "\u2192" or "\u20D7" => $"\\vec{{{baseTex}}}",
        "\u00AF" or "\u203E" or "\u2015" or "_" => $"\\overline{{{baseTex}}}",
        "^" or "\u02C6" => $"\\hat{{{baseTex}}}",
        "\u00B7" or "." => $"\\dot{{{baseTex}}}",
        _ => $"\\overset{{{accent}}}{{{baseTex}}}",
    };

    static string Op(string o) => o switch
    {
        "\u00D7" => "\\times ",
        "\u00B7" or "\u22C5" => "\\cdot ",
        "\u2212" => "-",
        "\u00B1" => "\\pm ",
        "\u2248" => "\\approx ",
        "\u2260" => "\\neq ",
        "\u2264" => "\\leq ",
        "\u2265" => "\\geq ",
        "\u221E" => "\\infty ",
        "\u00B0" => "^\\circ ",
        "\u2192" => "\\rightarrow ",
        "\u2211" => "\\sum ",
        "\u221D" => "\\propto ",
        "\u2206" => "\\Delta ",
        "\u00F7" => "\\div ",
        "\u2223" => "|",
        _ => o,
    };
}