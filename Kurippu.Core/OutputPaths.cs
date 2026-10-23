using System.Text.RegularExpressions;

namespace Kurippu.Core;

public static class OutputPaths
{
    public static string NextAvailable(string directory, string sourceName, string container, Func<string, bool> exists)
    {
        var name = Path.GetFileNameWithoutExtension(sourceName);
        name = Regex.Replace(name, @"(?:-\d+)+$", "");
        if (string.IsNullOrWhiteSpace(name)) name = "video";
        for (var index = 0; ; index++)
        {
            var path = Path.Combine(directory, name + (index == 0 ? "" : $"-{index}") + "." + container);
            if (!exists(path)) return path;
        }
    }
}
