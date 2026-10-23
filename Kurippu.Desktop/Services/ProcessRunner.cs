using System.Diagnostics;

namespace Kurippu.Desktop.Services;

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(string executable, IReadOnlyList<string> args, IProgress<string>? log,
        CancellationToken cancellationToken)
    {
        var info = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException($"Could not start {executable}");
        using var registration = cancellationToken.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        });
        async Task ReadLinesAsync(StreamReader reader)
        {
            while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is { } line)
                log?.Report(line);
        }
        await Task.WhenAll(ReadLinesAsync(process.StandardOutput), ReadLinesAsync(process.StandardError),
            process.WaitForExitAsync(CancellationToken.None)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return process.ExitCode;
    }
}
