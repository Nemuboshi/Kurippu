using SharpCompress.Common;
using SharpCompress.Writers;
using SharpCompress.Writers.Zip;

namespace Kurippu.Packaging.Tests;

public static class TestZipFactory
{
    public static void Write(string destination, params (string Name, byte[] Content)[] entries)
    {
        using var writer = WriterFactory.OpenWriter(destination, ArchiveType.Zip,
            new ZipWriterOptions(CompressionType.Deflate));
        foreach (var entry in entries)
        {
            using var content = new MemoryStream(entry.Content);
            writer.Write(entry.Name, content, DateTime.UtcNow);
        }
    }
}
