using Kurippu.Desktop.Services;

namespace Kurippu.Desktop.Tests.TestSupport;

public sealed class ControlledDetectionRunner : IProcessRunner
{
    private readonly TaskCompletionSource release =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource started =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly bool ignoreCancellation;
    private readonly int exitCode;
    private int calls;

    public ControlledDetectionRunner(bool ignoreCancellation = true, int exitCode = 0)
    {
        this.ignoreCancellation = ignoreCancellation;
        this.exitCode = exitCode;
    }

    public Task Started => started.Task;
    public Task Completed => completed.Task;
    public int Calls => Volatile.Read(ref calls);

    public async Task<int> RunAsync(string executable, IReadOnlyList<string> args,
        IProgress<string>? log, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref calls);
        started.TrySetResult();
        await release.Task.WaitAsync(ignoreCancellation ? CancellationToken.None : cancellationToken)
            .ConfigureAwait(false);
        log?.Report(Output);
        completed.TrySetResult();
        return exitCode;
    }

    public string Output { get; set; } = "";

    public void Complete(string output)
    {
        Output = output;
        release.TrySetResult();
    }
}
