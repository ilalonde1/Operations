#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
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
///               hone -> the verdict is unverified; we preserve its research
///               timestamp and serve the cached result, 0 tokens.
///   3. DUE    - stale AND new signal exists (or never honed) -> the ONLY case
///               that spends tokens. All DUE rows in one view are batched into a
///               single cheap Haiku call that re-derives verdict + status from
///               project-linked actions ALREADY in the database - not
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
    // Sonnet, not Haiku: the cost GATE (fresh/quiet/unhoned skip) keeps the
    // VOLUME of synthesis tiny, so we spend the per-call budget on ACCURACY - a
    // capable model that only moves a verdict when the new signals genuinely
    // warrant it. Accuracy over speed/cost, per direction 2026-07-10.
    private const string DefaultModel = "claude-sonnet-5";  // synthesis != research; accurate delta over DB signals
    private const int MaxDuePerCall = 6;   // batch cap - one call covers a whole view

    /// <summary>
    /// Wall-clock ceiling on synthesis for one view. The batch cap alone bounds
    /// the size of a CALL, not the number of them: a sector whose nightly honing
    /// has lapsed can have dozens of due rows, and the chunks run one after
    /// another with the report blocked behind them. K-12 Schools (561 projects,
    /// 72 due) sat on "Generating K-12 Schools report..." for over two minutes
    /// and had not finished - which reads as a report that never comes up.
    ///
    /// Past the budget the remaining rows keep their cached verdicts and get
    /// picked up by the next view or by the nightly pipeline. A view that is
    /// mostly-current NOW beats one that is perfectly current in five minutes.
    /// </summary>
    private static readonly TimeSpan DefaultViewBudget = TimeSpan.FromSeconds(20);

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

    // Unhoned = no prior honing row at all. On-demand does NOT touch these: a
    // cheap delta-synthesis needs a baseline verdict to update, and there is no
    // row to persist into (so they would re-fire on every view). First-pass
    // research is the nightly pipeline's job; on-demand only keeps EXISTING
    // verdicts current.
    public enum Freshness { Fresh, Quiet, Due, Unhoned }

    public sealed record ProjectFreshness(
        long MpiId, string ProjectName, string? Verdict, DateTimeOffset? HonedAtUtc,
        int AgeDays, int TtlDays, int NewSignalCount, Freshness Class,
        string? ExistingHoningJson, string? DeltaText,
        long? EnrichmentId, DateTimeOffset? EvidenceCutoffUtc, string EvidenceJson);

    /// <summary><paramref name="Deferred"/> counts due rows the view budget ran
    /// out before reaching; they keep their cached verdict for now.</summary>
    public sealed record EnsureResult(
        int Total, int Fresh, int Quiet, int Due, int Synthesized, int TokensSpentApprox,
        int Deferred = 0, int RejectedStale = 0);

    /// <summary>
    /// Classify each project (zero tokens), synthesize only the DUE ones (one
    /// batched call), persist, and return the run's cost breakdown. Safe to call
    /// on every view: when everything is FRESH/QUIET it makes no API call at all.
    /// </summary>
    public async Task<EnsureResult> EnsureFreshAsync(
        IReadOnlyList<long> mpiIds,
        CancellationToken ct,
        TimeSpan? viewBudget = null)
    {
        if (mpiIds is null || mpiIds.Count == 0)
        {
            return new EnsureResult(0, 0, 0, 0, 0, 0);
        }

        var budget = viewBudget ?? DefaultViewBudget;
        var clock = System.Diagnostics.Stopwatch.StartNew();

        var classified = await ClassifyAsync(mpiIds, ct).ConfigureAwait(false);
        var fresh = classified.Count(c => c.Class == Freshness.Fresh);
        var quiet = classified.Where(c => c.Class == Freshness.Quiet).ToList();
        var due = classified.Where(c => c.Class == Freshness.Due).ToList();

        // QUIET means only that directly linked project actions have no new records.
        // Preserve the research timestamp and cached verdict; neither is verified.

        // Spend the budget where it changes decisions: an URGENT or PURSUE
        // verdict going stale matters more than a DISCOVER one, and anything
        // deferred still shows its cached verdict.
        due = due
            .OrderBy(d => BdVerdicts.Rank(d.Verdict))
            .ThenByDescending(d => d.NewSignalCount)
            .ToList();

        var synthesized = 0;
        var tokens = 0;
        var deferred = 0;
        var rejectedStale = 0;
        foreach (var chunk in Chunk(due, MaxDuePerCall))
        {
            // Check BEFORE the call, never mid-flight: a half-finished batch
            // would spend the tokens and throw away the answer.
            if (clock.Elapsed >= budget)
            {
                deferred += chunk.Count;
                continue;
            }

            var (applied, approxTokens) = await SynthesizeAndPersistAsync(chunk, ct).ConfigureAwait(false);
            synthesized += applied.Updated;
            rejectedStale += applied.RejectedStale;
            tokens += approxTokens;
        }

        _logger?.LogInformation(
            "OnDemand synthesis: {Total} projects - {Fresh} fresh, {Quiet} quiet(0-tok), {Due} due, {Synth} synthesized, {Rejected} rejected as stale, {Deferred} deferred past the {Budget}s budget (~{Tok} tokens, {Elapsed}s).",
            classified.Count, fresh, quiet.Count, due.Count, synthesized, rejectedStale, deferred,
            (int)budget.TotalSeconds, tokens, (int)clock.Elapsed.TotalSeconds);

        return new EnsureResult(classified.Count, fresh, quiet.Count, due.Count, synthesized, tokens, deferred, rejectedStale);
    }

    // No defensible direct observation-to-MPI link is used here. Title similarity
    // is not identity: only actions carrying the resolved project FK are evidence.
    // Fetch one lookahead row so a timestamp tie at the batch boundary cannot be
    // accidentally acknowledged as a fully processed timestamp group.
    private const string ClassifySql = @"
DECLARE @readAtUtc datetimeoffset(7) = sysdatetimeoffset();
;WITH h AS (
  SELECT m.Id, m.ProjectName,
         eh.Id AS EnrichmentId, eh.LastRefreshAtUtc AS HonedAt, eh.ResultJson AS HoningJson,
         COALESCE(NULLIF(JSON_VALUE(eh.ResultJson,'$.honingPass.verdict'),''), NULLIF(JSON_VALUE(eh.ResultJson,'$.verdict'),'')) AS Verdict
  FROM opportunities.MajorProjectsInventory m
  OUTER APPLY (
     SELECT TOP 1 x.Id, x.LastRefreshAtUtc, x.ResultJson
     FROM opportunities.MajorProjectEnrichment x
     WHERE x.MajorProjectsInventoryId = m.Id AND x.ProviderName = N'ProjectBriefHoning'
     ORDER BY x.Id DESC) eh
  WHERE m.Id IN (/*PROJECT_IDS*/) AND m.RetiredAtUtc IS NULL
)
SELECT h.Id, h.ProjectName, h.Verdict, h.HonedAt, h.HoningJson,
  Ttl = CASE h.Verdict WHEN N'PURSUE_URGENT' THEN 7 WHEN N'PURSUE' THEN 30 ELSE 90 END,
  NewActions = (SELECT COUNT(*) FROM opportunities.IntelProjectAction a WHERE a.MajorProjectsInventoryId = h.Id AND (h.HonedAt IS NULL OR a.UpdatedAtUtc > h.HonedAt) AND a.UpdatedAtUtc <= @readAtUtc),
  EvidenceJson = (
     SELECT TOP (13) a.Id, a.UpdatedAtUtc, a.Recommendation
     FROM opportunities.IntelProjectAction a
     WHERE a.MajorProjectsInventoryId = h.Id AND (h.HonedAt IS NULL OR a.UpdatedAtUtc > h.HonedAt)
       AND a.UpdatedAtUtc <= @readAtUtc
     ORDER BY a.UpdatedAtUtc, a.Id
     FOR JSON PATH),
  h.EnrichmentId
FROM h ORDER BY h.Id;";

    /// <summary>Pure SQL cost gate - no tokens. Captures the exact assessment
    /// version and a bounded set of directly linked evidence for persistence.</summary>
    public async Task<IReadOnlyList<ProjectFreshness>> ClassifyAsync(IReadOnlyList<long> mpiIds, CancellationToken ct)
    {
        if (mpiIds.Count == 0) return Array.Empty<ProjectFreshness>();
        var idCsv = string.Join(",", mpiIds.Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var sql = ClassifySql.Replace("/*PROJECT_IDS*/", idCsv, StringComparison.Ordinal);
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
            // FOR JSON PATH over zero rows yields NULL, which is every fresh or quiet
            // project; GetString would throw and take the whole classify pass down.
            var evidenceJson = r.IsDBNull(7) ? "[]" : r.GetString(7);
            var signals = JsonSerializer.Deserialize<List<HoningSignal>>(evidenceJson) ?? new List<HoningSignal>();
            var newSignals = Math.Max(r.GetInt32(6), signals.Count);
            var evidence = SelectEvidence(honedAt, signals);
            long? enrichmentId = r.IsDBNull(8) ? null : r.GetInt64(8);

            // No prior honing row -> Unhoned (skipped; nightly first-pass owns it).
            Freshness cls;
            int age;
            if (honedAt is null || string.IsNullOrWhiteSpace(honingJson))
            {
                cls = Freshness.Unhoned;
                age = -1;
            }
            else
            {
                age = (int)Math.Max(0, (DateTimeOffset.UtcNow - honedAt.Value).TotalDays);
                var stale = age > ttl;
                cls = !stale ? Freshness.Fresh : (newSignals > 0 ? Freshness.Due : Freshness.Quiet);
            }

            results.Add(new ProjectFreshness(id, name, verdict, honedAt,
                age, ttl, newSignals, cls, honingJson, evidence.DeltaText, enrichmentId, evidence.CutoffUtc, evidenceJson));
        }
        return results;
    }

    internal sealed record HoningSignal(long Id, DateTimeOffset UpdatedAtUtc, string? Recommendation);
    internal sealed record EvidenceSelection(string DeltaText, DateTimeOffset? CutoffUtc);

    // A timestamp is consumed only when every fetched action at that timestamp
    // fits in the batch. If a tie exceeds the cap, retain the prior cutoff (or
    // the last complete earlier group). Nightly research can resolve large ties;
    // on-demand must never pretend the unseen tail was considered.
    internal static EvidenceSelection SelectEvidence(DateTimeOffset? priorCutoff, IReadOnlyList<HoningSignal> signals)
    {
        var ordered = signals.OrderBy(x => x.UpdatedAtUtc).ThenBy(x => x.Id).ToArray();
        var included = ordered.Take(12).ToArray();
        var firstOmitted = ordered.Skip(12).FirstOrDefault();
        var cutoff = included
            .Where(x => firstOmitted is null || x.UpdatedAtUtc < firstOmitted.UpdatedAtUtc)
            .Select(x => (DateTimeOffset?)x.UpdatedAtUtc).LastOrDefault() ?? priorCutoff;
        var text = string.Join("\n", included.Select(x =>
            $"[{x.UpdatedAtUtc:O}] action {x.Id}: {x.Recommendation}"));
        return new EvidenceSelection(text, cutoff);
    }

    private async Task<(SynthesisApplyResult applied, int approxTokens)> SynthesizeAndPersistAsync(
        IReadOnlyList<ProjectFreshness> due, CancellationToken ct)
    {
        if (due.Count == 0) return (new(0, 0), 0);
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger?.LogWarning("OnDemand synthesis skipped: no Anthropic API key configured.");
            return (new(0, 0), 0);
        }

        var sb = new StringBuilder();
        sb.AppendLine("You are updating BD verdicts. For each project you are given its PRIOR verdict (from a full research pass) plus the NEW signals ingested since. Update the verdict ONLY if the new signals genuinely warrant a change (e.g. a deadline passed, an award landed, a stage advanced); otherwise KEEP the prior verdict and only refresh the status wording to the current state. Never invent facts not present in the prior verdict or the new signals - if the signals are ambiguous, keep the prior verdict.");
        sb.AppendLine("Verdicts: PURSUE_URGENT (live deadline/imminent), PURSUE (open window), MONITOR (watching), DEAD (closed/lost/awarded away).");
        sb.AppendLine("Return ONLY JSON: {\"results\":[{\"id\":<int>,\"verdict\":\"...\",\"status\":\"<=280 chars, plain current state, no 'since June'/'changed' meta-framing\",\"changed\":true|false}]}");
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
            return (new(0, 0), 0);
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
        if (json is null) return (new(0, 0), approxTokens);

        var arr = JsonNode.Parse(json)?["results"]?.AsArray();
        var expected = due.ToDictionary(x => x.MpiId);
        var applied = await ApplySynthesisResultsAsync(arr, expected.Keys,
            (id, verdict, status, token) => PersistSynthesisAsync(expected[id], verdict, status, token), ct).ConfigureAwait(false);
        return (applied, approxTokens);
    }

    // Keep model output inside the requested batch and count confirmed writes,
    // not accepted-looking responses. The delegate also permits a no-DB gate.
    internal sealed record SynthesisApplyResult(int Updated, int RejectedStale);

    internal static async Task<SynthesisApplyResult> ApplySynthesisResultsAsync(JsonArray? arr,
        IReadOnlyCollection<long> requestedIds,
        Func<long, string, string?, CancellationToken, Task<bool>> persist,
        CancellationToken ct)
    {
        var updated = 0;
        var rejected = 0;
        var remaining = new HashSet<long>(requestedIds);
        if (arr is not null)
        {
            foreach (var item in arr)
            {
                var id = item?["id"]?.GetValue<long>() ?? 0;
                var verdict = BdVerdicts.Normalize(item?["verdict"]?.GetValue<string>());
                var status = item?["status"]?.GetValue<string>();
                if (id > 0 && verdict is not null && remaining.Remove(id))
                {
                    if (await persist(id, verdict, status, ct).ConfigureAwait(false)) updated++;
                    else rejected++;
                }
            }
        }
        return new(updated, rejected);
    }

    // BD-A02: a single conditional write checks both the captured row and its
    // content, and refuses if another provider row became latest. Locks exist
    // only for this statement, never across the model call. Legacy JSON still
    // gets its missing honingPass object before nested JSON_MODIFY calls.
    private const string PersistSynthesisSql = @"
UPDATE e WITH (UPDLOCK, HOLDLOCK)
SET e.ResultJson =
        JSON_MODIFY(JSON_MODIFY(
            CASE WHEN JSON_QUERY(e.ResultJson, '$.honingPass') IS NULL
                 THEN JSON_MODIFY(e.ResultJson, '$.honingPass', JSON_QUERY(N'{}'))
                 ELSE e.ResultJson END,
            '$.honingPass.verdict', @verdict),
            '$.honingPass.status', COALESCE(@status,
                                            JSON_VALUE(e.ResultJson,'$.honingPass.status'),
                                            JSON_VALUE(e.ResultJson,'$.status'))),
    e.LastRefreshAtUtc = @evidenceCutoffUtc,
    e.Notes = CONCAT(N'on-demand synthesis ', CONVERT(varchar(19), sysdatetimeoffset(), 126))
FROM opportunities.MajorProjectEnrichment e
WHERE e.MajorProjectsInventoryId = @id
  AND e.ProviderName = N'ProjectBriefHoning'
  AND e.Id = @expectedEnrichmentId
  AND e.LastRefreshAtUtc = @expectedRefreshAtUtc
  AND CONVERT(varbinary(max), e.ResultJson) = CONVERT(varbinary(max), @expectedJson)
  AND @evidenceCutoffUtc >= e.LastRefreshAtUtc
  AND NOT EXISTS (
      SELECT 1 FROM opportunities.MajorProjectEnrichment newer WITH (UPDLOCK, HOLDLOCK)
      WHERE newer.MajorProjectsInventoryId = e.MajorProjectsInventoryId
        AND newer.ProviderName = N'ProjectBriefHoning' AND newer.Id > e.Id)
  -- A late-committing action can carry a timestamp from before the read. Refuse
  -- to consume the interval if it now contains anything absent from the inputs.
  AND NOT EXISTS (
      SELECT 1 FROM opportunities.IntelProjectAction a WITH (UPDLOCK, HOLDLOCK)
      WHERE a.MajorProjectsInventoryId = @id
        AND a.UpdatedAtUtc > @expectedRefreshAtUtc AND a.UpdatedAtUtc <= @evidenceCutoffUtc
        AND NOT EXISTS (
            SELECT 1 FROM OPENJSON(@evidenceJson) WITH (
                Id bigint, UpdatedAtUtc datetimeoffset(7), Recommendation nvarchar(max)) seen
            WHERE seen.Id = a.Id AND seen.UpdatedAtUtc = a.UpdatedAtUtc
              AND (CONVERT(varbinary(max), seen.Recommendation) = CONVERT(varbinary(max), a.Recommendation)
                   OR (seen.Recommendation IS NULL AND a.Recommendation IS NULL))));";

    private async Task<bool> PersistSynthesisAsync(ProjectFreshness expected, string verdict, string? status, CancellationToken ct)
    {
        var normalizedVerdict = BdVerdicts.Normalize(verdict);
        if (normalizedVerdict is null || expected.EnrichmentId is null || expected.HonedAtUtc is null
            || expected.EvidenceCutoffUtc is null || string.IsNullOrWhiteSpace(expected.ExistingHoningJson))
            return false;

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(PersistSynthesisSql, con);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = expected.MpiId;
        cmd.Parameters.Add("@expectedEnrichmentId", SqlDbType.BigInt).Value = expected.EnrichmentId.Value;
        cmd.Parameters.Add("@expectedRefreshAtUtc", SqlDbType.DateTimeOffset).Value = expected.HonedAtUtc.Value;
        cmd.Parameters.Add("@expectedJson", SqlDbType.NVarChar, -1).Value = expected.ExistingHoningJson;
        cmd.Parameters.Add("@evidenceCutoffUtc", SqlDbType.DateTimeOffset).Value = expected.EvidenceCutoffUtc.Value;
        cmd.Parameters.Add("@evidenceJson", SqlDbType.NVarChar, -1).Value = expected.EvidenceJson;
        cmd.Parameters.Add("@verdict", SqlDbType.NVarChar, 40).Value = normalizedVerdict;
        cmd.Parameters.Add("@status", SqlDbType.NVarChar, 400).Value = (object?)status ?? DBNull.Value;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
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
