namespace Kurippu.Packaging;

public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string fileName, IReadOnlyList<string> arguments,
        CancellationToken cancellationToken);
}
