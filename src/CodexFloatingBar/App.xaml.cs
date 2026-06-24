using System.Windows;

namespace CodexFloatingBar;

public partial class App : System.Windows.Application
{
    private TrayService? _trayService;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _window = new MainWindow();
        _window.Show();
        _window.Activate();

        _trayService = new TrayService(_window);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        base.OnExit(e);
    }
}
