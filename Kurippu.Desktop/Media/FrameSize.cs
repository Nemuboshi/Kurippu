namespace Kurippu.Desktop.Media;

public static class FrameSize
{
    public static (int Width, int Height) ForViewport(double width, double height, double scaling)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(scaling) ||
            width <= 0 || height <= 0 || scaling <= 0 || width * scaling > 8192 || height * scaling > 8192)
            throw new ArgumentOutOfRangeException(nameof(width), "Preview surface exceeds supported size (8192 pixels per side)");
        return (Math.Max(1, (int)Math.Round(width * scaling)), Math.Max(1, (int)Math.Round(height * scaling)));
    }
}
