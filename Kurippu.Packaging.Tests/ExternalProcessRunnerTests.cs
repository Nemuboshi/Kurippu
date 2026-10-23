using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class ExternalProcessRunnerTests
{
    [Fact]
    public async Task Nonzero_process_exit_includes_stdout_and_stderr()
    {
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("pwsh", new[]
            {
                "-NoProfile", "-NonInteractive", "-Command",
                "[Console]::Out.WriteLine('stdout-marker'); " +
                "[Console]::Error.WriteLine('stderr-marker'); exit 7"
            })
            : ("/bin/sh", new[]
            {
                "-c", "printf 'stdout-marker\\n'; printf 'stderr-marker\\n' >&2; exit 7"
            });
        var runner = new ExternalProcessRunner();

        var exception = await Assert.ThrowsAsync<PackagingException>(() =>
            runner.RunAsync(fileName, arguments, CancellationToken.None));

        Assert.Contains("stdout-marker", exception.Message, StringComparison.Ordinal);
        Assert.Contains("stderr-marker", exception.Message, StringComparison.Ordinal);
        Assert.Contains("7", exception.Message, StringComparison.Ordinal);
    }
}
