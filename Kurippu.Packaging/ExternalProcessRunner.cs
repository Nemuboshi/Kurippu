using System.Diagnostics;

namespace Kurippu.Packaging;

public sealed class ExternalProcessRunner : IProcessRunner
{
    public async Task<ProcessResult> RunAsync(string fileName,
        IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
                throw new PackagingException($"Could not start process: {fileName}");
        }
        catch (Exception exception) when (exception is not PackagingException)
        {
            throw new PackagingException($"Could not start process {fileName}: {exception.Message}");
        }

        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
        });

        var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        var result = new ProcessResult(process.ExitCode, await standardOutput,
            await standardError);
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }
}
