using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace CodexFloatingBar;

internal sealed class UsageToastWindow : Window
{
    private const double ShadowPadding = 18;

    private readonly Border _shell;
    private readonly Border _accent;
    private readonly TextBlock _title;
    private readonly TextBlock _message;
    private readonly ScaleTransform _scale = new(0.96, 0.96);

    public UsageToastWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        Focusable = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;

        _accent = new Border
        {
            Width = 5,
            CornerRadius = new CornerRadius(2)
        };

        _title = new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        _message = new TextBlock
        {
            FontSize = 12,
            Margin = new Thickness(0, 3, 0, 0),
            TextWrapping = TextWrapping.Wrap
        };

        var contentGrid = new Grid();
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_accent, 0);
        contentGrid.Children.Add(_accent);

        var textStack = new StackPanel();
        textStack.Children.Add(_title);
        textStack.Children.Add(_message);
        Grid.SetColumn(textStack, 2);
        contentGrid.Children.Add(textStack);

        _shell = new Border
        {
            Width = 360,
            MaxWidth = 380,
            Padding = new Thickness(12, 10, 12, 10),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(2),
            SnapsToDevicePixels = true,
            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
            RenderTransform = _scale,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 28,
                ShadowDepth = 0,
                Opacity = 0.38
            },
            Child = contentGrid
        };

        var root = new Grid
        {
            Margin = new Thickness(ShadowPadding),
            SnapsToDevicePixels = true
        };
        root.Children.Add(_shell);

        Content = root;
        Opacity = 0;
    }

    public void ShowToast(Window owner, Rect workArea, string title, string message, bool isDanger)
    {
        Owner ??= owner;
        ApplyTone(isDanger);
        _title.Text = title;
        _message.Text = message;

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        Left = Math.Max(workArea.Left + 8, workArea.Right - ActualWidth - 8);
        Top = Math.Max(workArea.Top + 8, workArea.Bottom - ActualHeight - 36);

        BeginAnimation(OpacityProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        Opacity = 0;
        _scale.ScaleX = 0.96;
        _scale.ScaleY = 0.96;

        BeginAnimation(
            OpacityProperty,
            new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromMilliseconds(140),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            },
            HandoffBehavior.SnapshotAndReplace);

        var scaleAnimation = new DoubleAnimation
        {
            From = 0.96,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.18 }
        };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleAnimation, HandoffBehavior.SnapshotAndReplace);
    }

    public void HideToast()
    {
        if (!IsVisible)
        {
            return;
        }

        var animation = new DoubleAnimation
        {
            From = Opacity,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };

        animation.Completed += (_, _) =>
        {
            Opacity = 0;
            Hide();
        };

        BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyTone(bool isDanger)
    {
        if (isDanger)
        {
            _shell.Background = CreateBrush("#FFD84A4A");
            _shell.BorderBrush = CreateBrush("#B8FFFFFF");
            _accent.Background = CreateBrush("#FFFFFFFF");
            _title.Foreground = CreateBrush("#FFFFFFFF");
            _message.Foreground = CreateBrush("#F2FFFFFF");
            return;
        }

        _shell.Background = CreateBrush("#FFE0B341");
        _shell.BorderBrush = CreateBrush("#CC111827");
        _accent.Background = CreateBrush("#FF111827");
        _title.Foreground = CreateBrush("#FF111827");
        _message.Foreground = CreateBrush("#E6111827");
    }

    private static SolidColorBrush CreateBrush(string color)
    {
        return new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }
}
