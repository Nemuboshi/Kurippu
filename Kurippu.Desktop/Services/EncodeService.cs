using System.Globalization;
using System.Text.RegularExpressions;
using Kurippu.Core;

namespace Kurippu.Desktop.Services;

public sealed partial class EncodeService(IProcessRunner runner, string ffmpeg)
{
    public async Task EncodeAsync(EncodeOptions options, string output, string title,
        bool twoPass, bool test, IProgress<EncodeUpdate>? progress, CancellationToken cancellationToken,
        PreviewSource? preview = null, IReadOnlyList<string>? rawArguments = null)
    {
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Output path is required", nameof(output));
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var current = 0.0;
        var lastFrame = 0;
        var totalFrames = Math.Max(1, options.Duration * 25);
        var pass = 0;
        var log = new Progress<string>(line =>
        {
            var match = FrameRegex().Match(line);
            if (match.Success && int.TryParse(match.Groups["frame"].Value,
                    CultureInfo.InvariantCulture, out var frame))
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
                await RunTestAsync(raw, output, log, progress, cancellationToken).ConfigureAwait(false);
                return;
            }
            var container = string.Equals(options.VideoCodec, "x264", StringComparison.Ordinal) ? "mp4" : "webm";
            var main = preview is null ? output : Path.Combine(directory, "main." + container);
            await RunPassesAsync(raw, title, main, twoPass, directory, log,
                value => { pass = value; lastFrame = 0; }, cancellationToken).ConfigureAwait(false);
            if (preview is not null)
                await AddPreviewAsync(options, preview, output, main, directory, container, log,
                    cancellationToken).ConfigureAwait(false);
            progress?.Report(new($"Output: {output}", 100));
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private async Task RunTestAsync(string[] raw, string output,
        IProgress<string>? log, IProgress<EncodeUpdate>? progress,
        CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(ffmpeg, FfmpegArguments.BuildTest(raw, output), log,
            cancellationToken).ConfigureAwait(false);
        if (result != 0) throw new InvalidOperationException($"Test encode failed ({result})");
        progress?.Report(new($"Test encode done: {output}", 100));
    }

    private async Task RunPassesAsync(string[] raw, string title, string main,
        bool twoPass, string directory, IProgress<string>? log, Action<int> onPassStarted,
        CancellationToken cancellationToken)
    {
        var passlog = Path.Combine(directory, "pass-0.log");
        var count = twoPass ? 2 : 1;
        for (var index = 1; index <= count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pass = twoPass ? index : 0;
            onPassStarted(pass);
            var args = FfmpegArguments.BuildPass(raw, pass, title, main, passlog);
            var exit = await runner.RunAsync(ffmpeg, args, log, cancellationToken)
                .ConfigureAwait(false);
            if (exit != 0)
                throw new InvalidOperationException($"FFmpeg pass {index} failed ({exit})");
        }
    }

    private async Task AddPreviewAsync(EncodeOptions options, PreviewSource preview,
        string output, string main, string directory, string container,
        IProgress<string>? log, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var frame = Path.Combine(directory, "preview." + container);
        var list = Path.Combine(directory, "preview.txt");
        var result = await runner.RunAsync(ffmpeg, FfmpegArguments.BuildPreview(preview.Path,
            preview.Time, options.VideoCodec, container, frame, preview.Width, preview.Height,
            preview.Sar, preview.Fps), log, cancellationToken).ConfigureAwait(false);
        if (result != 0) throw new InvalidOperationException($"Preview encode failed ({result})");
        await File.WriteAllTextAsync(list, FfmpegArguments.ConcatList(frame, main),
            cancellationToken).ConfigureAwait(false);
        result = await runner.RunAsync(ffmpeg, FfmpegArguments.BuildConcat(list, main,
            options.HasAudio, preview.Fps, container, output), log,
            cancellationToken).ConfigureAwait(false);
        if (result != 0) throw new InvalidOperationException($"Preview concat failed ({result})");
    }

    [GeneratedRegex(@"^frame=\s*(?<frame>\d+)", RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex FrameRegex();
}
