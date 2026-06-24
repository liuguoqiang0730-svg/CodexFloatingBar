using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace CodexFloatingBar;

internal sealed class StartupService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexFloatingBar";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public bool TrySetEnabled(bool enabled, out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

            if (key is null)
            {
                errorMessage = "无法打开当前用户的开机启动注册表项。";
                return false;
            }

            if (enabled)
            {
                var executablePath = GetExecutablePath();
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    errorMessage = "无法获取当前程序路径。";
                    return false;
                }

                key.SetValue(ValueName, Quote(executablePath), RegistryValueKind.String);
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch (UnauthorizedAccessException)
        {
            errorMessage = "没有权限修改当前用户的开机启动项。";
            return false;
        }
        catch (SecurityException)
        {
            errorMessage = "系统阻止修改开机启动项。";
            return false;
        }
    }

    private static string? GetExecutablePath()
    {
        var path = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return path;
        }

        path = Process.GetCurrentProcess().MainModule?.FileName;
        return !string.IsNullOrWhiteSpace(path) && File.Exists(path) ? path : null;
    }

    private static string Quote(string value) => $"\"{value}\"";
}
