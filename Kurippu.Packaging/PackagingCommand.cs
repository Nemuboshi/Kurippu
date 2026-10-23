namespace Kurippu.Packaging;

public sealed class PackagingCommand(
    IPackagingStager stager,
    Action<string>? cleanSymbols = null)
{
    private static readonly HashSet<string> SupportedRids =
        new(StringComparer.Ordinal) { "win-x64", "osx-x64", "osx-arm64", "linux-x64" };

    public int Run(string[] args, CancellationToken cancellationToken)
        => RunAsync(args, cancellationToken).GetAwaiter().GetResult();

    public async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        if (args.Length != 3 || !string.Equals(args[0], "stage", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("usage: Kurippu.Packaging stage <win-x64|osx-x64|osx-arm64|linux-x64> <publish-dir>");
            return 2;
        }

        var rid = args[1];
        if (!SupportedRids.Contains(rid))
        {
            Console.Error.WriteLine($"Unsupported runtime identifier: {rid}");
            return 2;
        }

        var output = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(output);
        await stager.StageAsync(rid, output, cancellationToken).ConfigureAwait(false);
        (cleanSymbols ?? ReleaseSymbolCleaner.RemoveDebugSymbols)(output);
        Console.WriteLine($"Runtime assets ready: {rid} -> {output}");
        return 0;
    }
}
