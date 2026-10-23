namespace Kurippu.Packaging.Tests;

public sealed class FakeProcessRunner(
    ProcessResult? result = null,
    Exception? failure = null) : IProcessRunner
{
    public List<(string FileName, string[] Arguments)> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments.ToArray()));
        if (failure is not null) return Task.FromException<ProcessResult>(failure);
        return Task.FromResult(result ?? new ProcessResult(0, "", ""));
    }
}
