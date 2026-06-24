using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace CodexFloatingBar;

public partial class MainWindow : Window
{
    private static readonly string ConfigPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "config.toml");
    private readonly CodexConfigService _configService = new();
    private readonly DispatcherTimer _debounceTimer;
    private FileSystemWatcher? _watcher;

    public MainWindow()
    {
        InitializeComponent();

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
            }
        };

        Closing += (_, _) => _watcher?.Dispose();

        _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _debounceTimer.Tick += (_, _) =>
        {
            _debounceTimer.Stop();
            UpdateStatus();
        };
    }

    public void RefreshStatus() => UpdateStatus();

    private void RefreshClicked(object sender, RoutedEventArgs e) => UpdateStatus();

    private void UpdateStatus()
    {
        var result = _configService.Read(ConfigPath);
        if (!result.Exists)
        {
            ModelText.Text = "model: 未找到 ~/.codex/config.toml";
            StateText.Text = "登录/配置: 需手动查看";
            ConfigText.Text = "推理强度/速率: 需手动查看；套餐/余额/额度/到期: 官方未提供稳定本地读取";
            ManualText.Text = result.Message;
            return;
        }

        ModelText.Text = $"model: {result.Model ?? "未配置"}";
        StateText.Text = $"推理强度/速率: {result.ReasoningEffort ?? "未配置"}  |  登录/配置: 已读取本地配置";
        ConfigText.Text = "套餐/余额/额度/到期: 需手动查看/官方未提供稳定本地读取";
        ManualText.Text = $"配置文件: {ConfigPath}  |  最近刷新: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
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

    private sealed class CodexConfigService
    {
        private static readonly Regex ModelRegex = new("^\\s*model\\s*=\\s*['\\\"](?<value>[^'\\\"]+)['\\\"]\\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EffortRegex = new("^\\s*model_reasoning_effort\\s*=\\s*['\\\"](?<value>[^'\\\"]+)['\\\"]\\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public ConfigReadResult Read(string path)
        {
            if (!File.Exists(path))
            {
                return ConfigReadResult.Missing($"未找到配置文件: {path}");
            }

            string? model = null;
            string? effort = null;

            foreach (var line in File.ReadLines(path))
            {
                if (model is null)
                {
                    var match = ModelRegex.Match(line);
                    if (match.Success)
                    {
                        model = match.Groups["value"].Value;
                        continue;
                    }
                }

                if (effort is null)
                {
                    var match = EffortRegex.Match(line);
                    if (match.Success)
                    {
                        effort = match.Groups["value"].Value;
                    }
                }

                if (model is not null && effort is not null)
                {
                    break;
                }
            }

            return new ConfigReadResult(true, model, effort, null);
        }
    }

    private sealed record ConfigReadResult(bool Exists, string? Model, string? ReasoningEffort, string? Message)
    {
        public static ConfigReadResult Missing(string message) => new(false, null, null, message);
    }
}
