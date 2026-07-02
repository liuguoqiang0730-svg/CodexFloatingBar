using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexFloatingBar;

internal sealed class CodexSessionStatusService
{
    private const int MaxScanBytes = 16 * 1024 * 1024;
    private static readonly Regex FeedbackTagsRegex = new(
        "feedback_tags.*?model=\"(?<model>[^\"]+)\".*?effort=Some\\((?<effort>[^)]+)\\)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex TurnRegex = new(
        "turn\\{.*?model=(?<model>[^\\s}]+).*?codex\\.turn\\.reasoning_effort=(?<effort>[a-zA-Z_-]+)",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex JsonServiceTierRegex = new(
        "\"service_tier\"\\s*:\\s*(?:\"(?<tier>[^\"]+)\"|(?<tier>null))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex LogServiceTierRegex = new(
        "service_tier\\s*[=:]\\s*(?:Some\\((?<tier>[^)]+)\\)|(?<tier>[^\\s},]+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly string CodexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private static readonly string SessionsPath = Path.Combine(CodexHome, "sessions");
    private static readonly string[] LogPaths =
    [
        Path.Combine(CodexHome, "logs_2.sqlite"),
        Path.Combine(CodexHome, "logs_2.sqlite-wal")
    ];

    public Task<CodexSessionStatus> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadLatest(cancellationToken), cancellationToken);
    }

    private static CodexSessionStatus ReadLatest(CancellationToken cancellationToken)
    {
        var sessionStatus = ReadLatestSessionContext(cancellationToken);
        if (sessionStatus is not null)
        {
            return sessionStatus;
        }

        CodexSessionStatus latest = CodexSessionStatus.Unavailable;
        foreach (var path in LogPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = ReadTail(path);
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            latest = FindLatest(text) ?? latest;
        }

        return latest;
    }

    private static CodexSessionStatus? ReadLatestSessionContext(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(SessionsPath))
        {
            return null;
        }

        try
        {
            foreach (var file in Directory
                         .EnumerateFiles(SessionsPath, "*.jsonl", SearchOption.AllDirectories)
                         .Select(path => new FileInfo(path))
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Take(12))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var status = FindLatestSessionContext(file.FullName);
                if (status is not null)
                {
                    return status;
                }
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    private static CodexSessionStatus? FindLatestSessionContext(string path)
    {
        var text = ReadTail(path);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        CodexSessionStatus? latest = null;
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            var status = TryReadTurnContext(line);
            if (status is not null)
            {
                latest = status;
            }
        }

        return latest;
    }

    private static CodexSessionStatus? TryReadTurnContext(string line)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var type) || !string.Equals(type, "turn_context", StringComparison.Ordinal))
            {
                return null;
            }

            if (!root.TryGetProperty("payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            payload.TryGetProperty("collaboration_mode", out var collaborationMode);
            if (collaborationMode.ValueKind == JsonValueKind.Object)
            {
                collaborationMode.TryGetProperty("settings", out var settings);
                if (settings.ValueKind == JsonValueKind.Object)
                {
                    var settingsModel = GetString(settings, "model");
                    var settingsEffort = GetString(settings, "reasoning_effort");
                    var settingsSpeedTier = GetString(settings, "service_tier") ?? GetString(settings, "speed_tier");
                    return BuildStatus(
                        settingsModel ?? GetString(payload, "model"),
                        settingsEffort ?? GetString(payload, "reasoning_effort") ?? GetString(payload, "model_reasoning_effort"),
                        settingsSpeedTier ?? GetString(payload, "service_tier") ?? GetString(payload, "speed_tier"));
                }
            }

            return BuildStatus(
                GetString(payload, "model"),
                GetString(payload, "reasoning_effort") ?? GetString(payload, "model_reasoning_effort"),
                GetString(payload, "service_tier") ?? GetString(payload, "speed_tier"));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CodexSessionStatus? BuildStatus(string? model, string? effort, string? speedTier)
    {
        model = string.IsNullOrWhiteSpace(model) ? null : model.Trim();
        effort = string.IsNullOrWhiteSpace(effort) ? null : FormatEffort(effort);
        speedTier = string.IsNullOrWhiteSpace(speedTier) ? null : FormatSpeedTier(speedTier);
        return string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(effort) && string.IsNullOrWhiteSpace(speedTier)
            ? null
            : new CodexSessionStatus(model, effort, speedTier);
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return false;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            value = property.GetString();
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        value = property.ToString();
        return true;
    }

    private static CodexSessionStatus? FindLatest(string text)
    {
        var feedback = FindLastMatch(FeedbackTagsRegex, text);
        var turn = FindLastMatch(TurnRegex, text);
        var match = feedback.Index >= turn.Index ? feedback.Match : turn.Match;
        if (match is null || !match.Success)
        {
            return null;
        }

        var model = match.Groups["model"].Value.Trim().Trim('"');
        var effort = FormatEffort(match.Groups["effort"].Value);
        var speedTier = FindLatestSpeedTier(text);
        return string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(effort) && string.IsNullOrWhiteSpace(speedTier)
            ? null
            : new CodexSessionStatus(model, effort, speedTier);
    }

    private static (int Index, Match? Match) FindLastMatch(Regex regex, string text)
    {
        Match? latest = null;
        foreach (Match match in regex.Matches(text))
        {
            latest = match;
        }

        return latest is null ? (-1, null) : (latest.Index, latest);
    }

    private static string? ReadTail(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= 0)
            {
                return null;
            }

            var bytesToRead = (int)Math.Min(stream.Length, MaxScanBytes);
            stream.Seek(-bytesToRead, SeekOrigin.End);

            var bytes = new byte[bytesToRead];
            var totalRead = 0;
            while (totalRead < bytes.Length)
            {
                var read = stream.Read(bytes, totalRead, bytes.Length - totalRead);
                if (read == 0)
                {
                    break;
                }

                totalRead += read;
            }

            return Encoding.UTF8.GetString(bytes, 0, totalRead);
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

    private static string FormatEffort(string effort)
    {
        return effort.Trim().Trim('"').ToLowerInvariant();
    }

    private static string? FindLatestSpeedTier(string text)
    {
        var json = FindLastMatch(JsonServiceTierRegex, text);
        var log = FindLastMatch(LogServiceTierRegex, text);
        var match = json.Index >= log.Index ? json.Match : log.Match;
        if (match is null || !match.Success)
        {
            return null;
        }

        return FormatSpeedTier(match.Groups["tier"].Value);
    }

    private static string? FormatSpeedTier(string tier)
    {
        var normalized = tier.Trim().Trim('"').ToLowerInvariant();
        return normalized switch
        {
            "" or "null" or "none" or "default" or "standard" or "auto" => "standard",
            "priority" or "fast" => "fast",
            _ => normalized
        };
    }
}

internal sealed record CodexSessionStatus(string? Model, string? ReasoningEffort, string? SpeedTier)
{
    public static CodexSessionStatus Unavailable { get; } = new(null, null, null);
}
