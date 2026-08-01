using System.Text;
using System.Text.Json;
using Kor.Operations.Mcp.Ai;
using Kor.Operations.Mcp.Alerts;
using Kor.Operations.Mcp.Brief;

namespace Kor.Operations.Mcp.CooCard;

/// <summary>
/// Synthesizes the weekly COO Card by feeding the latest CooBrief sections
/// + active Alerts into the /ask gateway with a triage-ranking system
/// prompt and persisting the parsed Top 5 list. Runs after the brief
/// generator so it has fresh inputs.
/// </summary>
public sealed class CooCardGenerator
{
    private readonly AskService _ask;
    private readonly CooBriefRepository _briefRepo;
    private readonly AlertRepository _alertRepo;
    private readonly CooCardRepository _cardRepo;
    private readonly ILogger<CooCardGenerator> _logger;

    public CooCardGenerator(
        AskService ask,
        CooBriefRepository briefRepo,
        AlertRepository alertRepo,
        CooCardRepository cardRepo,
        ILogger<CooCardGenerator> logger)
    {
        _ask = ask;
        _briefRepo = briefRepo;
        _alertRepo = alertRepo;
        _cardRepo = cardRepo;
        _logger = logger;
    }

    public async Task GenerateForWeekAsync(DateTime weekOf, CancellationToken ct)
    {
        var brief = await _briefRepo.GetLatestWeekAsync(ct).ConfigureAwait(false);
        var alerts = await _alertRepo.GetActiveAsync(ct).ConfigureAwait(false);

        var briefText = BuildBriefBlock(brief);
        var alertsText = BuildAlertsBlock(alerts);
        var question = CooCardPrompts.UserQuestionTemplate
            .Replace("{brief}", briefText)
            .Replace("{alerts}", alertsText);

        var combined = CooCardPrompts.StrategistOverlay + "\n\n" + question;
        var req = new AskRequest(combined) { UserUpn = "coo-card@kor" };

        AskResponse resp;
        try
        {
            resp = await _ask.AskAsync(req, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "COO Card synthesis call to /ask failed.");
            return;
        }

        if (!TryParseRanked(resp.Answer, out var items))
        {
            _logger.LogWarning(
                "COO Card response was not a parseable JSON array of 5 items; preserving the previous week's card. Answer prefix: {Prefix}",
                Truncate(resp.Answer, 200));
            return;
        }

        try
        {
            await _cardRepo.ReplaceWeekAsync(weekOf, items, ct).ConfigureAwait(false);
            _logger.LogInformation("COO Card generated for week {WeekOf}: {Count} items.", weekOf.ToString("yyyy-MM-dd"), items.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "COO Card persist failed for week {WeekOf}.", weekOf.ToString("yyyy-MM-dd"));
        }
    }

    private static string BuildBriefBlock(IReadOnlyList<BriefRow> rows)
    {
        if (rows.Count == 0)
        {
            return "(no brief sections available for the latest week)";
        }

        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Append("[Section ").Append(row.Section).Append("] ");
            sb.AppendLine(row.Headline);
            sb.AppendLine(row.Body);
            if (!string.IsNullOrWhiteSpace(row.Recommendation))
            {
                sb.Append("Recommendation: ").AppendLine(row.Recommendation);
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildAlertsBlock(IReadOnlyList<AlertRow> rows)
    {
        if (rows.Count == 0)
        {
            return "(no active alerts)";
        }

        var sb = new StringBuilder();
        foreach (var row in rows)
        {
            sb.Append('[').Append(row.Severity).Append(' ').Append(row.RuleId).Append("] ");
            sb.AppendLine(row.Title);
            sb.AppendLine(row.Body);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// Parses the AI's strict-JSON-array response into 5 ranked items.
    /// Forgives a leading/trailing prose envelope by extracting the first
    /// balanced [...] in the answer. Validates exact count + presence of
    /// required string fields.
    /// </summary>
    private static bool TryParseRanked(string answer, out IReadOnlyList<RichCooCardItem> items)
    {
        items = Array.Empty<RichCooCardItem>();
        if (string.IsNullOrWhiteSpace(answer))
        {
            return false;
        }

        var span = ExtractFirstJsonArray(answer);
        if (span is null)
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(span);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var parsed = new List<RichCooCardItem>(5);
            foreach (var elt in doc.RootElement.EnumerateArray())
            {
                if (elt.ValueKind != JsonValueKind.Object) return false;

                var rank = elt.TryGetProperty("rank", out var rankEl) && rankEl.TryGetInt32(out var r) ? r : parsed.Count + 1;
                var severity = ReadString(elt, "severity") ?? "Medium";
                var headline = ReadString(elt, "headline");
                var body = ReadString(elt, "body");
                var recommendation = ReadString(elt, "recommendation") ?? string.Empty;
                var sourceTags = ReadString(elt, "sourceTags") ?? string.Empty;

                if (string.IsNullOrWhiteSpace(headline) || string.IsNullOrWhiteSpace(body))
                {
                    return false;
                }

                parsed.Add(new RichCooCardItem(
                    Rank: rank,
                    Severity: NormalizeSeverity(severity),
                    Headline: headline.Trim(),
                    Body: body.Trim(),
                    Recommendation: recommendation.Trim(),
                    SourceTags: sourceTags.Trim()));
            }

            if (parsed.Count == 0)
            {
                return false;
            }

            // Renumber ranks to be 1..N in the order returned, in case the
            // model emitted duplicate or skipped numbers. The list order is
            // authoritative.
            for (var i = 0; i < parsed.Count; i++)
            {
                parsed[i] = parsed[i] with { Rank = i + 1 };
            }

            items = parsed;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement obj, string property)
    {
        if (!obj.TryGetProperty(property, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Null => null,
            _ => el.ToString(),
        };
    }

    private static string NormalizeSeverity(string raw)
    {
        var s = (raw ?? string.Empty).Trim();
        return s.ToUpperInvariant() switch
        {
            "HIGH" or "CRITICAL" or "URGENT" => "High",
            "LOW" or "INFO" or "MONITOR" => "Low",
            _ => "Medium",
        };
    }

    private static string? ExtractFirstJsonArray(string answer)
    {
        // Walk the string skipping over string literals so a `[` inside a
        // string doesn't false-trigger the scan. Returns the first balanced
        // top-level array as a substring, or null if none.
        var depth = 0;
        var start = -1;
        var inString = false;
        var escape = false;

        for (var i = 0; i < answer.Length; i++)
        {
            var c = answer[i];

            if (inString)
            {
                if (escape) { escape = false; continue; }
                if (c == '\\') { escape = true; continue; }
                if (c == '"') { inString = false; }
                continue;
            }

            if (c == '"') { inString = true; continue; }

            if (c == '[')
            {
                if (depth == 0) start = i;
                depth++;
                continue;
            }

            if (c == ']')
            {
                depth--;
                if (depth == 0 && start >= 0)
                {
                    return answer.Substring(start, i - start + 1);
                }
            }
        }

        return null;
    }

    private static string Truncate(string s, int max)
        => string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s[..max] + "…");
}
