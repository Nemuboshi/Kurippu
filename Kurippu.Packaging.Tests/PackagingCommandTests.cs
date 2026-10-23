using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class PackagingCommandTests
{
    [Theory]
    [InlineData(new[] { "stage", "win-x64", "publish/win-x64" }, 0)]
    [InlineData(new[] { "stage", "bogus", "publish/win-x64" }, 2)]
    [InlineData(new[] { "unknown" }, 2)]
    public void Run_returns_expected_exit_code(string[] args, int expected)
    {
        var staging = new RecordingStager();
        var command = new PackagingCommand(staging);

        var exitCode = command.Run(args, CancellationToken.None);

        Assert.Equal(expected, exitCode);
        Assert.Equal(expected == 0 ? 1 : 0, staging.Calls);
    }

    [Fact]
    public void Run_rejects_missing_arguments_without_staging()
    {
        var staging = new RecordingStager();
        var command = new PackagingCommand(staging);

        var exitCode = command.Run(["stage"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, staging.Calls);
    }

    [Fact]
    public async Task Stage_command_invokes_ffmpeg_staging_native_staging_and_cleanup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-command-tests-" +
            Guid.NewGuid().ToString("N"));
        var output = Path.Combine(directory, "output");
        var stager = new RecordingStager();
        var command = new PackagingCommand(stager, ReleaseSymbolCleaner.RemoveDebugSymbols);

        try
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "debug.pdb"), "symbols");

            var exitCode = await command.RunAsync(
                ["stage", "win-x64", output], CancellationToken.None);

            Assert.Equal(0, exitCode);
            Assert.Equal(1, stager.Calls);
            Assert.Equal(Path.GetFullPath(output), stager.OutputDirectory);
            Assert.False(File.Exists(Path.Combine(output, "debug.pdb")));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Stage_command_returns_two_for_an_unsupported_rid()
    {
        var stager = new RecordingStager();
        var command = new PackagingCommand(stager, ReleaseSymbolCleaner.RemoveDebugSymbols);

        var exitCode = await command.RunAsync(
            ["stage", "bogus", "publish/bogus"], CancellationToken.None);

        Assert.Equal(2, exitCode);
        Assert.Equal(0, stager.Calls);
    }

    [Fact]
    public async Task Stage_command_propagates_staging_failures()
    {
        var stager = new RecordingStager
        {
            Failure = new PackagingException("download failed")
        };
        var command = new PackagingCommand(stager, ReleaseSymbolCleaner.RemoveDebugSymbols);

        var exception = await Assert.ThrowsAsync<PackagingException>(() =>
            command.RunAsync(["stage", "win-x64", "publish/win-x64"],
                CancellationToken.None));

        Assert.Contains("download failed", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, stager.Calls);
    }

    private sealed class RecordingStager : IPackagingStager
    {
        public int Calls { get; private set; }
        public string? OutputDirectory { get; private set; }
        public Exception? Failure { get; init; }

        public Task StageAsync(string rid, string outputDirectory,
            CancellationToken cancellationToken)
        {
            Calls++;
            OutputDirectory = outputDirectory;
            return Failure is null ? Task.CompletedTask : Task.FromException(Failure);
        }
    }
}
