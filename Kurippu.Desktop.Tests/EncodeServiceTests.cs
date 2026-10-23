using Kurippu.Core;
using Kurippu.Desktop.Services;

namespace Kurippu.Desktop.Tests;

public class EncodeServiceTests
{
    [Fact]
    public async Task ManualArgumentsReachEncodeAndTestInsteadOfGeneratedArguments()
    {
        var runner = new Runner();
        var service = new EncodeService(runner, "ffmpeg");
        var options = new EncodeOptions { InputPath = "/tmp/in.mkv", Duration = 10 };
        await service.EncodeAsync(options, "/tmp/out.mp4", "test", false, false, null, CancellationToken.None,
            rawArguments: ["-i", "file:/tmp/other path.mkv", "-c:v", "libx264", "-crf", "15", "-f", "mp4"]);
        Assert.Contains("file:/tmp/other path.mkv", runner.Calls[0], StringComparer.Ordinal);
        Assert.DoesNotContain("file:/tmp/in.mkv", runner.Calls[0], StringComparer.Ordinal);
        await service.EncodeAsync(options, "/tmp/test.mkv", "test", false, true, null, CancellationToken.None,
            rawArguments: ["-i", "file:/tmp/other path.mkv", "-c:v", "libx264", "-f", "mp4"]);
        Assert.Contains("file:/tmp/other path.mkv", runner.Calls[1], StringComparer.Ordinal);
    }
    private sealed class Runner : IProcessRunner
    {
        public List<string[]> Calls { get; } = [];
        public Task<int> RunAsync(string executable, IReadOnlyList<string> args, IProgress<string>? log,
            CancellationToken cancellationToken)
        {
            Calls.Add(args.ToArray());
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task DetectionReadsProcessOutputAndPreservesNoResult()
    {
        var runner = new DetectRunner("lavfi.cropdetect.w=640\nlavfi.cropdetect.h=400\n" +
            "lavfi.cropdetect.x=0\nlavfi.cropdetect.y=40");
        var service = new DetectionService(runner, "ffmpeg");
        Assert.Equal(new CropRect(640, 400, 0, 40), await service.DetectCropAsync("/tmp/in.mkv", 2, 0, CancellationToken.None));
        Assert.Equal("7", runner.LastArgs![Array.IndexOf(runner.LastArgs, "-ss") + 1]);
        Assert.False(await service.DetectInterlaceAsync("/tmp/in.mkv", 0, 0, CancellationToken.None));
    }

    private sealed class DetectRunner(string output) : IProcessRunner
    {
        public string[]? LastArgs { get; private set; }
        public Task<int> RunAsync(string executable, IReadOnlyList<string> args, IProgress<string>? log,
            CancellationToken cancellationToken)
        {
            LastArgs = args.ToArray();
            foreach (var line in output.Split('\n')) log?.Report(line);
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task TwoPassRunsInOrderAndPublishesOutputOnlyOnSecondPass()
    {
        var runner = new Runner();
        var service = new EncodeService(runner, "ffmpeg");
        var options = new EncodeOptions { InputPath = "/tmp/input.mkv", VideoCodec = "vp9", Duration = 10 };
        await service.EncodeAsync(options, "/tmp/output.webm", "test", twoPass: true,
            test: false, null, CancellationToken.None);

        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal("null", runner.Calls[0][Array.LastIndexOf(runner.Calls[0], "-f") + 1]);
        Assert.Equal("file:/tmp/output.webm", runner.Calls[1][^1]);
    }

    [Fact]
    public async Task TestEncodeWritesToRequestedPathRatherThanDeletedScratchDirectory()
    {
        var runner = new Runner();
        await new EncodeService(runner, "ffmpeg").EncodeAsync(
            new EncodeOptions { InputPath = "/tmp/input.mkv", Duration = 10 },
            "/tmp/preview-test.mkv", "test", false, true, null, CancellationToken.None);
        Assert.Equal("file:/tmp/preview-test.mkv", runner.Calls.Single()[^1]);
    }

    [Fact]
    public async Task PreviewRunsAfterMainAndConcatenatesIntoRequestedOutput()
    {
        var runner = new Runner();
        var preview = new PreviewSource("/tmp/image's clip.mp4", 2, 320, 180, 1, 25);
        await new EncodeService(runner, "ffmpeg").EncodeAsync(
            new EncodeOptions { InputPath = "/tmp/input.mkv", Duration = 10 },
            "/tmp/final.mp4", "test", false, false, null, CancellationToken.None, preview);
        Assert.Equal(3, runner.Calls.Count);
        Assert.Contains("-frames:v", runner.Calls[1], StringComparer.Ordinal);
        Assert.Contains("concat", runner.Calls[2], StringComparer.Ordinal);
        Assert.Equal("file:/tmp/final.mp4", runner.Calls[2][^1]);
    }
}
