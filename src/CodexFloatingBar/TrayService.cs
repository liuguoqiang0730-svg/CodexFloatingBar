using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace CodexFloatingBar;

internal sealed class TrayService : IDisposable
{
    private const string GitHubRepositoryUrl = "https://github.com/liuguoqiang0730-svg/CodexFloatingBar";

    private readonly NotifyIcon _notifyIcon;
    private readonly StartupService _startupService = new();
    private readonly ToolStripMenuItem _startupMenuItem;
    private readonly MainWindow _window;

    public TrayService(MainWindow window)
    {
        _window = window;

        var menu = new ContextMenuStrip();
        menu.Items.Add("刷新状态", null, (_, _) => InvokeOnUi(() => _window.RefreshStatus()));
        menu.Items.Add("复制当前状态", null, (_, _) => InvokeOnUi(CopyStatus));
        menu.Items.Add("显示/隐藏窗口", null, (_, _) => InvokeOnUi(() => _window.ToggleVisibilityFromTray()));
        menu.Items.Add("打开配置文件", null, (_, _) => OpenConfig());
        menu.Items.Add("打开 ChatGPT 账户页", null, (_, _) => OpenUrl("https://chatgpt.com"));
        menu.Items.Add("打开 Billing 页面", null, (_, _) => OpenUrl("https://platform.openai.com/account/billing/overview"));
        menu.Items.Add("打开 GitHub 仓库", null, (_, _) => OpenUrl(GitHubRepositoryUrl));
        menu.Items.Add(new ToolStripSeparator());
        _startupMenuItem = new ToolStripMenuItem("开机自启动")
        {
            CheckOnClick = false,
            Checked = _startupService.IsEnabled()
        };
        _startupMenuItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => InvokeOnUi(ExitApplication));
        menu.Opening += (_, _) => _startupMenuItem.Checked = _startupService.IsEnabled();

        _notifyIcon = new NotifyIcon
        {
            Text = "CodexFloatingBar",
            Icon = SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => InvokeOnUi(() =>
        {
            _window.ShowFromTray();
        });
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private void CopyStatus()
    {
        if (_window.CopyStatusToClipboard())
        {
            _notifyIcon.ShowBalloonTip(1200, "CodexFloatingBar", "当前状态已复制到剪贴板。", ToolTipIcon.Info);
        }
    }

    private void ExitApplication()
    {
        _window.AllowClose();
        System.Windows.Application.Current.Shutdown();
    }

    private void ToggleStartup()
    {
        var enable = !_startupService.IsEnabled();
        if (!_startupService.TrySetEnabled(enable, out var errorMessage))
        {
            System.Windows.MessageBox.Show(errorMessage ?? "修改开机自启动失败。", "CodexFloatingBar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        _startupMenuItem.Checked = _startupService.IsEnabled();
    }

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
