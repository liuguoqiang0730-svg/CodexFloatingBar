using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CodexFloatingBar;

internal sealed class OpenAiUsageService
{
    private const string AdminApiKeyEnvironmentVariable = "OPENAI_ADMIN_API_KEY";
    private static readonly HttpClient HttpClient = new()
    {
        BaseAddress = new Uri("https://api.openai.com/v1/"),
        Timeout = TimeSpan.FromSeconds(8)
    };

    public async Task<OpenAiUsageSummary> ReadTodayAsync(CancellationToken cancellationToken = default)
    {
        var apiKey = Environment.GetEnvironmentVariable(AdminApiKeyEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return OpenAiUsageSummary.Unconfigured($"API 用量: 设置 {AdminApiKeyEnvironmentVariable} 后可读取；余额/ChatGPT 额度仍需网页查看");
        }

        var startTime = new DateTimeOffset(DateTime.Today).ToUnixTimeSeconds();

        try
        {
            using var costs = await SendJsonRequestAsync($"organization/costs?start_time={startTime}&bucket_width=1d&limit=1", apiKey, cancellationToken);
            using var usage = await SendJsonRequestAsync($"organization/usage/completions?start_time={startTime}&bucket_width=1d&limit=1", apiKey, cancellationToken);

            var cost = SumCost(costs.RootElement);
            var completions = SumCompletionsUsage(usage.RootElement);
            return OpenAiUsageSummary.Available(cost, completions.InputTokens, completions.OutputTokens, completions.Requests);
        }
        catch (HttpRequestException ex)
        {
            return OpenAiUsageSummary.Failed($"API 用量读取失败: {ex.Message}");
        }
        catch (JsonException)
        {
            return OpenAiUsageSummary.Failed("API 用量读取失败: 返回数据格式无法解析");
        }
        catch (TaskCanceledException)
        {
            return OpenAiUsageSummary.Failed("API 用量读取超时");
        }
    }

    private static async Task<JsonDocument> SendJsonRequestAsync(string path, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await HttpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    private static decimal SumCost(JsonElement root)
    {
        decimal total = 0;

        foreach (var result in EnumerateResults(root))
        {
            if (result.TryGetProperty("amount", out var amount) &&
                amount.TryGetProperty("value", out var value) &&
                value.TryGetDecimal(out var cost))
            {
                total += cost;
            }
        }

        return total;
    }

    private static (long InputTokens, long OutputTokens, long Requests) SumCompletionsUsage(JsonElement root)
    {
        long inputTokens = 0;
        long outputTokens = 0;
        long requests = 0;

        foreach (var result in EnumerateResults(root))
        {
            inputTokens += GetInt64(result, "input_tokens");
            outputTokens += GetInt64(result, "output_tokens");
            requests += GetInt64(result, "num_model_requests");
        }

        return (inputTokens, outputTokens, requests);
    }

    private static IEnumerable<JsonElement> EnumerateResults(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var bucket in data.EnumerateArray())
        {
            if (!bucket.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var result in results.EnumerateArray())
            {
                yield return result;
            }
        }
    }

    private static long GetInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var value) && value.TryGetInt64(out var number) ? number : 0;
    }
}

internal sealed record OpenAiUsageSummary(
    OpenAiUsageStatus Status,
    string Message,
    decimal TodayCostUsd,
    long InputTokens,
    long OutputTokens,
    long Requests)
{
    public static OpenAiUsageSummary Unconfigured(string message) => new(OpenAiUsageStatus.Unconfigured, message, 0, 0, 0, 0);

    public static OpenAiUsageSummary Failed(string message) => new(OpenAiUsageStatus.Failed, message, 0, 0, 0, 0);

    public static OpenAiUsageSummary Available(decimal todayCostUsd, long inputTokens, long outputTokens, long requests)
    {
        var cost = todayCostUsd.ToString("0.####", CultureInfo.InvariantCulture);
        var message = $"今日 API: ${cost} | 请求 {requests:N0} | tokens {(inputTokens + outputTokens):N0}";
        return new OpenAiUsageSummary(OpenAiUsageStatus.Available, message, todayCostUsd, inputTokens, outputTokens, requests);
    }
}

internal enum OpenAiUsageStatus
{
    Unconfigured,
    Available,
    Failed
}
