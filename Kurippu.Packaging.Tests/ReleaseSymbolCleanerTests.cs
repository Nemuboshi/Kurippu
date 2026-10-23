using Kurippu.Packaging;

namespace Kurippu.Packaging.Tests;

public sealed class ReleaseSymbolCleanerTests
{
    [Fact]
    public void ReleaseCleanupRemovesPdbButKeepsRuntimeFiles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "kurippu-symbol-tests-" +
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllText(Path.Combine(directory, "Kurippu.Desktop.pdb"), "symbols");
            File.WriteAllText(Path.Combine(directory, "Kurippu.Desktop.dll"), "runtime");
            File.WriteAllText(Path.Combine(directory, "libmpv-2.dll"), "native");

            ReleaseSymbolCleaner.RemoveDebugSymbols(directory);

            Assert.False(File.Exists(Path.Combine(directory, "Kurippu.Desktop.pdb")));
            Assert.True(File.Exists(Path.Combine(directory, "Kurippu.Desktop.dll")));
            Assert.True(File.Exists(Path.Combine(directory, "libmpv-2.dll")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
