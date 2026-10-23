namespace Kurippu.Packaging;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            return new PackagingCommand(new RuntimeStager()).Run(args, CancellationToken.None);
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
