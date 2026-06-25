using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexFloatingBar;

public partial class MainWindow : Window
{
    private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
    private readonly CodexAccountService _accountService = new();
    private readonly AppearanceService _appearanceService = new();
    private readonly CodexConfigService _configService = new();
    private readonly OpenAiUsageService _usageService = new();
    private readonly WindowPlacementService _placementService = new();
    private readonly DispatcherTimer _debounceTimer;
    private FileSystemWatcher? _watcher;
    private AppearanceSettings _appearanceSettings = AppearanceSettings.Default;
    private int _usageRefreshVersion;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        _appearanceSettings = _appearanceService.Read();
        ApplyAppearance();
        _placementService.Restore(this);

        Loaded += (_, _) =>
        {
            UpdateAccountIdentity();
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

    internal AppearanceTheme CurrentTheme => _appearanceSettings.Theme;

    public double CurrentScale => _appearanceSettings.Scale;

    public void RefreshStatus() => UpdateStatus();

    public void AllowClose() => _allowClose = true;

    internal void SetTheme(AppearanceTheme theme)
    {
        _appearanceSettings = _appearanceSettings with { Theme = theme };
        _appearanceService.Save(_appearanceSettings);
        ApplyAppearance();
    }

    public void SetScale(double scale)
    {
        _appearanceSettings = _appearanceSettings with { Scale = Math.Clamp(scale, 0.82, 1.18) };
        _appearanceService.Save(_appearanceSettings);
        ApplyAppearance();
        _placementService.Save(this);
    }

    public bool CopyStatusToClipboard()
    {
        var status = string.Join(
            Environment.NewLine,
            TitleText.Text,
            AccountText.Text,
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

    private void ApplyAppearance()
    {
        Width = 720 * _appearanceSettings.Scale;
        Height = 92 * _appearanceSettings.Scale;
        MinWidth = Width;
        MinHeight = Height;
        Shell.LayoutTransform = new ScaleTransform(_appearanceSettings.Scale, _appearanceSettings.Scale);

        if (_appearanceSettings.Theme == AppearanceTheme.Light)
        {
            ApplyTheme(
                surface: "#F4F7F8FA",
                border: "#33000000",
                card: "#FFFFFFFF",
                cardBorder: "#1F000000",
                primaryText: "#FF1E252E",
                secondaryText: "#D91E252E",
                mutedText: "#8C1E252E",
                badge: "#0F000000",
                logo: "#18359764",
                logoBorder: "#38359764",
                logoText: "#FF2F7D52");
            return;
        }

        ApplyTheme(
            surface: "#F20F1216",
            border: "#2EFFFFFF",
            card: "#181D23",
            cardBorder: "#22FFFFFF",
            primaryText: "#FFFFFFFF",
            secondaryText: "#D8FFFFFF",
            mutedText: "#98FFFFFF",
            badge: "#1FFFFFFF",
            logo: "#22359764",
            logoBorder: "#55359764",
            logoText: "#FF7DE3B2");
    }

    private void ApplyTheme(
        string surface,
        string border,
        string card,
        string cardBorder,
        string primaryText,
        string secondaryText,
        string mutedText,
        string badge,
        string logo,
        string logoBorder,
        string logoText)
    {
        SetBrush("SurfaceBrush", surface);
        SetBrush("BorderBrushToken", border);
        SetBrush("CardBrush", card);
        SetBrush("CardBorderBrush", cardBorder);
        SetBrush("PrimaryTextBrush", primaryText);
        SetBrush("SecondaryTextBrush", secondaryText);
        SetBrush("MutedTextBrush", mutedText);
        SetBrush("AccentBrush", "#2F7D52");
        SetBrush("AccentHoverBrush", "#359764");
        SetBrush("AccentPressedBrush", "#256A45");
        SetBrush("BadgeBrush", badge);
        SetBrush("LogoBrush", logo);
        SetBrush("LogoBorderBrush", logoBorder);
        SetBrush("LogoTextBrush", logoText);
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void UpdateAccountIdentity()
    {
        AccountText.Text = _accountService.Read().DisplayText;
    }

    private async void UpdateStatus()
    {
        UpdateAccountIdentity();
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
