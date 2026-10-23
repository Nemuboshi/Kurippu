using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class RuntimeAssetCatalogTests
{
    [Fact]
    public void CatalogContainsAllFourSupportedRids()
    {
        var rids = RuntimeAssetCatalog.All.Select(asset => asset.Rid).ToArray();

        Assert.Equal(["linux-x64", "osx-arm64", "osx-x64", "win-x64"], rids);
    }

    [Fact]
    public void WindowsAssetsUseThePinnedReleaseAndHashes()
    {
        var asset = RuntimeAssetCatalog.Get("win-x64");

        Assert.Equal("https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/", asset.ReleaseUrl);
        Assert.Equal("ffmpeg-x86_64-git-9a861e968.7z", asset.Ffmpeg.FileName);
        Assert.Equal("fc9c135b799b410ffc78517f428904c299bc64cd8144f21f63f84fcd8c35ea19", asset.Ffmpeg.Sha256);
        Assert.Equal("mpv-dev-x86_64-20261021-git-e471496c56.7z", asset.Mpv!.FileName);
        Assert.Equal("efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633", asset.Mpv.Sha256);
    }

    [Theory]
    [InlineData("osx-x64", "https://www.osxexperts.net/ffmpeg80intel.zip",
        "2d24d22db78c87f394a5822867acd5c5dc5e762cd261a44bd26923f3a5af3e07")]
    [InlineData("osx-arm64", "https://www.osxexperts.net/ffmpeg711arm.zip",
        "59e39a5cec2e5d2307ed079c53227a9181e64b87454ed4de998349e044bfdc70")]
    [InlineData("linux-x64",
        "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/autobuild-2026-10-22-18-33/ffmpeg-N-127943-g2668cb6e31-linux64-gpl.tar.xz",
        "500bd0fa35e13c8256ea4a3d29d3bacac1d78ec33370fa5d383bb468b4a5e544")]
    public void NonWindowsAssetsUseThePinnedArchives(string rid, string url, string sha256)
    {
        var asset = RuntimeAssetCatalog.Get(rid);

        Assert.Equal(url, asset.Ffmpeg.Url);
        Assert.Equal(sha256, asset.Ffmpeg.Sha256);
        Assert.Null(asset.Mpv);
    }

    [Fact]
    public void UnsupportedRidIsNotReturned()
    {
        Assert.Throws<PackagingException>(() => RuntimeAssetCatalog.Get("bogus"));
    }
}
