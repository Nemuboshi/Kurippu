namespace Kurippu.Packaging;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(120) };
            var processRunner = new ExternalProcessRunner();
            var stager = new RuntimeStager(
                new HttpAssetDownloader(client),
                processRunner,
                new NativeDependencyStager(processRunner));
            return new PackagingCommand(stager).Run(args, CancellationToken.None);
        }
        catch (PackagingException exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }
}
