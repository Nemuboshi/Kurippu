using Avalonia;
using System;

namespace Kurippu.Desktop;

class Program
{
    internal static bool UiSmoke { get; private set; }
    internal static bool UiOpenSmoke { get; private set; }
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--smoke"))
        {
            Services.RuntimeSmoke.RunAsync().GetAwaiter().GetResult();
            return;
        }
        UiSmoke = args.Contains("--ui-smoke");
        UiOpenSmoke = args.Contains("--ui-open-smoke");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
