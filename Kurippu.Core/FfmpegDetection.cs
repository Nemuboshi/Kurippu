using System.Globalization;
using System.Text.RegularExpressions;

namespace Kurippu.Core;

public static partial class FfmpegDetection
{
    public static string[] InterlaceArgs(string path, double start, int track) => Build(path, start, track,
        "idet,metadata=print:lavfi.idet.multiple.tff:file=-", 10);

    public static string[] CropArgs(string path, double start, int track) => Build(path, start, track,
        "cropdetect=round=2,metadata=print:file=-", 3);

    private static string[] Build(string path, double start, int track, string filter, int frames) =>
        ["-v", "error", "-nostdin", "-y", "-ss", (start + 5).ToString("0.###", CultureInfo.InvariantCulture),
            "-i", path.StartsWith("file:", StringComparison.Ordinal) ? path : "file:" + path,
            "-map", $"0:v:{track}", "-vf", filter, "-frames:v", frames.ToString(CultureInfo.InvariantCulture),
            "-f", "null", "-"];

    public static bool ParseInterlace(string output) =>
        int.TryParse(InterlaceCountRegex().Matches(output).LastOrDefault()?.Groups["count"].Value,
            CultureInfo.InvariantCulture, out var count) && count > 3;

    public static CropRect? ParseCrop(string output)
    {
        int? Last(string key) => int.TryParse(CropValueRegex().Matches(output)
                .Where(match => string.Equals(match.Groups["key"].Value, key, StringComparison.Ordinal))
                .LastOrDefault()?.Groups["value"].Value,
            CultureInfo.InvariantCulture, out var number) ? number : null;
        var w = Last("w"); var h = Last("h"); var x = Last("x"); var y = Last("y");
        return w is > 0 && h is > 0 && x is >= 0 && y is >= 0 ? new(w.Value, h.Value, x.Value, y.Value) : null;
    }

    [GeneratedRegex(@"lavfi\.idet\.multiple\.tff=(?<count>\d+)",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex InterlaceCountRegex();

    [GeneratedRegex(@"lavfi\.cropdetect\.(?<key>w|h|x|y)=(?<value>-?\d+)",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CropValueRegex();

    public static bool IsFullFrame(CropRect crop, MediaInfo media, int trackIndex)
    {
        var track = media.VideoTracks[trackIndex];
        var width = track.Width > 0 ? track.Width : media.Width;
        var height = track.Height > 0 ? track.Height : media.Height;
        return crop.Width >= width && crop.Height >= height;
    }
}
