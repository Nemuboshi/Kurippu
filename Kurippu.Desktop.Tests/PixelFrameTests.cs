using Kurippu.Desktop.Media;

namespace Kurippu.Desktop.Tests;

public class PixelFrameTests
{
    [Fact]
    public void PreviewDoesNotSilentlyDownscaleLargeViewport()
    {
        Assert.Equal((2560, 1440), FrameSize.ForViewport(2560, 1440, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => FrameSize.ForViewport(9000, 5000, 1));
    }
    [Fact]
    public void CopiesRowsIntoPaddedStrideWithoutCrossingRowBoundary()
    {
        var source = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var target = new byte[12];
        PixelFrame.CopyRows(source, target, rowBytes: 4, targetStride: 6, height: 2);
        Assert.Equal([1, 2, 3, 4, 0, 0, 5, 6, 7, 8, 0, 0], target);
    }
}
