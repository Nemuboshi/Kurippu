namespace Kurippu.Packaging;

public sealed class PackagingCommand(IPackagingStager stager)
{
    private static readonly HashSet<string> SupportedRids =
        new(StringComparer.Ordinal) { "win-x64", "osx-x64", "osx-arm64", "linux-x64" };

    public int Run(string[] args, CancellationToken cancellationToken)
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

        stager.StageAsync(rid, args[2], cancellationToken).GetAwaiter().GetResult();
        return 0;
    }
}
