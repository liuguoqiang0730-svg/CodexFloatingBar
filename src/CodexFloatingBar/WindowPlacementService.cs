using System.IO;
using System.Text.Json;
using System.Windows;

namespace CodexFloatingBar;

internal sealed class WindowPlacementService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexFloatingBar",
        "window-placement.json");

    public bool Restore(Window window)
    {
        var placement = ReadPlacement();
        if (placement is null || !IsValidPlacement(placement))
        {
            return false;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = Math.Max(window.MinWidth, placement.Width);
        window.Height = Math.Max(window.MinHeight, placement.Height);
        return true;
    }

    public void Save(Window window)
    {
        if (window.WindowState != WindowState.Normal || !IsFinite(window.Left) || !IsFinite(window.Top))
        {
            return;
        }

        var placement = new WindowPlacement(window.Left, window.Top, window.Width, window.Height);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(placement, JsonOptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private WindowPlacement? ReadPlacement()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<WindowPlacement>(File.ReadAllText(_settingsPath));
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool IsValidPlacement(WindowPlacement placement)
    {
        if (!IsFinite(placement.Left) || !IsFinite(placement.Top) || placement.Width <= 0 || placement.Height <= 0)
        {
            return false;
        }

        var windowRect = new Rect(placement.Left, placement.Top, placement.Width, placement.Height);
        var screenRect = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);

        return windowRect.IntersectsWith(screenRect);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private sealed record WindowPlacement(double Left, double Top, double Width, double Height);
}
