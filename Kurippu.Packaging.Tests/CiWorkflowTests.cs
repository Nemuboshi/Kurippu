namespace Kurippu.Packaging.Tests;

public sealed class CiWorkflowTests
{
    [Fact]
    public void CiPackagesWithCsharpTargetAndArchivesFromRepositoryRoot()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "ci.yml"));

        Assert.Contains(
            "dotnet msbuild Kurippu.Desktop/Kurippu.Desktop.csproj -t:Package " +
            "-p:Configuration=Release -p:RuntimeIdentifier=${{ matrix.rid }}",
            workflow, StringComparison.Ordinal);
        Assert.Contains(
            "tar -czf kurippu-${{ matrix.rid }}.tar.gz -C publish ${{ matrix.rid }}",
            workflow, StringComparison.Ordinal);
        Assert.Contains(
            "tar -xzf kurippu-${{ matrix.rid }}.tar.gz -C extracted",
            workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("cd ../..", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("stage_runtime.py", workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativeParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. relativeParts]);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not find {Path.Combine(relativeParts)} above {AppContext.BaseDirectory}.");
    }
}
