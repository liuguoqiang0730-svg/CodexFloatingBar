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
    private const double DefaultHeightRatio = 0.70;
    private const double DefaultHeight = 118;
    private const double HorizontalMinimumWidth = 560;
    private const double HorizontalMinimumHeight = 92;
    private const double VerticalMinimumWidth = 280;
    private const double VerticalMinimumHeight = 420;
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
    private FileSystemWatcher? _stateWatcher;
    private AppearanceSettings _appearanceSettings = AppearanceSettings.Default;
    private string? _configuredModel;
    private string? _configuredReasoningEffort;
    private string? _currentModel;
    private string? _currentReasoningEffort;
    private string? _currentSpeedTier;
    private string _currentManualStatus = "读取中";
    private string _currentUsageStatus = "读取中";
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

    internal BarLayout CurrentLayout => _appearanceSettings.Layout;

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

    internal void SetLayout(BarLayout layout)
    {
        if (_appearanceSettings.Layout == layout)
        {
            return;
        }

        _appearanceSettings = _appearanceSettings with { Layout = layout };
        _appearanceService.Save(_appearanceSettings);
        ApplyAppearance();
        RenderStatusText();
        ApplyDefaultSize();
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

    private void ToggleThemeClicked(object sender, RoutedEventArgs e)
    {
        SetTheme(_appearanceSettings.Theme == AppearanceTheme.Dark ? AppearanceTheme.Light : AppearanceTheme.Dark);
    }

    private void ToggleLayoutClicked(object sender, RoutedEventArgs e)
    {
        SetLayout(_appearanceSettings.Layout == BarLayout.Horizontal ? BarLayout.Vertical : BarLayout.Horizontal);
    }

    private void ApplyAppearance()
    {
        ApplyLayout();
        Shell.LayoutTransform = new ScaleTransform(_appearanceSettings.Scale, _appearanceSettings.Scale);

        if (_appearanceSettings.Theme == AppearanceTheme.Light)
        {
            ApplyTheme(
                surface: "#F7F6F7F8",
                border: "#330F1720",
                card: "#FFFFFFFF",
                cardBorder: "#260F1720",
                primaryText: "#FF111827",
                secondaryText: "#D9111827",
                mutedText: "#8A111827",
                accent: "#315C6B",
                accentHover: "#3B7284",
                accentPressed: "#254956",
                badge: "#0D111827",
                logo: "#14315C6B",
                logoBorder: "#33315C6B",
                logoText: "#FF315C6B");
            return;
        }

        ApplyTheme(
            surface: "#F208090B",
            border: "#35FFFFFF",
            card: "#111317",
            cardBorder: "#2EFFFFFF",
            primaryText: "#FFFFFFFF",
            secondaryText: "#E0FFFFFF",
            mutedText: "#9CFFFFFF",
            accent: "#2E8B67",
            accentHover: "#37A77B",
            accentPressed: "#246F52",
            badge: "#20FFFFFF",
            logo: "#1F2E8B67",
            logoBorder: "#552E8B67",
            logoText: "#FF85E0B9");
    }

    private void ApplyTheme(
        string surface,
        string border,
        string card,
        string cardBorder,
        string primaryText,
        string secondaryText,
        string mutedText,
        string accent,
        string accentHover,
        string accentPressed,
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
        SetBrush("AccentBrush", accent);
        SetBrush("AccentHoverBrush", accentHover);
        SetBrush("AccentPressedBrush", accentPressed);
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
        if (_appearanceSettings.Layout == BarLayout.Vertical)
        {
            Width = Math.Max(MinWidth, 340 * _appearanceSettings.Scale);
            Height = Math.Max(MinHeight, workArea.Height * DefaultHeightRatio);
            return;
        }

        Width = Math.Max(MinWidth, workArea.Width * DefaultWidthRatio);
        Height = Math.Max(MinHeight, DefaultHeight * _appearanceSettings.Scale);
    }

    private void ApplyLayout()
    {
        var isVertical = _appearanceSettings.Layout == BarLayout.Vertical;

        MinWidth = (isVertical ? VerticalMinimumWidth : HorizontalMinimumWidth) * _appearanceSettings.Scale;
        MinHeight = (isVertical ? VerticalMinimumHeight : HorizontalMinimumHeight) * _appearanceSettings.Scale;
        ThemeToggleButton.Content = _appearanceSettings.Theme == AppearanceTheme.Dark ? "☼" : "●";
        ThemeToggleButton.ToolTip = _appearanceSettings.Theme == AppearanceTheme.Dark ? "切换灰白主题" : "切换黑色主题";
        LayoutToggleButton.Content = isVertical ? "↔" : "↕";
        LayoutToggleButton.ToolTip = isVertical ? "切换横版" : "切换竖版";
        AccountBadge.MaxWidth = isVertical ? 0 : 420;
        AccountBadge.Visibility = isVertical ? Visibility.Collapsed : Visibility.Visible;

        StatusColumn0.Width = isVertical ? new GridLength(1, GridUnitType.Star) : new GridLength(1.35, GridUnitType.Star);
        StatusColumn1.Width = isVertical ? new GridLength(0) : new GridLength(0.95, GridUnitType.Star);
        StatusColumn2.Width = isVertical ? new GridLength(0) : new GridLength(1.45, GridUnitType.Star);
        StatusRow0.Height = new GridLength(1, GridUnitType.Star);
        StatusRow1.Height = isVertical ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        StatusRow2.Height = isVertical ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        PositionPanel(ConfigPanel, 0, 0, isVertical ? new Thickness(0, 0, 0, 7) : new Thickness(0, 0, 7, 0));
        PositionPanel(RuntimePanel, isVertical ? 1 : 0, isVertical ? 0 : 1, isVertical ? new Thickness(0, 0, 0, 7) : new Thickness(0, 0, 7, 0));
        PositionPanel(UsagePanel, isVertical ? 2 : 0, isVertical ? 0 : 2, new Thickness(0));

        var alignment = isVertical ? TextAlignment.Center : TextAlignment.Left;
        ConfigCaptionText.TextAlignment = alignment;
        RuntimeCaptionText.TextAlignment = alignment;
        UsageCaptionText.TextAlignment = alignment;
        ManualText.TextAlignment = alignment;
        StateText.TextAlignment = alignment;
        ConfigText.TextAlignment = alignment;
    }

    private static void PositionPanel(Border panel, int row, int column, Thickness margin)
    {
        Grid.SetRow(panel, row);
        Grid.SetColumn(panel, column);
        panel.Margin = margin;
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
        var model = string.IsNullOrWhiteSpace(session.Model) ? _configuredModel : session.Model;
        var effort = string.IsNullOrWhiteSpace(session.ReasoningEffort) ? _configuredReasoningEffort : session.ReasoningEffort;
        var speedTier = string.IsNullOrWhiteSpace(session.SpeedTier) ? _currentSpeedTier : session.SpeedTier;
        SetSelectedStatus(model, effort, speedTier);
    }

    private void SetSelectedStatus(string? model, string? reasoningEffort, string? speedTier = null)
    {
        _currentModel = model;
        _currentReasoningEffort = reasoningEffort;
        _currentSpeedTier = speedTier;
        RenderSelectedStatus();
    }

    private void SetManualStatus(string text)
    {
        _currentManualStatus = text;
        RenderManualStatus();
    }

    private void SetUsageStatus(string text)
    {
        _currentUsageStatus = text;
        RenderUsageStatus();
    }

    private void RenderStatusText()
    {
        RenderSelectedStatus();
        RenderManualStatus();
        RenderUsageStatus();
    }

    private void RenderSelectedStatus()
    {
        var model = DisplayValue(_currentModel, "未配置模型");
        var effort = DisplayValue(_currentReasoningEffort, "读取中");
        var speed = FormatSpeedLabel(_currentSpeedTier);

        SetTextIfChanged(ModelText, $"{model} · 推理 {effort} · 速率 {speed}");

        var text = _appearanceSettings.Layout == BarLayout.Vertical
            ? $"模型{Environment.NewLine}{model}{Environment.NewLine}{Environment.NewLine}推理强度{Environment.NewLine}{effort}{Environment.NewLine}{Environment.NewLine}速率{Environment.NewLine}{speed}"
            : $"模型 {model}  |  推理强度 {effort}  |  速率 {speed}";
        SetTextIfChanged(StateText, text);
    }

    private void RenderManualStatus()
    {
        SetTextIfChanged(ManualText, FormatForLayout(_currentManualStatus));
    }

    private void RenderUsageStatus()
    {
        SetTextIfChanged(ConfigText, FormatForLayout(_currentUsageStatus));
    }

    private string FormatForLayout(string text)
    {
        return _appearanceSettings.Layout == BarLayout.Vertical
            ? text.Replace(" | ", Environment.NewLine, StringComparison.Ordinal)
            : text;
    }

    private static string DisplayValue(string? value, string fallback)
    {
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }

    private static string FormatSpeedLabel(string? speedTier)
    {
        return speedTier?.Trim().ToLowerInvariant() switch
        {
            "fast" or "priority" => "快速",
            _ => "标准"
        };
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
            _configuredModel = null;
            _configuredReasoningEffort = null;
            SetSelectedStatus("未找到配置", null, null);
            SetUsageStatus("读取中");
            SetManualStatus("未找到 config.toml");
            await UpdateUsageAsync(usageRefreshVersion);
            return;
        }

        if (!result.ReadSucceeded)
        {
            _configuredModel = null;
            _configuredReasoningEffort = null;
            SetSelectedStatus("读取配置失败", null, null);
            SetUsageStatus("读取中");
            SetManualStatus(result.Message ?? "配置读取失败");
            await UpdateUsageAsync(usageRefreshVersion);
            return;
        }

        _configuredModel = result.Model;
        _configuredReasoningEffort = result.ReasoningEffort;
        SetSelectedStatus(result.Model, result.ReasoningEffort, null);
        SetUsageStatus("读取中");
        SetManualStatus($"config.toml | {DateTime.Now:HH:mm:ss}");
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
        SetUsageStatus(usage.Message);
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

        _stateWatcher = new FileSystemWatcher(CodexHomePath, "state_5.sqlite*")
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
            EnableRaisingEvents = true
        };

        _stateWatcher.Changed += (_, _) => DebounceLiveRefresh();
        _stateWatcher.Created += (_, _) => DebounceLiveRefresh();
        _stateWatcher.Renamed += (_, _) => DebounceLiveRefresh();
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
        _stateWatcher?.Dispose();
        base.OnClosed(e);
    }
}
