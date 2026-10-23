namespace Kurippu.Core;

public sealed class EncodeOptions
{
    public string InputPath { get; set; } = "";
    public double? Start { get; set; }
    public double? End { get; set; }
    public double Duration { get; set; }
    public bool HasAudio { get; set; } = true;
    public int VideoTrack { get; set; }
    public int AudioTrack { get; set; }
    public string VideoCodec { get; set; } = "x264";
    public string AudioCodec { get; set; } = "aac";
    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount);
    public int? VideoBitrate { get; set; } = 5000;
    public int? Quality { get; set; }
    public string Preset { get; set; } = "veryslow";
    public string Multithreading { get; set; } = "row-mt";
    public bool Deinterlace { get; set; }
    public CropRect? Crop { get; set; }
    public int? ScaleWidth { get; set; }
    public int? ScaleHeight { get; set; }
    public int FinalWidth { get; set; }
    public int FinalHeight { get; set; }
    public bool FixSar { get; set; }
    public bool Anamorphic { get; set; }
    public bool BurnSubtitles { get; set; }
    public bool GraphicSubtitles { get; set; }
    public string? SubtitlePath { get; set; }
    public int? SubtitleTrack { get; set; }
    public double? Amplify { get; set; }
    public double? FadeIn { get; set; }
    public double? FadeOut { get; set; }
    public int AudioChannels { get; set; } = 2;
    public double? AudioBitrate { get; set; } = 128;
}

public sealed record CropRect(int Width, int Height, int X, int Y);

public sealed record MediaTrack(int Id, string Type, string Codec, int Width = 0, int Height = 0,
    double Sar = 1, double Fps = 0, int Channels = 0, string? Language = null, string? Title = null,
    int SampleRate = 0, double Bitrate = 0, string? Profile = null);

public sealed record MediaInfo(string Path, double Duration, int Width, int Height, double Sar,
    IReadOnlyList<MediaTrack> VideoTracks, IReadOnlyList<MediaTrack> AudioTracks,
    IReadOnlyList<MediaTrack> SubtitleTracks, string? Title = null, string? Format = null,
    double Bitrate = 0, long FileSize = 0);
