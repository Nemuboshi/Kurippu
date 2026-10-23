using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Kurippu.Desktop.Media;

public sealed class RangeTimeline : Control
{
    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<RangeTimeline, double>(nameof(Maximum), 100);
    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<RangeTimeline, double>(nameof(Value));
    public static readonly StyledProperty<double> HighlightStartProperty =
        AvaloniaProperty.Register<RangeTimeline, double>(nameof(HighlightStart));
    public static readonly StyledProperty<double> HighlightEndProperty =
        AvaloniaProperty.Register<RangeTimeline, double>(nameof(HighlightEnd), 100);
    public static readonly StyledProperty<IBrush?> TrackBrushProperty =
        AvaloniaProperty.Register<RangeTimeline, IBrush?>(nameof(TrackBrush));
    public static readonly StyledProperty<IBrush?> HighlightBrushProperty =
        AvaloniaProperty.Register<RangeTimeline, IBrush?>(nameof(HighlightBrush));
    public static readonly StyledProperty<IBrush?> ThumbBrushProperty =
        AvaloniaProperty.Register<RangeTimeline, IBrush?>(nameof(ThumbBrush));

    static RangeTimeline() => AffectsRender<RangeTimeline>(MaximumProperty, ValueProperty,
        HighlightStartProperty, HighlightEndProperty, TrackBrushProperty, HighlightBrushProperty, ThumbBrushProperty);

    public RangeTimeline() { Focusable = true; MinHeight = 28; }
    public double Maximum { get => GetValue(MaximumProperty); set => SetValue(MaximumProperty, value); }
    public double Value { get => GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public double HighlightStart { get => GetValue(HighlightStartProperty); set => SetValue(HighlightStartProperty, value); }
    public double HighlightEnd { get => GetValue(HighlightEndProperty); set => SetValue(HighlightEndProperty, value); }
    public IBrush? TrackBrush { get => GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public IBrush? HighlightBrush { get => GetValue(HighlightBrushProperty); set => SetValue(HighlightBrushProperty, value); }
    public IBrush? ThumbBrush { get => GetValue(ThumbBrushProperty); set => SetValue(ThumbBrushProperty, value); }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var width = Math.Max(0, Bounds.Width - 20);
        var middle = Bounds.Height / 2;
        context.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
        context.FillRectangle(TrackBrush ?? Brushes.Gray, new Rect(10, middle - 3, width, 6), 3);
        if (Maximum > 0 && width > 0)
        {
            var start = Math.Clamp(HighlightStart / Maximum, 0, 1);
            var end = Math.Clamp(HighlightEnd / Maximum, start, 1);
            context.FillRectangle(HighlightBrush ?? Brushes.Teal,
                new Rect(10 + start * width, middle - 3, (end - start) * width, 6), 3);
        }
        var position = Maximum > 0 ? Math.Clamp(Value / Maximum, 0, 1) : 0;
        context.DrawEllipse(ThumbBrush ?? Brushes.White, new Pen(HighlightBrush ?? Brushes.Teal, 2),
            new Point(10 + position * width, middle), 6, 6);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        Focus();
        e.Pointer.Capture(this);
        SeekAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (e.Pointer.Captured != this) return;
        SeekAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.Pointer.Captured != this) return;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 5;
        var target = e.Key switch
        {
            Key.Left => Value - step,
            Key.Right => Value + step,
            Key.Home => 0,
            Key.End => Maximum,
            _ => (double?)null
        };
        if (target is null) return;
        Value = Math.Clamp(target.Value, 0, Math.Max(0, Maximum));
        e.Handled = true;
    }

    private void SeekAt(double x)
    {
        var width = Bounds.Width - 20;
        if (width > 0 && Maximum > 0) Value = Math.Clamp((x - 10) / width, 0, 1) * Maximum;
    }
}
