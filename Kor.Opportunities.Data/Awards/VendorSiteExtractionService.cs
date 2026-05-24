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

public sealed class VendorSiteExtractionService
{
    private const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    private const string DefaultModel = "claude-haiku-4-5-20251001";
    private const string AnthropicVersion = "2023-06-01";

    private static readonly string SystemPrompt = """
You are a BD research analyst at KOR Structural, a structural engineering firm in Vancouver. You are given the
raw text content of a vendor company's own website (5-7 pages concatenated: home, about, services, projects,
team, careers, contact). Your job is to extract structured BD intelligence as STRICT JSON.

Return JSON ONLY, no prose, no markdown fences:
{
  "portfolio": [
    {"project_name":"...","client":"...","location":"City, Province/State","year":2023,"value":"$5M","summary":"1 sentence"}
  ],
  "specific_services": ["narrow service or niche 1", "narrow service or niche 2"],
  "sector_focus": ["healthcare", "education", "municipal", "industrial"],
  "open_positions": [{"title":"Senior Structural Engineer","location":"Vancouver","discipline":"structural"}],
  "leadership_detail": [
    {"name":"Jane Doe","title":"President","background":"20 yrs at Arup before joining","p_eng":true,"joined_year":2002}
  ],
  "bonding_capacity": "Up to $50M per project",
  "tagline": "Engineering structures that endure"
}

RULES:
- Extract ONLY information that is actually present in the supplied text. NEVER invent or guess.
- Portfolio: max 8 most-recent or marquee projects. Skip items if you can't tell the project name.
- Specific services: narrow, useful phrases (e.g. "seismic retrofit of unreinforced masonry"), not broad words like "structural engineering".
- Sector focus: short lowercase category words. Examples: healthcare, education, municipal, industrial, commercial,
  residential, transportation, mining, oil-and-gas, defense. Empty array if not stated.
- Open positions: only currently-listed jobs. If a Careers page just says "join us" generically, return empty array.
- Leadership: only people clearly named with a title or role. background is a one-sentence summary if findable.
  p_eng = true only if the bio explicitly mentions P.Eng or "Professional Engineer". Otherwise null.
- bonding_capacity: short string if the site mentions a bonding/insurance ceiling. Otherwise null.
- tagline: 1 short sentence positioning statement. Otherwise null.
- If a section's text didn't yield any extractable items, return an EMPTY ARRAY for arrays or NULL for scalars.
""";

    private readonly IVendorSiteCrawlStore _crawlStore;
    private readonly IOpportunityAwardStore _awardStore;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<VendorSiteExtractionService> _logger;

    public VendorSiteExtractionService(
        IVendorSiteCrawlStore crawlStore,
        IOpportunityAwardStore awardStore,
        HttpClient http,
        string apiKey,
        string? model,
        ILogger<VendorSiteExtractionService> logger)
    {
        _crawlStore = crawlStore;
        _awardStore = awardStore;
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _logger = logger;
    }

    public sealed record BatchResult(int Attempted, int Extracted, int Failed);

    public async Task<BatchResult> ExtractBatchAsync(int batchSize, int maxAttempts, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Anthropic API key not configured; vendor site extraction skipped.");
            return new BatchResult(0, 0, 0);
        }

        var pending = await _crawlStore.ListPendingExtractionAsync(batchSize, maxAttempts, ct).ConfigureAwait(false);
        if (pending.Count == 0) return new BatchResult(0, 0, 0);

        _logger.LogInformation("VendorSiteExtraction: batch of {Count} crawl(s).", pending.Count);

        var ok = 0;
        var failed = 0;
        foreach (var row in pending)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var payload = await ExtractOneAsync(row, ct).ConfigureAwait(false);
                if (payload is null)
                {
                    await _crawlStore.MarkExtractionFailedAsync(row.CrawlId, "Could not parse Claude JSON response.", ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                if (_awardStore is SqlOpportunityAwardStore sqlStore)
                {
                    await sqlStore.RecordSiteExtractionAsync(row.VendorWebsite, payload, ct).ConfigureAwait(false);
                }

                await _crawlStore.MarkExtractedAsync(row.CrawlId, ct).ConfigureAwait(false);
                ok++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "VendorSiteExtraction failed for crawl {Id} ({Site}): {Msg}", row.CrawlId, row.VendorWebsite, ex.Message);
                try
                {
                    await _crawlStore.MarkExtractionFailedAsync(row.CrawlId, ex.Message, ct).ConfigureAwait(false);
                }
                catch (Exception secondary)
                {
                    _logger.LogWarning(
                        secondary,
                        "Failed to record VendorSiteExtraction failure for award {AwardId}",
                        row.CrawlId);
                }

                failed++;
            }
        }

        return new BatchResult(pending.Count, ok, failed);
    }

    private async Task<VendorSiteExtractionPayload?> ExtractOneAsync(PendingExtractionRow row, CancellationToken ct)
    {
        var capture = JsonSerializer.Deserialize<RawSiteCapture>(row.RawCaptureJson);
        if (capture is null || capture.Pages.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine($"Vendor website: {row.VendorWebsite}");
        sb.AppendLine();
        foreach (var page in capture.Pages)
        {
            sb.AppendLine($"=== PAGE: {page.Url} ===");
            if (!string.IsNullOrWhiteSpace(page.Title))
            {
                sb.AppendLine($"Title: {page.Title}");
            }

            sb.AppendLine(page.Text);
            sb.AppendLine();
        }

        var body = new
        {
            model = _model,
            max_tokens = 3000,
            system = SystemPrompt,
            messages = new object[]
            {
                new { role = "user", content = sb.ToString() }
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

        var textBuilder = new StringBuilder();
        foreach (var node in contentArr)
        {
            if (node?["type"]?.GetValue<string>() == "text")
            {
                textBuilder.Append(node["text"]?.GetValue<string>() ?? "");
            }
        }

        var jsonText = ExtractJsonObject(textBuilder.ToString());
        if (jsonText is null) return null;

        try
        {
            var json = JsonNode.Parse(jsonText);
            if (json is null) return null;

            return new VendorSiteExtractionPayload
            {
                Portfolio = ReadPortfolio(json["portfolio"]),
                SpecificServices = ReadStringArray(json["specific_services"]),
                SectorFocus = ReadStringArray(json["sector_focus"]),
                OpenPositions = ReadPositions(json["open_positions"]),
                LeadershipDetail = ReadLeadership(json["leadership_detail"]),
                BondingCapacity = json["bonding_capacity"]?.GetValue<string?>(),
                Tagline = json["tagline"]?.GetValue<string?>(),
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractJsonObject(string text)
    {
        var depth = 0;
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                if (depth == 0) start = i;
                depth++;
            }
            else if (text[i] == '}')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    return text.Substring(start, i - start + 1);
                }
            }
        }

        return null;
    }

    private static List<string> ReadStringArray(JsonNode? node)
    {
        var values = new List<string>();
        var arr = node?.AsArray();
        if (arr is null) return values;
        foreach (var item in arr)
        {
            var value = item?.GetValue<string?>();
            if (!string.IsNullOrWhiteSpace(value)) values.Add(value!);
        }

        return values;
    }

    private static List<PortfolioItem> ReadPortfolio(JsonNode? node)
    {
        var values = new List<PortfolioItem>();
        var arr = node?.AsArray();
        if (arr is null) return values;
        foreach (var item in arr)
        {
            var name = item?["project_name"]?.GetValue<string?>();
            if (string.IsNullOrWhiteSpace(name)) continue;
            values.Add(new PortfolioItem(
                name!,
                item?["client"]?.GetValue<string?>(),
                item?["location"]?.GetValue<string?>(),
                item?["year"]?.GetValue<int?>(),
                item?["value"]?.GetValue<string?>(),
                item?["summary"]?.GetValue<string?>()));
        }

        return values;
    }

    private static List<OpenPosition> ReadPositions(JsonNode? node)
    {
        var values = new List<OpenPosition>();
        var arr = node?.AsArray();
        if (arr is null) return values;
        foreach (var item in arr)
        {
            var title = item?["title"]?.GetValue<string?>();
            if (string.IsNullOrWhiteSpace(title)) continue;
            values.Add(new OpenPosition(
                title!,
                item?["location"]?.GetValue<string?>(),
                item?["discipline"]?.GetValue<string?>()));
        }

        return values;
    }

    private static List<LeadershipBio> ReadLeadership(JsonNode? node)
    {
        var values = new List<LeadershipBio>();
        var arr = node?.AsArray();
        if (arr is null) return values;
        foreach (var item in arr)
        {
            var name = item?["name"]?.GetValue<string?>();
            if (string.IsNullOrWhiteSpace(name)) continue;
            values.Add(new LeadershipBio(
                name!,
                item?["title"]?.GetValue<string?>(),
                item?["background"]?.GetValue<string?>(),
                item?["p_eng"]?.GetValue<bool?>(),
                item?["joined_year"]?.GetValue<int?>()));
        }

        return values;
    }
}
