namespace Kurippu.Packaging;

public interface INativeDependencyInspector
{
    Task<IReadOnlyList<string>> GetDependenciesAsync(string file,
        CancellationToken cancellationToken);
}
