namespace Kurippu.Desktop.ViewModels;

public static class SourceTitle
{
    public static string DisplayTitle(string? probed, string? resolved) =>
        !string.IsNullOrWhiteSpace(resolved) ? resolved : probed ?? "";
}
