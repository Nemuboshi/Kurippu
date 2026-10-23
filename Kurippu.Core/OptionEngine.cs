using System.Globalization;
using System.Text.RegularExpressions;

namespace Kurippu.Core;

public static partial class OptionEngine
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
        if (parts.Length is < 1 or > 3 || parts.Any(p => !TimePartRegex().IsMatch(p)))
            throw new FormatException("Invalid time");
        var value = double.Parse(parts[^1], CultureInfo.InvariantCulture);
        if (parts.Length >= 2) value += int.Parse(parts[^2], CultureInfo.InvariantCulture) * 60;
        if (parts.Length == 3) value += int.Parse(parts[0], CultureInfo.InvariantCulture) * 3600;
        return value;
    }

    private static int Even(double value) => (int)Math.Floor((value + 1) / 2) * 2;

    [GeneratedRegex(@"^\d+(?:\.\d+)?$", RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex TimePartRegex();

    public static EncodingPlan Derive(OptionInputs input, MediaInfo media, int? threads = null)
    {
        media = SelectMediaTrack(input, media);
        var errors = new List<FieldError>();
        var warnings = new List<string>();
        var threadCount = threads ?? IntField(input.Threads, "threads", 1, 256, errors) ?? 8;
        var geometry = ParseGeometry(input, media, errors);
        var fragment = ParseFragment(input, media, errors);
        var hasAudio = input.HasAudio && media.AudioTracks.Count > 0;
        ValidateInput(input, media, hasAudio, errors);
        var quality = ParseQuality(input, errors);
        var audio = ParseAudioOptions(input, hasAudio, errors);
        var limit = ParseLimit(input, errors);
        var sar = media.Sar > 0 ? media.Sar : 1;
        var dimensions = CalculateDimensions(input, media, geometry.Crop, sar,
            geometry.ScaleWidth, geometry.ScaleHeight, errors);
        if (sar != 1 && !input.FixSar) warnings.Add("Anamorphic source: fix SAR to preserve aspect ratio");
        if (fragment.Duration < 6 && !input.CrfMode) warnings.Add("Consider CRF mode for a short fragment");
        var videoBitrate = CalculateVideoBitrate(input, hasAudio, fragment.Duration, audio.Bitrate, limit);
        var container = string.Equals(input.VideoCodec, "x264", StringComparison.Ordinal) ? "mp4" : "webm";
        if (errors.Count > 0) return new(null, errors, warnings, container);
        var graphic = input.BurnSubtitles && string.IsNullOrWhiteSpace(input.SubtitlePath) && input.SubtitleTrack is { } index &&
            !new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ass", "ssa", "subrip", "srt", "mov_text", "webvtt", "text" }
                .Contains(media.SubtitleTracks[index].Codec);
        return new(new EncodeOptions
        {
            InputPath = media.Path,
            Start = fragment.Start,
            End = fragment.End,
            Duration = fragment.Duration,
            HasAudio = hasAudio,
            VideoTrack = input.VideoTrack,
            AudioTrack = input.AudioTrack,
            VideoCodec = input.VideoCodec,
            AudioCodec = input.AudioCodec,
            Threads = threadCount,
            VideoBitrate = videoBitrate,
            Quality = quality,
            Preset = input.Preset,
            Multithreading = input.Multithreading,
            Deinterlace = input.Deinterlace,
            Crop = geometry.Crop.HasCrop
                ? new(geometry.Crop.Width, geometry.Crop.Height, geometry.Crop.X, geometry.Crop.Y)
                : null,
            ScaleWidth = geometry.ScaleWidth,
            ScaleHeight = geometry.ScaleHeight,
            FinalWidth = dimensions.Width,
            FinalHeight = dimensions.Height,
            FixSar = input.FixSar,
            Anamorphic = sar != 1,
            BurnSubtitles = input.BurnSubtitles,
            GraphicSubtitles = graphic,
            SubtitlePath = string.IsNullOrWhiteSpace(input.SubtitlePath) ? null : input.SubtitlePath,
            SubtitleTrack = input.SubtitleTrack,
            AudioChannels = hasAudio ? media.AudioTracks[input.AudioTrack].Channels : 0,
            Amplify = audio.Amplify,
            FadeIn = audio.FadeIn,
            FadeOut = audio.FadeOut,
            AudioBitrate = audio.Bitrate
        }, errors, warnings, container);
    }

    private static MediaInfo SelectMediaTrack(OptionInputs input, MediaInfo media)
    {
        if (input.VideoTrack < 0 || input.VideoTrack >= media.VideoTracks.Count) return media;
        var track = media.VideoTracks[input.VideoTrack];
        return media with
        {
            Width = track.Width > 0 ? track.Width : media.Width,
            Height = track.Height > 0 ? track.Height : media.Height,
            Sar = track.Sar > 0 ? track.Sar : media.Sar
        };
    }

    private static CropValidation ParseCrop(OptionInputs input, MediaInfo media,
        List<FieldError> errors)
    {
        var width = IntField(input.CropWidth, "cropw", 1, 100000, errors);
        var height = IntField(input.CropHeight, "croph", 1, 100000, errors);
        var x = IntField(input.CropX, "cropx", 0, 100000, errors);
        var y = IntField(input.CropY, "cropy", 0, 100000, errors);
        foreach (var (field, value) in new[] { ("cropw", width), ("croph", height),
            ("cropx", x), ("cropy", y) })
            if (value is { } number && number % 2 != 0)
                errors.Add(new(field, "must be an even number of pixels"));
        var hasCrop = width is not null || height is not null || x is not null || y is not null;
        var effectiveWidth = width ?? ((media.Width - (x ?? 0)) & ~1);
        var effectiveHeight = height ?? ((media.Height - (y ?? 0)) & ~1);
        var effectiveX = x ?? Math.Max(0, (media.Width - effectiveWidth) / 4) * 2;
        var effectiveY = y ?? Math.Max(0, (media.Height - effectiveHeight) / 4) * 2;
        if (hasCrop && effectiveWidth < 2)
            errors.Add(new("cropw", "crop must be at least 2 pixels wide"));
        if (hasCrop && effectiveHeight < 2)
            errors.Add(new("croph", "crop must be at least 2 pixels high"));
        if (hasCrop && effectiveX + effectiveWidth > media.Width)
            errors.Add(new("cropx", "crop exceeds source width"));
        if (hasCrop && effectiveY + effectiveHeight > media.Height)
            errors.Add(new("cropy", "crop exceeds source height"));
        return new(hasCrop, effectiveWidth, effectiveHeight, effectiveX, effectiveY);
    }

    private static GeometryValidation ParseGeometry(OptionInputs input, MediaInfo media,
        List<FieldError> errors)
    {
        var crop = ParseCrop(input, media, errors);
        var scaleWidth = IntField(input.ScaleWidth, "scalew", 1, 100000, errors);
        var scaleHeight = IntField(input.ScaleHeight, "scaleh", 1, 100000, errors);
        if (scaleWidth is { } width && width % 2 != 0)
            errors.Add(new("scalew", "must be an even number of pixels"));
        if (scaleHeight is { } height && height % 2 != 0)
            errors.Add(new("scaleh", "must be an even number of pixels"));
        return new(crop, scaleWidth, scaleHeight);
    }

    private static FragmentValidation ParseFragment(OptionInputs input, MediaInfo media,
        List<FieldError> errors)
    {
        var start = ParseOptionalTime(input.Start, "start", errors);
        var end = ParseOptionalTime(input.End, "end", errors);
        if (start is { } startValue && (startValue < 0 || startValue >= media.Duration))
            errors.Add(new("start", "out of range"));
        if (end is { } endValue &&
            (endValue > media.Duration + 0.0005 || endValue <= (start ?? 0)))
            errors.Add(new("end", "must be greater than start and within source"));
        var duration = (end ?? media.Duration) - (start ?? 0);
        if (duration <= 0) errors.Add(new("end", "empty fragment"));
        return new(start, end, duration);
    }

    private static void ValidateInput(OptionInputs input, MediaInfo media, bool hasAudio,
        List<FieldError> errors)
    {
        ValidateCodecs(input, errors);
        ValidateTracks(input, media, hasAudio, errors);
        ValidateSubtitles(input, media, errors);
    }

    private static int? ParseQuality(OptionInputs input, List<FieldError> errors)
    {
        var maximum = string.Equals(input.VideoCodec, "x264", StringComparison.Ordinal) ? 51 : 63;
        var quality = IntField(input.Quality, "quality", 0, maximum, errors);
        if (input.CrfMode)
            quality ??= string.Equals(input.VideoCodec, "x264", StringComparison.Ordinal) ? 23 : 25;
        return quality;
    }

    private static AudioOptions ParseAudioOptions(OptionInputs input, bool hasAudio,
        List<FieldError> errors)
    {
        var copy = string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal);
        return new(
            ParseAudioBitrate(input, hasAudio, errors),
            copy ? null : NumberField(input.Amplify, "amplify", 1, 64, errors),
            copy ? null : NumberField(input.FadeIn, "fadeIn", .001, double.MaxValue, errors),
            copy ? null : NumberField(input.FadeOut, "fadeOut", .001, double.MaxValue, errors));
    }

    private static double ParseLimit(OptionInputs input, List<FieldError> errors)
    {
        if (input.CrfMode) return 0;
        return NumberField(input.Limit, "limit", input.LimitMode ? .001 : 1, double.MaxValue,
                errors)
            ?? (input.LimitMode ? 19 : 5000);
    }

    private static double? ParseAudioBitrate(OptionInputs input, bool hasAudio,
        List<FieldError> errors)
    {
        if (string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal) || !hasAudio)
            return null;
        var minimum = input.AudioCodec switch { "vorbis" => -1, "aac" => 128, _ => 6 };
        var maximum = input.AudioCodec switch { "vorbis" => 10, "aac" => 768, _ => 510 };
        var fallback = string.Equals(input.AudioCodec, "vorbis", StringComparison.Ordinal) ? 4 : 128;
        return NumberField(input.AudioBitrate, "ab", minimum, maximum, errors) ?? fallback;
    }

    private static OutputDimensions CalculateDimensions(OptionInputs input, MediaInfo media,
        CropValidation crop, double sar, int? scaleWidth, int? scaleHeight,
        List<FieldError> errors)
    {
        var width = crop.HasCrop ? crop.Width : media.Width;
        var height = crop.HasCrop ? crop.Height : media.Height;
        var displayRatio = (double)width / Math.Max(1, height) * sar;
        var sourceRatio = (double)width / Math.Max(1, height);
        var finalWidth = scaleWidth ??
            (scaleHeight is { } requestedHeight
                ? Even(requestedHeight * (input.FixSar ? displayRatio : sourceRatio))
                : input.FixSar && sar > 1 ? Even(width * sar) : width);
        var finalHeight = scaleHeight ??
            (scaleWidth is { } requestedWidth
                ? Even(requestedWidth / (input.FixSar ? displayRatio : sourceRatio))
                : input.FixSar && sar < 1 ? Even(height / sar) : height);
        if (finalWidth <= 0 || finalHeight <= 0)
            errors.Add(new("scale", "invalid output dimensions"));
        if (finalWidth % 2 != 0 && scaleWidth is null)
            errors.Add(new("width", "output width must be even"));
        if (finalHeight % 2 != 0 && scaleHeight is null)
            errors.Add(new("height", "output height must be even"));
        return new(finalWidth, finalHeight);
    }

    private static int? CalculateVideoBitrate(OptionInputs input, bool hasAudio,
        double duration, double? audioBitrate, double limit)
    {
        if (input.CrfMode) return null;
        if (!input.LimitMode) return (int?)Math.Floor(limit);
        var audioRate = string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal)
            ? 320
            : string.Equals(input.AudioCodec, "vorbis", StringComparison.Ordinal)
                ? 128
                : audioBitrate ?? 0;
        return Math.Max(1, (int)Math.Floor(limit * 8 * 1024 / Math.Max(duration, .001) -
            (hasAudio ? audioRate : 0)));
    }

    private static void ValidateCodecs(OptionInputs input, List<FieldError> errors)
    {
        if (input.VideoCodec is not ("x264" or "vp8" or "vp9" or "av1"))
            errors.Add(new("vcodec", "unsupported video codec"));
        if (input.AudioCodec is not ("aac" or "opus" or "vorbis" or "copy"))
            errors.Add(new("acodec", "unsupported audio codec"));
        if (string.Equals(input.VideoCodec, "x264", StringComparison.Ordinal) &&
            input.AudioCodec is "opus" or "vorbis" && input.HasAudio)
            errors.Add(new("acodec", "MP4 requires AAC or a compatible copied track"));
    }

    private static void ValidateTracks(OptionInputs input, MediaInfo media, bool hasAudio,
        List<FieldError> errors)
    {
        if (input.VideoTrack < 0 || input.VideoTrack >= media.VideoTracks.Count)
            errors.Add(new("vtrack", "select a video track"));
        if (hasAudio && (input.AudioTrack < 0 || input.AudioTrack >= media.AudioTracks.Count))
            errors.Add(new("atrack", "select an audio track"));
        if (hasAudio && string.Equals(input.AudioCodec, "copy", StringComparison.Ordinal) &&
            input.AudioTrack >= 0 && input.AudioTrack < media.AudioTracks.Count &&
            media.AudioTracks[input.AudioTrack].Codec is not ("aac" or "vorbis" or "opus"))
            errors.Add(new("acodec", "selected audio track cannot be copied"));
    }

    private static void ValidateSubtitles(OptionInputs input, MediaInfo media,
        List<FieldError> errors)
    {
        if (!input.BurnSubtitles) return;
        if (!string.IsNullOrWhiteSpace(input.SubtitlePath))
        {
            if (!File.Exists(input.SubtitlePath))
                errors.Add(new("strack", "external subtitle file not found"));
            return;
        }
        if (input.SubtitleTrack is null || input.SubtitleTrack < 0 ||
            input.SubtitleTrack >= media.SubtitleTracks.Count)
            errors.Add(new("strack", "select an embedded subtitle track or an external subtitle file"));
    }

    private static double? ParseOptionalTime(string raw, string field, List<FieldError> errors)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        try { return ParseTime(raw); }
        catch { errors.Add(new(field, "invalid time")); return null; }
    }

    private sealed record CropValidation(bool HasCrop, int Width, int Height, int X, int Y);
    private sealed record GeometryValidation(CropValidation Crop, int? ScaleWidth, int? ScaleHeight);
    private sealed record FragmentValidation(double? Start, double? End, double Duration);
    private sealed record AudioOptions(double? Bitrate, double? Amplify, double? FadeIn,
        double? FadeOut);
    private sealed record OutputDimensions(int Width, int Height);
}
