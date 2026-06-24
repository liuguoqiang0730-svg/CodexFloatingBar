using System.Windows;

namespace CodexFloatingBar;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Local\\CodexFloatingBar";

    private Mutex? _singleInstanceMutex;
    private TrayService? _trayService;
    private MainWindow? _window;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _window = new MainWindow();
        _window.Show();
        _window.Activate();

        _trayService = new TrayService(_window);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
