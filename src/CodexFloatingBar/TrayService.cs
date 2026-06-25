using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;

namespace CodexFloatingBar;

internal sealed class TrayService : IDisposable
{
    private const string GitHubRepositoryUrl = "https://github.com/liuguoqiang0730-svg/CodexFloatingBar";
    private const string ChatGptUrl = "https://chatgpt.com";
    private const string BillingUrl = "https://platform.openai.com/account/billing/overview";
    private const string ApiUsageUrl = "https://platform.openai.com/usage";
    private const string ApiKeysUrl = "https://platform.openai.com/settings/organization/admin-keys";

    private readonly NotifyIcon _notifyIcon;
    private readonly Icon _trayIcon;
    private readonly StartupService _startupService = new();
    private readonly ToolStripMenuItem _darkThemeMenuItem;
    private readonly ToolStripMenuItem _lightThemeMenuItem;
    private readonly ToolStripMenuItem _horizontalLayoutMenuItem;
    private readonly ToolStripMenuItem _verticalLayoutMenuItem;
    private readonly ToolStripMenuItem _scaleSmallMenuItem;
    private readonly ToolStripMenuItem _scaleNormalMenuItem;
    private readonly ToolStripMenuItem _scaleLargeMenuItem;
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
        menu.Items.Add("打开 ChatGPT 账户页", null, (_, _) => OpenUrl(ChatGptUrl));
        menu.Items.Add("打开 Billing 页面", null, (_, _) => OpenUrl(BillingUrl));
        menu.Items.Add("打开 API 用量页面", null, (_, _) => OpenUrl(ApiUsageUrl));
        menu.Items.Add("打开 API Keys 页面", null, (_, _) => OpenUrl(ApiKeysUrl));
        menu.Items.Add("打开 GitHub 仓库", null, (_, _) => OpenUrl(GitHubRepositoryUrl));
        menu.Items.Add(new ToolStripSeparator());

        var themeMenu = new ToolStripMenuItem("配色");
        _darkThemeMenuItem = new ToolStripMenuItem("黑色磨砂");
        _darkThemeMenuItem.Click += (_, _) => InvokeOnUi(() => _window.SetTheme(AppearanceTheme.Dark));
        _lightThemeMenuItem = new ToolStripMenuItem("灰白清爽");
        _lightThemeMenuItem.Click += (_, _) => InvokeOnUi(() => _window.SetTheme(AppearanceTheme.Light));
        themeMenu.DropDownItems.Add(_darkThemeMenuItem);
        themeMenu.DropDownItems.Add(_lightThemeMenuItem);
        menu.Items.Add(themeMenu);

        var layoutMenu = new ToolStripMenuItem("布局");
        _horizontalLayoutMenuItem = new ToolStripMenuItem("横版");
        _horizontalLayoutMenuItem.Click += (_, _) => InvokeOnUi(() => _window.SetLayout(BarLayout.Horizontal));
        _verticalLayoutMenuItem = new ToolStripMenuItem("竖版");
        _verticalLayoutMenuItem.Click += (_, _) => InvokeOnUi(() => _window.SetLayout(BarLayout.Vertical));
        layoutMenu.DropDownItems.Add(_horizontalLayoutMenuItem);
        layoutMenu.DropDownItems.Add(_verticalLayoutMenuItem);
        menu.Items.Add(layoutMenu);

        var scaleMenu = new ToolStripMenuItem("缩放");
        _scaleSmallMenuItem = new ToolStripMenuItem("小 90%");
        _scaleSmallMenuItem.Click += (_, _) => InvokeOnUi(() => _window.SetScale(0.9));
        _scaleNormalMenuItem = new ToolStripMenuItem("标准 100%");
        _scaleNormalMenuItem.Click += (_, _) => InvokeOnUi(() => _window.SetScale(1.0));
        _scaleLargeMenuItem = new ToolStripMenuItem("大 110%");
        _scaleLargeMenuItem.Click += (_, _) => InvokeOnUi(() => _window.SetScale(1.1));
        scaleMenu.DropDownItems.Add(_scaleSmallMenuItem);
        scaleMenu.DropDownItems.Add(_scaleNormalMenuItem);
        scaleMenu.DropDownItems.Add(_scaleLargeMenuItem);
        menu.Items.Add(scaleMenu);

        _startupMenuItem = new ToolStripMenuItem("开机自启动")
        {
            CheckOnClick = false,
            Checked = _startupService.IsEnabled()
        };
        _startupMenuItem.Click += (_, _) => ToggleStartup();
        menu.Items.Add(_startupMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => InvokeOnUi(ExitApplication));
        menu.Opening += (_, _) => SyncMenuChecks();

        _trayIcon = CreateTrayIcon();
        _notifyIcon = new NotifyIcon
        {
            Text = "CodexFloatingBar",
            Icon = _trayIcon,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => InvokeOnUi(() =>
        {
            _window.ShowFromTray();
        });
    }

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static Icon CreateTrayIcon()
    {
        if (!string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            return Icon.ExtractAssociatedIcon(Environment.ProcessPath) ?? (Icon)SystemIcons.Application.Clone();
        }

        return (Icon)SystemIcons.Application.Clone();
    }

    private void SyncMenuChecks()
    {
        _startupMenuItem.Checked = _startupService.IsEnabled();
        _darkThemeMenuItem.Checked = _window.CurrentTheme == AppearanceTheme.Dark;
        _lightThemeMenuItem.Checked = _window.CurrentTheme == AppearanceTheme.Light;
        _horizontalLayoutMenuItem.Checked = _window.CurrentLayout == BarLayout.Horizontal;
        _verticalLayoutMenuItem.Checked = _window.CurrentLayout == BarLayout.Vertical;
        _scaleSmallMenuItem.Checked = Math.Abs(_window.CurrentScale - 0.9) < 0.001;
        _scaleNormalMenuItem.Checked = Math.Abs(_window.CurrentScale - 1.0) < 0.001;
        _scaleLargeMenuItem.Checked = Math.Abs(_window.CurrentScale - 1.1) < 0.001;
    }

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
        _trayIcon.Dispose();
    }
}
