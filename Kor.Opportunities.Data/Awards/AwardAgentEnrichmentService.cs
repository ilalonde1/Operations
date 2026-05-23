#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

public sealed class AwardAgentEnrichmentService
{
    private const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    private const string DefaultModel = "claude-sonnet-4-6";
    private const string AnthropicVersion = "2023-06-01";

    private static readonly string SystemPrompt = """
You are a research analyst at KOR Structural, a small Vancouver-based structural engineering firm doing BD
intelligence on procurement awards. Your job is to research ONE awarded contract row and produce a brief BD-useful
summary.

Use the web_search tool (up to 3 searches) to find:
- Who the vendor company is (size, location, specialties)
- What this specific contract was actually for, beyond its bare title
- Whether the vendor is a direct competitor for structural engineering work that KOR does (KOR specializes in:
structural engineering, seismic retrofit, building inspections; clients in BC + Alberta + LA + San Diego)

Return STRICT JSON only (no prose, no markdown fences):
{
  "vendor_profile": "2-3 sentence description of the vendor",
  "contract_context": "1-2 sentence description of what this contract was actually for",
  "competes_with_kor": true|false,
  "competition_notes": "1 sentence on how/whether they overlap with KOR's structural engineering work",
  "source_urls": ["url1","url2"]
}

If you cannot find useful info after searching, return null for that specific field. Never invent or guess.
""";

    private readonly IOpportunityAwardStore _store;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<AwardAgentEnrichmentService> _logger;

    public AwardAgentEnrichmentService(
        IOpportunityAwardStore store,
        HttpClient http,
        string apiKey,
        string? model,
        ILogger<AwardAgentEnrichmentService> logger)
    {
        _store = store;
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _logger = logger;
    }

    public sealed record AgentBatchResult(int Attempted, int Enriched, int Failed);

    public async Task<AgentBatchResult> EnrichBatchAsync(int batchSize, int maxAttempts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Anthropic API key not configured; agent enrichment skipped.");
            return new AgentBatchResult(0, 0, 0);
        }

        var pending = await _store.ListPendingAgentEnrichmentAsync(batchSize, maxAttempts, ct)
            .ConfigureAwait(false);
        if (pending.Count == 0) return new AgentBatchResult(0, 0, 0);

        _logger.LogInformation("Agent-enriching batch of {Count} awards.", pending.Count);

        var enriched = 0;
        var failed = 0;
        foreach (var row in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var payload = await EnrichOneAsync(row, ct).ConfigureAwait(false);
                if (payload is null)
                {
                    await _store.RecordAgentFailureAsync(
                        row.Id,
                        "Agent returned no parseable JSON.",
                        ct).ConfigureAwait(false);
                    failed++;
                }
                else
                {
                    await _store.RecordAgentEnrichmentAsync(row.Id, payload, ct).ConfigureAwait(false);
                    enriched++;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(
                    ex,
                    "Agent enrichment failed for award {Id} ({Vendor}).",
                    row.Id,
                    row.AwardedToOrganization);
                try { await _store.RecordAgentFailureAsync(row.Id, ex.Message, ct).ConfigureAwait(false); } catch { }
            }
        }

        return new AgentBatchResult(pending.Count, enriched, failed);
    }

    private async Task<AwardAgentEnrichmentPayload?> EnrichOneAsync(
        PendingAgentEnrichmentRow row,
        CancellationToken ct)
    {
        var userPrompt =
            $"Row to research:\n" +
            $"- Title: {row.Title}\n" +
            $"- Buyer: {row.AwardingOrganization}\n" +
            $"- Winner: {row.AwardedToOrganization}\n" +
            $"- Contract Value: {(row.ContractValue.HasValue ? $"{row.ContractValue.Value:C0} {row.ContractCurrency}" : "(unknown)")}\n" +
            $"- Awarded Date: {(row.AwardedAtUtc.HasValue ? row.AwardedAtUtc.Value.ToString("yyyy-MM-dd") : "(unknown)")}\n" +
            $"- Location: {row.IssuingLocation ?? "(unknown)"}\n" +
            $"- Source: {row.SourceName}\n" +
            $"- RFP Ref: {row.ExternalReference}\n\n" +
            "Research and return the JSON object specified in the system prompt.";

        var body = new
        {
            model = _model,
            max_tokens = 1024,
            system = SystemPrompt,
            tools = new object[]
            {
                new { type = "web_search_20250305", name = "web_search", max_uses = 1 }
            },
            messages = new object[]
            {
                new { role = "user", content = userPrompt }
            }
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, AnthropicEndpoint)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Anthropic API {(int)resp.StatusCode}: {respBody}");
        }

        var root = JsonNode.Parse(respBody);
        if (root is null) return null;

        var contentArr = root["content"]?.AsArray();
        if (contentArr is null) return null;

        var sb = new StringBuilder();
        foreach (var node in contentArr)
        {
            if (node is null) continue;
            if (node["type"]?.GetValue<string>() == "text")
            {
                sb.Append(node["text"]?.GetValue<string>() ?? "");
            }
        }

        var jsonText = ExtractJsonObject(sb.ToString());
        if (string.IsNullOrWhiteSpace(jsonText)) return null;

        try
        {
            var json = JsonNode.Parse(jsonText);
            if (json is null) return null;
            var urls = new List<string>();
            var urlsArr = json["source_urls"]?.AsArray();
            if (urlsArr is not null)
            {
                foreach (var u in urlsArr)
                {
                    var s = u?.GetValue<string>();
                    if (!string.IsNullOrWhiteSpace(s)) urls.Add(s);
                }
            }

            return new AwardAgentEnrichmentPayload
            {
                VendorProfile = StripCitations(json["vendor_profile"]?.GetValue<string?>()),
                ContractContext = StripCitations(json["contract_context"]?.GetValue<string?>()),
                CompetesWithKor = json["competes_with_kor"]?.GetValue<bool?>(),
                CompetitionNotes = StripCitations(json["competition_notes"]?.GetValue<string?>()),
                SourceUrls = urls,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the first balanced JSON object from a string. Tolerates prose
    /// before/after, markdown fences (```json ... ```), and Claude's habit of
    /// emitting an explanatory paragraph before the JSON block.
    /// </summary>
    private static string? ExtractJsonObject(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var start = text.IndexOf('{');
        if (start < 0) return null;

        // Walk balanced braces, respecting strings and escapes.
        var depth = 0;
        var inString = false;
        var escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"') inString = false;
                continue;
            }
            if (c == '"') { inString = true; continue; }
            if (c == '{') depth++;
            else if (c == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }
        return null;
    }

    private static readonly System.Text.RegularExpressions.Regex CitationTagRegex =
        new(@"<\s*/?\s*cite\b[^>]*>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Claude's web_search server tool injects citation markers like
    /// <cite index="4-3,4-8">text</cite> around grounded claims. Strip the
    /// tags but keep the wrapped text so the prose still reads naturally.
    /// </summary>
    private static string? StripCitations(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        var cleaned = CitationTagRegex.Replace(text, "");
        return cleaned.Trim();
    }
}
