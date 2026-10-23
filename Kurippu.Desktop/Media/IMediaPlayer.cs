using Kurippu.Core;

namespace Kurippu.Desktop.Media;

public interface IMediaPlayer : IDisposable
{
    Task<MediaInfo> OpenAsync(string path, CancellationToken cancellationToken = default);
    string GetProperty(string name);
    double GetNumericProperty(string name);
    void Play();
    void Pause();
    void Seek(double seconds);
    void FrameStep();
    void FrameBackStep();
    void SetVolume(double volume);
    void SetMuted(bool muted);
    void SetTrack(TrackKind kind, int? id);
    void SetDeinterlace(bool value);
    byte[] RenderFrame(int width, int height);
}
