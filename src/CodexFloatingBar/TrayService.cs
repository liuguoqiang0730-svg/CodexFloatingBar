using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace CodexFloatingBar;

internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly MainWindow _window;

    public TrayService(MainWindow window)
    {
        _window = window;

        var menu = new ContextMenuStrip();
        menu.Items.Add("刷新状态", null, (_, _) => InvokeOnUi(() => _window.RefreshStatus()));
        menu.Items.Add("打开配置文件", null, (_, _) => OpenConfig());
        menu.Items.Add("打开 ChatGPT 账户页", null, (_, _) => OpenUrl("https://chatgpt.com"));
        menu.Items.Add("打开 Billing 页面", null, (_, _) => OpenUrl("https://platform.openai.com/account/billing/overview"));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => InvokeOnUi(() => System.Windows.Application.Current.Shutdown()));

        _notifyIcon = new NotifyIcon
        {
            Text = "CodexFloatingBar",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => InvokeOnUi(() =>
        {
            if (!_window.IsVisible)
            {
                _window.Show();
            }
            _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.Topmost = true;
            _window.Topmost = false;
            _window.Topmost = true;
        });
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void OpenConfig()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
        if (!File.Exists(path))
        {
            System.Windows.MessageBox.Show($"未找到配置文件: {path}", "CodexFloatingBar", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void InvokeOnUi(Action action)
    {
        if (_window.Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _window.Dispatcher.Invoke(action);
        }
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
