using System.Text;
using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class RuntimeStagerTests
{
    [Fact]
    public async Task WindowsStagesFfmpegAndLibmpvDlls()
    {
        using var fixture = new StagerFixture();
        fixture.CreateWindowsArchives();
        var runner = new FakeProcessRunner();
        var stager = fixture.CreateStager(runner);

        await stager.StageAsync("win-x64", fixture.Output, CancellationToken.None);

        Assert.Equal("ffmpeg", File.ReadAllText(Path.Combine(fixture.Output, "ffmpeg.exe")));
        Assert.Equal("mpv", File.ReadAllText(Path.Combine(fixture.Output, "libmpv-2.dll")));
        Assert.Equal("codec", File.ReadAllText(Path.Combine(fixture.Output, "avcodec-62.dll")));
        var call = Assert.Single(runner.Calls);
        Assert.EndsWith("ffmpeg.exe", call.FileName, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(["-version"], call.Arguments);
    }

    [Fact]
    public async Task MissingLibmpv2DllIsAnError()
    {
        using var fixture = new StagerFixture();
        fixture.CreateWindowsArchives(includeMpvLibrary: false);
        var stager = fixture.CreateStager(new FakeProcessRunner());

        var exception = await Assert.ThrowsAsync<PackagingException>(() =>
            stager.StageAsync("win-x64", fixture.Output, CancellationToken.None));

        Assert.Contains("libmpv-2.dll", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NonzeroFfmpegVersionCheckIsAnError()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var fixture = new StagerFixture();
        fixture.CreateLinuxArchive();
        var runner = new FakeProcessRunner(new ProcessResult(1, "bad", "failed"));
        var stager = fixture.CreateStager(runner);

        var exception = await Assert.ThrowsAsync<PackagingException>(() =>
            stager.StageAsync("linux-x64", fixture.Output, CancellationToken.None));

        Assert.Contains("ffmpeg", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StagerFixture : IDisposable
    {
        private readonly Dictionary<string, string> _sources = [];

        public StagerFixture()
        {
            Root = Path.Combine(Path.GetTempPath(), "kurippu-stager-tests-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Root);
            Output = Path.Combine(Root, "output");
            Directory.CreateDirectory(Output);
        }

        public string Root { get; }
        public string Output { get; }

        public RuntimeStager CreateStager(IProcessRunner runner) =>
            new(new FakeAssetDownloader(_sources), runner);

        public void CreateWindowsArchives(bool includeMpvLibrary = true)
        {
            var ffmpeg = Path.Combine(Root, "ffmpeg.zip");
            TestZipFactory.Write(ffmpeg, ("bin/ffmpeg.exe", Encoding.UTF8.GetBytes("ffmpeg")));
            _sources[RuntimeAssetCatalog.Get("win-x64").Ffmpeg.Url] = ffmpeg;

            var mpv = Path.Combine(Root, "mpv.zip");
            var entries = new List<(string, byte[])>
            {
                ("libmpv-2.dll", Encoding.UTF8.GetBytes("mpv")),
                ("avcodec-62.dll", Encoding.UTF8.GetBytes("codec"))
            };
            if (!includeMpvLibrary) entries.RemoveAt(0);
            TestZipFactory.Write(mpv, entries.ToArray());
            _sources[RuntimeAssetCatalog.Get("win-x64").Mpv!.Url] = mpv;
        }

        public void CreateLinuxArchive()
        {
            var archive = Path.Combine(Root, "ffmpeg.tar.xz");
            TestArchiveFactory.WriteEmbeddedTarXz(archive, TestArchiveFactory.SampleTarXz);
            _sources[RuntimeAssetCatalog.Get("linux-x64").Ffmpeg.Url] = archive;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }
}
