using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Kurippu.Core;
using Kurippu.Desktop.Media;
using Kurippu.Desktop.Theming;
using Kurippu.Desktop.ViewModels;

namespace Kurippu.Desktop.Tests;

public class WorkbenchTests
{
    [Theory]
    [InlineData("System", null)]
    [InlineData("Light", "Light")]
    [InlineData("Dark", "Dark")]
    [InlineData("unknown", null)]
    public void ThemeSelectionResolvesToExplicitOrSystemVariant(string mode, string? expected)
    {
        Assert.Equal(expected, ThemeMode.Resolve(mode)?.Key?.ToString());
    }

    [Theory]
    [InlineData(320, 180, 1, 800, 600, 800, 450)]
    [InlineData(720, 576, 16d / 15, 800, 600, 800, 600)]
    [InlineData(1920, 1080, 1, 600, 600, 600, 337.5)]
    public void VideoStageFitsSelectedTrackDisplayAspect(int width, int height, double sar,
        double availableWidth, double availableHeight, double expectedWidth, double expectedHeight)
    {
        var rect = VideoStage.Fit(new Size(availableWidth, availableHeight), width, height, sar);
        Assert.Equal(expectedWidth, rect.Width, 3);
        Assert.Equal(expectedHeight, rect.Height, 3);
        Assert.Equal((availableWidth - expectedWidth) / 2, rect.X, 3);
        Assert.Equal((availableHeight - expectedHeight) / 2, rect.Y, 3);
    }

    [Fact]
    public void TrackAndSarSwitchRearrangesPreviewAndCropInTheSameSourceRectangle()
    {
        var stage = new VideoStage();
        var preview = new Border();
        var crop = new CropOverlayControl();
        stage.Children.Add(preview);
        stage.Children.Add(crop);
        stage.SetSource(320, 180, 1);
        stage.Measure(new Size(800, 600));
        stage.Arrange(new Rect(0, 0, 800, 600));
        Assert.Equal(preview.Bounds, crop.Bounds);
        Assert.Equal(450, preview.Bounds.Height, 3);

        stage.SetSource(720, 576, 16d / 15);
        crop.SourceWidth = 720;
        crop.SourceHeight = 576;
        crop.Sar = 16d / 15;
        crop.SetCrop(new CropRect(360, 288, 0, 0));
        stage.Arrange(new Rect(0, 0, 800, 600));
        Assert.Equal(preview.Bounds, crop.Bounds);
        Assert.Equal(800, preview.Bounds.Width, 3);
        Assert.Equal(600, preview.Bounds.Height, 3);
        var cropPixels = VideoGeometry.ToViewport(crop.Bounds.Width, crop.Bounds.Height,
            crop.SourceWidth, crop.SourceHeight, crop.Sar, crop.Crop!);
        Assert.Equal(400, cropPixels.Width, 3);
        Assert.Equal(300, cropPixels.Height, 3);
    }

    [Fact]
    public void SubtitleBurnRequiresAnEmbeddedTrackOrExistingExternalFile()
    {
        var media = new MediaInfo("/tmp/video.mp4", 20, 320, 180, 1,
            [new(1, "video", "h264", 320, 180)], [], []);
        Assert.False(SubtitleAvailability.CanBurn(media, 0, ""));
        Assert.False(SubtitleAvailability.CanBurn(media, 0, "missing.srt"));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".srt");
        try
        {
            File.WriteAllText(path, "1\n00:00:00,000 --> 00:00:01,000\nHello");
            Assert.True(SubtitleAvailability.CanBurn(media, 0, path));
        }
        finally { File.Delete(path); }
        Assert.True(SubtitleAvailability.CanBurn(media with { SubtitleTracks = [new(2, "sub", "srt")] }, 1, ""));
        Assert.False(SubtitleAvailability.CanBurn(media with { SubtitleTracks = [new(2, "sub", "srt")] }, 1, "missing.srt"));
    }

    [Fact]
    public void ResolvedSourceTitleFillsBlankProbeTitle()
    {
        Assert.Equal("Downloaded clip", SourceTitle.DisplayTitle("", "Downloaded clip"));
        Assert.Equal("Downloaded clip", SourceTitle.DisplayTitle("source.mp4", "Downloaded clip"));
        Assert.Equal("Probed clip", SourceTitle.DisplayTitle("Probed clip", null));
    }

    [Theory]
    [InlineData(Key.OemOpenBrackets, "start")]
    [InlineData(Key.OemCloseBrackets, "end")]
    [InlineData(Key.F, null)]
    public void PlaybackMarkShortcutsExcludeFullscreen(Key key, string? expected) =>
        Assert.Equal(expected, PlaybackShortcuts.MarkFor(key));
}
