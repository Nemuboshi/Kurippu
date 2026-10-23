using Kurippu.Core;
using Kurippu.Desktop.Media;

namespace Kurippu.Desktop.Tests.TestSupport;

public sealed class FakeMediaPlayer : IMediaPlayer
{
    private readonly TaskCompletionSource<MediaInfo> open =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private readonly string? defaultSource;

    public FakeMediaPlayer(bool blockOpen = false, string? defaultSource = null)
    {
        this.defaultSource = defaultSource;
        if (!blockOpen) CompleteOpen();
    }

    public Task OpenStarted { get; private set; } = Task.CompletedTask;

    public Task<MediaInfo> OpenAsync(string source, CancellationToken cancellationToken = default)
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        OpenStarted = started.Task;
        started.TrySetResult();
        return open.Task;
    }

    public void CompleteOpen(string source) =>
        open.TrySetResult(new MediaInfo(source, 3, 320, 180, 1,
            [new MediaTrack(1, "video", "h264", 320, 180)], [], []));

    public string Property(string name) => name == "pause" ? "yes" : "";
    public double Numeric(string name) => 0;
    public void Play() { }
    public void Pause() { }
    public void Seek(double seconds) { }
    public void FrameStep() { }
    public void FrameBackStep() { }
    public void SetVolume(double volume) { }
    public void SetMuted(bool muted) { }
    public void SetTrack(string kind, int? id) { }
    public void SetDeinterlace(bool value) { }
    public byte[] RenderFrame(int width, int height) => new byte[width * height * 4];
    public void Dispose() { }

    private void CompleteOpen() => CompleteOpen(defaultSource ?? "source.mp4");
}
