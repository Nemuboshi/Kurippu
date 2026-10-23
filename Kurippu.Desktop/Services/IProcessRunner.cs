namespace Kurippu.Desktop.Services;

public interface IProcessRunner
{
    Task<int> RunAsync(string executable, IReadOnlyList<string> args, IProgress<string>? log,
        CancellationToken cancellationToken);
}
