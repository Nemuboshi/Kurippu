using System.Globalization;

namespace Kurippu.Core;

public static class FfmpegArguments
{
    public static string FormatRaw(IReadOnlyList<string> args) => string.Join(' ', args.Select(arg =>
        arg.Length == 0 || arg.Any(char.IsWhiteSpace) || arg.Contains('"') || arg.Contains('\\') || arg.Contains('\'')
            ? "\"" + arg.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"" : arg));

    public static string[] ParseRaw(string raw)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        char quote = '\0';
        var active = false;
        for (var i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (quote != '\0')
            {
                if (ch == quote) quote = '\0';
                else if (ch == '\\' && quote == '"' && i + 1 < raw.Length) current.Append(raw[++i]);
                else current.Append(ch);
            }
            else if (ch is '\'' or '"') { quote = ch; active = true; }
            else if (char.IsWhiteSpace(ch))
            {
                if (active || current.Length > 0) result.Add(current.ToString());
                current.Clear(); active = false;
            }
            else { current.Append(ch); active = true; }
        }
        if (quote != '\0') throw new FormatException("Unmatched quote in raw arguments");
        if (active || current.Length > 0) result.Add(current.ToString());
        return result.ToArray();
    }

    private static readonly string[] Prefix = ["-hide_banner", "-nostdin", "-y"];
    private static string Number(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    private static string FileArg(string path) => path.StartsWith("file:", StringComparison.Ordinal) ? path : $"file:{path}";

    public static string[] Build(EncodeOptions o)
    {
        if (string.IsNullOrWhiteSpace(o.InputPath)) throw new ArgumentException("Input path is required");
        if (o.VideoCodec is not ("x264" or "vp8" or "vp9" or "av1")) throw new ArgumentException("Unsupported video codec");
        if (o.AudioCodec is not ("aac" or "opus" or "vorbis" or "copy")) throw new ArgumentException("Unsupported audio codec");
        var a = new List<string>();
        if (o.Start is { } start) a.AddRange(["-ss", Number(start)]);
        a.AddRange(["-i", FileArg(o.InputPath)]);
        if (o.Start is not null && o.HasAudio && o.AudioCodec == "copy") a.AddRange(["-ss", "0"]);
        if (o.End is { } end) a.AddRange(["-t", Number(end - (o.Start ?? 0))]);
        var graphic = o.BurnSubtitles && o.GraphicSubtitles && o.SubtitleTrack is not null;
        var filters = VideoFilters(o);
        if (graphic)
        {
            var graph = $"[0:v:{o.VideoTrack}][0:s:{o.SubtitleTrack}]overlay";
            if (filters.Count > 0) graph += "," + string.Join(',', filters);
            a.AddRange(["-filter_complex", graph + "[v]", "-map", "[v]"]);
        }
        else a.AddRange(["-map", $"0:v:{o.VideoTrack}"]);
        if (o.HasAudio) a.AddRange(["-map", $"0:a:{o.AudioTrack}"]);
        a.AddRange(["-threads", o.Threads.ToString(CultureInfo.InvariantCulture)]);
        switch (o.VideoCodec)
        {
            case "av1":
                a.AddRange(["-c:v", "libaom-av1", "-cpu-used", o.Multithreading == "no-mt" ? "0" : "4"]);
                if (o.Multithreading != "no-mt")
                {
                    var tiles = (int)Math.Ceiling(Math.Log2(Math.Max(1, o.Threads)) / 2);
                    var cols = Math.Min(tiles, (int)Math.Floor(Math.Log2(Math.Max(1, (o.FinalWidth + 63) / 64.0))));
                    var rows = Math.Min(tiles, (int)Math.Floor(Math.Log2(Math.Max(1, (o.FinalHeight + 63) / 64.0))));
                    a.AddRange(["-tile-columns", cols.ToString(), "-tile-rows", rows.ToString()]);
                }
                if (o.Multithreading == "row-mt") a.AddRange(["-row-mt", "1"]);
                a.AddRange(["-strict", "experimental"]);
                break;
            case "vp9":
                a.AddRange(["-c:v", "libvpx-vp9", "-speed", o.Multithreading == "no-mt" ? "0" : "1",
                    "-tile-columns", o.Multithreading == "no-mt" ? "0" : "6"]);
                if (o.Multithreading == "row-mt") a.AddRange(["-row-mt", "1"]);
                a.AddRange(["-frame-parallel", "0"]);
                break;
            case "vp8": a.AddRange(["-c:v", "libvpx", "-speed", "0"]); break;
            default: a.AddRange(["-c:v", "libx264"]); break;
        }
        a.AddRange(["-b:v", o.VideoBitrate is { } bitrate ? $"{bitrate}k" : "0"]);
        if (o.Quality is { } quality)
        {
            a.AddRange(["-crf", quality.ToString()]);
            if (o.VideoCodec == "vp8" && quality < 4) a.AddRange(["-qmin", "0"]);
            if (o.VideoCodec == "vp9" && quality == 0) a.AddRange(["-lossless", "1"]);
        }
        a.AddRange(["-preset", o.Preset]);
        if (o.VideoCodec != "x264") a.AddRange(["-auto-alt-ref", "1", "-lag-in-frames", "25", "-g", "128"]);
        a.AddRange(["-pix_fmt", "yuv420p"]);
        if (!graphic && filters.Count > 0) a.AddRange(["-vf", string.Join(',', filters)]);
        if (o.HasAudio)
        {
            if (o.AudioCodec == "copy") a.AddRange(["-c:a", "copy"]);
            else
            {
                switch (o.AudioCodec)
                {
                    case "opus": a.AddRange(["-c:a", "libopus", "-b:a", $"{Number(o.AudioBitrate ?? 128)}k"]); break;
                    case "aac": a.AddRange(["-c:a", "aac", "-b:a", $"{Number(o.AudioBitrate ?? 128)}k"]); break;
                    case "vorbis": a.AddRange(["-c:a", "libvorbis", "-q:a", Number(o.AudioBitrate ?? 4)]); break;
                }
                if (o.AudioChannels > 2) a.AddRange(["-ac", "2"]);
                var audio = new List<string>();
                if (o.Amplify is { } amp) audio.Add($"acompressor=makeup={Number(amp)}");
                if (o.FadeIn is { } fadeIn) audio.Add($"afade=t=in:d={Number(fadeIn)}");
                if (o.FadeOut is { } fadeOut) audio.Add($"afade=t=out:d={Number(fadeOut)}:st={(o.Duration - fadeOut).ToString("0.000", CultureInfo.InvariantCulture)}");
                if (audio.Count > 0) a.AddRange(["-af", string.Join(',', audio)]);
            }
        }
        a.AddRange(["-f", o.VideoCodec == "x264" ? "mp4" : "webm"]);
        return a.ToArray();
    }

    private static List<string> VideoFilters(EncodeOptions o)
    {
        var filters = new List<string>();
        if (o.Deinterlace) filters.Add("yadif");
        if (o.Crop is { } crop) filters.Add($"crop={crop.Width}:{crop.Height}:{crop.X}:{crop.Y}");
        if (o.FixSar || o.ScaleWidth is not null || o.ScaleHeight is not null)
        {
            filters.Add($"scale={o.FinalWidth}:{o.FinalHeight}");
            if (o.FixSar || !o.Anamorphic) filters.Add("setsar=1");
        }
        if (o.BurnSubtitles && !o.GraphicSubtitles)
        {
            var path = (o.SubtitlePath ?? o.InputPath).Replace("\\", "\\\\").Replace(":", "\\:").Replace("'", "\\'");
            var subtitle = $"subtitles='{path}'";
            if (o.SubtitlePath is null && o.SubtitleTrack is not null) subtitle += $":si={o.SubtitleTrack}";
            if (o.Start is { } start) filters.Add($"setpts=PTS+{Number(start)}/TB");
            filters.Add(subtitle);
            if (o.Start is not null) filters.Add("setpts=PTS-STARTPTS");
        }
        return filters;
    }

    public static string[] BuildPass(string[] raw, int pass, string title, string output, string passlog)
    {
        if (pass is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(pass));
        var a = raw.ToList();
        if (pass == 1)
        {
            var speed = a.IndexOf("-speed");
            if (speed >= 0) a[speed + 1] = "4";
            else a.AddRange(["-speed", "4"]);
            a.AddRange(["-an", "-pass", "1", "-passlogfile", PasslogBase(passlog), "-f", "null", "-"]);
        }
        else
        {
            if (pass == 2) a.AddRange(["-pass", "2", "-passlogfile", PasslogBase(passlog)]);
            a.AddRange(["-metadata", $"title={title}", FileArg(output)]);
        }
        return [.. Prefix, .. a];
    }

    private static string PasslogBase(string path) => path.EndsWith("-0.log", StringComparison.Ordinal)
        ? path[..^6] : path;

    public static string[] BuildTest(string[] raw, string output)
    {
        var stripped = new HashSet<string> { "-b:v", "-speed", "-cpu-used", "-row-mt", "-tile-columns",
            "-tile-rows", "-frame-parallel", "-auto-alt-ref", "-lag-in-frames", "-strict", "-g",
            "-crf", "-lossless", "-qmin" };
        var args = new List<string>();
        for (var i = 0; i < raw.Length; i++)
        {
            if (stripped.Contains(raw[i])) { i++; continue; }
            if (raw[i] == "-f") { i++; continue; }
            if (raw[i] == "-c:v") { args.AddRange(["-c:v", "libx264"]); i++; continue; }
            if (raw[i] == "-preset") { args.AddRange(["-preset", "ultrafast"]); i++; continue; }
            args.Add(raw[i]);
        }
        if (!args.Contains("-c:v")) args.AddRange(["-c:v", "libx264"]);
        if (!args.Contains("-preset")) args.AddRange(["-preset", "ultrafast"]);
        args.AddRange(["-crf", "18"]);
        var vf = args.IndexOf("-vf");
        const string evenScale = "scale=floor((iw+1)/2)*2:-2";
        if (vf >= 0) args[vf + 1] += "," + evenScale;
        else if (args.IndexOf("-filter_complex") is var graphIndex && graphIndex >= 0)
        {
            var graph = args[graphIndex + 1];
            var label = System.Text.RegularExpressions.Regex.Match(graph, @"(\[[^\]]+\])$");
            args[graphIndex + 1] = label.Success
                ? graph[..^label.Length] + "," + evenScale + label.Value
                : graph + "," + evenScale;
        }
        else args.AddRange(["-vf", evenScale]);
        args.AddRange(["-f", "matroska", FileArg(output)]);
        return [.. Prefix, .. args];
    }

    public static string[] BuildPreview(string path, double? time, string codec, string format,
        string output, int width, int height, double sar, double fps)
    {
        var w = sar > 1 ? (int)Math.Round(width * sar) : width;
        var h = sar is > 0 and < 1 ? (int)Math.Round(height / sar) : height;
        var filter = $"setpts=PTS-STARTPTS,scale=iw:iw/dar,scale={w + 1}:{h + 1}:force_original_aspect_ratio=decrease[main];" +
            $"color=color=#DDDDDD:size={w}x{h}[bg];[bg][main]overlay=(W-w)/2:(H-h)/2";
        var args = new List<string>(Prefix);
        if (time is { } seconds) args.AddRange(["-ss", Number(seconds)]);
        args.AddRange(["-i", FileArg(path), "-c:v", codec switch {
            "x264" => "libx264", "vp8" => "libvpx", "vp9" => "libvpx-vp9", "av1" => "libaom-av1", _ => codec
        }, "-b:v", "0", "-crf", "30", "-vf", filter, "-r", Number(fps),
            "-an", "-sn", "-dn", "-frames:v", "1", "-pix_fmt", "yuv420p",
            "-strict", "experimental", "-f", format, FileArg(output)]);
        return [.. args];
    }

    public static string[] BuildConcat(string list, string main, bool hasAudio, double fps,
        string format, string output)
    {
        var args = new List<string>(Prefix);
        args.AddRange(["-f", "concat", "-safe", "0", "-i", FileArg(list),
            "-itsoffset", Number(1 / Math.Max(1, fps)), "-i", FileArg(main), "-map", "0:v:0"]);
        if (hasAudio) args.AddRange(["-map", "1:a:0?"]);
        args.AddRange(["-c", "copy", "-strict", "experimental", "-f", format, FileArg(output)]);
        return [.. args];
    }

    public static string ConcatLine(string path) => $"file '{path.Replace('\\', '/').Replace("'", "'\\''")}'\n";
    public static string ConcatList(string preview, string main) => ConcatLine(preview) + ConcatLine(main);
}
