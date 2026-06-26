using System.IO;
using System.Text.Json;

namespace CodexFloatingBar;

internal sealed class AppearanceService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexFloatingBar",
        "appearance.json");

    public AppearanceSettings Read()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return AppearanceSettings.Default;
            }

            var settings = JsonSerializer.Deserialize<AppearanceSettings>(File.ReadAllText(_settingsPath));
            return Normalize(settings ?? AppearanceSettings.Default);
        }
        catch (JsonException)
        {
            return AppearanceSettings.Default;
        }
        catch (IOException)
        {
            return AppearanceSettings.Default;
        }
        catch (UnauthorizedAccessException)
        {
            return AppearanceSettings.Default;
        }
    }

    public void Save(AppearanceSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(Normalize(settings), JsonOptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static AppearanceSettings Normalize(AppearanceSettings settings)
    {
        var theme = Enum.IsDefined(settings.Theme) ? settings.Theme : AppearanceTheme.Dark;
        var layout = Enum.IsDefined(settings.Layout) ? settings.Layout : BarLayout.Horizontal;
        var scale = Math.Clamp(settings.Scale, 0.82, 1.18);
        return new AppearanceSettings(theme, scale, layout, settings.AutoCollapse);
    }
}

internal sealed record AppearanceSettings(
    AppearanceTheme Theme,
    double Scale,
    BarLayout Layout = BarLayout.Horizontal,
    bool AutoCollapse = false)
{
    public static AppearanceSettings Default { get; } = new(AppearanceTheme.Dark, 1.0, BarLayout.Horizontal, false);
}

internal enum AppearanceTheme
{
    Dark,
    Light
}

internal enum BarLayout
{
    Horizontal,
    Vertical
}
