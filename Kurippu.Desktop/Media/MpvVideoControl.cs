using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace Kurippu.Desktop.Media;

public sealed class MpvVideoControl : Control, IDisposable
{
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private WriteableBitmap? bitmap;
    private bool rendering;
    private bool hasFrame;
    private bool oversized;
    private bool disposed;
    public IMediaPlayer? Player { get; set; }
    public bool HasFrame => hasFrame;
    public event Action<double>? TimeChanged;

    public MpvVideoControl()
    {
        timer.Tick += Tick;
        AttachedToVisualTree += (_, _) => timer.Start();
        DetachedFromVisualTree += (_, _) => timer.Stop();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (bitmap is { } frame)
            context.DrawImage(frame, new Rect(frame.Size), new Rect(Bounds.Size));
    }

    private async void Tick(object? sender, EventArgs e)
    {
        if (disposed || rendering || Player is not { } player || Bounds.Width <= 0 || Bounds.Height <= 0) return;
        rendering = true;
        try
        {
            var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
            var (width, height) = FrameSize.ForViewport(Bounds.Width, Bounds.Height, scale);
            var pixels = await Task.Run(() => player.RenderFrame(width, height));
            if (disposed || Player != player) return;
            if (bitmap?.PixelSize != new PixelSize(width, height))
            {
                bitmap?.Dispose();
                hasFrame = false;
                bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96),
                    PixelFormat.Bgra8888, AlphaFormat.Opaque);
            }
            using (var framebuffer = bitmap.Lock())
            {
                for (var y = 0; y < height; y++)
                    Marshal.Copy(pixels, y * width * 4, framebuffer.Address + y * framebuffer.RowBytes, width * 4);
            }
            hasFrame = true;
            InvalidateVisual();
            if (oversized) { oversized = false; RenderRecovered?.Invoke(); }
            TimeChanged?.Invoke(player.Numeric("time-pos"));
        }
        catch (ObjectDisposedException) { }
        catch (ArgumentOutOfRangeException ex) when (ex.ParamName == "width")
        {
            if (!oversized) { oversized = true; RenderFailed?.Invoke(ex); }
        }
        catch (Exception ex) { timer.Stop(); RenderFailed?.Invoke(ex); }
        finally { rendering = false; }
    }

    public event Action<Exception>? RenderFailed;
    public event Action? RenderRecovered;

    public void Dispose()
    {
        disposed = true;
        timer.Stop();
        bitmap?.Dispose();
        bitmap = null;
        hasFrame = false;
    }
}
