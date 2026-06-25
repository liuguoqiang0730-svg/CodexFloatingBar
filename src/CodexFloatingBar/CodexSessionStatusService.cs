using System.IO;
using System.Text;
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
    private static readonly string CodexHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
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
        return string.IsNullOrWhiteSpace(model) && string.IsNullOrWhiteSpace(effort)
            ? null
            : new CodexSessionStatus(model, effort);
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
}

internal sealed record CodexSessionStatus(string? Model, string? ReasoningEffort)
{
    public static CodexSessionStatus Unavailable { get; } = new(null, null);
}
