namespace Kurippu.Packaging.Tests;

public sealed class FakeProcessRunner(
    ProcessResult? result = null,
    Exception? failure = null,
    Func<string, IReadOnlyList<string>, ProcessResult>? response = null) : IProcessRunner
{
    private readonly List<(string FileName, string[] Arguments)> calls = [];
    public IReadOnlyList<(string FileName, string[] Arguments)> Calls => calls;

    public Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        calls.Add((fileName, arguments.ToArray()));
        if (failure is not null) return Task.FromException<ProcessResult>(failure);
        if (response is not null) return Task.FromResult(response(fileName, arguments));
        return Task.FromResult(result ?? new ProcessResult(0, "", ""));
    }
}
