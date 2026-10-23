namespace Kurippu.Desktop.Media;

public static class PixelFrame
{
    public static void CopyRows(ReadOnlySpan<byte> source, Span<byte> target,
        int rowBytes, int targetStride, int height)
    {
        if (rowBytes < 0 || targetStride < rowBytes || height < 0 ||
            source.Length < checked(rowBytes * height) || target.Length < checked(targetStride * height))
            throw new ArgumentOutOfRangeException(nameof(targetStride));
        for (var row = 0; row < height; row++)
            source.Slice(row * rowBytes, rowBytes).CopyTo(target.Slice(row * targetStride, rowBytes));
    }
}
