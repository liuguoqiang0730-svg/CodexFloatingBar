using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CodexFloatingBar;

public partial class MainWindow : Window
{
    private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
    private readonly CodexConfigService _configService = new();
    private readonly OpenAiUsageService _usageService = new();
    private readonly WindowPlacementService _placementService = new();
    private readonly DispatcherTimer _debounceTimer;
    private FileSystemWatcher? _watcher;
    private int _usageRefreshVersion;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        _placementService.Restore(this);

        Loaded += (_, _) =>
        {
            UpdateStatus();
            StartWatcher();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                _placementService.Save(this);
            }
        };

        Closing += (_, e) =>
        {
            _placementService.Save(this);
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            UpdateStatus();
        };
    }

    public void RefreshStatus() => UpdateStatus();

    public void AllowClose() => _allowClose = true;

    public bool CopyStatusToClipboard()
    {
        var status = string.Join(
            Environment.NewLine,
            TitleText.Text,
            ModelText.Text,
            StateText.Text,
            ConfigText.Text,
            ManualText.Text);

        try
        {
            System.Windows.Clipboard.SetText(status);
            return true;
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            System.Windows.MessageBox.Show($"复制状态失败: {ex.Message}", "CodexFloatingBar", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }

    public void ShowFromTray()
    {
        if (!IsVisible)
        {
            Show();
        }

        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
        Topmost = true;
    }

    public void ToggleVisibilityFromTray()
    {
        if (IsVisible)
        {
            _placementService.Save(this);
            Hide();
            return;
        }

        ShowFromTray();
    }

    private void RefreshClicked(object sender, RoutedEventArgs e) => UpdateStatus();

    private async void UpdateStatus()
    {
        var usageRefreshVersion = Interlocked.Increment(ref _usageRefreshVersion);
        var result = _configService.Read(ConfigPath);
        if (!result.Exists)
        {
            ModelText.Text = "model: 未找到 ~/.codex/config.toml";
            StateText.Text = "登录/配置: 需手动查看";
            ConfigText.Text = "推理强度/速率: 需手动查看；套餐/余额/额度/到期: 官方未提供稳定本地读取";
            ManualText.Text = result.Message;
            await UpdateUsageAsync(usageRefreshVersion);
            return;
        }

        if (!result.ReadSucceeded)
        {
            ModelText.Text = "model: 读取配置失败";
            StateText.Text = "登录/配置: 本地配置读取失败";
            ConfigText.Text = "推理强度/速率: 暂不可用；套餐/余额/额度/到期: 官方未提供稳定本地读取";
            ManualText.Text = result.Message;
            await UpdateUsageAsync(usageRefreshVersion);
            return;
        }

        ModelText.Text = $"model: {result.Model ?? "未配置"}";
        StateText.Text = $"推理强度/速率: {result.ReasoningEffort ?? "未配置"}  |  登录/配置: 已读取本地配置";
        ConfigText.Text = "API 用量: 正在读取；余额/ChatGPT 额度需手动查看";
        ManualText.Text = $"配置文件: {ConfigPath}  |  最近刷新: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
        await UpdateUsageAsync(usageRefreshVersion);
    }

    private async Task UpdateUsageAsync(int refreshVersion)
    {
        var usage = await _usageService.ReadTodayAsync();
        if (refreshVersion != _usageRefreshVersion)
        {
            return;
        }

        ConfigText.Text = usage.Message;
    }

    private void StartWatcher()
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return;
        }

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(ConfigPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _watcher.Changed += (_, _) => DebounceRefresh();
        _watcher.Created += (_, _) => DebounceRefresh();
        _watcher.Renamed += (_, _) => DebounceRefresh();
    }

    private void DebounceRefresh()
    {
        Dispatcher.Invoke(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _watcher?.Dispose();
        base.OnClosed(e);
    }
}
