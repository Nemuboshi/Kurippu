using System.Collections.Concurrent;
using Kurippu.Core;

namespace Kurippu.Desktop.Services;

public sealed class DetectionService(IProcessRunner runner, string ffmpeg)
{
    public async Task<CropRect?> DetectCropAsync(string path, double start, int track, CancellationToken cancellationToken) =>
        FfmpegDetection.ParseCrop(await RunAsync(FfmpegDetection.CropArgs(path, start, track),
            cancellationToken).ConfigureAwait(false));

    public async Task<bool> DetectInterlaceAsync(string path, double start, int track, CancellationToken cancellationToken) =>
        FfmpegDetection.ParseInterlace(await RunAsync(FfmpegDetection.InterlaceArgs(path, start, track),
            cancellationToken).ConfigureAwait(false));

    private async Task<string> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var lines = new ConcurrentQueue<string>();
        var result = await runner.RunAsync(ffmpeg, args, new InlineProgress(line => lines.Enqueue(line)),
            cancellationToken).ConfigureAwait(false);
        if (result != 0) throw new InvalidOperationException($"FFmpeg detection failed ({result})");
        return string.Join('\n', lines);
    }

    private sealed class InlineProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }
}
