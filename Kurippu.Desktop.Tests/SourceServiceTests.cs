using Kurippu.Desktop.Services;

namespace Kurippu.Desktop.Tests;

public class SourceServiceTests
{
    private sealed class Runner(string json) : IProcessRunner
    {
        public List<string[]> Calls { get; } = [];
        public Task<int> RunAsync(string executable, IReadOnlyList<string> args, IProgress<string>? log,
            CancellationToken cancellationToken)
        {
            Calls.Add(args.ToArray());
            if (Calls.Count == 1) log?.Report(json);
            else
            {
                var output = args[Array.IndexOf(args.ToArray(), "--output") + 1];
                File.WriteAllText(output.Replace("%(ext)s", "en.vtt"), "subtitle");
                File.WriteAllText(output.Replace("%(ext)s", "mkv"), "fixture");
            }
            return Task.FromResult(0);
        }
    }

    [Fact]
    public async Task ResolveOffersSeparateVideoAndAudioChoices()
    {
        var runner = new Runner("""{"title":"Clip","webpage_url":"https://example.org/video","formats":[{"format_id":"v1","vcodec":"vp9","acodec":"none","height":1080},{"format_id":"a1","vcodec":"none","acodec":"opus","abr":128}]}""");
        var source = await new SourceService(runner, "yt-dlp", null).ResolveAsync("https://example.org/video", CancellationToken.None);
        Assert.Equal("v1", source.Videos.Single().Id);
        Assert.Equal("a1", source.Audios.First().Id);
        Assert.Equal("none", source.Audios.Last().Id);
        Assert.Contains("--dump-json", runner.Calls[0]);
    }

    [Fact]
    public async Task ResolveRetainsSuggestedFileNameAndTitle()
    {
        var runner = new Runner("""{"title":"Clip title","_filename":"clip-4.mkv","formats":[{"format_id":"v1","vcodec":"vp9"}]}""");
        var choice = await new SourceService(runner, "yt-dlp", null).ResolveAsync("https://example.org/video", CancellationToken.None);
        Assert.Equal("Clip title", choice.Title);
        Assert.Equal("clip-4.mkv", choice.SaveAs);
    }

    [Fact]
    public async Task NonEnglishSubtitlesDefaultToNone()
    {
        var runner = new Runner("""{"requested_subtitles":{"fr":{}},"formats":[{"format_id":"v1","vcodec":"vp9"}]}""");
        var choice = await new SourceService(runner, "yt-dlp", null).ResolveAsync("https://example.org/video", CancellationToken.None);
        Assert.Equal("none", choice.Subtitles[choice.DefaultSubtitleIndex].Id);
    }

    [Fact]
    public async Task DownloadUsesSelectedFormatAndFfmpegWithoutShell()
    {
        var runner = new Runner("""{"formats":[{"format_id":"v1","vcodec":"vp9","acodec":"none"}]}""");
        var service = new SourceService(runner, "yt-dlp", "/tools/ffmpeg");
        await service.ResolveAsync("https://example.org/video", CancellationToken.None);
        var path = await service.DownloadAsync("https://example.org/video", "v1", "a1", "none", null,
            CancellationToken.None);
        try
        {
            Assert.Contains("v1+a1", runner.Calls[1]);
            Assert.Contains("/tools/ffmpeg", runner.Calls[1]);
            Assert.Equal("https://example.org/video", runner.Calls[1][^1]);
            Assert.EndsWith(".mkv", path);
        }
        finally { service.Release(path); }
        Assert.False(File.Exists(path));
    }
}
