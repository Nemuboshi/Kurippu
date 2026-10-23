using System.Globalization;

namespace Kurippu.Core;

public static class PlaybackTimeline
{
    private static double? Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try { return OptionEngine.ParseTime(text); }
        catch (FormatException) { return null; }
    }

    public static (double Start, double End) HighlightBounds(double duration, string start, string end)
    {
        if (!double.IsFinite(duration) || duration <= 0) return (0, 0);
        var beginning = Parse(start) ?? 0;
        var finish = Parse(end) ?? duration;
        return beginning < 0 || finish > duration || beginning >= finish ?
            (0, duration) : (beginning, finish);
    }

    public static double? LoopTarget(double time, string start, string end, double duration)
    {
        var beginning = Parse(start);
        var finish = Parse(end);
        if (finish is null || finish > duration || beginning >= finish || time <= finish) return null;
        return beginning ?? 0;
    }

    public static double? ResumeTarget(double time, double duration, bool loop, string start, string end)
    {
        if (loop && LoopTarget(time, start, end, duration) is { } target) return target;
        return duration > 0 && time >= duration - .05 ? 0 : null;
    }

    public static (string Start, string End) MarkStart(double time, string start, string end) =>
        (ShowTime(time), Parse(end) is { } finish && finish <= time ? "" : end);

    public static (string Start, string End) MarkEnd(double time, string start, string end, double duration) =>
        (Parse(start) is { } beginning && beginning >= time ? "" : start, EndMark(time, duration));

    public static string EndMark(double time, double duration) => time >= duration - .001 ? "" : ShowTime(time, ceil: true);

    public static string ShowTime(double seconds, bool ceil = false)
    {
        var milliseconds = (long)(ceil ? Math.Ceiling(seconds * 1000) : Math.Floor(seconds * 1000));
        var span = TimeSpan.FromMilliseconds(Math.Max(0, milliseconds));
        var prefix = span.TotalHours >= 1 ? $"{(long)span.TotalHours:00}:" : "";
        return prefix + span.ToString(@"mm\:ss\.fff", CultureInfo.InvariantCulture);
    }
}
