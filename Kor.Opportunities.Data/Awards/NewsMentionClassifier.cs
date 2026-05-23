#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.Awards;

/// <summary>
/// Reads pending NewsArticle rows, asks Claude to extract org mentions and classify
/// mention type, resolves each raw org name to a CanonicalOrg, and persists mentions.
/// </summary>
public sealed class NewsMentionClassifier
{
    private const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    private const string DefaultModel = "claude-haiku-4-5-20251001";
    private const string AnthropicVersion = "2023-06-01";

    private static readonly Regex HtmlTagRegex = new("<[^>]+>", RegexOptions.Compiled);
    private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

    private static readonly string SystemPrompt = """
You are an analyst at KOR Structural, a Vancouver structural engineering firm. You receive
construction-industry news articles. Extract every organization mention that appears to be a
real firm or agency, with the context of what's being said about it.

Return STRICT JSON only (no prose, no markdown fences):
{
  "mentions": [
    {
      "org_name": "exact name as it appears in the article",
      "mention_type": "project_win | m_and_a | hiring | leadership | award | expansion | regulatory | partnership | other",
      "excerpt": "1-2 sentence verbatim excerpt showing the mention",
      "confidence": 0-100
    }
  ]
}

Rules:
- ONLY real organizations (firms, agencies, government bodies, school districts, health authorities). Skip generic references like "the city", "a contractor", "the developer", "officials".
- mention_type categories:
  * project_win - org won/was awarded a contract; org has been selected/hired for a project
  * m_and_a - merger, acquisition, ownership change, divestiture
  * hiring - significant hiring drive, layoffs, headcount change
  * leadership - exec appointment/retirement/departure/change
  * award - industry recognition, certification, ranking
  * expansion - new office, market entry, geographic growth
  * regulatory - licensing, compliance, disciplinary action, lawsuit
  * partnership - joint venture, teaming arrangement
  * other - any other relevant commercial activity
- Excerpt MUST be verbatim from the article (no paraphrasing, no editorializing).
- Confidence: how certain the mention is commercially relevant (90+ for explicit named-business news, 50-70 for tangential).
- If no organizations are mentioned in a commercial context, return {"mentions":[]}.
- Skip generic mentions of provinces/cities unless they're acting as a procuring entity in the article.
""";

    private readonly INewsStore _newsStore;
    private readonly CanonicalOrgResolver _resolver;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<NewsMentionClassifier> _logger;

    public NewsMentionClassifier(
        INewsStore newsStore,
        CanonicalOrgResolver resolver,
        HttpClient http,
        string apiKey,
        string? model,
        ILogger<NewsMentionClassifier> logger)
    {
        _newsStore = newsStore;
        _resolver = resolver;
        _http = http;
        _apiKey = apiKey;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model;
        _logger = logger;
    }

    public sealed record BatchResult(int Attempted, int Ok, int Failed, int MentionsFound);

    public async Task<BatchResult> ClassifyBatchAsync(int batchSize, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogWarning("Anthropic API key not configured; news classification skipped.");
            return new BatchResult(0, 0, 0, 0);
        }

        var pending = await _newsStore.ListPendingClassificationAsync(batchSize, ct).ConfigureAwait(false);
        if (pending.Count == 0)
        {
            return new BatchResult(0, 0, 0, 0);
        }

        var ok = 0;
        var failed = 0;
        var totalMentions = 0;

        foreach (var article in pending)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var mentions = await ClassifyOneAsync(article, ct).ConfigureAwait(false);
                if (mentions is null)
                {
                    await _newsStore.MarkArticleClassifiedAsync(
                        article.Id,
                        NewsClassificationStatuses.Failed,
                        ct).ConfigureAwait(false);
                    failed++;
                    continue;
                }

                foreach (var mention in mentions)
                {
                    if (string.IsNullOrWhiteSpace(mention.OrgName))
                    {
                        continue;
                    }

                    var canonicalId = await _resolver.ResolveAsync(
                        mention.OrgName,
                        OrgKinds.Unknown,
                        "News.Article",
                        ct).ConfigureAwait(false);

                    if (!canonicalId.HasValue)
                    {
                        continue;
                    }

                    await _newsStore.RecordMentionAsync(
                        new NewsMentionInsert(
                            article.Id,
                            canonicalId.Value,
                            NormalizeMentionType(mention.MentionType),
                            Math.Clamp(mention.Confidence, 0, 100),
                            mention.Excerpt),
                        ct).ConfigureAwait(false);
                    totalMentions++;
                }

                await _newsStore.MarkArticleClassifiedAsync(
                    article.Id,
                    NewsClassificationStatuses.Ok,
                    ct).ConfigureAwait(false);
                ok++;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Classification failed for article {Id}.", article.Id);
                try
                {
                    await _newsStore.MarkArticleClassifiedAsync(
                        article.Id,
                        NewsClassificationStatuses.Failed,
                        ct).ConfigureAwait(false);
                }
                catch
                {
                }

                failed++;
            }
        }

        return new BatchResult(pending.Count, ok, failed, totalMentions);
    }

    private sealed record ParsedMention(string OrgName, string? MentionType, int Confidence, string? Excerpt);

    private async Task<List<ParsedMention>?> ClassifyOneAsync(
        NewsArticleForClassification article,
        CancellationToken ct)
    {
        var text = StripHtml(article.Summary) + "\n\n" + StripHtml(article.Content);
        if (text.Length > 60_000)
        {
            text = text.Substring(0, 60_000);
        }

        var userPrompt =
            $"Title: {article.Title}\n" +
            $"URL: {article.Url}\n\n" +
            $"Article text:\n{text}\n\n" +
            "Extract organization mentions per the JSON schema in the system prompt.";

        var body = new
        {
            model = _model,
            max_tokens = 2000,
            system = SystemPrompt,
            messages = new object[] { new { role = "user", content = userPrompt } },
        };

        using var req = new HttpRequestMessage(HttpMethod.Post, AnthropicEndpoint)
        {
            Content = JsonContent.Create(body),
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
        if (contentArr is null)
        {
            return null;
        }

        var sb = new StringBuilder();
        foreach (var node in contentArr)
        {
            if (node?["type"]?.GetValue<string>() == "text")
            {
                sb.Append(node["text"]?.GetValue<string>() ?? "");
            }
        }

        var jsonText = ExtractJsonObject(sb.ToString());
        if (jsonText is null)
        {
            return null;
        }

        try
        {
            var parsed = JsonNode.Parse(jsonText);
            var arr = parsed?["mentions"]?.AsArray();
            if (arr is null)
            {
                return new List<ParsedMention>();
            }

            var list = new List<ParsedMention>();
            foreach (var item in arr)
            {
                var name = item?["org_name"]?.GetValue<string?>();
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                list.Add(new ParsedMention(
                    name!,
                    item?["mention_type"]?.GetValue<string?>(),
                    item?["confidence"]?.GetValue<int?>() ?? 50,
                    item?["excerpt"]?.GetValue<string?>()));
            }

            return list;
        }
        catch
        {
            return null;
        }
    }

    private static string StripHtml(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return "";
        }

        var withoutTags = HtmlTagRegex.Replace(input, " ");
        return WhitespaceRegex.Replace(withoutTags, " ").Trim();
    }

    private static string? NormalizeMentionType(string? value)
    {
        return value switch
        {
            NewsMentionTypes.ProjectWin => value,
            NewsMentionTypes.MAndA => value,
            NewsMentionTypes.Hiring => value,
            NewsMentionTypes.Leadership => value,
            NewsMentionTypes.Award => value,
            NewsMentionTypes.Expansion => value,
            NewsMentionTypes.Regulatory => value,
            NewsMentionTypes.Partnership => value,
            NewsMentionTypes.Other => value,
            _ => NewsMentionTypes.Other,
        };
    }

    private static string? ExtractJsonObject(string text)
    {
        var depth = 0;
        var start = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '{')
            {
                if (depth == 0)
                {
                    start = i;
                }

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
}
