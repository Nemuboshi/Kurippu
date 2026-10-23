namespace Kurippu.Desktop.Services;

public sealed record PreviewSource(string Path, double? Time, int Width, int Height, double Sar, double Fps);
