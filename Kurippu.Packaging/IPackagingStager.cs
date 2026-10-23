namespace Kurippu.Packaging;

public interface IPackagingStager
{
    Task StageAsync(string rid, string outputDirectory, CancellationToken cancellationToken);
}
