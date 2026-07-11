#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Kor.Opportunities.Data.BdReports;

/// <summary>
/// View-time verdict freshness, done cheaply. When a sector report / project is
/// opened, this keeps the honing verdict current WITHOUT the nightly batch
/// pre-honing the whole inventory (most of which rots unviewed).
///
/// The token budget is controlled by construction, in priority order:
///   1. FRESH  - honed within its verdict-class TTL -> served from cache, 0 tokens.
///   2. QUIET  - stale by the clock, but NO new signal has landed since the last
///               hone -> the verdict is still true; we bump its "verified current"
///               timestamp and serve it, 0 tokens.
///   3. DUE    - stale AND new signal exists (or never honed) -> the ONLY case
///               that spends tokens. All DUE rows in one view are batched into a
///               single cheap Haiku call that re-derives verdict + status from
///               data ALREADY in the database (signals/actions/tenders) - not
///               fresh web research. Web research stays on the nightly path.
///
/// Net effect: opening a sector where nothing changed is free; only genuinely
/// moved pursuits cost one small batched call. Mirrors the Haiku batch pattern
/// in <see cref="Kor.Opportunities.Data.Awards.NewsMentionClassifier"/>.
/// </summary>
public sealed class OnDemandHoningSynthesizer
{
    private const string AnthropicEndpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string DefaultModel = "claude-haiku-4-5-20251001";  // cheapest tier; synthesis != research
    private const int MaxDuePerCall = 6;   // batch cap - one call covers a whole view

    private readonly string _connectionString;
    private readonly HttpClient _http;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly ILogger<OnDemandHoningSynthesizer>? _logger;

    public OnDemandHoningSynthesizer(
        string connectionString,
        HttpClient http,
        string apiKey,
        string? model = null,
        ILogger<OnDemandHoningSynthesizer>? logger = null)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _apiKey = apiKey ?? string.Empty;
        _model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model!;
        _logger = logger;
    }

    public enum Freshness { Fresh, Quiet, Due }

    public sealed record ProjectFreshness(
        long MpiId, string ProjectName, string? Verdict, DateTimeOffset? HonedAtUtc,
        int AgeDays, int TtlDays, int NewSignalCount, Freshness Class,
        string? ExistingHoningJson, string? DeltaText);

    public sealed record EnsureResult(int Total, int Fresh, int Quiet, int Due, int Synthesized, int TokensSpentApprox);

    /// <summary>
    /// Classify each project (zero tokens), synthesize only the DUE ones (one
    /// batched call), persist, and return the run's cost breakdown. Safe to call
    /// on every view: when everything is FRESH/QUIET it makes no API call at all.
    /// </summary>
    public async Task<EnsureResult> EnsureFreshAsync(IReadOnlyList<long> mpiIds, CancellationToken ct)
    {
        if (mpiIds is null || mpiIds.Count == 0)
        {
            return new EnsureResult(0, 0, 0, 0, 0, 0);
        }

        var classified = await ClassifyAsync(mpiIds, ct).ConfigureAwait(false);
        var fresh = classified.Count(c => c.Class == Freshness.Fresh);
        var quiet = classified.Where(c => c.Class == Freshness.Quiet).ToList();
        var due = classified.Where(c => c.Class == Freshness.Due).ToList();

        // QUIET: nothing changed since the last hone -> the verdict is still
        // true. Just mark it verified-current so it stops re-triggering. 0 tokens.
        foreach (var q in quiet)
        {
            await TouchVerifiedAsync(q.MpiId, ct).ConfigureAwait(false);
        }

        var synthesized = 0;
        var tokens = 0;
        foreach (var chunk in Chunk(due, MaxDuePerCall))
        {
            var (updated, approxTokens) = await SynthesizeAndPersistAsync(chunk, ct).ConfigureAwait(false);
            synthesized += updated;
            tokens += approxTokens;
        }

        _logger?.LogInformation(
            "OnDemand synthesis: {Total} projects - {Fresh} fresh, {Quiet} quiet(0-tok), {Due} due, {Synth} synthesized (~{Tok} tokens).",
            classified.Count, fresh, quiet.Count, due.Count, synthesized, tokens);

        return new EnsureResult(classified.Count, fresh, quiet.Count, due.Count, synthesized, tokens);
    }

    /// <summary>Pure SQL cost gate - no tokens. Returns each project's freshness
    /// class and, for stale rows, the new-signal delta text used for synthesis.</summary>
    public async Task<IReadOnlyList<ProjectFreshness>> ClassifyAsync(IReadOnlyList<long> mpiIds, CancellationToken ct)
    {
        var idCsv = string.Join(",", mpiIds.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var sql = $@"
;WITH h AS (
  SELECT m.Id, m.ProjectName,
         eh.LastRefreshAtUtc AS HonedAt, eh.ResultJson AS HoningJson,
         COALESCE(NULLIF(JSON_VALUE(eh.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(eh.ResultJson,'$.verdict'),'')) AS Verdict
  FROM opportunities.MajorProjectsInventory m
  OUTER APPLY (
     SELECT TOP 1 x.LastRefreshAtUtc, x.ResultJson
     FROM opportunities.MajorProjectEnrichment x
     WHERE x.MajorProjectsInventoryId = m.Id AND x.ProviderName = N'ProjectBriefHoning' AND x.ResultJson IS NOT NULL
     ORDER BY x.Id DESC) eh
  WHERE m.Id IN ({idCsv}) AND m.RetiredAtUtc IS NULL
)
SELECT h.Id, h.ProjectName, h.Verdict, h.HonedAt, h.HoningJson,
  Ttl = CASE h.Verdict WHEN N'PURSUE_URGENT' THEN 7 WHEN N'PURSUE' THEN 30 ELSE 90 END,
  NewActions = (SELECT COUNT(*) FROM opportunities.IntelProjectAction a WHERE a.MajorProjectsInventoryId = h.Id AND (h.HonedAt IS NULL OR a.UpdatedAtUtc > h.HonedAt)),
  NewObs = (SELECT COUNT(*) FROM opportunities.OpportunityObservations o WHERE (h.HonedAt IS NULL OR o.IngestedAtUtc > h.HonedAt) AND o.Title LIKE '%' + LEFT(h.ProjectName, 14) + '%'),
  DeltaText = (
     SELECT STRING_AGG(CONVERT(nvarchar(max), d.Line), CHAR(10)) FROM (
        SELECT TOP 12 CONCAT('[', CONVERT(varchar(10), o.IngestedAtUtc, 23), '] tender: ', LEFT(o.Title,140)) AS Line
        FROM opportunities.OpportunityObservations o
        WHERE (h.HonedAt IS NULL OR o.IngestedAtUtc > h.HonedAt) AND o.Title LIKE '%' + LEFT(h.ProjectName, 14) + '%'
        UNION ALL
        SELECT TOP 12 CONCAT('[', CONVERT(varchar(10), a.UpdatedAtUtc, 23), '] action: ', LEFT(a.Recommendation,140))
        FROM opportunities.IntelProjectAction a
        WHERE a.MajorProjectsInventoryId = h.Id AND (h.HonedAt IS NULL OR a.UpdatedAtUtc > h.HonedAt)
     ) d)
FROM h ORDER BY h.Id;";

        var results = new List<ProjectFreshness>();
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = r.GetInt64(0);
            var name = r.GetString(1);
            var verdict = r.IsDBNull(2) ? null : r.GetString(2);
            DateTimeOffset? honedAt = r.IsDBNull(3) ? null : r.GetDateTimeOffset(3);
            var honingJson = r.IsDBNull(4) ? null : r.GetString(4);
            var ttl = r.GetInt32(5);
            var newSignals = r.GetInt32(6) + r.GetInt32(7);
            var delta = r.IsDBNull(8) ? null : r.GetString(8);

            var age = honedAt is null ? int.MaxValue : (int)Math.Max(0, (DateTimeOffset.UtcNow - honedAt.Value).TotalDays);
            var stale = honedAt is null || age > ttl;
            var cls = !stale ? Freshness.Fresh : (newSignals > 0 || honedAt is null ? Freshness.Due : Freshness.Quiet);

            results.Add(new ProjectFreshness(id, name, verdict, honedAt,
                age == int.MaxValue ? -1 : age, ttl, newSignals, cls, honingJson, delta));
        }
        return results;
    }

    private async Task<(int updated, int approxTokens)> SynthesizeAndPersistAsync(
        IReadOnlyList<ProjectFreshness> due, CancellationToken ct)
    {
        if (due.Count == 0) return (0, 0);
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger?.LogWarning("OnDemand synthesis skipped: no Anthropic API key configured.");
            return (0, 0);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Re-derive the current BD verdict for each project below from its PRIOR verdict plus the NEW signals since it was last assessed. Do not invent facts beyond the signals. For each, output the verdict that best fits now.");
        sb.AppendLine("Verdicts: PURSUE_URGENT, PURSUE, MONITOR, DEAD.");
        sb.AppendLine("Return ONLY JSON: {\"results\":[{\"id\":<int>,\"verdict\":\"...\",\"status\":\"<=280 chars, plain current state, no 'since June' framing\"}]}");
        sb.AppendLine();
        foreach (var p in due)
        {
            var prior = p.Verdict ?? "(never assessed)";
            var priorStatus = TryExtractStatus(p.ExistingHoningJson);
            sb.AppendLine($"PROJECT id={p.MpiId}: {p.ProjectName}");
            sb.AppendLine($"  prior verdict: {prior}");
            if (!string.IsNullOrWhiteSpace(priorStatus)) sb.AppendLine($"  prior status: {priorStatus}");
            sb.AppendLine($"  NEW signals since last assessment:\n{(string.IsNullOrWhiteSpace(p.DeltaText) ? "  (none - keep prior verdict, refresh wording only)" : p.DeltaText)}");
            sb.AppendLine();
        }

        var body = new
        {
            model = _model,
            max_tokens = 1200,
            system = "You are a terse BD analyst. Output only the requested JSON.",
            messages = new object[] { new { role = "user", content = sb.ToString() } },
        };
        using var req = new HttpRequestMessage(HttpMethod.Post, AnthropicEndpoint) { Content = JsonContent.Create(body) };
        req.Headers.Add("x-api-key", _apiKey);
        req.Headers.Add("anthropic-version", AnthropicVersion);

        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        var respBody = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            _logger?.LogWarning("OnDemand synthesis API {Code}: {Body}", (int)resp.StatusCode, respBody);
            return (0, 0);
        }

        var root = JsonNode.Parse(respBody);
        var approxTokens = (root?["usage"]?["input_tokens"]?.GetValue<int>() ?? 0)
                         + (root?["usage"]?["output_tokens"]?.GetValue<int>() ?? 0);
        var text = new StringBuilder();
        foreach (var node in root?["content"]?.AsArray() ?? new JsonArray())
        {
            if (node?["type"]?.GetValue<string>() == "text") text.Append(node?["text"]?.GetValue<string>() ?? "");
        }
        var json = ExtractJsonObject(text.ToString());
        if (json is null) return (0, approxTokens);

        var updated = 0;
        var arr = JsonNode.Parse(json)?["results"]?.AsArray();
        if (arr is not null)
        {
            foreach (var item in arr)
            {
                var id = item?["id"]?.GetValue<long>() ?? 0;
                var verdict = item?["verdict"]?.GetValue<string>();
                var status = item?["status"]?.GetValue<string>();
                if (id > 0 && !string.IsNullOrWhiteSpace(verdict))
                {
                    await PersistSynthesisAsync(id, verdict!, status, ct).ConfigureAwait(false);
                    updated++;
                }
            }
        }
        return (updated, approxTokens);
    }

    private async Task PersistSynthesisAsync(long mpiId, string verdict, string? status, CancellationToken ct)
    {
        const string sql = @"
UPDATE e
SET e.ResultJson = JSON_MODIFY(JSON_MODIFY(e.ResultJson, '$.honingPass.verdict', @verdict),
                               '$.honingPass.status', COALESCE(@status, JSON_VALUE(e.ResultJson,'$.honingPass.status'))),
    e.LastRefreshAtUtc = sysdatetimeoffset(),
    e.Notes = CONCAT(N'on-demand synthesis ', CONVERT(varchar(19), sysdatetimeoffset(), 126))
FROM opportunities.MajorProjectEnrichment e
JOIN (SELECT MajorProjectsInventoryId, MAX(Id) MaxId FROM opportunities.MajorProjectEnrichment
      WHERE ProviderName = N'ProjectBriefHoning' GROUP BY MajorProjectsInventoryId) l ON l.MaxId = e.Id
WHERE e.MajorProjectsInventoryId = @id;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
        cmd.Parameters.Add("@verdict", SqlDbType.NVarChar, 40).Value = verdict;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 400).Value = (object?)status ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private async Task TouchVerifiedAsync(long mpiId, CancellationToken ct)
    {
        const string sql = @"
UPDATE e SET e.LastRefreshAtUtc = sysdatetimeoffset()
FROM opportunities.MajorProjectEnrichment e
JOIN (SELECT MajorProjectsInventoryId, MAX(Id) MaxId FROM opportunities.MajorProjectEnrichment
      WHERE ProviderName = N'ProjectBriefHoning' GROUP BY MajorProjectsInventoryId) l ON l.MaxId = e.Id
WHERE e.MajorProjectsInventoryId = @id;";
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = mpiId;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static string? TryExtractStatus(string? honingJson)
    {
        if (string.IsNullOrWhiteSpace(honingJson)) return null;
        try { return JsonNode.Parse(honingJson)?["honingPass"]?["status"]?.GetValue<string>(); }
        catch { return null; }
    }

    private static string? ExtractJsonObject(string s)
    {
        var start = s.IndexOf('{');
        var end = s.LastIndexOf('}');
        return (start >= 0 && end > start) ? s.Substring(start, end - start + 1) : null;
    }

    private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> src, int size)
    {
        for (var i = 0; i < src.Count; i += size)
            yield return src.Skip(i).Take(size).ToList();
    }
}
