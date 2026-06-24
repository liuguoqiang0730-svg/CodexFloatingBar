using System.IO;
using System.Text.Json;

namespace CodexFloatingBar;

internal sealed class CodexAccountService
{
    private static readonly string AuthPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex",
        "auth.json");

    public CodexAccountSummary Read()
    {
        var userName = Environment.UserName;
        var authMode = ReadAuthMode();

        return new CodexAccountSummary(userName, authMode, FormatDisplayText(userName, authMode));
    }

    private static string? ReadAuthMode()
    {
        try
        {
            if (!File.Exists(AuthPath))
            {
                return null;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(AuthPath));
            if (document.RootElement.TryGetProperty("auth_mode", out var authMode) &&
                authMode.ValueKind == JsonValueKind.String)
            {
                return authMode.GetString();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private static string FormatDisplayText(string userName, string? authMode)
    {
        var codexAccount = string.Equals(authMode, "chatgpt", StringComparison.OrdinalIgnoreCase)
            ? "ChatGPT"
            : authMode ?? "未登录";

        return $"用户: {userName} | Codex: {codexAccount}";
    }
}

internal sealed record CodexAccountSummary(string UserName, string? AuthMode, string DisplayText);
