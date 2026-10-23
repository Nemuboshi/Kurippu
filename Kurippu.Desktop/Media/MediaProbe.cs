using System.Globalization;
using Kurippu.Core;

namespace Kurippu.Desktop.Media;

public static class MediaProbe
{
    public static MediaInfo FromProperties(string path, IReadOnlyDictionary<string, string> values)
    {
        string Get(string key) => values.GetValueOrDefault(key) ?? "";
        int Integer(string key) => int.TryParse(Get(key), CultureInfo.InvariantCulture, out var n) ? n : 0;
        double Floating(string key) => double.TryParse(Get(key), CultureInfo.InvariantCulture, out var n) ? n : 0;
        long Long(string key) => long.TryParse(Get(key), CultureInfo.InvariantCulture, out var n) ? n : 0;
        var videos = new List<MediaTrack>();
        var audios = new List<MediaTrack>();
        var subtitles = new List<MediaTrack>();
        for (var i = 0; i < Integer("track-list/count"); i++)
        {
            var prefix = $"track-list/{i}/";
            var kind = Get(prefix + "type");
            var width = Integer(prefix + "demux-w");
            var height = Integer(prefix + "demux-h");
            var sar = Floating(prefix + "demux-par");
            if (string.Equals(kind, "video", StringComparison.Ordinal) && videos.Count == 0)
            {
                if (width <= 0) width = Integer("video-params/w");
                if (height <= 0) height = Integer("video-params/h");
                if (sar <= 0) sar = Floating("video-params/par");
            }
            var track = new MediaTrack(Integer(prefix + "id"), kind, Get(prefix + "codec"),
                width, height, sar > 0 ? sar : 1,
                Floating(prefix + "demux-fps"), Integer(prefix + "demux-channel-count"),
                Get(prefix + "lang"), Get(prefix + "title"), Integer(prefix + "demux-samplerate"),
                Floating(prefix + "demux-bitrate"), Get(prefix + "profile"));
            switch (kind)
            {
                case "video": videos.Add(track); break;
                case "audio": audios.Add(track); break;
                case "sub": subtitles.Add(track); break;
            }
        }
        if (videos.Count == 0) throw new InvalidDataException("No video tracks found");
        var first = videos[0];
        return new MediaInfo(path, Floating("duration"), first.Width, first.Height, first.Sar,
            videos, audios, subtitles, Get("media-title"), Get("file-format"),
            Floating("demuxer-bitrate"), Long("file-size"));
    }
}
