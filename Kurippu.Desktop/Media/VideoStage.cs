using Avalonia;
using Avalonia.Controls;
using Kurippu.Core;

namespace Kurippu.Desktop.Media;

public sealed class VideoStage : Panel
{
    public int SourceWidth { get; set; } = 16;
    public int SourceHeight { get; set; } = 9;
    public double Sar { get; set; } = 1;

    public static Rect Fit(Size available, int width, int height, double sar)
    {
        var rect = VideoGeometry.Letterbox(available.Width, available.Height, width, height, sar);
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        foreach (var child in Children) child.Measure(availableSize);
        return availableSize;
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var rect = Fit(finalSize, SourceWidth, SourceHeight, Sar);
        foreach (var child in Children) child.Arrange(rect);
        return finalSize;
    }

    public void SetSource(int width, int height, double sar)
    {
        SourceWidth = width;
        SourceHeight = height;
        Sar = sar;
        InvalidateArrange();
    }
}
