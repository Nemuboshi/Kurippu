namespace Kurippu.Desktop.Services;

public sealed class ToolLocator(string baseDirectory, bool searchPath = true)
{
    public static ToolLocator Default { get; } = new(AppContext.BaseDirectory);

    public string? Ffmpeg(string? overridePath = null) => Locate("ffmpeg", overridePath, "KURIPPU_FFMPEG", false);
    public string? YtDlp(string? overridePath = null) => Locate("yt-dlp", overridePath, "KURIPPU_YT_DLP", searchPath);

    private string? Locate(string name, string? overridePath, string variable, bool usePath)
    {
        var exe = OperatingSystem.IsWindows() ? name + ".exe" : name;
        foreach (var candidate in new[] { overridePath, Environment.GetEnvironmentVariable(variable),
            Path.Combine(baseDirectory, exe) })
            if (candidate is not null && File.Exists(candidate)) return candidate;
        if (!usePath) return null;
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory, exe);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
