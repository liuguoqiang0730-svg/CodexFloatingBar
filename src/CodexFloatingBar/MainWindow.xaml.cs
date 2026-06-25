using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace CodexFloatingBar;

public partial class MainWindow : Window
{
    private const double DefaultWidthRatio = 0.70;
    private const double DefaultHeight = 92;
    private const double MinimumWidth = 560;
    private const double MinimumHeight = 92;
    private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
    private static readonly string CodexHomePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private readonly CodexAccountService _accountService = new();
    private readonly AppearanceService _appearanceService = new();
    private readonly CodexConfigService _configService = new();
    private readonly CodexRateLimitService _rateLimitService = new();
    private readonly CodexSessionStatusService _sessionStatusService = new();
    private readonly WindowPlacementService _placementService = new();
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _liveRefreshTimer;
    private FileSystemWatcher? _configWatcher;
    private FileSystemWatcher? _rateLimitWatcher;
    private AppearanceSettings _appearanceSettings = AppearanceSettings.Default;
    private int _refreshVersion;
    private bool _allowClose;

    public MainWindow()
    {
        InitializeComponent();
        _appearanceSettings = _appearanceService.Read();
        ApplyAppearance();
        if (!_placementService.Restore(this))
        {
            ApplyDefaultSize();
        }
        else if (LooksLikeLegacyFixedSize())
        {
            ApplyDefaultSize();
        }

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

        SizeChanged += (_, _) => _placementService.Save(this);

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            UpdateStatus();
        };

        _liveRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _liveRefreshTimer.Tick += async (_, _) =>
        {
            _liveRefreshTimer.Stop();
            await UpdateLiveStatusAsync(Interlocked.Increment(ref _refreshVersion));
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
        var oldScale = _appearanceSettings.Scale;
        _appearanceSettings = _appearanceSettings with { Scale = Math.Clamp(scale, 0.82, 1.18) };
        _appearanceService.Save(_appearanceSettings);
        ApplyAppearance();
        ResizeForScale(oldScale, _appearanceSettings.Scale);
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
        MinWidth = MinimumWidth * _appearanceSettings.Scale;
        MinHeight = MinimumHeight * _appearanceSettings.Scale;
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

    private void ApplyDefaultSize()
    {
        var workArea = SystemParameters.WorkArea;
        Width = Math.Max(MinWidth, workArea.Width * DefaultWidthRatio);
        Height = Math.Max(MinHeight, DefaultHeight * _appearanceSettings.Scale);
    }

    private bool LooksLikeLegacyFixedSize()
    {
        var unscaledWidth = Width / _appearanceSettings.Scale;
        var unscaledHeight = Height / _appearanceSettings.Scale;
        return Math.Abs(unscaledHeight - 92) < 1
            && (Math.Abs(unscaledWidth - 560) < 1
                || Math.Abs(unscaledWidth - 720) < 1
                || Math.Abs(unscaledWidth - 850) < 1);
    }

    private void ResizeForScale(double oldScale, double newScale)
    {
        if (oldScale <= 0 || Math.Abs(oldScale - newScale) < 0.001)
        {
            return;
        }

        Width = Math.Max(MinWidth, Width / oldScale * newScale);
        Height = Math.Max(MinHeight, Height / oldScale * newScale);
    }

    private Task UpdateLiveStatusAsync(int refreshVersion) => UpdateUsageAsync(refreshVersion);

    private void ApplySessionStatus(CodexSessionStatus session)
    {
        if (!string.IsNullOrWhiteSpace(session.Model))
        {
            SetTextIfChanged(ModelText, $"model: {session.Model}");
        }

        if (!string.IsNullOrWhiteSpace(session.ReasoningEffort))
        {
            SetTextIfChanged(StateText, $"当前会话推理强度: {session.ReasoningEffort}");
        }
    }

    private static void SetTextIfChanged(TextBlock textBlock, string text)
    {
        if (!string.Equals(textBlock.Text, text, StringComparison.Ordinal))
        {
            textBlock.Text = text;
        }
    }

    private void UpdateAccountIdentity()
    {
        SetTextIfChanged(AccountText, _accountService.Read().DisplayText);
    }

    private async void UpdateStatus()
    {
        UpdateAccountIdentity();
        var usageRefreshVersion = Interlocked.Increment(ref _refreshVersion);
        var result = _configService.Read(ConfigPath);
        if (!result.Exists)
        {
            SetTextIfChanged(ModelText, "model: 未找到 ~/.codex/config.toml");
            SetTextIfChanged(StateText, "当前会话: 正在读取本地 Codex 记录");
            SetTextIfChanged(ConfigText, "剩余用量: 正在读取本地 Codex 记录");
            SetTextIfChanged(ManualText, result.Message ?? string.Empty);
            await UpdateUsageAsync(usageRefreshVersion);
            return;
        }

        if (!result.ReadSucceeded)
        {
            SetTextIfChanged(ModelText, "model: 读取配置失败");
            SetTextIfChanged(StateText, "当前会话: 正在读取本地 Codex 记录");
            SetTextIfChanged(ConfigText, "剩余用量: 正在读取本地 Codex 记录");
            SetTextIfChanged(ManualText, result.Message ?? string.Empty);
            await UpdateUsageAsync(usageRefreshVersion);
            return;
        }

        SetTextIfChanged(ModelText, $"model: {result.Model ?? "未配置"}");
        SetTextIfChanged(StateText, "当前会话推理强度: 读取中");
        SetTextIfChanged(ConfigText, "剩余用量: 正在读取本地 Codex 记录");
        SetTextIfChanged(ManualText, $"配置文件: {ConfigPath}  |  最近刷新: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        await UpdateUsageAsync(usageRefreshVersion);
    }

    private async Task UpdateUsageAsync(int refreshVersion)
    {
        var session = await _sessionStatusService.ReadLatestAsync();
        var usage = await _rateLimitService.ReadLatestAsync();
        if (refreshVersion != _refreshVersion)
        {
            return;
        }

        ApplySessionStatus(session);
        SetTextIfChanged(ConfigText, usage.Message);
    }

    private void StartWatcher()
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
        {
            return;
        }

        _configWatcher = new FileSystemWatcher(dir, Path.GetFileName(ConfigPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _configWatcher.Changed += (_, _) => DebounceRefresh();
        _configWatcher.Created += (_, _) => DebounceRefresh();
        _configWatcher.Renamed += (_, _) => DebounceRefresh();

        if (!Directory.Exists(CodexHomePath))
        {
            return;
        }

        _rateLimitWatcher = new FileSystemWatcher(CodexHomePath, "logs_2.sqlite*")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _rateLimitWatcher.Changed += (_, _) => DebounceLiveRefresh();
        _rateLimitWatcher.Created += (_, _) => DebounceLiveRefresh();
        _rateLimitWatcher.Renamed += (_, _) => DebounceLiveRefresh();
    }

    private void DebounceRefresh()
    {
        Dispatcher.Invoke(() =>
        {
            _debounceTimer.Stop();
            _debounceTimer.Start();
        });
    }

    private void DebounceLiveRefresh()
    {
        Dispatcher.Invoke(() =>
        {
            _liveRefreshTimer.Stop();
            _liveRefreshTimer.Start();
        });
    }

    protected override void OnClosed(EventArgs e)
    {
        _configWatcher?.Dispose();
        _rateLimitWatcher?.Dispose();
        base.OnClosed(e);
    }
}
