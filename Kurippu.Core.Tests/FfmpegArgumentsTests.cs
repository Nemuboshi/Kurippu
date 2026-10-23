using Kurippu.Core;

namespace Kurippu.Core.Tests;

public class FfmpegArgumentsTests
{
    [Fact]
    public void DetectedCropUsesTheSelectedVideoTrackDimensions()
    {
        var media = new MediaInfo("multi.mkv", 10, 640, 360, 1,
            [new MediaTrack(1, "video", "h264", 640, 360),
                new MediaTrack(2, "video", "h264", 1920, 1080)], [], []);
        Assert.False(FfmpegDetection.IsFullFrame(new CropRect(1600, 900, 160, 90), media, 1));
        Assert.True(FfmpegDetection.IsFullFrame(new CropRect(1920, 1080, 0, 0), media, 1));
    }

    [Fact]
    public void DetectionUsesSelectedTrackAndFragmentOffset()
    {
        var args = FfmpegDetection.CropArgs("C:/视频/a b.mkv", 4, 2);
        Assert.Equal("9", args[Array.IndexOf(args, "-ss") + 1]);
        Assert.Equal("file:C:/视频/a b.mkv", args[Array.IndexOf(args, "-i") + 1]);
        Assert.Equal("0:v:2", args[Array.IndexOf(args, "-map") + 1]);
        Assert.Equal("cropdetect=round=2,metadata=print:file=-", args[Array.IndexOf(args, "-vf") + 1]);
        Assert.Equal("3", args[Array.IndexOf(args, "-frames:v") + 1]);
        Assert.Equal("10", FfmpegDetection.InterlaceArgs("a", 0, 0)[^4]);
    }

    [Fact]
    public void DetectionParsesMetadataFromLastFrameAndIgnoresMissingValues()
    {
        Assert.Equal(new CropRect(1800, 1000, 60, 40), FfmpegDetection.ParseCrop(
            "lavfi.cropdetect.w=1900\nlavfi.cropdetect.h=1080\nlavfi.cropdetect.x=0\nlavfi.cropdetect.y=0\n" +
            "lavfi.cropdetect.w=1800\nlavfi.cropdetect.h=1000\nlavfi.cropdetect.x=60\nlavfi.cropdetect.y=40"));
        Assert.Null(FfmpegDetection.ParseCrop("lavfi.cropdetect.w=1800"));
        Assert.True(FfmpegDetection.ParseInterlace("lavfi.idet.multiple.tff=1\nlavfi.idet.multiple.tff=4"));
        Assert.False(FfmpegDetection.ParseInterlace("lavfi.idet.multiple.tff=4\nlavfi.idet.multiple.tff=2"));
    }

    [Fact]
    public void RawArgumentsRoundTripSpacesQuotesAndRejectUnbalancedQuotes()
    {
        var raw = FfmpegArguments.FormatRaw(["-i", "file:C:/视频/a b.mkv", "-metadata", "title=it's good", "-vf", "crop=10:10"]);
        Assert.Equal(["-i", "file:C:/视频/a b.mkv", "-metadata", "title=it's good", "-vf", "crop=10:10"], FfmpegArguments.ParseRaw(raw));
        Assert.Throws<FormatException>(() => FfmpegArguments.ParseRaw("-i 'unfinished"));
    }

    [Fact]
    public void CropSelectionUsesSourcePixelsAcrossAnamorphicLetterbox()
    {
        var crop = VideoGeometry.FromViewport(1000, 600, 720, 576, 1.25,
            125, 75, 500, 300);
        Assert.Equal(new CropRect(384, 288, 72, 72), crop);
        var display = VideoGeometry.ToViewport(1000, 600, 720, 576, 1.25, crop!);
        Assert.Equal(125, display.X, 1);
        Assert.Equal(75, display.Y, 1);
        Assert.Equal(500, display.Width, 1);
        Assert.Equal(300, display.Height, 1);
    }

    [Fact]
    public void DragCropSnapsOriginAndDimensionsToEvenPixelsWithinSource()
    {
        var crop = VideoGeometry.FromViewport(321, 181, 321, 181, 1, 3, 5, 317, 175);
        var selected = Assert.IsType<CropRect>(crop);
        Assert.Equal(new CropRect(316, 174, 4, 6), selected);
        Assert.Equal(0, selected.X % 2);
        Assert.Equal(0, selected.Y % 2);
        Assert.Equal(0, selected.Width % 2);
        Assert.Equal(0, selected.Height % 2);
        Assert.True(selected.X + selected.Width <= 321);
        Assert.True(selected.Y + selected.Height <= 181);
    }

    [Theory]
    [InlineData("w", 10, 0, 90, 100)]
    [InlineData("n", 0, 10, 100, 90)]
    [InlineData("ne", 0, 10, 110, 90)]
    [InlineData("sw", 10, 0, 90, 110)]
    public void CropHandlesResizeTheSelectedEdge(string handle, double x, double y, double w, double h)
    {
        var r = VideoGeometry.DragViewport((0, 0, 100, 100), handle, 10, 10);
        Assert.Equal((x, y, w, h), r);
    }

    [Theory]
    [InlineData(0, 0, "nw")]
    [InlineData(50, 0, "n")]
    [InlineData(100, 0, "ne")]
    [InlineData(100, 50, "e")]
    [InlineData(100, 100, "se")]
    [InlineData(50, 100, "s")]
    [InlineData(0, 100, "sw")]
    [InlineData(0, 50, "w")]
    [InlineData(50, 50, "move")]
    [InlineData(150, 150, "draw")]
    public void CropHitTestSelectsAllEdgesAndCorners(double x, double y, string expected)
    {
        Assert.Equal(expected, VideoGeometry.PickCropHandle((0, 0, 100, 100), x, y));
    }

    [Fact]
    public void TestEncodeAddsScaleInsideComplexGraphNotAsSimpleVideoFilter()
    {
        var args = FfmpegArguments.BuildTest(["-filter_complex", "[0:v:0][0:s:0]overlay[v]",
            "-map", "[v]", "-c:v", "libx264", "-f", "mp4"], "/tmp/test.mkv");
        Assert.DoesNotContain("-vf", args, StringComparer.Ordinal);
        Assert.Equal("[0:v:0][0:s:0]overlay,scale=floor((iw+1)/2)*2:-2[v]",
            args[Array.IndexOf(args, "-filter_complex") + 1]);
    }
    [Fact]
    public void EncodePreservesUnicodePathAsOneArgument()
    {
        var options = new EncodeOptions { InputPath = "C:/视频/a b.mkv", VideoCodec = "x264" };
        var args = FfmpegArguments.Build(options);

        Assert.Equal("file:C:/视频/a b.mkv", args[Array.IndexOf(args, "-i") + 1]);
        Assert.Equal("mp4", args[Array.LastIndexOf(args, "-f") + 1]);
    }

    [Fact]
    public void TwoPassFirstPassWritesNullAndNoAudio()
    {
        var args = FfmpegArguments.BuildPass(
            ["-i", "file:/tmp/in.mkv", "-c:v", "libvpx-vp9", "-speed", "1"],
            1, "title", "/tmp/out.webm", "/tmp/pass-0.log");

        Assert.Equal(["-f", "null", "-"], args[^3..]);
        Assert.Contains("-an", args, StringComparer.Ordinal);
        Assert.Equal("/tmp/pass", args[Array.IndexOf(args, "-passlogfile") + 1]);
        Assert.Equal("4", args[Array.IndexOf(args, "-speed") + 1]);
    }

    [Fact]
    public void CropAndLetterboxUseSourcePixelGeometry()
    {
        var crop = VideoGeometry.Letterbox(1000, 600, 720, 576, 1.25);
        Assert.Equal(937.5, crop.Width, 1);
        Assert.Equal(600, crop.Height, 1);
        Assert.Equal(31.25, crop.X, 1);
    }

    [Fact]
    public void TestEncodeReplacesCodecAndOutputWithoutShellQuoting()
    {
        var args = FfmpegArguments.BuildTest(["-i", "file:/tmp/a b.mkv", "-c:v", "libaom-av1",
            "-crf", "30", "-f", "webm"], "/tmp/test.mkv");
        Assert.Equal("libx264", args[Array.IndexOf(args, "-c:v") + 1]);
        Assert.Equal("18", args[Array.IndexOf(args, "-crf") + 1]);
        Assert.Equal("matroska", args[Array.LastIndexOf(args, "-f") + 1]);
        Assert.Equal("file:/tmp/test.mkv", args[^1]);
        Assert.Contains("file:/tmp/a b.mkv", args, StringComparer.Ordinal);
    }

    [Fact]
    public void ConcatListEscapesQuoteAndUnicodePath()
    {
        Assert.Equal("file 'C:/视频/it'\\''s.mp4'\n", FfmpegArguments.ConcatLine("C:/视频/it's.mp4"));
    }

    [Fact]
    public void PreviewConcatListContainsBothFrameAndMainMovie()
    {
        Assert.Equal("file '/tmp/preview.mp4'\nfile '/tmp/main.mp4'\n",
            FfmpegArguments.ConcatList("/tmp/preview.mp4", "/tmp/main.mp4"));
    }

    [Fact]
    public void TestEncodeDropsOptionsSpecificToOriginalVideoEncoder()
    {
        var args = FfmpegArguments.BuildTest(["-c:v", "libaom-av1", "-tile-columns", "2",
            "-tile-rows", "1", "-lossless", "1", "-qmin", "0", "-f", "webm"], "/tmp/test.mkv");
        Assert.DoesNotContain("-tile-rows", args, StringComparer.Ordinal);
        Assert.DoesNotContain("-lossless", args, StringComparer.Ordinal);
        Assert.DoesNotContain("-qmin", args, StringComparer.Ordinal);
    }
}
