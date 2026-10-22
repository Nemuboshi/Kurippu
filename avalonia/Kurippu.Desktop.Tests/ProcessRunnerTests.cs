using Kurippu.Desktop.Services;

namespace Kurippu.Desktop.Tests;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunsExecutableWithoutShellAndReportsExit()
    {
        var runner = new ProcessRunner();
        var lines = new List<string>();
        var result = await runner.RunAsync("dotnet", ["--version"],
            new Progress<string>(line => lines.Add(line)), CancellationToken.None);
        Assert.Equal(0, result);
    }

    [Fact]
    public void MissingOptionalYtDlpDoesNotHideBundledFfmpeg()
    {
        var locator = new ToolLocator("/missing/runtime", searchPath: false);
        Assert.Null(locator.YtDlp("/missing/yt-dlp"));
        Assert.Null(locator.Ffmpeg("/missing/ffmpeg"));
    }

    [Fact]
    public void FfmpegNeverFallsBackToArbitraryPathExecutable()
    {
        var locator = new ToolLocator("/missing/runtime");
        Assert.Null(locator.Ffmpeg("/missing/ffmpeg"));
    }
}
