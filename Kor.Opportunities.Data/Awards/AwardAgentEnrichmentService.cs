#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

public sealed class AwardAgentEnrichmentService
{
    private const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    private const string DefaultModel = "claude-haiku-4-5-20251001";
    private const string AnthropicVersion = "2023-06-01";

    // Strips citation markers Claude's web_search server tool injects around
    // grounded claims. Three formats observed in the wild:
    //   <cite index="3-1">...</cite>  — HTML-style (the common web_search output)
    //   [1], [12]                     — bracketed numerics
    //   【...】                        — Chinese brackets
    private static readonly Regex CitationRegex = new(
        @"<\s*/?\s*cite\b[^>]*>|\s*(?:\[\d+\]|【[^】]+】)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string SystemPrompt = """
You are a research analyst at KOR Structural, a small Vancouver-based structural engineering firm doing BD
intelligence on procurement awards. Your job is to research ONE awarded contract row and produce a brief BD-useful
summary.

Use the web_search tool (one search) to find:
- Who the vendor company is (size, location, specialties, leadership)
- Their public website / official URL
- What this specific contract was actually for, beyond its bare title
- Whether the vendor is a direct competitor for structural engineering work that KOR does (KOR specializes in:
structural engineering, seismic retrofit, building inspections; clients in BC + Alberta + LA + San Diego)

Return STRICT JSON only (no prose, no markdown fences):
{
  "vendor_profile": "2-3 sentence description of the vendor",
  "contract_context": "1-2 sentence description of what this contract was actually for",
  "competes_with_kor": true|false,
  "competition_notes": "1 sentence on how/whether they overlap with KOR's structural engineering work",
  "vendor_website": "https://...",
  "vendor_hq_location": "City, Province/State, Country",
  "vendor_size_band": "small|mid|large|unknown",
  "vendor_founded_year": 1998,
  "vendor_ownership_status": "private|public|employee-owned|subsidiary|unknown",
  "vendor_parent_company": "Parent Co Name, or null if not a subsidiary",
  "vendor_specialties": ["service or discipline 1", "service or discipline 2"],
  "key_leadership": [{"name":"Jane Doe","title":"President"}, {"name":"John Smith","title":"CTO"}],
  "source_urls": ["url1","url2"]
}

Size-band rules: small = <50 employees, mid = 50-500, large = 500+. Use "unknown" if you can't tell.
Ownership: "subsidiary" means owned by a larger parent (set vendor_parent_company). "employee-owned" / "private" /
"public" are mutually exclusive, pick the most accurate. Use "unknown" if you genuinely can't tell.
Leadership: only include names you actually find on the web (About / Leadership page). Empty array if none found.
Never invent names.
If any field can't be determined, use null (for scalars), empty string (vendor_website), or empty array
(specialties/leadership). Never invent.
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

        var pending = await _store.ListPendingAgentEnrichmentAsync(batchSize, maxAttempts, ct).ConfigureAwait(false);
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
                    await _store.RecordAgentFailureAsync(row.Id, "Agent returned no parseable JSON.", ct)
                        .ConfigureAwait(false);
                    failed++;
                }
                else
                {
                    await _store.RecordAgentEnrichmentAsync(row.Id, payload, ct).ConfigureAwait(false);
                    if (_store is SqlOpportunityAwardStore sqlStore)
                    {
                        await sqlStore.RecordAgentVendorDetailsAsync(row.Id, payload, ct).ConfigureAwait(false);
                    }
                    enriched++;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failed++;
                _logger.LogWarning(ex, "Agent enrichment failed for award {Id} ({Vendor}).", row.Id, row.AwardedToOrganization);
                try
                {
                    await _store.RecordAgentFailureAsync(row.Id, ex.Message, ct).ConfigureAwait(false);
                }
                catch
                {
                }
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
            $"Research and return the JSON object specified in the system prompt.";

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
        var contentArr = root?["content"]?.AsArray();
        if (contentArr is null) return null;

        var sb = new StringBuilder();
        foreach (var node in contentArr)
        {
            if (node?["type"]?.GetValue<string>() == "text")
            {
                sb.Append(node["text"]?.GetValue<string>() ?? "");
            }
        }

        var jsonText = sb.ToString().Trim();
        if (string.IsNullOrWhiteSpace(jsonText)) return null;

        if (jsonText.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = jsonText.IndexOf('\n');
            if (firstNl > 0) jsonText = jsonText.Substring(firstNl + 1);
            if (jsonText.EndsWith("```", StringComparison.Ordinal))
            {
                jsonText = jsonText.Substring(0, jsonText.Length - 3);
            }

            jsonText = jsonText.Trim();
        }

        try
        {
            var json = JsonNode.Parse(jsonText);
            if (json is null) return null;

            var urls = ReadStringArray(json["source_urls"]);
            var specialties = ReadStringArray(json["vendor_specialties"]);
            var leadership = ReadLeadershipArray(json["key_leadership"]);

            return new AwardAgentEnrichmentPayload
            {
                VendorProfile = StripCitations(json["vendor_profile"]?.GetValue<string?>()),
                ContractContext = StripCitations(json["contract_context"]?.GetValue<string?>()),
                CompetesWithKor = json["competes_with_kor"]?.GetValue<bool?>(),
                CompetitionNotes = StripCitations(json["competition_notes"]?.GetValue<string?>()),
                SourceUrls = urls,
                VendorWebsite = StripCitations(json["vendor_website"]?.GetValue<string?>()),
                VendorHqLocation = StripCitations(json["vendor_hq_location"]?.GetValue<string?>()),
                VendorSizeBand = StripCitations(json["vendor_size_band"]?.GetValue<string?>()),
                VendorFoundedYear = json["vendor_founded_year"]?.GetValue<int?>(),
                VendorSpecialties = specialties,
                VendorLeadership = leadership,
                VendorOwnershipStatus = StripCitations(json["vendor_ownership_status"]?.GetValue<string?>()),
                VendorParentCompany = StripCitations(json["vendor_parent_company"]?.GetValue<string?>()),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static List<string> ReadStringArray(JsonNode? node)
    {
        var values = new List<string>();
        var arr = node?.AsArray();
        if (arr is null) return values;

        foreach (var item in arr)
        {
            var value = StripCitations(item?.GetValue<string>());
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
        }

        return values;
    }

    private static List<VendorLeader> ReadLeadershipArray(JsonNode? node)
    {
        var values = new List<VendorLeader>();
        var arr = node?.AsArray();
        if (arr is null) return values;

        foreach (var item in arr)
        {
            var name = StripCitations(item?["name"]?.GetValue<string?>());
            var title = StripCitations(item?["title"]?.GetValue<string?>());
            if (!string.IsNullOrWhiteSpace(name))
            {
                values.Add(new VendorLeader(name, string.IsNullOrWhiteSpace(title) ? null : title));
            }
        }

        return values;
    }

    private static string? StripCitations(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var stripped = CitationRegex.Replace(value, "").Trim();
        return string.IsNullOrWhiteSpace(stripped) ? null : stripped;
    }
}
