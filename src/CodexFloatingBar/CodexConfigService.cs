using System.IO;
using System.Text.RegularExpressions;

namespace CodexFloatingBar;

internal sealed class CodexConfigService
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

        try
        {
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
        }
        catch (UnauthorizedAccessException)
        {
            return ConfigReadResult.Failed($"没有权限读取配置文件: {path}");
        }
        catch (IOException ex)
        {
            return ConfigReadResult.Failed($"读取配置文件失败: {ex.Message}");
        }

        return new ConfigReadResult(true, true, model, effort, null);
    }
}

internal sealed record ConfigReadResult(bool Exists, bool ReadSucceeded, string? Model, string? ReasoningEffort, string? Message)
{
    public static ConfigReadResult Missing(string message) => new(false, false, null, null, message);

    public static ConfigReadResult Failed(string message) => new(true, false, null, null, message);
}
