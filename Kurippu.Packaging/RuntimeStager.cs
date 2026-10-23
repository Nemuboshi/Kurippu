namespace Kurippu.Packaging;

public sealed class RuntimeStager : IPackagingStager
{
    public Task StageAsync(string rid, string outputDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetFullPath(outputDirectory));
        return Task.CompletedTask;
    }
}
