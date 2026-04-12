#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace Kor.Operations.Services;

internal sealed class AppAiService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _apiKey;
    private readonly AppAiContextBuilder _contextBuilder;

    private static readonly string SystemPromptBase =
        "You are an analytics assistant for KOR Structural, a structural engineering firm in Vancouver, BC. " +
        "You have access to the firm's complete project, employee, and financial performance data provided below.\n\n" +
        "Your audience is firm principals and project managers who are NOT data analysts. " +
        "Explain metrics, scores, and trends in plain, actionable language. Use specific names and numbers. " +
        "Be concise but thorough — answer in 3-6 sentences unless the question needs more.\n\n" +
        "You can compare employees, identify trends, flag concerns, rank projects, analyze clients, " +
        "and make recommendations based on the data. " +
        "If asked about something not in the data, say so clearly. Do NOT make up data or reference " +
        "information outside of what's provided below.\n\n" +
        "SCORING METHODOLOGY:\n" +
        "Employee Productivity Score (0-100, maps to A+ through F):\n" +
        "  Billable Rate (30%) — % of total hours on billable projects vs overhead/admin\n" +
        "  Efficiency (40%) — Fee/Hr percentile rank vs all employees. 50 = median.\n" +
        "  Project Health (30%) — % of hours on projects NOT over budget\n\n" +
        "PM/DM Performance Score (0-100, maps to A+ through F):\n" +
        "  Delivery Health (30%) — % of projects not over budget\n" +
        "  Estimation Accuracy (30%) — Budget delta percentile rank\n" +
        "  Revenue Efficiency (20%) — Fee/Hr percentile rank\n" +
        "  AR Management (20%) — % of AR not 90+ days overdue\n\n" +
        "Delivery Confidence (per project): Critical / At Risk / Watch / High Confidence\n" +
        "Consistency: CV of hours across projects. Steady < 0.3, Variable < 0.6, Erratic ≥ 0.6\n" +
        "Peer Comparison: Fee/Hr compared against employees working on same construction type\n\n";

    internal bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

    internal AppAiService(string apiKey, AppAiContextBuilder contextBuilder)
    {
        _apiKey = (apiKey ?? "").Trim();
        _contextBuilder = contextBuilder;
    }

    internal async Task<string> AskAsync(
        IReadOnlyList<(string Role, string Content)> conversation,
        string? localContext = null,
        CancellationToken ct = default)
    {
        if (!IsConfigured) return "AI is not configured. Set the KOR_ANTHROPIC_KEY environment variable.";
        if (conversation.Count == 0) return "";

        var fullContext = _contextBuilder.BuildFullContext(localContext);
        var systemPrompt = SystemPromptBase + "FIRM DATA:\n" + fullContext;

        var messages = conversation.Select(m => new { role = m.Role, content = m.Content }).ToArray();

        var requestBody = new
        {
            model = "claude-sonnet-4-6",
            max_tokens = 800,
            system = systemPrompt,
            messages
        };

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-api-key", _apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");

                using var response = await _http.SendAsync(request, ct);

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(5 * (attempt + 1));
                    await Task.Delay(retryAfter, ct);
                    continue;
                }

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);
                return doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";
            }
            catch (OperationCanceledException)
            {
                return "";
            }
            catch (Exception ex)
            {
                if (attempt == 2)
                {
                    Log.Warning(ex, "AI request failed after 3 attempts.");
                    return $"Unable to get AI response: {ex.Message}";
                }
                await Task.Delay(TimeSpan.FromSeconds(2 * (attempt + 1)), ct);
            }
        }
        return "AI request failed after retries. Try again in a moment.";
    }
}
