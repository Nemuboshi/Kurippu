using System.Text.RegularExpressions;

namespace Kurippu.Packaging;

public sealed partial class LinuxNativeDependencyInspector(IProcessRunner processRunner)
    : INativeDependencyInspector
{
    public async Task<IReadOnlyList<string>> GetDependenciesAsync(string file,
        CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync("ldd", [file], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw MacNativeDependencyInspector.ProcessFailure("ldd", file, result);
        if (result.StandardOutput.Contains("not found", StringComparison.Ordinal))
            throw new PackagingException($"Missing dependency for {file}:{Environment.NewLine}" +
                result.StandardOutput);

        return result.StandardOutput.Split(["\r\n", "\n"],
                StringSplitOptions.RemoveEmptyEntries)
            .Select(line => DependencyRegex().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups["path"].Value)
            .ToArray();
    }

    [GeneratedRegex(@"^\s+(?<library>\S+\.so(?:\.\d+)*)\s+=>\s+(?<path>.+?)(?:\s+\(0x[0-9a-fA-F]+\))?\s*$",
        RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex DependencyRegex();
}
