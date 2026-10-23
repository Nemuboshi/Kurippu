using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class FakeAssetDownloader(
    IReadOnlyDictionary<string, string>? sources = null,
    Exception? failure = null) : IAssetDownloader
{
    public List<(Uri Source, string Destination, string Sha256)> Calls { get; } = [];

    public async Task DownloadAsync(Uri source, string destination, string sha256,
        CancellationToken cancellationToken)
    {
        Calls.Add((source, destination, sha256));
        if (failure is not null) throw failure;
        if (sources is not null && sources.TryGetValue(source.AbsoluteUri, out var local))
            await CopyAsync(local, destination, cancellationToken);
        else
            await File.WriteAllTextAsync(destination, "fake", cancellationToken);
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = File.OpenRead(source);
        await using var output = File.Create(destination);
        await input.CopyToAsync(output, cancellationToken);
    }
}
