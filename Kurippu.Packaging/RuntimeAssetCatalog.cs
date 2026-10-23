namespace Kurippu.Packaging;

public sealed record RuntimeAssetFile(string FileName, string Url, string Sha256);

public sealed record RuntimeAsset(string Rid, string ReleaseUrl, RuntimeAssetFile Ffmpeg, RuntimeAssetFile? Mpv);

public static class RuntimeAssetCatalog
{
    private const string WindowsReleaseUrl =
        "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/";

    public static IReadOnlyList<RuntimeAsset> All { get; } =
    [
        new("linux-x64", "", new(
            "ffmpeg-7.0.2-amd64-static.tar.xz",
            "https://johnvansickle.com/ffmpeg/releases/ffmpeg-7.0.2-amd64-static.tar.xz",
            "abda8d77ce8309141f83ab8edf0596834087c52467f6badf376a6a2a4c87cf67"), null),
        new("osx-arm64", "", new(
            "ffmpeg711arm.zip",
            "https://www.osxexperts.net/ffmpeg711arm.zip",
            "59e39a5cec2e5d2307ed079c53227a9181e64b87454ed4de998349e044bfdc70"), null),
        new("osx-x64", "", new(
            "ffmpeg-7.1.1.zip",
            "https://evermeet.cx/ffmpeg/ffmpeg-7.1.1.zip",
            "8d7917c1cebd7a29e68c0a0a6cc4ecc3fe05c7fffed958636c7018b319afdda4"), null),
        new("win-x64", WindowsReleaseUrl, new(
            "ffmpeg-x86_64-git-9a861e968.7z",
            WindowsReleaseUrl + "ffmpeg-x86_64-git-9a861e968.7z",
            "fc9c135b799b410ffc78517f428904c299bc64cd8144f21f63f84fcd8c35ea19"), new(
            "mpv-dev-x86_64-20261021-git-e471496c56.7z",
            WindowsReleaseUrl + "mpv-dev-x86_64-20261021-git-e471496c56.7z",
            "efc719eff3dc5954b4a5efc800632808bcec01cc9034940dd14e5eb7fe9ea633"))
    ];

    public static RuntimeAsset Get(string rid) =>
        All.FirstOrDefault(asset => string.Equals(asset.Rid, rid, StringComparison.Ordinal))
        ?? throw new PackagingException($"Unsupported runtime identifier: {rid}");
}
