namespace Kurippu.Packaging;

public sealed class RuntimeStager(
    IAssetDownloader downloader,
    IProcessRunner processRunner,
    NativeDependencyStager? nativeDependencyStager = null) : IPackagingStager
{
    public async Task StageAsync(string rid, string outputDirectory,
        CancellationToken cancellationToken)
    {
        ValidateHost(rid);
        var asset = RuntimeAssetCatalog.Get(rid);
        var output = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(output);
        var scratch = Path.Combine(Path.GetTempPath(), "kurippu-assets-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);

        try
        {
            var ffmpegArchive = Path.Combine(scratch, asset.Ffmpeg.FileName);
            await downloader.DownloadAsync(new Uri(asset.Ffmpeg.Url), ffmpegArchive,
                asset.Ffmpeg.Sha256, cancellationToken).ConfigureAwait(false);
            var ffmpegExtracted = Path.Combine(scratch, "ffmpeg");
            Directory.CreateDirectory(ffmpegExtracted);
            ArchiveExtractor.ExtractToDirectory(ffmpegArchive, ffmpegExtracted);

            var executableName = string.Equals(rid, "win-x64", StringComparison.Ordinal) ? "ffmpeg.exe" : "ffmpeg";
            var source = Directory.EnumerateFiles(ffmpegExtracted, executableName,
                SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new PackagingException($"Archive did not contain {executableName}.");
            var target = Path.Combine(output, executableName);
            File.Copy(source, target, overwrite: true);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(target, UnixFileMode.UserRead | UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            var result = await processRunner.RunAsync(target, ["-version"], cancellationToken).ConfigureAwait(false);
            if (result.ExitCode != 0)
                throw new PackagingException($"ffmpeg -version failed for {target}: " +
                    $"exit {result.ExitCode}{FormatOutput(result)}");

            if (string.Equals(rid, "win-x64", StringComparison.Ordinal))
                await StageWindowsMpv(asset, output, scratch, cancellationToken).ConfigureAwait(false);
            else if (nativeDependencyStager is not null)
            {
                if (rid.StartsWith("osx-", StringComparison.Ordinal))
                    await nativeDependencyStager.StageMacOSAsync(output, cancellationToken).ConfigureAwait(false);
                else
                    await nativeDependencyStager.StageLinuxAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    private static void ValidateHost(string rid)
    {
        if (rid.StartsWith("osx-", StringComparison.Ordinal) && !OperatingSystem.IsMacOS())
            throw new PackagingException(
                $"RID {rid} must be packaged on macOS; current host is {CurrentHost()}.");
        if (string.Equals(rid, "linux-x64", StringComparison.Ordinal) && !OperatingSystem.IsLinux())
            throw new PackagingException(
                $"RID {rid} must be packaged on Linux; current host is {CurrentHost()}.");
    }

    private static string CurrentHost()
    {
        if (OperatingSystem.IsWindows()) return "windows";
        if (OperatingSystem.IsMacOS()) return "macos";
        if (OperatingSystem.IsLinux()) return "linux";
        return "unknown";
    }

    private async Task StageWindowsMpv(RuntimeAsset asset, string output, string scratch,
        CancellationToken cancellationToken)
    {
        var mpv = asset.Mpv ?? throw new PackagingException("Windows mpv asset metadata is missing.");
        var archive = Path.Combine(scratch, mpv.FileName);
        await downloader.DownloadAsync(new Uri(mpv.Url), archive, mpv.Sha256, cancellationToken).ConfigureAwait(false);
        var extracted = Path.Combine(scratch, "mpv");
        Directory.CreateDirectory(extracted);
        ArchiveExtractor.ExtractToDirectory(archive, extracted);

        foreach (var source in Directory.EnumerateFiles(extracted, "*.dll",
                     SearchOption.AllDirectories))
            File.Copy(source, Path.Combine(output, Path.GetFileName(source)), overwrite: true);

        if (!File.Exists(Path.Combine(output, "libmpv-2.dll")))
            throw new PackagingException("Windows mpv archive did not contain libmpv-2.dll.");
    }

    private static string FormatOutput(ProcessResult result)
    {
        var output = string.Join(Environment.NewLine,
            new[] { result.StandardOutput, result.StandardError }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        return output.Length == 0 ? "." : ":" + Environment.NewLine + output;
    }
}
