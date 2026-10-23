using Kurippu.Core;

namespace Kurippu.Core.Tests;

public class OptionEngineTests
{
    private static readonly MediaInfo Media = new("/tmp/in.mkv", 120, 1920, 1080, 1,
        [new(1, "video", "h264", 1920, 1080)], [new(2, "audio", "aac", Channels: 2)], []);

    [Fact]
    public void DefaultsSelectFirstTracksAndMp4()
    {
        var result = OptionEngine.Derive(new OptionInputs(), Media);
        Assert.Empty(result.Errors);
        Assert.Equal("mp4", result.Container);
        Assert.Equal(0, result.Options!.VideoTrack);
        Assert.Equal(0, result.Options.AudioTrack);
        Assert.Equal(120, result.Options.Duration);
        Assert.Equal("veryslow", result.Options.Preset);
        Assert.Equal(23, result.Options.Quality);
        Assert.Null(result.Options.VideoBitrate);
    }

    [Fact]
    public void SubtitleBurnWithoutSourceExplainsBothPossibleSources()
    {
        var plan = OptionEngine.Derive(new OptionInputs { BurnSubtitles = true }, Media);
        Assert.Contains(plan.Errors, error => error.Field == "strack" &&
            error.Message.Contains("embedded") && error.Message.Contains("external"));
    }

    [Fact]
    public void MissingExternalSubtitlesBlockBurnEvenWithEmbeddedTrackSelected()
    {
        var media = Media with { SubtitleTracks = [new(3, "sub", "subrip")] };
        var plan = OptionEngine.Derive(new OptionInputs { BurnSubtitles = true, SubtitleTrack = 0,
            SubtitlePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt") }, media);
        Assert.Contains(plan.Errors, error => error.Field == "strack" && error.Message.Contains("external"));
    }

    [Fact]
    public void ExistingExternalSubtitlesOverrideEmbeddedGraphicTrack()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt");
        try
        {
            File.WriteAllText(path, "1\n00:00:00,000 --> 00:00:01,000\nHello");
            var media = Media with { SubtitleTracks = [new(3, "sub", "hdmv_pgs_subtitle")] };
            var plan = OptionEngine.Derive(new OptionInputs { BurnSubtitles = true,
                SubtitleTrack = 0, SubtitlePath = path }, media);
            Assert.True(plan.IsValid);
            Assert.False(plan.Options!.GraphicSubtitles);
            var args = FfmpegArguments.Build(plan.Options);
            Assert.Contains(args, item => item.Contains("subtitles='"));
            Assert.DoesNotContain("-filter_complex", args);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ThreadCountIsEditableAndInvalidValuesBlockEncoding()
    {
        Assert.Equal(4, OptionEngine.Derive(new OptionInputs { Threads = "4", VideoCodec = "vp9",
            Multithreading = "tile-col" }, Media).Options!.Threads);
        Assert.Contains(OptionEngine.Derive(new OptionInputs { Threads = "0" }, Media).Errors,
            error => error.Field == "threads");
    }

    [Fact]
    public void SelectedVideoTrackDeterminesCropBoundsAndOutputDimensions()
    {
        var multi = Media with { VideoTracks = [Media.VideoTracks[0], new MediaTrack(5, "video", "h264", 640, 360)] };
        var valid = OptionEngine.Derive(new OptionInputs { VideoTrack = 1 }, multi);
        Assert.Equal(640, valid.Options!.FinalWidth);
        Assert.Equal(360, valid.Options.FinalHeight);
        Assert.Contains(OptionEngine.Derive(new OptionInputs { VideoTrack = 1, CropWidth = "800" }, multi).Errors,
            error => error.Field == "cropx");
    }

    [Fact]
    public void PartialCropCoordinatesDeriveCenteredVisibleRectangle()
    {
        var result = OptionEngine.Derive(new OptionInputs { CropWidth = "1280", CropHeight = "720" }, Media);
        Assert.Equal(new CropRect(1280, 720, 320, 180), result.Options!.Crop);
    }

    [Theory]
    [InlineData("319", "180", "0", "0", "cropw")]
    [InlineData("320", "179", "0", "0", "croph")]
    [InlineData("320", "180", "1", "0", "cropx")]
    [InlineData("320", "180", "0", "1", "cropy")]
    public void OddManualCropValuesBlockEncoding(string width, string height, string x, string y, string field)
    {
        var result = OptionEngine.Derive(new OptionInputs
        {
            CropWidth = width, CropHeight = height, CropX = x, CropY = y
        }, Media);
        Assert.Contains(result.Errors, error => error.Field == field && error.Message.Contains("even"));
        Assert.Null(result.Options);
    }

    [Theory]
    [InlineData("319", "", "scalew")]
    [InlineData("", "179", "scaleh")]
    public void OddExplicitScaleDimensionsBlockEncoding(string width, string height, string field)
    {
        var result = OptionEngine.Derive(new OptionInputs { ScaleWidth = width, ScaleHeight = height }, Media);
        Assert.Contains(result.Errors, error => error.Field == field && error.Message.Contains("even"));
        Assert.Null(result.Options);
    }

    [Fact]
    public void CenteredPartialCropKeepsItsOriginEven()
    {
        var source = Media with { Width = 1922, VideoTracks = [new MediaTrack(1, "video", "h264", 1922, 1080)] };
        var result = OptionEngine.Derive(new OptionInputs { CropWidth = "1280" }, source);
        Assert.Equal(new CropRect(1280, 1080, 320, 0), result.Options!.Crop);
    }

    [Fact]
    public void CoordinateOnlyCropUsesRemainingEvenSourceExtent()
    {
        var result = OptionEngine.Derive(new OptionInputs { CropX = "2" }, Media);
        Assert.Equal(new CropRect(1918, 1080, 2, 0), result.Options!.Crop);
        Assert.Equal(1918, result.Options.FinalWidth);
    }

    [Fact]
    public void PartialCropOnOddSourceKeepsImplicitDimensionsEven()
    {
        var source = Media with { Width = 321, Height = 181,
            VideoTracks = [new MediaTrack(1, "video", "h264", 321, 181)] };
        var result = OptionEngine.Derive(new OptionInputs { CropWidth = "320",
            ScaleWidth = "320", ScaleHeight = "180" }, source);
        Assert.Empty(result.Errors);
        Assert.Equal(new CropRect(320, 180, 0, 0), result.Options!.Crop);
    }

    [Fact]
    public void AudioCopyRequiresCompatibleSourceTrack()
    {
        var mp3 = Media with { AudioTracks = [new MediaTrack(2, "audio", "mp3", Channels: 2)] };
        var result = OptionEngine.Derive(new OptionInputs { AudioCodec = "copy" }, mp3);
        Assert.Contains(result.Errors, error => error.Field == "acodec");
        Assert.Null(result.Options);
    }

    [Fact]
    public void OutputNamesAvoidExistingFilesAndUseSelectedContainer()
    {
        var path = OutputPaths.NextAvailable("C:/Videos", "clip-1.mkv", "webm",
            candidate => candidate.Replace('\\', '/') is "C:/Videos/clip.webm" or "C:/Videos/clip-1.webm");
        Assert.Equal("C:/Videos/clip-2.webm", path.Replace('\\', '/'));
    }

    [Fact]
    public void InvalidFragmentCannotProduceEncodeArguments()
    {
        var result = OptionEngine.Derive(new OptionInputs { Start = "90", End = "40" }, Media);
        Assert.Contains(result.Errors, error => error.Field == "end");
        Assert.Null(result.Options);
    }

    [Fact]
    public void CropOutsideSourceIsRejected()
    {
        var result = OptionEngine.Derive(new OptionInputs { CropWidth = "100", CropX = "1900" }, Media);
        Assert.Contains(result.Errors, error => error.Field == "cropx");
    }

    [Fact]
    public void WidthOnlyCropIsCenteredLikeExistingEncoder()
    {
        var result = OptionEngine.Derive(new OptionInputs { CropWidth = "1280", CropHeight = "720" }, Media);
        Assert.Equal(new CropRect(1280, 720, 320, 180), result.Options!.Crop);
    }

    [Fact]
    public void Mp4RejectsVorbisAudioBeforeFfmpegStarts()
    {
        var result = OptionEngine.Derive(new OptionInputs { VideoCodec = "x264", AudioCodec = "vorbis" }, Media);
        Assert.Contains(result.Errors, error => error.Field == "acodec");
    }
}
