namespace Kurippu.Core;

public sealed record MediaInfo(string Path, double Duration, int Width, int Height, double Sar,
    IReadOnlyList<MediaTrack> VideoTracks, IReadOnlyList<MediaTrack> AudioTracks,
    IReadOnlyList<MediaTrack> SubtitleTracks, string? Title = null, string? Format = null,
    double Bitrate = 0, long FileSize = 0);
