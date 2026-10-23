namespace Kurippu.Core;

public static class VideoGeometry
{
    public static string PickCropHandle((double X, double Y, double Width, double Height) rect, double x, double y)
    {
        var onX = x >= rect.X - 5 && x <= rect.X + rect.Width + 5;
        var onY = y >= rect.Y - 5 && y <= rect.Y + rect.Height + 5;
        var west = onY && Math.Abs(x - rect.X) <= 10;
        var east = onY && Math.Abs(x - rect.X - rect.Width) <= 10;
        var north = onX && Math.Abs(y - rect.Y) <= 10;
        var south = onX && Math.Abs(y - rect.Y - rect.Height) <= 10;
        return north && west ? "nw" : north && east ? "ne" : south && west ? "sw" :
            south && east ? "se" : west ? "w" : east ? "e" : north ? "n" : south ? "s" :
            onX && onY ? "move" : "draw";
    }

    public static (double X, double Y, double Width, double Height) DragViewport(
        (double X, double Y, double Width, double Height) rect, string handle, double dx, double dy)
    {
        var (x, y, width, height) = rect;
        if (string.Equals(handle, "move", StringComparison.Ordinal)) return (x + dx, y + dy, width, height);
        if (handle.Contains('w')) { x += dx; width -= dx; }
        if (handle.Contains('e')) width += dx;
        if (handle.Contains('n')) { y += dy; height -= dy; }
        if (handle.Contains('s')) height += dy;
        if (width < 8) { if (handle.Contains('w')) x -= 8 - width; width = 8; }
        if (height < 8) { if (handle.Contains('n')) y -= 8 - height; height = 8; }
        return (x, y, width, height);
    }

    public static CropRect? FromViewport(double viewportWidth, double viewportHeight,
        int sourceWidth, int sourceHeight, double sar, double x, double y, double width, double height)
    {
        var rect = Letterbox(viewportWidth, viewportHeight, sourceWidth, sourceHeight, sar);
        if (rect.Width <= 0 || rect.Height <= 0 || width < 2 || height < 2 ||
            sourceWidth < 2 || sourceHeight < 2) return null;
        var left = Math.Clamp(x, rect.X, rect.X + rect.Width);
        var top = Math.Clamp(y, rect.Y, rect.Y + rect.Height);
        var right = Math.Clamp(x + width, rect.X, rect.X + rect.Width);
        var bottom = Math.Clamp(y + height, rect.Y, rect.Y + rect.Height);
        if (right <= left || bottom <= top) return null;
        static int SnapEven(double value) => (int)Math.Floor(value / 2 + .5 + 1e-9) * 2;
        var maxX = sourceWidth & ~1;
        var maxY = sourceHeight & ~1;
        var cx = Math.Clamp(SnapEven((left - rect.X) / rect.Width * sourceWidth), 0, maxX - 2);
        var cy = Math.Clamp(SnapEven((top - rect.Y) / rect.Height * sourceHeight), 0, maxY - 2);
        var endX = Math.Clamp(SnapEven((right - rect.X) / rect.Width * sourceWidth), cx + 2, maxX);
        var endY = Math.Clamp(SnapEven((bottom - rect.Y) / rect.Height * sourceHeight), cy + 2, maxY);
        return new CropRect(endX - cx, endY - cy, cx, cy);
    }

    public static (double X, double Y, double Width, double Height) ToViewport(
        double viewportWidth, double viewportHeight, int sourceWidth, int sourceHeight, double sar, CropRect crop)
    {
        var rect = Letterbox(viewportWidth, viewportHeight, sourceWidth, sourceHeight, sar);
        return (rect.X + crop.X * rect.Width / sourceWidth, rect.Y + crop.Y * rect.Height / sourceHeight,
            crop.Width * rect.Width / sourceWidth, crop.Height * rect.Height / sourceHeight);
    }

    public static (double X, double Y, double Width, double Height) Letterbox(
        double viewportWidth, double viewportHeight, double sourceWidth, double sourceHeight, double sar)
    {
        if (viewportWidth <= 0 || viewportHeight <= 0 || sourceWidth <= 0 || sourceHeight <= 0)
            return (0, 0, 0, 0);
        var displayWidth = sourceWidth * (sar > 0 ? sar : 1);
        var scale = Math.Min(viewportWidth / displayWidth, viewportHeight / sourceHeight);
        var width = displayWidth * scale;
        var height = sourceHeight * scale;
        return ((viewportWidth - width) / 2, (viewportHeight - height) / 2, width, height);
    }
}
