using System.Windows;

namespace DesktopOverlayBoard;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var config = new Services.ConfigService().Load();
        Services.StartupService.ApplyStartWithWindows(config.Startup.StartWithWindows);
        var launchedFromStartup = e.Args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow();
        window.SetLaunchedFromStartup(launchedFromStartup);
        if (config.Startup.StartMinimizedToTray)
        {
            window.HideAfterInitialLoad();
        }

        window.Show();
    }
}
