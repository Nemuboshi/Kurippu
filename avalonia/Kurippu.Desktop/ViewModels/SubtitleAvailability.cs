using Kurippu.Core;

namespace Kurippu.Desktop.ViewModels;

public static class SubtitleAvailability
{
    public static bool CanBurn(MediaInfo media, int selectedIndex, string? externalPath) =>
        !string.IsNullOrWhiteSpace(externalPath) ? File.Exists(externalPath) :
        selectedIndex > 0 && selectedIndex <= media.SubtitleTracks.Count;
}
