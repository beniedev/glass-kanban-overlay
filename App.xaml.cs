using System;
using System.Threading;
using System.Windows;

namespace DesktopOverlayBoard;

public partial class App : System.Windows.Application
{
    private readonly Services.SingleInstanceService _singleInstance = new();
    private MainWindow? _mainWindow;
    private int _activationPending;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!_singleInstance.TryAcquire(OnActivationRequested))
        {
            Shutdown();
            return;
        }

        var config = new Services.ConfigService().Load();
        Services.StartupService.ApplyStartWithWindows(config.Startup.StartWithWindows);
        var launchedFromStartup = e.Args.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
        var window = new MainWindow();
        _mainWindow = window;
        window.SetLaunchedFromStartup(launchedFromStartup);
        if (config.Startup.StartMinimizedToTray)
        {
            window.HideAfterInitialLoad();
        }

        window.Show();
        if (Interlocked.Exchange(ref _activationPending, 0) == 1)
        {
            ActivateMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance.Dispose();
        base.OnExit(e);
    }

    private void OnActivationRequested()
    {
        var window = _mainWindow;
        if (window is null)
        {
            Interlocked.Exchange(ref _activationPending, 1);
            return;
        }

        window.Dispatcher.BeginInvoke(new Action(ActivateMainWindow));
    }

    private void ActivateMainWindow()
    {
        var window = _mainWindow;
        if (window is null || window.Dispatcher.HasShutdownStarted)
        {
            return;
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        if (!window.IsVisible)
        {
            window.Show();
        }

        window.Activate();
    }
}
