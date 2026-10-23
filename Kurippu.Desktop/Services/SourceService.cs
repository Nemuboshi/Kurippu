using System.Text.Json;

namespace Kurippu.Desktop.Services;

public sealed record FormatChoice(string Id, string Label);
public sealed record SourceChoices(string Url, string Title, IReadOnlyList<FormatChoice> Videos,
    IReadOnlyList<FormatChoice> Audios, IReadOnlyList<FormatChoice> Subtitles, string? SaveAs = null)
{
    public int DefaultSubtitleIndex
    {
        get
        {
            for (var i = 0; i < Subtitles.Count; i++)
                if (Subtitles[i].Id is "en" or "eng" or "en-US" or "en_US") return i;
            for (var i = 0; i < Subtitles.Count; i++)
                if (Subtitles[i].Id == "none") return i;
            return -1;
        }
    }
}

public sealed class SourceService(IProcessRunner runner, string ytDlp, string? ffmpeg) : IDisposable
{
    private readonly HashSet<string> directories = [];

    public async Task<SourceChoices> ResolveAsync(string url, CancellationToken cancellationToken)
    {
        ValidateUrl(url);
        var lines = new List<string>();
        var result = await runner.RunAsync(ytDlp, ["--no-playlist", "--dump-json", "--write-subs", "--", url],
            new InlineProgress(lines.Add), cancellationToken);
        if (result != 0) throw new InvalidOperationException($"yt-dlp could not resolve URL ({result})");
        using var json = JsonDocument.Parse(lines.LastOrDefault(line => line.TrimStart().StartsWith('{'))
            ?? throw new InvalidDataException("yt-dlp did not return media information"));
        var root = json.RootElement;
        var formats = root.GetProperty("formats").EnumerateArray().ToArray();
        static string Text(JsonElement item, string key) => item.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
        static double Number(JsonElement item, string key) => item.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.Number ? value.GetDouble() : 0;
        var videos = formats.Where(f => Text(f, "vcodec") is not ("" or "none"))
            .OrderByDescending(f => Number(f, "height")).ThenByDescending(f => Number(f, "width"))
            .ThenByDescending(f => Number(f, "fps"))
            .Select(f => new FormatChoice(Text(f, "format_id"),
                $"{Text(f, "vcodec")} {Number(f, "width"):0}x{Number(f, "height"):0} {Number(f, "fps"):0}fps"))
            .ToArray();
        if (videos.Length == 0) throw new InvalidDataException("No video formats available");
        var audios = formats.Where(f => Text(f, "vcodec") == "none" && Text(f, "acodec") is not ("" or "none"))
            .OrderByDescending(f => Number(f, "abr"))
            .Select(f => new FormatChoice(Text(f, "format_id"), $"{Text(f, "acodec")} {Number(f, "abr"):0}kbit/s"))
            .Append(new FormatChoice("none", "None")).ToArray();
        var subtitles = root.TryGetProperty("requested_subtitles", out var subs) && subs.ValueKind == JsonValueKind.Object
            ? subs.EnumerateObject().Select(s => new FormatChoice(s.Name, s.Name)).ToList() : [];
        subtitles.Add(new FormatChoice("none", "None"));
        return new(Text(root, "webpage_url") is { Length: > 0 } resolved ? resolved : url,
            Text(root, "title"), videos, audios, subtitles,
            string.IsNullOrWhiteSpace(Text(root, "_filename")) ? null : Text(root, "_filename"));
    }

    public async Task<string> DownloadAsync(string url, string videoId, string audioId, string subtitleId,
        IProgress<string>? log, CancellationToken cancellationToken)
    {
        ValidateUrl(url);
        if (string.IsNullOrWhiteSpace(videoId)) throw new ArgumentException("Choose a video format", nameof(videoId));
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-source-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "source.%(ext)s");
        try
        {
            var args = new List<string> { "--no-part", "--no-playlist", "--newline", "--format",
                audioId == "none" ? videoId : videoId + "+" + audioId,
                "--merge-output-format", "mkv" };
            if (ffmpeg is not null) args.AddRange(["--ffmpeg-location", ffmpeg]);
            if (subtitleId != "none") args.AddRange(["--sub-langs", subtitleId, "--write-subs", "--embed-subs"]);
            args.AddRange(["--output", output, "--", url]);
            var result = await runner.RunAsync(ytDlp, args, log, cancellationToken);
            if (result != 0) throw new InvalidOperationException($"yt-dlp download failed ({result})");
            var file = Directory.GetFiles(directory).FirstOrDefault(path =>
                Path.GetFileName(path).StartsWith("source.", StringComparison.Ordinal) &&
                new[] { ".mkv", ".mp4", ".webm", ".mov" }.Contains(
                    Path.GetExtension(path), StringComparer.OrdinalIgnoreCase));
            if (file is null) throw new FileNotFoundException("yt-dlp did not create a video file");
            lock (directories) directories.Add(directory);
            return file;
        }
        catch
        {
            Directory.Delete(directory, recursive: true);
            throw;
        }
    }

    public void Release(string path)
    {
        var directory = Path.GetDirectoryName(path);
        if (directory is null) return;
        lock (directories)
        {
            if (!directories.Remove(directory)) return;
        }
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }

    public void Dispose()
    {
        string[] pending;
        lock (directories) pending = [.. directories];
        foreach (var directory in pending) Release(Path.Combine(directory, "source.mkv"));
    }

    private static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            throw new ArgumentException("Enter a valid HTTP(S) video URL", nameof(url));
    }

    private sealed class InlineProgress(Action<string> callback) : IProgress<string>
    {
        public void Report(string value) => callback(value);
    }
}
