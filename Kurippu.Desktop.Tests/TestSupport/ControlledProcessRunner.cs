using Kurippu.Desktop.Services;

namespace Kurippu.Desktop.Tests.TestSupport;

public sealed class ControlledProcessRunner : IProcessRunner
{
    private int exitCode;
    private readonly TaskCompletionSource releaseFirstPass =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource releaseSecondPass =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource firstProgressReported =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource finished =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int calls;

    public ControlledProcessRunner(int exitCode = 0)
    {
        this.exitCode = exitCode;
    }

    public Task FirstProgressReported => firstProgressReported.Task;
    public Task Finished => finished.Task;
    public int Calls => Volatile.Read(ref calls);

    public void SetExitCode(int value) => exitCode = value;

    public async Task<int> RunAsync(string executable, IReadOnlyList<string> args,
        IProgress<string>? log, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref calls);
        log?.Report($"frame={call * 10}");
        firstProgressReported.TrySetResult();
        var release = call == 2 ? releaseSecondPass.Task : releaseFirstPass.Task;
        await release.WaitAsync(cancellationToken);
        finished.TrySetResult();
        return exitCode;
    }

    public void CompleteFirstPass() => releaseFirstPass.TrySetResult();

    public void CompleteSecondPass() => releaseSecondPass.TrySetResult();

    public void CompleteEncoding()
    {
        CompleteFirstPass();
        CompleteSecondPass();
    }
}
