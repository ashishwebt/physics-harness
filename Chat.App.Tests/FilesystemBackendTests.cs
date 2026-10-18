using DeepHarness.Backend;
using Xunit;

namespace Chat.App.Tests;

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

    [Fact]
    public void VirtualPath_RejectsTraversalAndComputesPhysicalPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deepagent-virtual-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            Assert.Equal("/docs/readme.md", VirtualPath.Normalize("docs\\readme.md"));
            Assert.Equal("/docs/readme.md", VirtualPath.Normalize("/docs/readme.md"));
            Assert.Throws<ArgumentException>(() => VirtualPath.Normalize("../secret.txt"));
            Assert.Throws<ArgumentException>(() => VirtualPath.Normalize("~/secret.txt"));

            var fullPath = VirtualPath.UnderRoot(root, "/notes/file.txt");
            Assert.Equal(Path.Combine(root, "notes", "file.txt"), fullPath);

            Assert.Equal("/notes/file.txt", VirtualPath.FromPhysical(root, fullPath));
            Assert.Equal("/", VirtualPath.FromPhysical(root, root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void FilesystemBackend_SupportsListGlobGrepDeleteAndDownloadUpload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"deepagent-backend-ops-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var backend = new FilesystemBackend(root);
            backend.Write("/docs/alpha.txt", "hello\nworld\nhello");
            backend.Write("/docs/beta.md", "# Header\nalpha");

            var list = backend.List("/docs");
            Assert.Contains(list, entry => entry.Path == "/docs/alpha.txt");
            Assert.Contains(list, entry => entry.Path == "/docs/beta.md");

            var glob = backend.Glob("**/*.md", "/");
            Assert.Contains(glob, entry => entry.Path == "/docs/beta.md");

            var grep = backend.Grep("alpha", "/docs");
            Assert.Contains(grep, match => match.Path == "/docs/beta.md" && match.Line == 2);

            var read = backend.Read("/docs/alpha.txt", offset: 1, limit: 1);
            Assert.Equal(3, read.TotalLines);
            Assert.Equal(2, read.StartLine);
            Assert.Equal("world", read.Content.Trim());

            var invalidOffset = backend.Read("/docs/alpha.txt", offset: -1);
            Assert.Equal("offset must be non-negative", invalidOffset.Error);

            var delete = backend.Delete("/docs/alpha.txt");
            Assert.Null(delete.Error);
            Assert.False(File.Exists(Path.Combine(root, "docs", "alpha.txt")));

            var download = backend.DownloadFiles(["/docs/beta.md"]).Single();
            Assert.Null(download.Error);
            Assert.Equal("/docs/beta.md", download.Path);
            Assert.Contains("# Header", System.Text.Encoding.UTF8.GetString(download.Content!));

            backend.UploadFiles([new UploadedFile("/docs/uploaded.txt", new byte[] { 1, 2, 3 })]);
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(Path.Combine(root, "docs", "uploaded.txt")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void StoreBackend_TracksNamespacedVirtualFiles()
    {
        var store = new InMemoryFileStore();
        var backend = new StoreBackend(store, "artifacts");

        backend.Write("/docs/hello.txt", "one\ntwo");
        var read = backend.Read("/docs/hello.txt");
        Assert.Equal("one\ntwo", read.Content.Replace("\r", string.Empty));

        var edit = backend.Edit("/docs/hello.txt", "one", "alpha");
        Assert.Null(edit.Error);
        Assert.Equal(1, edit.Occurrences);

        var list = backend.List("/");
        Assert.Contains(list, entry => entry.Path == "/docs/");

        var glob = backend.Glob("**/*.txt");
        Assert.Contains(glob, entry => entry.Path == "/docs/hello.txt");

        var grep = backend.Grep("alpha", "/docs");
        Assert.Contains(grep, match => match.Path == "/docs/hello.txt" && match.Line == 1);

        var upload = backend.UploadFiles([new UploadedFile("/notes/bytes.bin", new byte[] { 9, 8, 7 })]).Single();
        Assert.Null(upload.Error);

        var downloaded = backend.DownloadFiles(["/notes/bytes.bin"]).Single();
        Assert.Equal(new byte[] { 9, 8, 7 }, downloaded.Content);

        var deleted = backend.Delete("/docs/hello.txt");
        Assert.Null(deleted.Error);
        Assert.Null(backend.Read("/docs/hello.txt").Content);
    }

    [Fact]
    public void CompositeBackend_RoutesPathRequestsByPrefix()
    {
        var defaultRoot = Path.Combine(Path.GetTempPath(), $"deepagent-default-{Guid.NewGuid():N}");
        Directory.CreateDirectory(defaultRoot);
        var defaultBackend = new FilesystemBackend(defaultRoot);
        defaultBackend.Write("/docs/default.txt", "default-value");

        var routeRoot = Path.Combine(Path.GetTempPath(), $"deepagent-route-{Guid.NewGuid():N}");
        Directory.CreateDirectory(routeRoot);
        var routeBackend = new FilesystemBackend(routeRoot);
        routeBackend.Write("/file.txt", "route-value");

        try
        {
            var backend = new CompositeBackend(defaultBackend, new Dictionary<string, IBackend>
            {
                ["/route/"] = routeBackend
            });

            Assert.Equal("default-value", backend.Read("/docs/default.txt").Content);
            Assert.Equal("route-value", backend.Read("/route/file.txt").Content);

            var list = backend.List("/");
            Assert.Contains(list, entry => entry.Path == "/route/");

            var write = backend.Write("/route/new.txt", "ok");
            Assert.Equal("/route/new.txt", write.Path);
            Assert.Equal("ok", routeBackend.Read("/new.txt").Content);

            var glob = backend.Glob("**/*.txt", "/route/");
            Assert.Contains(glob, entry => entry.Path == "/route/file.txt");
        }
        finally
        {
            Directory.Delete(defaultRoot, recursive: true);
            Directory.Delete(routeRoot, recursive: true);
        }
    }
}
