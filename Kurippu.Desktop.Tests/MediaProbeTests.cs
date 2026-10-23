using Kurippu.Desktop.Media;

namespace Kurippu.Desktop.Tests;

public class MediaProbeTests
{
    [Fact]
    public void NoVideoTrackIsRejectedBeforeEnteringEncoder()
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["duration"] = "25",
            ["track-list/count"] = "1",
            ["track-list/0/type"] = "audio",
            ["track-list/0/id"] = "1"
        };
        Assert.Throws<InvalidDataException>(() => MediaProbe.FromProperties("/tmp/audio.mp3", properties));
    }

    [Fact]
    public void VideoAndAudioTracksMapByType()
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["duration"] = "25",
            ["track-list/count"] = "2",
            ["track-list/0/type"] = "audio",
            ["track-list/0/id"] = "2",
            ["track-list/0/codec"] = "aac",
            ["track-list/1/type"] = "video",
            ["track-list/1/id"] = "1",
            ["track-list/1/codec"] = "h264",
            ["track-list/1/demux-w"] = "1920",
            ["track-list/1/demux-h"] = "1080"
        };
        var media = MediaProbe.FromProperties("/tmp/a.mkv", properties);
        Assert.Equal(1920, media.Width);
        Assert.Single(media.VideoTracks);
        Assert.Single(media.AudioTracks);
    }

    [Fact]
    public void MediaDetailsIncludeBitrateSizeAndAudioSampleRate()
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["duration"] = "25",
            ["file-size"] = "1048576",
            ["demuxer-bitrate"] = "500000",
            ["track-list/count"] = "2",
            ["track-list/0/type"] = "video",
            ["track-list/0/id"] = "1",
            ["track-list/0/demux-w"] = "640",
            ["track-list/0/demux-h"] = "360",
            ["track-list/0/demux-fps"] = "24",
            ["track-list/1/type"] = "audio",
            ["track-list/1/id"] = "2",
            ["track-list/1/demux-samplerate"] = "48000"
        };
        var media = MediaProbe.FromProperties("/tmp/a.mkv", properties);
        Assert.Equal(1048576, media.FileSize);
        Assert.Equal(500000, media.Bitrate);
        Assert.Equal(48000, media.AudioTracks[0].SampleRate);
    }
}
