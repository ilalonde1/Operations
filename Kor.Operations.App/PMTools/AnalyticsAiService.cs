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

namespace Kor.Operations.PMTools
{
    internal sealed class AnalyticsAiService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
        private readonly string _apiKey;

        internal bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        internal AnalyticsAiService(string apiKey)
        {
            _apiKey = (apiKey ?? "").Trim();
        }

        internal async Task<string> ExplainAsync(string question, string dataContext, CancellationToken ct = default)
        {
            if (!IsConfigured) return "AI is not configured. Set the KOR_ANTHROPIC_KEY environment variable.";
            if (string.IsNullOrWhiteSpace(question)) return "";

            var systemPrompt =
                "You are an analytics assistant for KOR Structural, a structural engineering firm in Vancouver, BC. " +
                "You can ONLY discuss the data provided below — do not reference external information, make assumptions " +
                "about data you haven't been given, or discuss topics outside of this firm's project and employee analytics.\n\n" +
                "Your audience is firm principals and project managers who are NOT data analysts. " +
                "Explain metrics, scores, and trends in plain language. Be concise — 2-4 sentences unless the question " +
                "requires more detail. Use specific numbers from the data when relevant.\n\n" +
                "If asked about something not in the data below, say so clearly.\n\n" +
                "DATA CONTEXT:\n" + dataContext;

            var requestBody = new
            {
                model = "claude-haiku-4-5-20251001",
                max_tokens = 500,
                system = systemPrompt,
                messages = new[] { new { role = "user", content = question } }
            };

            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
                {
                    Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json")
                };
                request.Headers.Add("x-api-key", _apiKey);
                request.Headers.Add("anthropic-version", "2023-06-01");

                using var response = await _http.SendAsync(request, ct);
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
                Log.Warning(ex, "Analytics AI request failed.");
                return $"Unable to get AI response: {ex.Message}";
            }
        }

        internal static string BuildContext(string title, string subtitle, IReadOnlyList<DetailMetric> metrics)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Currently viewing: {title} ({subtitle})");
            sb.AppendLine();

            foreach (var m in metrics)
            {
                if (m.IsHeader)
                    sb.AppendLine($"\n[{m.Label}]");
                else if (m.IsExplanation)
                    continue;
                else if (!string.IsNullOrWhiteSpace(m.Value))
                    sb.AppendLine($"  {m.Label}: {m.Value}");
            }

            return sb.ToString();
        }
    }
}
