using System.Text.Json;
using Kor.Operations.Mcp.Ai;
using Kor.Opportunities.Knowledge.Search;
using Kor.Opportunities.Knowledge.Store;

namespace Kor.Operations.Mcp.Director;

public sealed class DirectorBriefGenerator
{
    private static readonly SemaphoreSlim RunLock = new(1, 1);
    private readonly Func<DirectorSection, CancellationToken, Task<string>> _facts;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<KnowledgeClaimHit>>> _search;
    private readonly Func<string, CancellationToken, Task<DirectorAnswer>> _ask;
    private readonly Func<DateTime, RichDirectorSection, CancellationToken, Task<long>> _insert;
    private readonly ILogger<DirectorBriefGenerator> _logger;

    public DirectorBriefGenerator(DirectorFacts facts, KnowledgeSearch search, AskService ask,
        DirectorBriefRepository repo, ILogger<DirectorBriefGenerator> logger)
        : this(facts.GetAsync, (query, ct) => search.SearchClaimsAsync(query, 12, ct: ct), async (prompt, ct) =>
        {
            var answer = await ask.AskAsync(new AskRequest(prompt) { UserUpn = "director-brief@kor" }, ct);
            return new DirectorAnswer(answer.Answer, answer.InputTokens, answer.OutputTokens, answer.ToolCallsExecuted);
        }, repo.InsertAsync, logger) { }

    // Delegates keep the exact production orchestration testable without a model or database.
    public DirectorBriefGenerator(Func<DirectorSection, CancellationToken, Task<string>> facts,
        Func<string, CancellationToken, Task<IReadOnlyList<KnowledgeClaimHit>>> search,
        Func<string, CancellationToken, Task<DirectorAnswer>> ask,
        Func<DateTime, RichDirectorSection, CancellationToken, Task<long>> insert, ILogger<DirectorBriefGenerator> logger)
    { _facts = facts; _search = search; _ask = ask; _insert = insert; _logger = logger; }

    public async Task GenerateForWeekAsync(DateTime weekOf, CancellationToken ct)
    {
        await RunLock.WaitAsync(ct);
        try
        {
            foreach (var section in Enum.GetValues<DirectorSection>())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var facts = await _facts(section, ct);
                    IReadOnlyList<KnowledgeClaimHit> claims;
                    try { claims = (await _search(DirectorBriefPrompts.ClaimsQuery(section), ct)).Take(12).ToArray(); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { _logger.LogWarning(ex, "Director claims unavailable for {Section}; using facts alone.", section); claims = Array.Empty<KnowledgeClaimHit>(); }
                    var answer = await _ask(DirectorBriefPrompts.Build(section, facts, claims), ct);
                    if (!TryParse(answer, section, claims, out var rich))
                    { _logger.LogWarning("Director section {Section} returned non-JSON or invalid answer; skipped.", section); continue; }
                    if (claims.Count == 0) rich = rich with { Body = rich.Body + " No applicable claims were available; this section uses facts alone." };
                    await _insert(weekOf, rich, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { _logger.LogError(ex, "Director section {Section} failed; skipping.", section); }
            }
        }
        finally { RunLock.Release(); }
    }

    public static bool TryParse(DirectorAnswer answer, DirectorSection section, IReadOnlyList<KnowledgeClaimHit> offered, out RichDirectorSection rich)
    {
        rich = new(section, "", "", null, Array.Empty<DirectorCitation>(), answer.InputTokens, answer.OutputTokens, answer.ToolCalls);
        foreach (var candidate in JsonObjects(answer.Answer ?? "").Reverse())
        {
            try
            {
                using var document = JsonDocument.Parse(candidate);
                var root = document.RootElement;
                if (!root.TryGetProperty("headline", out var h) || h.ValueKind != JsonValueKind.String
                    || !root.TryGetProperty("body", out var b) || b.ValueKind != JsonValueKind.String) continue;
                var headline = h.GetString(); var body = b.GetString();
                if (string.IsNullOrWhiteSpace(headline) || string.IsNullOrWhiteSpace(body) || headline.Length > 512) continue;
                string? recommendation = null;
                if (root.TryGetProperty("recommendation", out var r) && r.ValueKind != JsonValueKind.Null)
                { if (r.ValueKind != JsonValueKind.String) continue; recommendation = r.GetString(); }
                var citations = new List<DirectorCitation>();
                var known = offered.GroupBy(c => c.ClaimId).ToDictionary(g => g.Key, g => g.First());
                var seen = new HashSet<long>();
                if (!string.IsNullOrWhiteSpace(recommendation) && root.TryGetProperty("citations", out var ids) && ids.ValueKind == JsonValueKind.Array)
                    foreach (var id in ids.EnumerateArray())
                        if (id.ValueKind == JsonValueKind.Number && id.TryGetInt64(out var value) && seen.Add(value) && known.TryGetValue(value, out var claim))
                            citations.Add(new(claim.ClaimId, claim.PassageId, claim.Statement, claim.SourceTitle));
                rich = new(section, headline.Trim(), body.Trim(), string.IsNullOrWhiteSpace(recommendation) ? null : recommendation.Trim(), citations,
                    answer.InputTokens, answer.OutputTokens, answer.ToolCalls);
                return true;
            }
            catch (JsonException) { /* Try an earlier balanced object, as the COO parser does. */ }
        }
        return false;
    }

    private static IEnumerable<string> JsonObjects(string text)
    {
        for (var start = 0; start < text.Length; start++)
        {
            if (text[start] != '{') continue;
            var depth = 0; var quoted = false; var escaped = false;
            for (var i = start; i < text.Length; i++)
            {
                var ch = text[i];
                if (escaped) { escaped = false; continue; }
                if (quoted && ch == '\\') { escaped = true; continue; }
                if (ch == '"') { quoted = !quoted; continue; }
                if (quoted) continue;
                if (ch == '{') depth++;
                else if (ch == '}' && --depth == 0) { yield return text[start..(i + 1)]; start = i; break; }
            }
        }
    }
}
