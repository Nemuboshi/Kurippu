using Avalonia.Input;

namespace Kurippu.Desktop.Media;

public static class PlaybackShortcuts
{
    public static string? MarkFor(Key key) => key switch
    {
        Key.OemOpenBrackets => "start",
        Key.OemCloseBrackets => "end",
        _ => null
    };
}
