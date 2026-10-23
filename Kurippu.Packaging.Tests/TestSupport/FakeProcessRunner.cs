namespace Kurippu.Packaging.Tests;

public sealed class FakeProcessRunner(
    ProcessResult? result = null,
    Exception? failure = null,
    Func<string, IReadOnlyList<string>, ProcessResult>? response = null) : IProcessRunner
{
    public List<(string FileName, string[] Arguments)> Calls { get; } = [];

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        Calls.Add((fileName, arguments.ToArray()));
        if (failure is not null) return Task.FromException<ProcessResult>(failure);
        if (response is not null) return Task.FromResult(response(fileName, arguments));
        return Task.FromResult(result ?? new ProcessResult(0, "", ""));
    }
}
