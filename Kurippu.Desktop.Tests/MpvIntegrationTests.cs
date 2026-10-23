using Kurippu.Desktop.Media;

namespace Kurippu.Desktop.Tests;

public class MpvIntegrationTests
{
    [Fact]
    public async Task BundledLibraryCanOpenAndRenderVideoWhenFixtureIsProvided()
    {
        var path = Environment.GetEnvironmentVariable("KURIPPU_TEST_VIDEO");
        if (string.IsNullOrWhiteSpace(path)) return;
        using var player = new MpvPlayer();
        var media = await player.OpenAsync(path, TestContext.Current.CancellationToken);
        Assert.True(media.Width == 320, $"width={media.Width}, video-params/w={player.Property("video-params/w")}, " +
            $"track-list/0/demux-w={player.Property("track-list/0/demux-w")}, track-list/0/type={player.Property("track-list/0/type")}");
        Assert.Equal(180, media.Height);
        player.Play();
        player.SetMuted(true);
        Assert.Equal("yes", player.Property("mute"));
        player.SetMuted(false);
        player.FrameBackStep();
        await Task.Delay(200, TestContext.Current.CancellationToken);
        var frame = player.RenderFrame(320, 180);
        Assert.Equal(320 * 180 * 4, frame.Length);
        Assert.Contains(frame, value => value != 0);
    }
}
