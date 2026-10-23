namespace Kurippu.Packaging;

public sealed record RuntimeAsset(string Rid, string ReleaseUrl, RuntimeAssetFile Ffmpeg, RuntimeAssetFile? Mpv);
