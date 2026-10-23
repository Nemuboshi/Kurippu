using System.Text.RegularExpressions;

namespace Kurippu.Packaging;

public sealed partial class MacNativeDependencyInspector(IProcessRunner processRunner)
    : INativeDependencyInspector
{
    public async Task<IReadOnlyList<string>> GetDependenciesAsync(string file,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("otool", ["-L", file], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw ProcessFailure("otool -L", file, result);

        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Skip(1)
            .Select(line => DependencyRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["library"].Value)
            .ToArray();
    }

    internal static PackagingException ProcessFailure(string tool, string file,
        ProcessResult result) =>
        new($"{tool} failed for {file} with exit {result.ExitCode}." +
            Environment.NewLine + result.StandardOutput + Environment.NewLine +
            result.StandardError);

    [GeneratedRegex(@"^\s+(?<library>\S+)\s+\(", RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 1000)]
    private static partial Regex DependencyRegex();
}
