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
            await CopyAsync(local, destination, cancellationToken).ConfigureAwait(false);
        else
            await File.WriteAllTextAsync(destination, "fake", cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var input = File.OpenRead(source);
        await using (input.ConfigureAwait(false))
        {
            await using var output = File.Create(destination);
            await input.CopyToAsync(output, cancellationToken);
        }
    }
}
