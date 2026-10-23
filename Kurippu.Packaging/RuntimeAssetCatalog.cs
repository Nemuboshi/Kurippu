namespace Kurippu.Packaging;

public static class RuntimeAssetCatalog
{
    private const string WindowsReleaseUrl =
        "https://github.com/shinchiro/mpv-winbuild-cmake/releases/download/20261021/";

    public static IReadOnlyList<RuntimeAsset> All { get; } =
    [
        new("linux-x64", "", new(
            "ffmpeg-N-127943-g2668cb6e31-linux64-gpl.tar.xz",
            "https://github.com/yt-dlp/FFmpeg-Builds/releases/download/autobuild-2026-10-22-18-33/ffmpeg-N-127943-g2668cb6e31-linux64-gpl.tar.xz",
            "500bd0fa35e13c8256ea4a3d29d3bacac1d78ec33370fa5d383bb468b4a5e544"), null),
        new("osx-arm64", "", new(
            "ffmpeg711arm.zip",
            "https://www.osxexperts.net/ffmpeg711arm.zip",
            "59e39a5cec2e5d2307ed079c53227a9181e64b87454ed4de998349e044bfdc70"), null),
        new("osx-x64", "", new(
            "ffmpeg80intel.zip",
            "https://www.osxexperts.net/ffmpeg80intel.zip",
            "2d24d22db78c87f394a5822867acd5c5dc5e762cd261a44bd26923f3a5af3e07"), null),
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
