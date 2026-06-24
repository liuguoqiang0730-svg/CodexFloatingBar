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
        var auth = ReadAuthFile();
        var identity = ReadIdentityClaims(auth.IdToken);
        var displayText = FormatDisplayText(identity, auth.AuthMode);

        return new CodexAccountSummary(auth.AuthMode, identity.Name, identity.Email, displayText);
    }

    private static CodexAuthState ReadAuthFile()
    {
        try
        {
            if (!File.Exists(AuthPath))
            {
                return new CodexAuthState(null, null);
            }

            using var document = JsonDocument.Parse(File.ReadAllText(AuthPath));
            var root = document.RootElement;
            var authMode = ReadString(root, "auth_mode");
            string? idToken = null;

            if (root.TryGetProperty("tokens", out var tokens) && tokens.ValueKind == JsonValueKind.Object)
            {
                idToken = ReadString(tokens, "id_token");
            }

            return new CodexAuthState(authMode, idToken);
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

        return new CodexAuthState(null, null);
    }

    private static CodexIdentityClaims ReadIdentityClaims(string? idToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return new CodexIdentityClaims(null, null);
        }

        try
        {
            var parts = idToken.Split('.');
            if (parts.Length < 2)
            {
                return new CodexIdentityClaims(null, null);
            }

            var payload = DecodeBase64Url(parts[1]);
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;

            var name = ReadString(root, "name") ?? ReadString(root, "preferred_username") ?? ReadString(root, "nickname");
            var email = ReadString(root, "email");
            return new CodexIdentityClaims(name, email);
        }
        catch (FormatException)
        {
        }
        catch (JsonException)
        {
        }

        return new CodexIdentityClaims(null, null);
    }

    private static byte[] DecodeBase64Url(string value)
    {
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Convert.FromBase64String(base64);
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string FormatDisplayText(CodexIdentityClaims identity, string? authMode)
    {
        if (!string.IsNullOrWhiteSpace(identity.Name) && !string.IsNullOrWhiteSpace(identity.Email))
        {
            return $"Codex: {identity.Name} <{identity.Email}>";
        }

        if (!string.IsNullOrWhiteSpace(identity.Email))
        {
            return $"Codex: {identity.Email}";
        }

        if (!string.IsNullOrWhiteSpace(identity.Name))
        {
            return $"Codex: {identity.Name}";
        }

        var codexAccount = string.Equals(authMode, "chatgpt", StringComparison.OrdinalIgnoreCase)
            ? "ChatGPT"
            : authMode ?? "not signed in";

        return $"Codex: {codexAccount}";
    }

    private sealed record CodexAuthState(string? AuthMode, string? IdToken);

    private sealed record CodexIdentityClaims(string? Name, string? Email);
}

internal sealed record CodexAccountSummary(string? AuthMode, string? Name, string? Email, string DisplayText);
