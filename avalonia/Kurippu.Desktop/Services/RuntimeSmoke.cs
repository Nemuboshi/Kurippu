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
            var args = new[] { "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i",
                "testsrc=size=320x180:rate=25", "-t", "1", "-c:v", "mpeg4", video };
            var exit = await new ProcessRunner().RunAsync(ffmpeg, args, null, CancellationToken.None);
            if (exit != 0 || !File.Exists(video)) throw new InvalidOperationException("FFmpeg smoke encode failed");
            using var player = new MpvPlayer();
            var media = await player.OpenAsync(video);
            if (media.Width != 320 || media.Height != 180) throw new InvalidDataException("libmpv probe mismatch");
            player.Play();
            player.Seek(.5);
            await Task.Delay(200);
            var frame = player.RenderFrame(320, 180);
            if (frame.Length != 320 * 180 * 4 || frame.All(value => value == 0))
                throw new InvalidDataException("libmpv software frame is missing");
            player.Pause();
            player.SetMuted(true);
            if (player.Property("mute") != "yes") throw new InvalidDataException("libmpv mute failed");
            player.SetMuted(false);
            player.FrameBackStep();
            var detectionVideo = Path.Combine(directory, "detect.mp4");
            var detectArgs = new[] { "-hide_banner", "-nostdin", "-y", "-f", "lavfi", "-i",
                "testsrc=size=320x180:rate=25", "-t", "7", "-c:v", "mpeg4", detectionVideo };
            if (await new ProcessRunner().RunAsync(ffmpeg, detectArgs, null, CancellationToken.None) != 0)
                throw new InvalidOperationException("Detection fixture encode failed");
            var detected = await new DetectionService(new ProcessRunner(), ffmpeg).DetectCropAsync(
                detectionVideo, 0, 0, CancellationToken.None);
            if (detected is null || detected.Width != 320 || detected.Height != 180)
                throw new InvalidDataException("FFmpeg crop detection failed");
            var options = new EncodeOptions { InputPath = video, HasAudio = false,
                Duration = 1, VideoCodec = "x264", FinalWidth = 320, FinalHeight = 180 };
            var output = Path.Combine(directory, "output.mp4");
            var encoder = new EncodeService(new ProcessRunner(), ffmpeg);
            await encoder.EncodeAsync(options, output,
                "smoke", false, false, null, CancellationToken.None);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidDataException("FFmpeg output is missing");
            var test = Path.Combine(directory, "test.mkv");
            await encoder.EncodeAsync(options, test, "smoke", false, true, null, CancellationToken.None);
            if (!File.Exists(test)) throw new InvalidDataException("Test output is missing");
            var graphTest = Path.Combine(directory, "graph-test.mkv");
            var graphArgs = FfmpegArguments.BuildTest(["-i", "file:" + video, "-i", "file:" + video,
                "-filter_complex", "[0:v][1:v]overlay[v]", "-map", "[v]", "-c:v", "libx264",
                "-f", "mp4"], graphTest);
            if (await new ProcessRunner().RunAsync(ffmpeg, graphArgs, null, CancellationToken.None) != 0 ||
                !File.Exists(graphTest)) throw new InvalidDataException("Complex filter test encode failed");
            var preview = Path.Combine(directory, "preview.mp4");
            await encoder.EncodeAsync(options, preview, "smoke", false, false, null,
                CancellationToken.None, new PreviewSource(video, .5, 320, 180, 1, 25));
            if (!File.Exists(preview)) throw new InvalidDataException("Preview output is missing");
            using (var previewPlayer = new MpvPlayer())
            {
                var previewInfo = await previewPlayer.OpenAsync(preview);
                if (previewInfo.Duration < .8) throw new InvalidDataException("Preview concat omitted the main movie");
            }
            var twoPass = Path.Combine(directory, "two-pass.mp4");
            await encoder.EncodeAsync(options, twoPass, "smoke", true, false, null, CancellationToken.None);
            if (!File.Exists(twoPass)) throw new InvalidDataException("Two-pass output is missing");
            Console.WriteLine("Kurippu runtime smoke passed");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
