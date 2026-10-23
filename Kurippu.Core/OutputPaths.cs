using System.Text.RegularExpressions;

namespace Kurippu.Core;

public static partial class OutputPaths
{
    public static string? ExistingDirectory(string? outputPath, Func<string, bool> exists)
    {
        var directory = Path.GetDirectoryName(outputPath);
        return directory is not null && exists(directory) ? directory : null;
    }

    public static string NextAvailable(string directory, string sourceName, string container, Func<string, bool> exists)
    {
        var name = Path.GetFileNameWithoutExtension(sourceName);
        name = TrailingNumberRegex().Replace(name, "");
        if (string.IsNullOrWhiteSpace(name)) name = "video";
        for (var index = 0; ; index++)
        {
            var path = Path.Combine(directory, name + (index == 0 ? "" : $"-{index}") + "." + container);
            if (!exists(path)) return path;
        }
    }

    [GeneratedRegex(@"(?:-\d+)+$", RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TrailingNumberRegex();
}
