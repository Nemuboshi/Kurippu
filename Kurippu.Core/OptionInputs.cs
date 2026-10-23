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
