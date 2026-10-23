namespace Kurippu.Core;

public sealed record MediaTrack(int Id, string Type, string Codec, int Width = 0, int Height = 0,
    double Sar = 1, double Fps = 0, int Channels = 0, string? Language = null, string? Title = null,
    int SampleRate = 0, double Bitrate = 0, string? Profile = null);
