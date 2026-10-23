namespace Kurippu.Packaging;

public interface IAssetDownloader
{
    Task DownloadAsync(Uri source, string destination, string sha256, CancellationToken cancellationToken);
}
