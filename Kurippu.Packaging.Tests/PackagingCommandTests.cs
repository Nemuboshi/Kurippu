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

    private sealed class RecordingStager : IPackagingStager
    {
        public int Calls { get; private set; }

        public Task StageAsync(string rid, string outputDirectory, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.CompletedTask;
        }
    }
}
