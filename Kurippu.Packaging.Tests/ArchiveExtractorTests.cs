using System.IO.Compression;
using System.Text;
using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class ArchiveExtractorTests
{
    [Fact]
    public void ExtractsZipAndPreservesRelativePaths()
    {
        using var fixture = new ArchiveFixture("sample.zip");
        using (var zip = new ZipArchive(File.Create(fixture.Archive), ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry("nested/file.txt").Open());
            writer.Write("hello");
        }
        var destination = fixture.Destination;

        ArchiveExtractor.ExtractToDirectory(fixture.Archive, destination);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(destination, "nested", "file.txt")));
    }

    [Fact]
    public void ExtractsTarXz()
    {
        using var fixture = new ArchiveFixture("sample.tar.xz");
        TestArchiveFactory.WriteEmbeddedTarXz(fixture.Archive, TestArchiveFactory.SampleTarXz);
        var destination = fixture.Destination;

        ArchiveExtractor.ExtractToDirectory(fixture.Archive, destination);

        Assert.Equal("hello", File.ReadAllText(Path.Combine(destination, "nested", "file.txt")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("nested/../../escape.txt")]
    [InlineData("/absolute.txt")]
    public void RejectsAbsoluteAndParentTraversalEntries(string name)
    {
        using var fixture = new ArchiveFixture("unsafe.zip");
        using (var zip = new ZipArchive(File.Create(fixture.Archive), ZipArchiveMode.Create))
        {
            using var writer = new StreamWriter(zip.CreateEntry(name).Open());
            writer.Write("escape");
        }

        var exception = Assert.Throws<PackagingException>(() =>
            ArchiveExtractor.ExtractToDirectory(fixture.Archive, fixture.Destination));

        Assert.Contains("unsafe path", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Destination, "escape.txt")));
        Assert.False(File.Exists(Path.Combine(fixture.Destination, "nested", "escape.txt")));
    }

    [Fact]
    public void RejectsSymbolicLinks()
    {
        using var fixture = new ArchiveFixture("unsafe.tar.xz");
        TestArchiveFactory.WriteEmbeddedTarXz(fixture.Archive, TestArchiveFactory.SymbolicLinkTarXz);

        var exception = Assert.Throws<PackagingException>(() =>
            ArchiveExtractor.ExtractToDirectory(fixture.Archive, fixture.Destination));

        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Destination, "link")));
    }

    [Fact]
    public void RejectsZipSymbolicLinks()
    {
        using var fixture = new ArchiveFixture("unsafe.zip");
        using (var zip = new ZipArchive(File.Create(fixture.Archive), ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("link");
            entry.ExternalAttributes = 0xA1FF << 16;
            using var writer = new StreamWriter(entry.Open());
            writer.Write("target");
        }

        var exception = Assert.Throws<PackagingException>(() =>
            ArchiveExtractor.ExtractToDirectory(fixture.Archive, fixture.Destination));

        Assert.Contains("symbolic link", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(fixture.Destination, "link")));
    }

    [Fact]
    public void RejectsDuplicateEntriesThatCollide()
    {
        using var fixture = new ArchiveFixture("duplicate.zip");
        using (var zip = new ZipArchive(File.Create(fixture.Archive), ZipArchiveMode.Create))
        {
            using (var first = new StreamWriter(zip.CreateEntry("same.txt").Open())) first.Write("first");
            using (var second = new StreamWriter(zip.CreateEntry("same.txt").Open())) second.Write("second");
        }

        var exception = Assert.Throws<PackagingException>(() =>
            ArchiveExtractor.ExtractToDirectory(fixture.Archive, fixture.Destination));

        Assert.Contains("duplicate", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ArchiveFixture : IDisposable
    {
        public ArchiveFixture(string archiveName)
        {
            Root = Path.Combine(Path.GetTempPath(), "kurippu-archive-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Archive = Path.Combine(Root, archiveName);
            Destination = Path.Combine(Root, "output");
            Directory.CreateDirectory(Destination);
        }

        public string Root { get; }
        public string Archive { get; }
        public string Destination { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
