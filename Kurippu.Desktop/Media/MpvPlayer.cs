using System.Globalization;
using System.Runtime.InteropServices;
using Kurippu.Core;

namespace Kurippu.Desktop.Media;

public sealed class MpvPlayer : IMediaPlayer
{
    private readonly Lock gate = new();
    private IntPtr handle;
    private IntPtr renderer;
    private bool disposed;

    public MpvPlayer()
    {
        handle = MpvNative.Create();
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Could not create libmpv instance");
        try
        {
            Check(MpvNative.SetOption(handle, "vo", "libmpv"), "set video output");
            Check(MpvNative.Initialize(handle), "initialize libmpv");
            var api = Marshal.StringToCoTaskMemUTF8("sw");
            try
            {
                Check(MpvNative.RenderCreate(out renderer, handle,
                    [new(1, api), new(0, IntPtr.Zero)]), "create software renderer");
            }
            finally { Marshal.FreeCoTaskMem(api); }
        }
        catch { Dispose(); throw; }
    }

    public async Task<MediaInfo> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Video path is empty", nameof(path));
        Pause();
        Command("loadfile", path, "replace");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                int eventId;
                lock (gate) { ThrowIfDisposed(); eventId = Marshal.ReadInt32(MpvNative.WaitEvent(handle, 0)); }
                if (eventId == 7) throw new InvalidDataException("libmpv could not open the media file");
                if (eventId == 8)
                {
                    var count = Integer("track-list/count");
                    var values = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["duration"] = Property("duration"),
                        ["track-list/count"] = count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["media-title"] = Property("media-title"),
                        ["file-format"] = Property("file-format"),
                        ["file-size"] = Property("file-size"),
                        ["demuxer-bitrate"] = Property("demuxer-bitrate")
                    };
                    for (var i = 0; i < count; i++)
                    {
                        var prefix = $"track-list/{i}/";
                        foreach (var key in new[] { "id", "type", "codec", "demux-w", "demux-h", "demux-par",
                            "demux-fps", "demux-channel-count", "demux-samplerate", "demux-bitrate",
                            "profile", "lang", "title" })
                            values[prefix + key] = Property(prefix + key);
                    }
                    values["video-params/w"] = Property("video-params/w");
                    values["video-params/h"] = Property("video-params/h");
                    values["video-params/par"] = Property("video-params/par");
                    return MediaProbe.FromProperties(path, values);
                }
                await Task.Delay(60, timeout.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("Timed out while loading media");
        }
    }

    public string Property(string name)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            var value = MpvNative.GetProperty(handle, name);
            if (value == IntPtr.Zero) return "";
            try { return Marshal.PtrToStringUTF8(value) ?? ""; }
            finally { MpvNative.Free(value); }
        }
    }

    public double Numeric(string name) => double.TryParse(Property(name), NumberStyles.Float,
        CultureInfo.InvariantCulture, out var value) ? value : 0;
    public int Integer(string name) => int.TryParse(Property(name), System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0;
    public void Play() => Set("pause", "no");
    public void Pause() => Set("pause", "yes");
    public void Seek(double seconds) => Command("seek", seconds.ToString("0.###", CultureInfo.InvariantCulture), "absolute");
    public void FrameStep() => Command("frame-step");
    public void FrameBackStep() => Command("frame-back-step");
    public void SetVolume(double volume) => Set("volume", Math.Clamp(volume, 0, 100).ToString(CultureInfo.InvariantCulture));
    public void SetMuted(bool muted) => Set("mute", muted ? "yes" : "no");
    public void SetTrack(string kind, int? id) => Set(kind, id?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "no");
    public void SetDeinterlace(bool value) => Set("deinterlace", value ? "yes" : "no");

    public byte[] RenderFrame(int width, int height)
    {
        if (width is < 1 or > 8192 || height is < 1 or > 8192)
            throw new ArgumentOutOfRangeException(nameof(width), "Invalid preview dimensions");
        var stride = checked(width * 4);
        var bytes = new byte[checked(stride * height)];
        var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        var size = Marshal.AllocHGlobal(8);
        var stridePointer = Marshal.AllocHGlobal(IntPtr.Size);
        var format = Marshal.StringToCoTaskMemUTF8("bgr0");
        try
        {
            Marshal.WriteInt32(size, width);
            Marshal.WriteInt32(size, 4, height);
            Marshal.WriteIntPtr(stridePointer, new IntPtr(stride));
            var parameters = new MpvNative.RenderParameter[]
            {
                new(17, size), new(18, format), new(19, stridePointer), new(20, pinned.AddrOfPinnedObject()),
                new(0, IntPtr.Zero)
            };
            lock (gate)
            {
                ThrowIfDisposed();
                MpvNative.RenderUpdate(renderer);
                Check(MpvNative.Render(renderer, parameters), "render frame");
            }
            return bytes;
        }
        finally
        {
            Marshal.FreeHGlobal(size);
            Marshal.FreeHGlobal(stridePointer);
            Marshal.FreeCoTaskMem(format);
            pinned.Free();
        }
    }

    private void Set(string name, string value)
    {
        lock (gate) { ThrowIfDisposed(); Check(MpvNative.SetProperty(handle, name, value), $"set {name}"); }
    }

    private void Command(params string[] arguments)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            var pointers = arguments.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            var argv = Marshal.AllocHGlobal((pointers.Length + 1) * IntPtr.Size);
            try
            {
                for (var i = 0; i < pointers.Length; i++) Marshal.WriteIntPtr(argv, i * IntPtr.Size, pointers[i]);
                Marshal.WriteIntPtr(argv, pointers.Length * IntPtr.Size, IntPtr.Zero);
                Check(MpvNative.Command(handle, argv), arguments[0]);
            }
            finally
            {
                Marshal.FreeHGlobal(argv);
                foreach (var pointer in pointers) Marshal.FreeCoTaskMem(pointer);
            }
        }
    }

    private static void Check(int result, string operation)
    {
        if (result < 0) throw new InvalidOperationException($"libmpv {operation} failed ({result})");
    }

    private void ThrowIfDisposed()
    {
        if (disposed) throw new ObjectDisposedException(nameof(MpvPlayer));
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            if (renderer != IntPtr.Zero) { MpvNative.RenderFree(renderer); renderer = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { MpvNative.TerminateDestroy(handle); handle = IntPtr.Zero; }
        }
    }
}
