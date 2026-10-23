using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class FakeAssetDownloader(
    IReadOnlyDictionary<string, string>? sources = null,
    Exception? failure = null) : IAssetDownloader
{
    private readonly List<(Uri Source, string Destination, string Sha256)> calls = [];
    public IReadOnlyList<(Uri Source, string Destination, string Sha256)> Calls => calls;

    public async Task DownloadAsync(Uri source, string destination, string sha256,
        CancellationToken cancellationToken)
    {
        calls.Add((source, destination, sha256));
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
            var output = File.Create(destination);
            await using (output.ConfigureAwait(false))
                await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
        }
    }
}
