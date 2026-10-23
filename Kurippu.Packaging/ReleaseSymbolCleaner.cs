namespace Kurippu.Packaging;

public static class ReleaseSymbolCleaner
{
    public static void RemoveDebugSymbols(string outputDirectory)
    {
        foreach (var symbol in Directory.EnumerateFiles(outputDirectory, "*.pdb",
                     SearchOption.TopDirectoryOnly))
            File.Delete(symbol);
    }
}
