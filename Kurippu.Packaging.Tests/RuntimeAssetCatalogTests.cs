using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class RuntimeAssetCatalogTests
{
    [Fact]
    public void Catalog_contains_all_four_supported_rids()
    {
        var rids = RuntimeAssetCatalog.All.Select(asset => asset.Rid).ToArray();

        Assert.Equal(["linux-x64", "osx-arm64", "osx-x64", "win-x64"], rids);
    }

    [Fact]
    public void Windows_assets_use_the_pinned_release_and_hashes()
    {
        var asset = RuntimeAssetCatalog.Get("win-x64");

        Assert.Equal("https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/", asset.ReleaseUrl);
        Assert.Equal("ffmpeg-x86_64-git-9a861e968.7z", asset.Ffmpeg.FileName);
        Assert.Equal("fc9c135b799b410ffc78517f428904c299bc64cd8144f21f63f84fcd8c35ea19", asset.Ffmpeg.Sha256);
        Assert.Equal("mpv-dev-x86_64-20261021-git-e471496c56.7z", asset.Mpv!.FileName);
        Assert.Equal("efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633", asset.Mpv.Sha256);
    }

    [Theory]
    [InlineData("osx-x64", "https://evermeet.cx/ffmpeg/ffmpeg-7.1.1.zip",
        "8d7917c1cebd7a29e68c0a0a6cc4ecc3fe05c7fffed958636c7018b319afdda4")]
    [InlineData("osx-arm64", "https://www.osxexperts.net/ffmpeg711arm.zip",
        "59e39a5cec2e5d2307ed079c53227a9181e64b87454ed4de998349e044bfdc70")]
    [InlineData("linux-x64", "https://johnvansickle.com/ffmpeg/releases/ffmpeg-7.0.2-amd64-static.tar.xz",
        "abda8d77ce8309141f83ab8edf0596834087c52467f6badf376a6a2a4c87cf67")]
    public void Non_windows_assets_use_the_pinned_archives(string rid, string url, string sha256)
    {
        var asset = RuntimeAssetCatalog.Get(rid);

        Assert.Equal(url, asset.Ffmpeg.Url);
        Assert.Equal(sha256, asset.Ffmpeg.Sha256);
        Assert.Null(asset.Mpv);
    }

    [Fact]
    public void Unsupported_rid_is_not_returned()
    {
        Assert.Throws<PackagingException>(() => RuntimeAssetCatalog.Get("bogus"));
    }
}
