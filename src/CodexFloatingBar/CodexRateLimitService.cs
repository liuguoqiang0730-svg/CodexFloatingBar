using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CodexFloatingBar;

internal sealed class CodexRateLimitService
{
    private const string EventMarker = "\"type\":\"codex.rate_limits\"";
    private const int MaxScanBytes = 256 * 1024 * 1024;
    private static readonly string CodexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    private static readonly string[] LogPaths =
    [
        Path.Combine(CodexHome, "logs_2.sqlite"),
        Path.Combine(CodexHome, "logs_2.sqlite-wal")
    ];

    public Task<CodexRateLimitSummary> ReadLatestAsync(CancellationToken cancellationToken = default)
    {
        return Task.Run(() => ReadLatest(cancellationToken), cancellationToken);
    }

    private static CodexRateLimitSummary ReadLatest(CancellationToken cancellationToken)
    {
        RateLimitCandidate? latest = null;

        for (var pathIndex = 0; pathIndex < LogPaths.Length; pathIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var candidate in FindRateLimitCandidates(LogPaths[pathIndex], pathIndex))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsNewerCandidate(candidate, latest))
                {
                    latest = candidate;
                }
            }
        }

        if (latest is null)
        {
            return CodexRateLimitSummary.Unavailable("等待 Codex 用量记录");
        }

        try
        {
            using var document = JsonDocument.Parse(latest.Json);
            var root = document.RootElement;

            var planType = ReadString(root, "plan_type");
            if (!root.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
            {
                return CodexRateLimitSummary.Unavailable("等待有效用量记录");
            }

            var primary = ReadWindow(limits, "primary");
            var secondary = ReadWindow(limits, "secondary");

            if (primary is null && secondary is null)
            {
                return CodexRateLimitSummary.Unavailable("等待额度窗口记录");
            }

            var parts = new List<string>();
            if (primary is not null)
            {
                parts.Add($"{FormatWindowName(primary.WindowMinutes)} {primary.RemainingPercent}% {FormatResetTime(primary.ResetAt)}");
            }

            if (secondary is not null)
            {
                parts.Add($"{FormatWindowName(secondary.WindowMinutes)} {secondary.RemainingPercent}% {FormatResetTime(secondary.ResetAt)}");
            }

            var plan = string.IsNullOrWhiteSpace(planType) ? null : FormatPlanType(planType);
            var message = plan is null
                ? string.Join(" | ", parts)
                : $"{string.Join(" | ", parts)} | {plan}";

            return CodexRateLimitSummary.Available(message, plan, primary, secondary);
        }
        catch (JsonException)
        {
            return CodexRateLimitSummary.Unavailable("等待有效用量记录");
        }
    }

    private static IEnumerable<RateLimitCandidate> FindRateLimitCandidates(string path, int pathIndex)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        string text;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length <= 0)
            {
                yield break;
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

            text = Encoding.UTF8.GetString(bytes, 0, totalRead);
        }
        catch (IOException)
        {
            yield break;
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        var searchEnd = text.Length;
        while (searchEnd > 0)
        {
            var markerIndex = text.LastIndexOf(EventMarker, searchEnd - 1, StringComparison.Ordinal);
            if (markerIndex < 0)
            {
                yield break;
            }

            var start = text.LastIndexOf('{', markerIndex);
            while (start >= 0)
            {
                var json = ExtractJsonObject(text, start);
                if (IsRateLimitEvent(json))
                {
                    yield return new RateLimitCandidate(json!, GetObservedAt(json!), pathIndex, markerIndex);
                    break;
                }

                start = start > 0 ? text.LastIndexOf('{', start - 1) : -1;
            }

            searchEnd = markerIndex;
        }
    }

    private static bool IsNewerCandidate(RateLimitCandidate candidate, RateLimitCandidate? latest)
    {
        if (latest is null)
        {
            return true;
        }

        if (candidate.ObservedAt is not null || latest.ObservedAt is not null)
        {
            if (candidate.ObservedAt is null)
            {
                return false;
            }

            if (latest.ObservedAt is null)
            {
                return true;
            }

            if (candidate.ObservedAt.Value != latest.ObservedAt.Value)
            {
                return candidate.ObservedAt.Value > latest.ObservedAt.Value;
            }
        }

        if (candidate.PathIndex != latest.PathIndex)
        {
            return candidate.PathIndex > latest.PathIndex;
        }

        return candidate.MarkerIndex > latest.MarkerIndex;
    }

    private static long? GetObservedAt(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (!root.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            long? observedAt = null;
            foreach (var propertyName in new[] { "primary", "secondary" })
            {
                var windowObservedAt = GetWindowObservedAt(limits, propertyName);
                if (windowObservedAt is not null)
                {
                    observedAt = observedAt is null ? windowObservedAt : Math.Max(observedAt.Value, windowObservedAt.Value);
                }
            }

            return observedAt;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long? GetWindowObservedAt(JsonElement limits, string propertyName)
    {
        if (!limits.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var resetAt = ReadLong(window, "reset_at");
        var resetAfterSeconds = ReadLong(window, "reset_after_seconds");
        return resetAt is null || resetAfterSeconds is null ? null : resetAt.Value - resetAfterSeconds.Value;
    }

    private static string? ExtractJsonObject(string text, int start)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = start; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                depth++;
            }
            else if (ch == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text[start..(i + 1)];
                }
            }
        }

        return null;
    }

    private static bool IsRateLimitEvent(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            return string.Equals(ReadString(root, "type"), "codex.rate_limits", StringComparison.Ordinal)
                && root.TryGetProperty("rate_limits", out var limits)
                && limits.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static CodexRateLimitWindow? ReadWindow(JsonElement limits, string propertyName)
    {
        if (!limits.TryGetProperty(propertyName, out var window) || window.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var usedPercent = ReadInt(window, "used_percent");
        var windowMinutes = ReadInt(window, "window_minutes");
        var resetAt = ReadLong(window, "reset_at");

        if (usedPercent is null || windowMinutes is null || resetAt is null)
        {
            return null;
        }

        var remainingPercent = Math.Clamp(100 - usedPercent.Value, 0, 100);
        return new CodexRateLimitWindow(usedPercent.Value, remainingPercent, windowMinutes.Value, resetAt.Value);
    }

    private static string FormatWindowName(int windowMinutes)
    {
        return windowMinutes switch
        {
            300 => "5小时",
            10080 => "1周",
            _ when windowMinutes % 1440 == 0 => $"{windowMinutes / 1440}天",
            _ when windowMinutes % 60 == 0 => $"{windowMinutes / 60}小时",
            _ => $"{windowMinutes}分钟"
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

    private static string FormatPlanType(string planType)
    {
        return planType switch
        {
            "prolite" => "Pro Lite",
            "pro" => "Pro",
            "plus" => "Plus",
            "free" => "Free",
            _ => planType
        };
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static int? ReadInt(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt32(out var number) ? number : null;
    }

    private static long? ReadLong(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number) ? number : null;
    }
}

internal sealed record RateLimitCandidate(
    string Json,
    long? ObservedAt,
    int PathIndex,
    int MarkerIndex);

internal sealed record CodexRateLimitSummary(
    CodexRateLimitStatus Status,
    string Message,
    string? PlanType,
    CodexRateLimitWindow? Primary,
    CodexRateLimitWindow? Secondary)
{
    public static CodexRateLimitSummary Available(
        string message,
        string? planType,
        CodexRateLimitWindow? primary,
        CodexRateLimitWindow? secondary) =>
        new(CodexRateLimitStatus.Available, message, planType, primary, secondary);

    public static CodexRateLimitSummary Unavailable(string message) =>
        new(CodexRateLimitStatus.Unavailable, message, null, null, null);
}

internal sealed record CodexRateLimitWindow(
    int UsedPercent,
    int RemainingPercent,
    int WindowMinutes,
    long ResetAt);

internal enum CodexRateLimitStatus
{
    Available,
    Unavailable
}
