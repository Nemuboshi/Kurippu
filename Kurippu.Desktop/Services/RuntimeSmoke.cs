using Kurippu.Core;
using Kurippu.Desktop.Media;

namespace Kurippu.Desktop.Services;

public static class RuntimeSmoke
{
    public static async Task RunAsync()
    {
        var ffmpeg = ToolLocator.Default.Ffmpeg() ?? throw new FileNotFoundException("Bundled FFmpeg is missing");
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var video = Path.Combine(directory, "source.mp4");
        try
        {
            await CreateFixtureAsync(ffmpeg, video, 1).ConfigureAwait(false);
            using var player = new MpvPlayer();
            var media = await player.OpenAsync(video).ConfigureAwait(false);
            if (media.Width != 320 || media.Height != 180) throw new InvalidDataException("libmpv probe mismatch");
            await VerifyPlaybackAsync(player).ConfigureAwait(false);
            var detectionVideo = Path.Combine(directory, "detect.mp4");
            await CreateFixtureAsync(ffmpeg, detectionVideo, 7).ConfigureAwait(false);
            var detected = await new DetectionService(new ProcessRunner(), ffmpeg).DetectCropAsync(
                detectionVideo, 0, 0, CancellationToken.None).ConfigureAwait(false);
            if (detected is null || detected.Width != 320 || detected.Height != 180)
                throw new InvalidDataException("FFmpeg crop detection failed");
            var options = new EncodeOptions
            {
                InputPath = video,
                HasAudio = false,
                Duration = 1,
                VideoCodec = "x264",
                FinalWidth = 320,
                FinalHeight = 180
            };
            var output = Path.Combine(directory, "output.mp4");
            var encoder = new EncodeService(new ProcessRunner(), ffmpeg);
            await VerifyEncodingAsync(encoder, options, output, video).ConfigureAwait(false);
            var test = Path.Combine(directory, "test.mkv");
            await encoder.EncodeAsync(options, test, "smoke", false, true, null,
                CancellationToken.None).ConfigureAwait(false);
            if (!File.Exists(test)) throw new InvalidDataException("Test output is missing");
            await VerifyGraphTestAsync(ffmpeg, video, directory).ConfigureAwait(false);
            var preview = Path.Combine(directory, "preview.mp4");
            await VerifyPreviewAsync(encoder, options, video, preview).ConfigureAwait(false);
            var twoPass = Path.Combine(directory, "two-pass.mp4");
            await encoder.EncodeAsync(options, twoPass, "smoke", true, false, null,
                CancellationToken.None).ConfigureAwait(false);
            if (!File.Exists(twoPass)) throw new InvalidDataException("Two-pass output is missing");
            Console.WriteLine("Kurippu runtime smoke passed");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static async Task CreateFixtureAsync(string ffmpeg, string output, int seconds)
    {
        var args = new[] { "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i",
            "testsrc=size=320x180:rate=25", "-t", seconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture), "-c:v", "mpeg4", output };
        var exit = await new ProcessRunner().RunAsync(ffmpeg, args, null, CancellationToken.None)
            .ConfigureAwait(false);
        if (exit != 0 || !File.Exists(output))
            throw new InvalidOperationException("FFmpeg fixture encode failed");
    }

    private static async Task VerifyPlaybackAsync(MpvPlayer player)
    {
        player.Play();
        player.Seek(.5);
        await Task.Delay(200).ConfigureAwait(false);
        var frame = player.RenderFrame(320, 180);
        if (frame.Length != 320 * 180 * 4 || frame.All(value => value == 0))
            throw new InvalidDataException("libmpv software frame is missing");
        player.Pause();
        player.SetMuted(true);
        if (!string.Equals(player.GetProperty("mute"), "yes", StringComparison.Ordinal))
            throw new InvalidDataException("libmpv mute failed");
        player.SetMuted(false);
        player.FrameBackStep();
    }

    private static async Task VerifyEncodingAsync(EncodeService encoder, EncodeOptions options,
        string output, string source)
    {
        await encoder.EncodeAsync(options, output, "smoke", false, false, null,
            CancellationToken.None).ConfigureAwait(false);
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
            throw new InvalidDataException($"FFmpeg output is missing for {source}");
    }

    private static async Task VerifyGraphTestAsync(string ffmpeg, string source, string directory)
    {
        var output = Path.Combine(directory, "graph-test.mkv");
        var args = FfmpegArguments.BuildTest(["-i", "file:" + source, "-i", "file:" + source,
            "-filter_complex", "[0:v][1:v]overlay[v]", "-map", "[v]", "-c:v", "libx264",
            "-f", "mp4"], output);
        if (await new ProcessRunner().RunAsync(ffmpeg, args, null, CancellationToken.None)
                .ConfigureAwait(false) != 0 || !File.Exists(output))
            throw new InvalidDataException("Complex filter test encode failed");
    }

    private static async Task VerifyPreviewAsync(EncodeService encoder, EncodeOptions options,
        string source, string output)
    {
        await encoder.EncodeAsync(options, output, "smoke", false, false, null,
            CancellationToken.None, new PreviewSource(source, .5, 320, 180, 1, 25))
            .ConfigureAwait(false);
        if (!File.Exists(output)) throw new InvalidDataException("Preview output is missing");
        using var player = new MpvPlayer();
        var info = await player.OpenAsync(output).ConfigureAwait(false);
        if (info.Duration < .8)
            throw new InvalidDataException("Preview concat omitted the main movie");
    }
}
