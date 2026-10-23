using System.Globalization;
using System.Text.RegularExpressions;

namespace Kurippu.Core;

public sealed class OptionInputs
{
    public string Threads { get; set; } = "8";
    public string CropWidth { get; set; } = "";
    public string CropHeight { get; set; } = "";
    public string CropX { get; set; } = "";
    public string CropY { get; set; } = "";
    public string ScaleWidth { get; set; } = "";
    public string ScaleHeight { get; set; } = "";
    public bool Deinterlace { get; set; }
    public bool FixSar { get; set; }
    public bool BurnSubtitles { get; set; }
    public int? SubtitleTrack { get; set; }
    public string SubtitlePath { get; set; } = "";
    public int VideoTrack { get; set; }
    public int AudioTrack { get; set; }
    public bool HasAudio { get; set; } = true;
    public string FadeIn { get; set; } = "";
    public string FadeOut { get; set; } = "";
    public string Amplify { get; set; } = "";
    public string Start { get; set; } = "";
    public string End { get; set; } = "";
    public string VideoCodec { get; set; } = "x264";
    public string AudioCodec { get; set; } = "aac";
    public string Limit { get; set; } = "";
    public string Quality { get; set; } = "";
    public string AudioBitrate { get; set; } = "";
    public bool TwoPass { get; set; }
    public bool LimitMode { get; set; }
    public bool CrfMode { get; set; } = true;
    public string Multithreading { get; set; } = "row-mt";
    public string Preset { get; set; } = "veryslow";
}

public sealed record FieldError(string Field, string Message);
public sealed record EncodingPlan(EncodeOptions? Options, IReadOnlyList<FieldError> Errors,
    IReadOnlyList<string> Warnings, string Container)
{
    public bool IsValid => Options is not null && Errors.Count == 0;
}

public static class OptionEngine
{
    private static int? IntField(string raw, string name, int min, int max, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!int.TryParse(raw.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < min || value > max)
        {
            errors.Add(new(name, $"must be an integer between {min} and {max}"));
            return null;
        }
        return value;
    }

    private static double? NumberField(string raw, string name, double min, double max, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (!double.TryParse(raw.Trim(), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) ||
            !double.IsFinite(value) || value < min || value > max)
        {
            errors.Add(new(name, $"must be between {min} and {max}"));
            return null;
        }
        return value;
    }

    public static double ParseTime(string raw)
    {
        var parts = raw.Trim().Split(':');
        if (parts.Length is < 1 or > 3 || parts.Any(p => !Regex.IsMatch(p, @"^\d+(?:\.\d+)?$")))
            throw new FormatException("Invalid time");
        var value = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        if (parts.Length >= 2) value += int.Parse(parts[^2], CultureInfo.InvariantCulture) * 60;
        if (parts.Length == 3) value += int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600;
        return value;
    }

    private static int Even(double value) => (int)Math.Floor((value + 1) / 2) * 2;

    public static EncodingPlan Derive(OptionInputs input, MediaInfo media, int? threads = null)
    {
        if (input.VideoTrack >= 0 && input.VideoTrack < media.VideoTracks.Count)
        {
            var track = media.VideoTracks[input.VideoTrack];
            media = media with
            {
                Width = track.Width > 0 ? track.Width : media.Width,
                Height = track.Height > 0 ? track.Height : media.Height,
                Sar = track.Sar > 0 ? track.Sar : media.Sar
            };
        }
        var errors = new List<FieldError>();
        var warnings = new List<string>();
        var threadCount = threads ?? IntField(input.Threads, "threads", 1, 256, errors) ?? 8;
        var cropw = IntField(input.CropWidth, "cropw", 1, 100000, errors);
        var croph = IntField(input.CropHeight, "croph", 1, 100000, errors);
        var cropx = IntField(input.CropX, "cropx", 0, 100000, errors);
        var cropy = IntField(input.CropY, "cropy", 0, 100000, errors);
        var scalew = IntField(input.ScaleWidth, "scalew", 1, 100000, errors);
        var scaleh = IntField(input.ScaleHeight, "scaleh", 1, 100000, errors);
        foreach (var (field, value) in new[] { ("cropw", cropw), ("croph", croph),
            ("cropx", cropx), ("cropy", cropy), ("scalew", scalew), ("scaleh", scaleh) })
            if (value is { } number && number % 2 != 0)
                errors.Add(new(field, "must be an even number of pixels"));
        var hasCrop = cropw is not null || croph is not null || cropx is not null || cropy is not null;
        var effectiveWidth = cropw ?? ((media.Width - (cropx ?? 0)) & ~1);
        var effectiveHeight = croph ?? ((media.Height - (cropy ?? 0)) & ~1);
        var effectiveX = cropx ?? Math.Max(0, (media.Width - effectiveWidth) / 4) * 2;
        var effectiveY = cropy ?? Math.Max(0, (media.Height - effectiveHeight) / 4) * 2;
        if (hasCrop && effectiveWidth < 2) errors.Add(new("cropw", "crop must be at least 2 pixels wide"));
        if (hasCrop && effectiveHeight < 2) errors.Add(new("croph", "crop must be at least 2 pixels high"));
        if (hasCrop && effectiveX + effectiveWidth > media.Width) errors.Add(new("cropx", "crop exceeds source width"));
        if (hasCrop && effectiveY + effectiveHeight > media.Height) errors.Add(new("cropy", "crop exceeds source height"));
        var start = ParseOptionalTime(input.Start, "start", errors);
        var end = ParseOptionalTime(input.End, "end", errors);
        if (start is { } s && (s < 0 || s >= media.Duration)) errors.Add(new("start", "out of range"));
        if (end is { } e && (e > media.Duration + 0.0005 || e <= (start ?? 0))) errors.Add(new("end", "must be greater than start and within source"));
        var duration = (end ?? media.Duration) - (start ?? 0);
        if (duration <= 0) errors.Add(new("end", "empty fragment"));
        if (input.VideoCodec is not ("x264" or "vp8" or "vp9" or "av1")) errors.Add(new("vcodec", "unsupported video codec"));
        if (input.AudioCodec is not ("aac" or "opus" or "vorbis" or "copy")) errors.Add(new("acodec", "unsupported audio codec"));
        if (string.Equals(input.VideoCodec, "x264", StringComparison.Ordinal) && input.AudioCodec is "opus" or "vorbis" && input.HasAudio)
            errors.Add(new("acodec", "MP4 requires AAC or a compatible copied track"));
        if (input.VideoTrack < 0 || input.VideoTrack >= media.VideoTracks.Count) errors.Add(new("vtrack", "select a video track"));
        var hasAudio = input.HasAudio && media.AudioTracks.Count > 0;
        if (hasAudio && (input.AudioTrack < 0 || input.AudioTrack >= media.AudioTracks.Count)) errors.Add(new("atrack", "select an audio track"));
        if (hasAudio && string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal) && input.AudioTrack >= 0 && input.AudioTrack < media.AudioTracks.Count &&
            media.AudioTracks[input.AudioTrack].Codec is not ("aac" or "vorbis" or "opus"))
            errors.Add(new("acodec", "selected audio track cannot be copied"));
        if (input.BurnSubtitles)
        {
            if (!string.IsNullOrWhiteSpace(input.SubtitlePath))
            {
                if (!File.Exists(input.SubtitlePath)) errors.Add(new("strack", "external subtitle file not found"));
            }
            else if (input.SubtitleTrack is null || input.SubtitleTrack < 0 || input.SubtitleTrack >= media.SubtitleTracks.Count)
                errors.Add(new("strack", "select an embedded subtitle track or an external subtitle file"));
        }
        var quality = IntField(input.Quality, "quality", 0, string.Equals(input.VideoCodec, "x264", StringComparison.Ordinal) ? 51 : 63, errors);
        if (input.CrfMode) quality ??= string.Equals(input.VideoCodec, "x264", StringComparison.Ordinal) ? 23 : 25;
        double? ab = string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal) || !hasAudio ? null :
            NumberField(input.AudioBitrate, "ab", string.Equals(input.AudioCodec, "vorbis", global::System.StringComparison.Ordinal) ? -1 : string.Equals(input.AudioCodec, "aac", global::System.StringComparison.Ordinal) ? 128 : 6,
string.Equals(input.AudioCodec, "vorbis", global::System.StringComparison.Ordinal) ? 10 : string.Equals(input.AudioCodec, "aac", global::System.StringComparison.Ordinal) ? 768 : 510, errors)
            ?? (string.Equals(input.AudioCodec, "vorbis", global::System.StringComparison.Ordinal) ? 4 : 128);
        var limit = input.CrfMode ? 0 : NumberField(input.Limit, "limit", input.LimitMode ? .001 : 1, double.MaxValue, errors)
            ?? (input.LimitMode ? 19 : 5000);
        var amp = string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal) ? null : NumberField(input.Amplify, "amplify", 1, 64, errors);
        var fadeIn = string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal) ? null : NumberField(input.FadeIn, "fadeIn", .001, double.MaxValue, errors);
        var fadeOut = string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal) ? null : NumberField(input.FadeOut, "fadeOut", .001, double.MaxValue, errors);
        var sar = media.Sar > 0 ? media.Sar : 1;
        var w = hasCrop ? effectiveWidth : media.Width;
        var h = hasCrop ? effectiveHeight : media.Height;
        var dar = (double)w / Math.Max(1, h) * sar;
        var finalWidth = scalew ?? (scaleh is { } sh ? Even(sh * (input.FixSar ? dar : (double)w / Math.Max(1, h))) :
            input.FixSar && sar > 1 ? Even(w * sar) : w);
        var finalHeight = scaleh ?? (scalew is { } sw ? Even(sw / (input.FixSar ? dar : (double)w / Math.Max(1, h))) :
            input.FixSar && sar < 1 ? Even(h / sar) : h);
        if (finalWidth <= 0 || finalHeight <= 0) errors.Add(new("scale", "invalid output dimensions"));
        if (finalWidth % 2 != 0 && scalew is null) errors.Add(new("width", "output width must be even"));
        if (finalHeight % 2 != 0 && scaleh is null) errors.Add(new("height", "output height must be even"));
        if (sar != 1 && !input.FixSar) warnings.Add("Anamorphic source: fix SAR to preserve aspect ratio");
        if (duration < 6 && !input.CrfMode) warnings.Add("Consider CRF mode for a short fragment");
        var audioRate = string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal) ? 320 : string.Equals(input.AudioCodec, "vorbis", StringComparison.Ordinal) ? 128 : ab ?? 0;
        var vb = input.CrfMode ? null : input.LimitMode
            ? Math.Max(1, (int)Math.Floor(limit * 8 * 1024 / Math.Max(duration, .001) - (hasAudio ? audioRate : 0)))
            : (int?)Math.Floor(limit);
        var container = string.Equals(input.VideoCodec, "x264", StringComparison.Ordinal) ? "mp4" : "webm";
        if (errors.Count > 0) return new(null, errors, warnings, container);
        var graphic = input.BurnSubtitles && string.IsNullOrWhiteSpace(input.SubtitlePath) && input.SubtitleTrack is { } index &&
            !new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ass", "ssa", "subrip", "srt", "mov_text", "webvtt", "text" }
                .Contains(media.SubtitleTracks[index].Codec);
        return new(new EncodeOptions
        {
            InputPath = media.Path,
            Start = start,
            End = end,
            Duration = duration,
            HasAudio = hasAudio,
            VideoTrack = input.VideoTrack,
            AudioTrack = input.AudioTrack,
            VideoCodec = input.VideoCodec,
            AudioCodec = input.AudioCodec,
            Threads = threadCount,
            VideoBitrate = vb,
            Quality = quality,
            Preset = input.Preset,
            Multithreading = input.Multithreading,
            Deinterlace = input.Deinterlace,
            Crop = hasCrop ? new(effectiveWidth, effectiveHeight, effectiveX, effectiveY) : null,
            ScaleWidth = scalew,
            ScaleHeight = scaleh,
            FinalWidth = finalWidth,
            FinalHeight = finalHeight,
            FixSar = input.FixSar,
            Anamorphic = sar != 1,
            BurnSubtitles = input.BurnSubtitles,
            GraphicSubtitles = graphic,
            SubtitlePath = string.IsNullOrWhiteSpace(input.SubtitlePath) ? null : input.SubtitlePath,
            SubtitleTrack = input.SubtitleTrack,
            AudioChannels = hasAudio ? media.AudioTracks[input.AudioTrack].Channels : 0,
            Amplify = amp,
            FadeIn = fadeIn,
            FadeOut = fadeOut,
            AudioBitrate = ab
        }, errors, warnings, container);
    }

    private static double? ParseOptionalTime(string raw, string field, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return ParseTime(raw); }
        catch { errors.Add(new(field, "invalid time")); return null; }
    }
}
