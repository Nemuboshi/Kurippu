using Kurippu.Core;
using Kurippu.Desktop.ViewModels;

namespace Kurippu.Desktop.Tests;

public class EditorViewModelTests
{
    [Fact]
    public void ManualRawArgsSurviveRefreshUntilGeneratedOptionsChange()
    {
        var media = new MediaInfo("/tmp/a.mkv", 60, 640, 480, 1,
            [new(1, "video", "h264", 640, 480)], [], []);
        var editor = new EditorViewModel(media);
        editor.RawArguments = "-i custom";
        editor.Refresh();
        Assert.Equal("-i custom", editor.RawArguments);
        editor.Options.Quality = "19";
        editor.Refresh();
        Assert.Contains("-crf 19", editor.RawArguments);
    }

    [Fact]
    public void PartialCropFieldsExposeEffectiveCenteredPreviewCrop()
    {
        var media = new MediaInfo("/tmp/a.mkv", 60, 640, 480, 1,
            [new(1, "video", "h264", 640, 480)], [], []);
        var editor = new EditorViewModel(media);
        editor.Options.CropWidth = "320";
        editor.Options.CropHeight = "240";
        editor.Refresh();
        Assert.Equal(new CropRect(320, 240, 160, 120), editor.VisibleCrop);
    }

    [Fact]
    public void UnrelatedSubtitleErrorDoesNotEraseTheVisibleCrop()
    {
        var editor = CreateEditor();
        editor.Options.CropWidth = "320";
        editor.Options.CropHeight = "240";
        editor.Refresh();
        var selected = editor.VisibleCrop;
        editor.Options.BurnSubtitles = true;
        editor.Refresh();
        Assert.False(editor.CanEncode);
        Assert.Equal(selected, editor.VisibleCrop);

        editor.Options.CropWidth = "";
        editor.Options.CropHeight = "";
        editor.Refresh();
        Assert.Null(editor.VisibleCrop);
    }

    [Fact]
    public void EncodingModesKeepCrfAndLimitExclusive()
    {
        var editor = CreateEditor();
        editor.SetCrfMode(true);
        Assert.True(editor.Options.CrfMode);
        Assert.False(editor.Options.LimitMode);
        Assert.Equal("0", editor.Options.Limit);
        editor.SetLimitMode(true);
        Assert.False(editor.Options.CrfMode);
        Assert.True(editor.Options.LimitMode);
        Assert.True(editor.Options.TwoPass);
        Assert.Equal("", editor.Options.Limit);
        editor.SetLimitMode(false);
        Assert.False(editor.Options.TwoPass);
    }

    [Fact]
    public void CodecChangeDropsUnavailableAudioCopyAndClearsOldBitrate()
    {
        var media = CreateEditor().Media with { AudioTracks = [new MediaTrack(2, "audio", "mp3", Channels: 2)] };
        var editor = new EditorViewModel(media);
        editor.Options.AudioCodec = "copy";
        editor.Options.AudioBitrate = "300";
        editor.SelectAudioCodec("copy");
        Assert.Equal("aac", editor.Options.AudioCodec);
        Assert.Equal("", editor.Options.AudioBitrate);
    }

    private static EditorViewModel CreateEditor() => new(new MediaInfo("/tmp/a.mkv", 60, 640, 480, 1,
        [new(1, "video", "h264", 640, 480)], [new(2, "audio", "aac", Channels: 2)], []));

    [Fact]
    public void DetectionSynchronizesOptionsBeforeRegisteringItsCancellation()
    {
        using var detection = new DetectionSession();
        var synced = false;
        var token = detection.Begin(() => { synced = true; detection.CancelForEdit(); });
        Assert.True(synced);
        Assert.False(token.IsCancellationRequested);
        detection.CancelForEdit();
        Assert.True(token.IsCancellationRequested);
    }
    [Fact]
    public void InvalidFragmentDisablesEncodeWithoutDiscardingMedia()
    {
        var media = new MediaInfo("/tmp/a.mkv", 60, 640, 480, 1,
            [new(1, "video", "h264", 640, 480)], [], []);
        var editor = new EditorViewModel(media);
        editor.Options.End = "70";
        editor.Refresh();
        Assert.False(editor.CanEncode);
        Assert.Same(media, editor.Media);
        Assert.Contains(editor.Errors, error => error.Field == "end");
    }

    [Fact]
    public void SourceFlowMovesThroughProbeToEditorAndCanReturn()
    {
        var model = new MainWindowViewModel();
        model.BeginProbe();
        Assert.Equal("info", model.Page);
        var media = new MediaInfo("/tmp/a.mkv", 60, 640, 480, 1,
            [new(1, "video", "h264", 640, 480)], [], []);
        model.SetMedia(media);
        Assert.Equal("encoder", model.Page);
        Assert.NotNull(model.Editor);
        model.ChangeSource();
        Assert.Equal("source", model.Page);
        Assert.Null(model.Editor);
    }
}
