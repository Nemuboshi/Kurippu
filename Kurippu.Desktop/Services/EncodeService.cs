using System.Globalization;
using System.Text.RegularExpressions;
using Kurippu.Core;

namespace Kurippu.Desktop.Services;

public sealed record EncodeUpdate(string Line, double Percent);
public sealed record PreviewSource(string Path, double? Time, int Width, int Height, double Sar, double Fps);

public sealed class EncodeService(IProcessRunner runner, string ffmpeg)
{
    public async Task EncodeAsync(EncodeOptions options, string output, string title,
        bool twoPass, bool test, IProgress<EncodeUpdate>? progress, CancellationToken cancellationToken,
        PreviewSource? preview = null, IReadOnlyList<string>? rawArguments = null)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Output path is required", nameof(output));
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var passlog = Path.Combine(directory, "pass-0.log");
        var current = 0.0;
        var lastFrame = 0;
        var totalFrames = Math.Max(1, options.Duration * 25);
        var pass = 0;
        var log = new Progress<string>(line =>
        {
            var match = Regex.Match(line, @"^frame=\s*(\d+)");
            if (match.Success && int.TryParse(match.Groups[1].Value, out var frame))
            {
                current = Math.Min(99, current + (frame - lastFrame) / totalFrames *
                    (pass == 1 ? 30 : pass == 2 ? 70 : 100));
                lastFrame = frame;
            }
            progress?.Report(new(line, current));
        });
        try
        {
            var raw = rawArguments?.ToArray() ?? FfmpegArguments.Build(options);
            if (test)
            {
                var result = await runner.RunAsync(ffmpeg, FfmpegArguments.BuildTest(raw, output), log, cancellationToken);
                if (result != 0) throw new InvalidOperationException($"Test encode failed ({result})");
                progress?.Report(new($"Test encode done: {output}", 100));
                return;
            }
            var container = options.VideoCodec == "x264" ? "mp4" : "webm";
            var main = preview is null ? output : Path.Combine(directory, "main." + container);
            var count = twoPass ? 2 : 1;
            for (var index = 1; index <= count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pass = twoPass ? index : 0;
                lastFrame = 0;
                var args = FfmpegArguments.BuildPass(raw, pass, title, main, passlog);
                var exit = await runner.RunAsync(ffmpeg, args, log, cancellationToken);
                if (exit != 0) throw new InvalidOperationException($"FFmpeg pass {index} failed ({exit})");
            }
            if (preview is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var frame = Path.Combine(directory, "preview." + container);
                var list = Path.Combine(directory, "preview.txt");
                var codec = options.VideoCodec;
                var result = await runner.RunAsync(ffmpeg, FfmpegArguments.BuildPreview(preview.Path,
                    preview.Time, codec, container, frame, preview.Width, preview.Height,
                    preview.Sar, preview.Fps), log, cancellationToken);
                if (result != 0) throw new InvalidOperationException($"Preview encode failed ({result})");
                await File.WriteAllTextAsync(list, FfmpegArguments.ConcatList(frame, main), cancellationToken);
                result = await runner.RunAsync(ffmpeg, FfmpegArguments.BuildConcat(list, main,
                    options.HasAudio, preview.Fps, container, output), log, cancellationToken);
                if (result != 0) throw new InvalidOperationException($"Preview concat failed ({result})");
            }
            progress?.Report(new($"Output: {output}", 100));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
