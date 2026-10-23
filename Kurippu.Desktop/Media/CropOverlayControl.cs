using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Kurippu.Core;

namespace Kurippu.Desktop.Media;

public sealed class CropOverlayControl : Control
{
    private Point origin;
    private (double X, double Y, double Width, double Height) initial;
    private string? dragMode;
    public int SourceWidth { get; set; }
    public int SourceHeight { get; set; }
    public double Sar { get; set; } = 1;
    public CropRect? Crop { get; private set; }
    public event Action<CropRect?>? CropChanged;
    public event Action? Clicked;

    public void SetCrop(CropRect? crop) { Crop = crop; InvalidateVisual(); }
    public void ApplyDetectedCrop(CropRect crop) => Commit(crop);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        if (Crop is not { } crop || SourceWidth <= 0 || SourceHeight <= 0) return;
        var r = VideoGeometry.ToViewport(Bounds.Width, Bounds.Height, SourceWidth, SourceHeight, Sar, crop);
        context.DrawRectangle(null, new Pen(Brushes.Orange, 3), new Rect(r.X, r.Y, r.Width, r.Height));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (SourceWidth <= 0 || SourceHeight <= 0) return;
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            Commit(null);
            e.Handled = true;
            return;
        }
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        origin = e.GetPosition(this);
        initial = Crop is { } crop ? VideoGeometry.ToViewport(Bounds.Width, Bounds.Height,
            SourceWidth, SourceHeight, Sar, crop) : default;
        dragMode = Crop is null ? "draw" : VideoGeometry.PickCropHandle(initial, origin.X, origin.Y);
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (dragMode is null) return;
        var point = e.GetPosition(this);
        var dx = point.X - origin.X;
        var dy = point.Y - origin.Y;
        var r = (Math.Min(origin.X, point.X), Math.Min(origin.Y, point.Y), Math.Abs(dx), Math.Abs(dy));
        if (!string.Equals(dragMode, "draw", StringComparison.Ordinal)) r = VideoGeometry.DragViewport(initial, dragMode, dx, dy);
        if (r.Item3 >= 8 && r.Item4 >= 8)
            Commit(VideoGeometry.FromViewport(Bounds.Width, Bounds.Height, SourceWidth, SourceHeight,
                Sar, r.Item1, r.Item2, r.Item3, r.Item4));
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (dragMode is null) return;
        var point = e.GetPosition(this);
        if (string.Equals(dragMode, "draw", StringComparison.Ordinal) && Math.Abs(point.X - origin.X) < 8 && Math.Abs(point.Y - origin.Y) < 8)
            Clicked?.Invoke();
        dragMode = null;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void Commit(CropRect? crop)
    {
        SetCrop(crop);
        CropChanged?.Invoke(crop);
    }
}
