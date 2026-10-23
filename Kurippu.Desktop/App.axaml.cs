using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Kurippu.Desktop;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            if (Program.UiSmoke)
                desktop.MainWindow.Opened += (_, _) =>
                    DispatcherTimer.RunOnce(() => desktop.MainWindow?.Close(), TimeSpan.FromSeconds(1));
            if (Program.UiOpenSmoke)
                desktop.MainWindow.Opened += (_, _) =>
                    _ = RunOpenSmokeAsync(desktop);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunOpenSmokeAsync(IClassicDesktopStyleApplicationLifetime desktop)
    {
        try
        {
            await ((MainWindow)desktop.MainWindow!).RunOpenSmokeAsync().ConfigureAwait(true);
            Console.WriteLine("Kurippu UI open smoke passed");
            desktop.Shutdown(0);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            desktop.Shutdown(1);
        }
    }
}
