using System.Security.Cryptography;

namespace Kurippu.Packaging;

public sealed class HttpAssetDownloader(HttpClient client) : IAssetDownloader
{
    public async Task DownloadAsync(Uri source, string destination, string sha256,
        CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(destination))!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, "." + Path.GetFileName(destination) + ".partial-" +
            Guid.NewGuid().ToString("N"));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, source);
            request.Headers.UserAgent.ParseAdd("Kurippu-build");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using (input.ConfigureAwait(false))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 131072, FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    var buffer = new byte[131072];
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }
                }

                var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!string.Equals(actual, sha256, StringComparison.OrdinalIgnoreCase))
                    throw new PackagingException($"SHA-256 checksum mismatch for {source}: expected {sha256}, got {actual}.");

                File.Move(temporary, destination, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
