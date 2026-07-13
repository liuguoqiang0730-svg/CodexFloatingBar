using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace CodexFloatingBar;

public partial class MainWindow : Window
{
    private const double HorizontalDefaultWidth = 1104;
    private const double DefaultHeight = 96;
    private const double ScreenEdgeMargin = 12;
    private const double HorizontalMinimumWidth = 980;
    private const double HorizontalMinimumHeight = 96;
    private const double VerticalDefaultWidth = 244;
    private const double VerticalDefaultHeight = 468;
    private const double VerticalMinimumWidth = 220;
    private const double VerticalMinimumHeight = 430;
    private const double CollapsedThickness = 18;
    private const int AutoCollapseDelayMilliseconds = 1100;
    private const int EdgeCollapseAnimationMilliseconds = 240;
    private const int LayoutTransitionMilliseconds = 120;
    private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
    private static readonly string CodexHomePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private static readonly string CodexSessionsPath = Path.Combine(CodexHomePath, "sessions");
    private readonly CodexAccountService _accountService = new();
    private readonly AppearanceService _appearanceService = new();
    private readonly CodexConfigService _configService = new();
    private readonly CodexRateLimitService _rateLimitService = new();
    private readonly CodexSessionStatusService _sessionStatusService = new();
    private readonly WindowPlacementService _placementService = new();
    private readonly DispatcherTimer _debounceTimer;
    private readonly DispatcherTimer _liveRefreshTimer;
    private readonly DispatcherTimer _usageTipTimer;
    private readonly DispatcherTimer _autoCollapseTimer;
    private FileSystemWatcher? _configWatcher;
    private FileSystemWatcher? _rateLimitWatcher;
    private FileSystemWatcher? _sessionWatcher;
    private FileSystemWatcher? _stateWatcher;
    private UsageToastWindow? _usageToastWindow;
    private AppearanceSettings _appearanceSettings = AppearanceSettings.Default;
    private string? _configuredModel;
    private string? _configuredReasoningEffort;
    private string? _configuredSpeedTier;
    private string? _currentModel;
    private string? _currentReasoningEffort;
    private string? _currentSpeedTier;
    private string _accountDisplayText = "账户读取中";
    private string _currentUsageStatus = "读取中";
    private CodexRateLimitSummary? _currentUsageSummary;
    private UsageLevel? _secondaryUsageLevel;
    private (double Left, double Top, double Width, double Height)? _expandedGeometry;
    private int _refreshVersion;
    private bool _isCollapsed;
    private bool _suspendPlacementSave;
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
        else if (LooksLikeLegacyVerticalSize())
        {
            ApplyDefaultSize();
        }

        Loaded += (_, _) =>
        {
            UpdateAccountIdentity();
            UpdateStatus();
            StartWatcher();
            ScheduleAutoCollapse();
        };

        MouseLeftButtonDown += (_, e) =>
        {
            if (_isCollapsed)
            {
                ExpandFromEdge();
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
                SaveExpandedPlacement();
            }
        };

        MouseEnter += (_, _) => ExpandFromEdge();
        MouseLeave += (_, _) => ScheduleAutoCollapse();

        Closing += (_, e) =>
        {
            SaveExpandedPlacement();
            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
            }
        };

        SizeChanged += (_, _) => SaveExpandedPlacement();
        LocationChanged += (_, _) => SaveExpandedPlacement();

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

        _usageTipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.8) };
        _usageTipTimer.Tick += (_, _) =>
        {
            _usageTipTimer.Stop();
            HideUsageTip();
        };

        _autoCollapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AutoCollapseDelayMilliseconds) };
        _autoCollapseTimer.Tick += (_, _) =>
        {
            _autoCollapseTimer.Stop();
            CollapseToEdge();
        };
    }

    internal AppearanceTheme CurrentTheme => _appearanceSettings.Theme;

    internal BarLayout CurrentLayout => _appearanceSettings.Layout;

    internal bool AutoCollapseEnabled => _appearanceSettings.AutoCollapse;

    public double CurrentScale => _appearanceSettings.Scale;

    public void RefreshStatus() => UpdateStatus();

    public void AllowClose() => _allowClose = true;

    internal void SetTheme(AppearanceTheme theme)
    {
        _appearanceSettings = _appearanceSettings with { Theme = theme };
        _appearanceService.Save(_appearanceSettings);
        ApplyAppearance();
        if (_isCollapsed)
        {
            ApplyCollapsedChrome();
        }
    }

    public void SetScale(double scale)
    {
        var oldScale = _appearanceSettings.Scale;
        _appearanceSettings = _appearanceSettings with { Scale = Math.Clamp(scale, 0.82, 1.18) };
        _appearanceService.Save(_appearanceSettings);
        ApplyAppearance();
        ResizeForScale(oldScale, _appearanceSettings.Scale);
        SaveExpandedPlacement();
    }

    internal void SetLayout(BarLayout layout)
    {
        if (_appearanceSettings.Layout == layout)
        {
            return;
        }

        var targetWorkArea = GetCurrentWorkArea();
        var targetSettings = _appearanceSettings with { Layout = layout };
        var targetGeometry = GetDefaultGeometry(layout, targetSettings.Scale, targetWorkArea);
        _appearanceSettings = targetSettings;
        _appearanceService.Save(_appearanceSettings);
        ApplyLayoutChange(layout, targetSettings.Scale, targetGeometry, animate: IsVisible);
        SaveExpandedPlacement();
    }

    internal void SetAutoCollapse(bool isEnabled)
    {
        _appearanceSettings = _appearanceSettings with { AutoCollapse = isEnabled };
        _appearanceService.Save(_appearanceSettings);
        ApplyLayout();

        if (isEnabled)
        {
            ScheduleAutoCollapse();
            return;
        }

        _autoCollapseTimer.Stop();
        ExpandFromEdge();
    }

    public bool CopyStatusToClipboard()
    {
        var status = string.Join(
            Environment.NewLine,
            TitleText.Text,
            AccountText.Text,
            ModelText.Text,
            StateText.Text,
            _currentUsageStatus);

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
        ExpandFromEdge();
        Activate();
        Topmost = true;
        Topmost = false;
        Topmost = true;
    }

    public void ToggleVisibilityFromTray()
    {
        if (IsVisible)
        {
            SaveExpandedPlacement();
            Hide();
            return;
        }

        ShowFromTray();
    }

    private void RefreshClicked(object sender, RoutedEventArgs e) => UpdateStatus();

    private void HideClicked(object sender, RoutedEventArgs e)
    {
        SaveExpandedPlacement();
        Hide();
    }

    private void ScheduleAutoCollapse()
    {
        if (!_appearanceSettings.AutoCollapse || _isCollapsed || !IsVisible)
        {
            return;
        }

        _autoCollapseTimer.Stop();
        _autoCollapseTimer.Start();
    }

    private void CollapseToEdge()
    {
        if (!_appearanceSettings.AutoCollapse || _isCollapsed || IsMouseOver || !IsVisible || WindowState != WindowState.Normal)
        {
            return;
        }

        RememberExpandedGeometry();
        var workArea = GetCurrentWorkArea();
        var thickness = GetCollapsedThickness();
        var isVertical = _appearanceSettings.Layout == BarLayout.Vertical;
        var expanded = _expandedGeometry ?? (Left, Top, Width, Height);
        (double Left, double Top, double Width, double Height) targetGeometry;

        HideUsageTip();
        _isCollapsed = true;
        _suspendPlacementSave = true;
        ResizeMode = ResizeMode.NoResize;
        ApplyCollapsedChrome();

        if (isVertical)
        {
            MinWidth = thickness;
            MinHeight = thickness;
            var targetHeight = Math.Min(Math.Max(expanded.Height, thickness), workArea.Height);
            targetGeometry = (
                workArea.Right - thickness,
                Math.Min(Math.Max(expanded.Top, workArea.Top), workArea.Bottom - targetHeight),
                thickness,
                targetHeight);
            AnimateWindowTo(targetGeometry, () => _suspendPlacementSave = false);
            return;
        }

        MinWidth = thickness;
        MinHeight = thickness;
        var targetWidth = Math.Min(Math.Max(expanded.Width, HorizontalMinimumWidth * _appearanceSettings.Scale), workArea.Width);
        targetGeometry = (
            Math.Min(Math.Max(expanded.Left, workArea.Left), workArea.Right - targetWidth),
            workArea.Top,
            targetWidth,
            thickness);
        AnimateWindowTo(targetGeometry, () => _suspendPlacementSave = false);
    }

    private void ExpandFromEdge()
    {
        _autoCollapseTimer.Stop();
        if (!_isCollapsed)
        {
            return;
        }

        var workArea = GetCurrentWorkArea();
        var geometry = _expandedGeometry ?? GetDefaultGeometry(_appearanceSettings.Layout, _appearanceSettings.Scale, workArea);
        var displayGeometry = GetEdgeExpandedGeometry(geometry, workArea);
        _suspendPlacementSave = true;
        AnimateWindowTo(displayGeometry, () =>
        {
            _isCollapsed = false;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            MainContent.Visibility = Visibility.Visible;
            Shell.Opacity = 1;
            Shell.ToolTip = null;
            ApplyWindowConstraints(_appearanceSettings.Layout, _appearanceSettings.Scale);
            ApplyGeometry(displayGeometry);
            ApplyAppearance();
            RenderStatusText();
            _suspendPlacementSave = false;
        });
    }

    private void SaveExpandedPlacement()
    {
        if (_suspendPlacementSave || _isCollapsed || WindowState != WindowState.Normal || !IsFinite(Left) || !IsFinite(Top))
        {
            return;
        }

        RememberExpandedGeometry();
        _placementService.Save(this);
    }

    private void RememberExpandedGeometry()
    {
        if (_isCollapsed || WindowState != WindowState.Normal || !IsFinite(Left) || !IsFinite(Top))
        {
            return;
        }

        _expandedGeometry = (Left, Top, Width, Height);
    }

    private double GetCollapsedThickness() => Math.Max(6, CollapsedThickness * _appearanceSettings.Scale);

    private void AnimateWindowTo((double Left, double Top, double Width, double Height) geometry, Action? completed = null)
    {
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);

        var easing = new QuadraticEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(EdgeCollapseAnimationMilliseconds);
        var leftAnimation = CreateWindowAnimation(Left, geometry.Left, duration, easing);
        var topAnimation = CreateWindowAnimation(Top, geometry.Top, duration, easing);
        var widthAnimation = CreateWindowAnimation(Width, geometry.Width, duration, easing);
        var heightAnimation = CreateWindowAnimation(Height, geometry.Height, duration, easing);

        heightAnimation.Completed += (_, _) =>
        {
            BeginAnimation(LeftProperty, null);
            BeginAnimation(TopProperty, null);
            BeginAnimation(WidthProperty, null);
            BeginAnimation(HeightProperty, null);
            Left = geometry.Left;
            Top = geometry.Top;
            Width = geometry.Width;
            Height = geometry.Height;
            completed?.Invoke();
        };

        BeginAnimation(LeftProperty, leftAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(TopProperty, topAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(WidthProperty, widthAnimation, HandoffBehavior.SnapshotAndReplace);
        BeginAnimation(HeightProperty, heightAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimation CreateWindowAnimation(double from, double to, TimeSpan duration, IEasingFunction easing)
    {
        return new DoubleAnimation
        {
            From = from,
            To = to,
            Duration = duration,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
    }

    private (double Left, double Top, double Width, double Height) GetEdgeExpandedGeometry(
        (double Left, double Top, double Width, double Height) geometry,
        Rect workArea)
    {
        if (_appearanceSettings.Layout == BarLayout.Vertical)
        {
            var width = Math.Min(Math.Max(geometry.Width, MinWidth), workArea.Width);
            var height = Math.Min(Math.Max(geometry.Height, MinHeight), workArea.Height);
            var left = workArea.Right - width;
            var top = Math.Min(Math.Max(geometry.Top, workArea.Top), workArea.Bottom - height);
            return (left, top, width, height);
        }

        var horizontalWidth = Math.Min(Math.Max(geometry.Width, MinWidth), workArea.Width);
        var horizontalHeight = Math.Min(Math.Max(geometry.Height, MinHeight), workArea.Height);
        var horizontalLeft = Math.Min(Math.Max(geometry.Left, workArea.Left), workArea.Right - horizontalWidth);
        return (horizontalLeft, workArea.Top, horizontalWidth, horizontalHeight);
    }

    private void ApplyCollapsedChrome()
    {
        var isVertical = _appearanceSettings.Layout == BarLayout.Vertical;
        MainContent.Visibility = Visibility.Collapsed;
        Shell.Padding = new Thickness(0);
        Shell.Opacity = 0.72;
        Shell.ToolTip = "移到这里展开 Codex Status";
        Shell.CornerRadius = isVertical
            ? new CornerRadius(7, 0, 0, 7)
            : new CornerRadius(0, 0, 7, 7);
    }

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
                cardBorder: "#220F1720",
                primaryText: "#FF111827",
                secondaryText: "#D9111827",
                mutedText: "#8A4B5563",
                accent: "#253241",
                accentHover: "#314255",
                accentPressed: "#1A2531",
                usageGood: "#2E8B67",
                usageWarn: "#C58B12",
                usageDanger: "#C93F3F",
                badge: "#1027333F",
                logo: "#1436A370",
                logoBorder: "#3336A370",
                logoText: "#FF287A55");
            return;
        }

        ApplyTheme(
            surface: "#F211161D",
            border: "#3AFFFFFF",
            card: "#171D24",
            cardBorder: "#263541",
            primaryText: "#FFFFFFFF",
            secondaryText: "#DCE6EF",
            mutedText: "#98A7B5",
            accent: "#1B2630",
            accentHover: "#263645",
            accentPressed: "#13202A",
            usageGood: "#36A370",
            usageWarn: "#C79522",
            usageDanger: "#E05252",
            badge: "#27313A",
            logo: "#16251F",
            logoBorder: "#203D31",
            logoText: "#FF77E0AD");
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
        string usageGood,
        string usageWarn,
        string usageDanger,
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
        SetBrush("UsageGoodBrush", usageGood);
        SetBrush("UsageWarnBrush", usageWarn);
        SetBrush("UsageDangerBrush", usageDanger);
        SetBrush("BadgeBrush", badge);
        SetBrush("LogoBrush", logo);
        SetBrush("LogoBorderBrush", logoBorder);
        SetBrush("LogoTextBrush", logoText);
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private void ApplyDefaultSize(Rect? targetWorkArea = null)
    {
        var workArea = targetWorkArea ?? GetCurrentWorkArea();
        ApplyGeometry(GetDefaultGeometry(_appearanceSettings.Layout, _appearanceSettings.Scale, workArea));
    }

    private (double Left, double Top, double Width, double Height) GetDefaultGeometry(BarLayout layout, double scale, Rect workArea)
    {
        var minWidth = (layout == BarLayout.Vertical ? VerticalMinimumWidth : HorizontalMinimumWidth) * scale;
        var minHeight = (layout == BarLayout.Vertical ? VerticalMinimumHeight : HorizontalMinimumHeight) * scale;
        double width;
        double height;
        double left;
        double top;

        if (layout == BarLayout.Vertical)
        {
            width = Math.Max(minWidth, VerticalDefaultWidth * scale);
            height = Math.Max(minHeight, VerticalDefaultHeight * scale);
            left = workArea.Right - width - ScreenEdgeMargin;
            top = workArea.Top + ScreenEdgeMargin;
            return ClampGeometry(workArea, left, top, width, height);
        }

        width = Math.Max(minWidth, Math.Min(HorizontalDefaultWidth * scale, workArea.Width));
        height = Math.Max(minHeight, DefaultHeight * scale);
        left = workArea.Left + Math.Max(0, (workArea.Width - width) / 2);
        top = workArea.Top + ScreenEdgeMargin;
        return ClampGeometry(workArea, left, top, width, height);
    }

    private void ApplyGeometry((double Left, double Top, double Width, double Height) geometry)
    {
        WindowStartupLocation = WindowStartupLocation.Manual;

        if (geometry.Width > Width)
        {
            Left = geometry.Left;
            Top = geometry.Top;
            Width = geometry.Width;
            Height = geometry.Height;
            return;
        }

        Width = geometry.Width;
        Height = geometry.Height;
        Left = geometry.Left;
        Top = geometry.Top;
    }

    private void ApplyLayoutChange(
        BarLayout layout,
        double scale,
        (double Left, double Top, double Width, double Height) geometry,
        bool animate)
    {
        BeginAnimation(OpacityProperty, null);

        if (animate)
        {
            Opacity = 0;
            IsHitTestVisible = false;
        }

        ApplyWindowConstraints(layout, scale);
        ApplyGeometry(geometry);
        ApplyAppearance();
        RenderStatusText();

        if (animate)
        {
            BeginLayoutFadeIn();
        }
        else
        {
            Opacity = 1;
            IsHitTestVisible = true;
        }
    }

    private void BeginLayoutFadeIn()
    {
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(LayoutTransitionMilliseconds),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            Opacity = 1;
            IsHitTestVisible = true;
        };

        BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private Rect GetCurrentWorkArea()
    {
        if (IsFinite(Left) && IsFinite(Top))
        {
            var centerX = Left + Math.Max(Width, MinWidth) / 2;
            var centerY = Top + Math.Max(Height, MinHeight) / 2;
            var screen = System.Windows.Forms.Screen.FromPoint(ToDevicePoint(new System.Windows.Point(centerX, centerY)));
            return ToDipRect(screen.WorkingArea);
        }

        return SystemParameters.WorkArea;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private System.Drawing.Point ToDevicePoint(System.Windows.Point point)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return new System.Drawing.Point((int)Math.Round(point.X), (int)Math.Round(point.Y));
        }

        var transformed = source.CompositionTarget.TransformToDevice.Transform(point);
        return new System.Drawing.Point((int)Math.Round(transformed.X), (int)Math.Round(transformed.Y));
    }

    private Rect ToDipRect(System.Drawing.Rectangle rectangle)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is null)
        {
            return SystemParameters.WorkArea;
        }

        var transform = source.CompositionTarget.TransformFromDevice;
        var topLeft = transform.Transform(new System.Windows.Point(rectangle.Left, rectangle.Top));
        var bottomRight = transform.Transform(new System.Windows.Point(rectangle.Right, rectangle.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private static (double Left, double Top, double Width, double Height) ClampGeometry(
        Rect workArea,
        double left,
        double top,
        double width,
        double height)
    {
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);
        left = Math.Min(Math.Max(left, workArea.Left), workArea.Right - width);
        top = Math.Min(Math.Max(top, workArea.Top), workArea.Bottom - height);
        return (left, top, width, height);
    }

    private void ApplyLayout()
    {
        var isVertical = _appearanceSettings.Layout == BarLayout.Vertical;

        ApplyWindowConstraints(_appearanceSettings.Layout, _appearanceSettings.Scale);
        Shell.Padding = isVertical ? new Thickness(20) : new Thickness(20);
        LogoMark.Width = 34;
        LogoMark.Height = 34;
        ThemeToggleButton.Content = _appearanceSettings.Theme == AppearanceTheme.Dark ? "☼" : "●";
        ThemeToggleButton.ToolTip = _appearanceSettings.Theme == AppearanceTheme.Dark ? "切换灰白主题" : "切换黑色主题";
        ThemeToggleButton.Width = 34;
        ThemeToggleButton.Height = 34;
        ThemeToggleButton.Margin = new Thickness(0, 0, 6, 0);
        LayoutToggleButton.Content = isVertical ? "↔" : "↕";
        LayoutToggleButton.ToolTip = isVertical ? "切换横版" : "切换竖版";
        LayoutToggleButton.Width = 34;
        LayoutToggleButton.Height = 34;
        LayoutToggleButton.Margin = new Thickness(0, 0, 6, 0);
        HideButton.Width = 34;
        HideButton.Height = 34;
        HideButton.Margin = new Thickness(0, 0, 6, 0);
        RefreshButton.Width = 34;
        RefreshButton.Height = 34;
        AccountBadge.Visibility = Visibility.Collapsed;
        TitleStack.Visibility = Visibility.Visible;
        ModelText.Visibility = Visibility.Visible;
        SetTextIfChanged(ModelText, "local status");

        ConfigPanel.Visibility = Visibility.Collapsed;
        RuntimePanel.Visibility = Visibility.Visible;
        UsagePanel.Visibility = Visibility.Visible;
        MainColumn0.Width = isVertical ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        MainColumn1.Width = isVertical ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        MainColumn2.Width = isVertical ? new GridLength(0) : GridLength.Auto;
        MainRow0.Height = GridLength.Auto;
        MainRow1.Height = isVertical ? GridLength.Auto : new GridLength(0);
        MainRow2.Height = isVertical ? GridLength.Auto : new GridLength(0);

        Grid.SetRow(BrandPanel, 0);
        Grid.SetColumn(BrandPanel, 0);
        Grid.SetRow(StatusGrid, isVertical ? 1 : 0);
        Grid.SetColumn(StatusGrid, isVertical ? 0 : 1);
        Grid.SetRow(ControlsPanel, isVertical ? 2 : 0);
        Grid.SetColumn(ControlsPanel, isVertical ? 0 : 2);

        BrandPanel.Margin = isVertical ? new Thickness(0, 0, 0, 22) : new Thickness(0, 0, 24, 0);
        ControlsPanel.Margin = isVertical ? new Thickness(0, 24, 0, 0) : new Thickness(16, 0, 0, 0);
        ControlsPanel.HorizontalAlignment = isVertical ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Left;

        StatusColumn0.Width = isVertical ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        StatusColumn1.Width = isVertical ? new GridLength(0) : GridLength.Auto;
        StatusColumn2.Width = new GridLength(0);
        StatusRow0.Height = isVertical ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        StatusRow1.Height = isVertical ? GridLength.Auto : new GridLength(0);
        StatusRow2.Height = new GridLength(0);
        StatusGrid.VerticalAlignment = isVertical ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        StatusGrid.HorizontalAlignment = isVertical ? System.Windows.HorizontalAlignment.Stretch : System.Windows.HorizontalAlignment.Center;

        PositionPanel(RuntimePanel, 0, 0, isVertical ? new Thickness(0, 0, 0, 18) : new Thickness(0, 0, 10, 0));
        PositionPanel(UsagePanel, isVertical ? 1 : 0, isVertical ? 0 : 1, new Thickness(0));
        UsagePanel.Padding = isVertical ? new Thickness(14, 10, 14, 12) : new Thickness(18, 7, 20, 7);
        SecondaryUsageRow.MinWidth = isVertical ? 0 : 344;
        UsagePanel.Width = isVertical ? double.NaN : 384;

        ConfigureRuntimeLayout(isVertical);

        var alignment = isVertical ? TextAlignment.Center : TextAlignment.Left;
        RuntimeCaptionText.TextAlignment = alignment;
        UsageCaptionText.TextAlignment = alignment;
        StateText.TextAlignment = alignment;
        UsageUnavailableText.TextAlignment = alignment;
    }

    private void ConfigureRuntimeLayout(bool isVertical)
    {
        RuntimeColumn0.Width = isVertical ? new GridLength(1, GridUnitType.Star) : new GridLength(142);
        RuntimeColumn1.Width = isVertical ? new GridLength(0) : new GridLength(96);
        RuntimeColumn2.Width = isVertical ? new GridLength(0) : new GridLength(108);
        RuntimeRow0.Height = GridLength.Auto;
        RuntimeRow1.Height = isVertical ? GridLength.Auto : new GridLength(0);
        RuntimeRow2.Height = isVertical ? GridLength.Auto : new GridLength(0);

        PositionRuntimeChip(ModelChip, 0, 0, isVertical ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 10, 0));
        PositionRuntimeChip(EffortChip, isVertical ? 1 : 0, isVertical ? 0 : 1, isVertical ? new Thickness(0, 0, 0, 10) : new Thickness(0, 0, 10, 0));
        PositionRuntimeChip(SpeedChip, isVertical ? 2 : 0, isVertical ? 0 : 2, new Thickness(0));
    }

    private static void PositionRuntimeChip(Border chip, int row, int column, Thickness margin)
    {
        Grid.SetRow(chip, row);
        Grid.SetColumn(chip, column);
        chip.Margin = margin;
    }

    private void ApplyWindowConstraints(BarLayout layout, double scale)
    {
        var isVertical = layout == BarLayout.Vertical;
        MinWidth = (isVertical ? VerticalMinimumWidth : HorizontalMinimumWidth) * scale;
        MinHeight = (isVertical ? VerticalMinimumHeight : HorizontalMinimumHeight) * scale;
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

    private bool LooksLikeLegacyVerticalSize()
    {
        if (_appearanceSettings.Layout != BarLayout.Vertical)
        {
            return false;
        }

        var unscaledWidth = Width / _appearanceSettings.Scale;
        var unscaledHeight = Height / _appearanceSettings.Scale;
        return unscaledWidth > 240 || unscaledHeight > 520;
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

    private void ApplySessionStatus(CodexSessionStatus session)
    {
        var model = string.IsNullOrWhiteSpace(session.Model) ? _configuredModel : session.Model;
        var effort = string.IsNullOrWhiteSpace(session.ReasoningEffort) ? _configuredReasoningEffort : session.ReasoningEffort;
        var speedTier = string.IsNullOrWhiteSpace(session.SpeedTier) ? _configuredSpeedTier : session.SpeedTier;

        SetSelectedStatus(model, effort, speedTier);
    }

    private void SetSelectedStatus(string? model, string? reasoningEffort, string? speedTier = null)
    {
        _currentModel = model;
        _currentReasoningEffort = reasoningEffort;
        _currentSpeedTier = speedTier;
        RenderSelectedStatus();
    }

    private void SetUsageStatus(string text)
    {
        _currentUsageStatus = text;
        _currentUsageSummary = null;
        RenderUsageStatus();
    }

    private void SetUsageStatus(CodexRateLimitSummary usage)
    {
        _currentUsageStatus = usage.Message;
        _currentUsageSummary = usage;
        RenderUsageStatus();
    }

    private void RenderStatusText()
    {
        RenderSelectedStatus();
        RenderHeaderText();
        RenderUsageStatus();
    }

    private void RenderSelectedStatus()
    {
        var model = DisplayValue(_currentModel, "未配置模型");
        var effort = DisplayValue(_currentReasoningEffort, "读取中");
        var speed = FormatSpeedLabel(_currentSpeedTier);

        var text = $"模型：{model}{Environment.NewLine}推理强度：{effort}{Environment.NewLine}速率：{speed}";
        SetTextIfChanged(ModelValueText, model);
        SetTextIfChanged(EffortValueText, FormatEffortLabel(effort));
        SetTextIfChanged(SpeedValueText, speed);
        SetTextIfChanged(StateText, text);
        RenderHeaderText();
    }

    private void RenderHeaderText()
    {
        SetTextIfChanged(ModelText, "local status");
    }

    private void RenderUsageStatus()
    {
        if (_currentUsageSummary?.Status == CodexRateLimitStatus.Available)
        {
            UsageUnavailableText.Visibility = Visibility.Collapsed;
            RenderWeeklyUsageWindow(_currentUsageSummary.Secondary, _currentUsageSummary.PlanType);
            ObserveUsageLevelChange("1 周", _currentUsageSummary.Secondary, ref _secondaryUsageLevel);
            return;
        }

        _secondaryUsageLevel = null;
        RenderPlaceholderUsageWindow();
        SetTextIfChanged(UsageUnavailableText, FormatForLayout(_currentUsageStatus));
        UsageUnavailableText.Visibility = Visibility.Visible;
    }

    private void RenderPlaceholderUsageWindow()
    {
        SetUsageWindowVisibility(SecondaryUsageRow, true);
        SetTextIfChanged(UsageCaptionText, "一周额度");
        SetTextIfChanged(UsageMetaText, "等待用量记录");
        SetTextIfChanged(SecondaryUsageValue, "--");
        SetUsageBarPercent(SecondaryUsageFillColumn, SecondaryUsageEmptyColumn, 0);
        SecondaryUsageFill.Background = GetResourceBrush("BadgeBrush");
        SecondaryUsageRow.SetValue(ToolTipProperty, "等待用量记录");
    }

    private void RenderWeeklyUsageWindow(CodexRateLimitWindow? window, string? planType)
    {
        if (window is null)
        {
            RenderPlaceholderUsageWindow();
            return;
        }

        SetUsageWindowVisibility(SecondaryUsageRow, true);
        SetTextIfChanged(UsageCaptionText, "一周额度");
        SetTextIfChanged(SecondaryUsageValue, $"{window.RemainingPercent}%");
        SetTextIfChanged(UsageMetaText, FormatUsageMeta(planType, window.ResetAt));
        SetUsageBarPercent(SecondaryUsageFillColumn, SecondaryUsageEmptyColumn, window.RemainingPercent);
        SecondaryUsageFill.Background = GetUsageBrush(window.RemainingPercent);
        SecondaryUsageRow.SetValue(ToolTipProperty, $"剩余 {window.RemainingPercent}% / 已用 {window.UsedPercent}%");
    }

    private static void SetUsageWindowVisibility(UIElement row, bool isVisible)
    {
        var visibility = isVisible ? Visibility.Visible : Visibility.Collapsed;
        row.Visibility = visibility;
    }

    private static void SetUsageBarPercent(ColumnDefinition fillColumn, ColumnDefinition emptyColumn, int percent)
    {
        var clamped = Math.Clamp(percent, 0, 100);
        fillColumn.Width = new GridLength(clamped, GridUnitType.Star);
        emptyColumn.Width = new GridLength(100 - clamped, GridUnitType.Star);
    }

    private System.Windows.Media.Brush GetUsageBrush(int remainingPercent)
    {
        var resourceKey = remainingPercent switch
        {
            >= 50 => "UsageGoodBrush",
            > 20 => "UsageWarnBrush",
            _ => "UsageDangerBrush"
        };

        return GetResourceBrush(resourceKey);
    }

    private System.Windows.Media.Brush GetResourceBrush(string resourceKey)
    {
        return (System.Windows.Media.Brush)Resources[resourceKey];
    }

    private void ObserveUsageLevelChange(string label, CodexRateLimitWindow? window, ref UsageLevel? currentLevel)
    {
        if (window is null)
        {
            currentLevel = null;
            return;
        }

        var nextLevel = GetUsageLevel(window.RemainingPercent);
        if (currentLevel is { } previousLevel && nextLevel > previousLevel)
        {
            ShowUsageTip(label, window.RemainingPercent, nextLevel);
        }

        currentLevel = nextLevel;
    }

    private static UsageLevel GetUsageLevel(int remainingPercent)
    {
        return remainingPercent switch
        {
            >= 50 => UsageLevel.Good,
            > 20 => UsageLevel.Warn,
            _ => UsageLevel.Danger
        };
    }

    private void ShowUsageTip(string label, int remainingPercent, UsageLevel level)
    {
        var isDanger = level == UsageLevel.Danger;
        var title = isDanger ? $"{label}额度快用完了" : $"{label}额度低于 50%";
        var text = isDanger
            ? $"当前剩余 {remainingPercent}%，建议放慢使用或等待重置。"
            : $"当前剩余 {remainingPercent}%，用量进入提醒区间。";

        _usageToastWindow ??= new UsageToastWindow();
        _usageToastWindow.ShowToast(this, GetCurrentWorkArea(), title, text, isDanger);
        _usageTipTimer.Stop();
        _usageTipTimer.Start();
    }

    private void HideUsageTip()
    {
        _usageToastWindow?.HideToast();
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

    private static string FormatEffortLabel(string? effort)
    {
        return effort?.Trim().ToLowerInvariant() switch
        {
            "none" => "无",
            "minimal" => "极低",
            "low" => "低",
            "medium" => "中",
            "high" => "高",
            "xhigh" => "超高",
            _ => string.IsNullOrWhiteSpace(effort) ? "读取中" : effort
        };
    }

    private static string FormatSpeedLabel(string? speedTier)
    {
        if (string.IsNullOrWhiteSpace(speedTier))
        {
            return "未读取到";
        }

        return speedTier.Trim().ToLowerInvariant() switch
        {
            "fast" or "priority" => "快速",
            "standard" or "default" or "auto" => "标准",
            _ => "标准"
        };
    }

    private static string FormatUsageMeta(string? planType, long resetAt)
    {
        var resetText = $"{FormatResetTime(resetAt)}重置";
        return string.IsNullOrWhiteSpace(planType) ? resetText : $"{planType} · {resetText}";
    }

    private static string FormatWindowName(int windowMinutes)
    {
        return windowMinutes switch
        {
            300 => "5 小时",
            10080 => "1 周",
            _ when windowMinutes % 1440 == 0 => $"{windowMinutes / 1440} 天",
            _ when windowMinutes % 60 == 0 => $"{windowMinutes / 60} 小时",
            _ => $"{windowMinutes} 分钟"
        };
    }

    private static string FormatResetTime(long resetAt)
    {
        var reset = DateTimeOffset.FromUnixTimeSeconds(resetAt).LocalDateTime;
        var now = DateTime.Now;
        if (reset.Date == now.Date)
        {
            return reset.ToString("HH:mm", CultureInfo.CurrentCulture);
        }

        return reset.ToString("M月d日", CultureInfo.CurrentCulture);
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
        _accountDisplayText = _accountService.Read().DisplayText;
        SetTextIfChanged(AccountText, _accountDisplayText);
        RenderHeaderText();
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
            _configuredSpeedTier = null;
            SetSelectedStatus("未找到配置", null, null);
            SetUsageStatus("读取中");
            await UpdateLiveStatusAsync(usageRefreshVersion);
            return;
        }

        if (!result.ReadSucceeded)
        {
            _configuredModel = null;
            _configuredReasoningEffort = null;
            _configuredSpeedTier = null;
            SetSelectedStatus("读取配置失败", null, null);
            SetUsageStatus("读取中");
            await UpdateLiveStatusAsync(usageRefreshVersion);
            return;
        }

        _configuredModel = result.Model;
        _configuredReasoningEffort = result.ReasoningEffort;
        _configuredSpeedTier = result.SpeedTier;
        SetSelectedStatus(result.Model, result.ReasoningEffort, result.SpeedTier);
        SetUsageStatus("读取中");
        await UpdateLiveStatusAsync(usageRefreshVersion);
    }

    private async Task UpdateLiveStatusAsync(int refreshVersion)
    {
        var sessionTask = _sessionStatusService.ReadLatestAsync();
        var usageTask = _rateLimitService.ReadLatestAsync();

        var session = await sessionTask;
        if (refreshVersion != _refreshVersion)
        {
            return;
        }

        ApplySessionStatus(session);

        var usage = await usageTask;
        if (refreshVersion != _refreshVersion)
        {
            return;
        }

        SetUsageStatus(usage);
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

        if (Directory.Exists(CodexSessionsPath))
        {
            _sessionWatcher = new FileSystemWatcher(CodexSessionsPath, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };

            _sessionWatcher.Changed += (_, _) => DebounceLiveRefresh();
            _sessionWatcher.Created += (_, _) => DebounceLiveRefresh();
            _sessionWatcher.Renamed += (_, _) => DebounceLiveRefresh();
        }

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
        _sessionWatcher?.Dispose();
        _stateWatcher?.Dispose();
        _usageToastWindow?.Close();
        base.OnClosed(e);
    }
}

internal enum UsageLevel
{
    Good = 0,
    Warn = 1,
    Danger = 2
}
