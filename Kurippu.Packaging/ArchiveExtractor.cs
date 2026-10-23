using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using SharpCompress.Compressors.Xz;

namespace Kurippu.Packaging;

public static class ArchiveExtractor
{
    public static void ExtractToDirectory(string archivePath, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var root = Path.GetFullPath(destinationDirectory);
        var names = new HashSet<string>(StringComparer.Ordinal);
        var extension = archivePath.EndsWith(".tar.xz", StringComparison.OrdinalIgnoreCase)
            ? ".tar.xz"
            : Path.GetExtension(archivePath).ToLowerInvariant();

        if (extension == ".tar.xz")
        {
            ExtractTarXz(archivePath, root, names);
            return;
        }

        if (extension is not (".zip" or ".7z"))
            throw new PackagingException($"Unsupported archive format: {archivePath}");

        using var archive = ArchiveFactory.OpenArchive(archivePath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.Key ?? throw new PackagingException("Archive entry has no name.");
            if (entry.IsDirectory) continue;
            if (IsZipSymbolicLink(entry))
                throw new PackagingException($"Archive contains a symbolic link or hard link: {name}");
            RejectLinks(name, entry.LinkTarget);
            var destination = ResolveSafePath(root, name, names);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            entry.WriteToFile(destination);
        }
    }

    private static void ExtractTarXz(string archivePath, string root, HashSet<string> names)
    {
        using var file = File.OpenRead(archivePath);
        using var xz = new XZStream(file);
        using var tar = new TarReader(xz, leaveOpen: false);
        while (tar.GetNextEntry(copyData: false) is { } entry)
        {
            if (entry.EntryType == TarEntryType.Directory) continue;
            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
                throw new PackagingException($"Archive contains a symbolic link or hard link: {entry.Name}");

            var destination = ResolveSafePath(root, entry.Name, names);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            using var output = File.Create(destination);
            entry.DataStream?.CopyTo(output);
        }
    }

    private static void RejectLinks(string name, string? linkTarget)
    {
        if (!string.IsNullOrEmpty(linkTarget))
            throw new PackagingException($"Archive contains a symbolic link or hard link: {name}");
    }

    private static bool IsZipSymbolicLink(IArchiveEntry entry)
    {
        if (entry.Attrib is not { } attributes) return false;
        var fileType = (attributes >> 16) & 0xF000;
        return fileType == 0xA000;
    }

    private static string ResolveSafePath(string root, string name, HashSet<string> names)
    {
        var normalized = name.Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(normalized) || Path.IsPathRooted(normalized) ||
            normalized.StartsWith('/') || normalized.Contains(':', StringComparison.Ordinal))
            throw new PackagingException($"Archive entry uses an unsafe path: {name}");

        var destination = Path.GetFullPath(Path.Combine(root, normalized));
        if (!destination.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new PackagingException($"Archive entry uses an unsafe path: {name}");
        if (!names.Add(destination))
            throw new PackagingException($"Archive contains duplicate entries for: {name}");
        return destination;
    }
}
