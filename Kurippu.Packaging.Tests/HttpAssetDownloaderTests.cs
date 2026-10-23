using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class HttpAssetDownloaderTests
{
    [Fact]
    public async Task MatchingChecksumMovesTheArchiveIntoPlace()
    {
        var content = Encoding.UTF8.GetBytes("verified payload");
        using var client = new HttpClient(new StubHandler(content));
        var downloader = new HttpAssetDownloader(client);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "asset.bin");

        try
        {
            await downloader.DownloadAsync(new Uri("https://example.test/asset.bin"), destination,
                Sha256(content), CancellationToken.None);

            Assert.Equal(content, await File.ReadAllBytesAsync(destination));
            Assert.Single(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MismatchedChecksumThrowsAndLeavesNoArchive()
    {
        using var client = new HttpClient(new StubHandler(Encoding.UTF8.GetBytes("payload")));
        var downloader = new HttpAssetDownloader(client);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "asset.bin");

        try
        {
            var exception = await Assert.ThrowsAsync<PackagingException>(() =>
                downloader.DownloadAsync(new Uri("https://example.test/asset.bin"), destination,
                    new string('0', 64), CancellationToken.None));

            Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CancellationDoesNotPublishAPartialFile()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var client = new HttpClient(new StubHandler(Encoding.UTF8.GetBytes("payload")));
        var downloader = new HttpAssetDownloader(client);
        var directory = CreateTempDirectory();
        var destination = Path.Combine(directory, "asset.bin");

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                downloader.DownloadAsync(new Uri("https://example.test/asset.bin"), destination,
                    new string('0', 64), cancellation.Token));

            Assert.False(File.Exists(destination));
            Assert.Empty(Directory.EnumerateFiles(directory));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-download-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static string Sha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class StubHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }
}
