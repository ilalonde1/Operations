// ScoringDiff — freeze the real, uncapped opportunity corpus BEFORE changing rules.
// COMPARES: every ID's stored and recomputed score/tier, proposed score/tier,
// deterministic descending-score rank (ID tie-break), explanation factors, and
// gains/losses per distinct source and kind. Reports EVERY tier change as CSV.
// Uses vIslandApplications.IsBigTicket verbatim; does not duplicate its predicate.
// DOES NOT COMPARE: ingestion admission, absent/rejected opportunities, description
// correctness, missing applicant data, UI sorting, Deltek ODBC facts (Worker uses
// no facts accessor), or future feeds. It is not a business-value ground truth.
// SAME-CLASS MISS: a multi-residential project with no form in its latest title /
// description is invisible to a vocabulary change; missing text can make both
// arms agree on the wrong ranking. We report missing-description coverage separately.
// Baseline scores are frozen in the snapshot; they cannot silently adopt new rules.
// Read-only unless a later, explicit --apply mode is selected.
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Scoring;
using Microsoft.Data.SqlClient;

namespace Kor.ScoringDiff;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };

    public static async Task<int> Main(string[] args)
    {
        var snapshotPath = Arg(args, "--snapshot") ?? throw new ArgumentException("--snapshot <local.json> is required.");
        var output = Arg(args, "--out") ?? Path.Combine(Path.GetDirectoryName(snapshotPath)!, "report");
        Directory.CreateDirectory(output);
        if (args.Contains("--capture"))
        {
            if (File.Exists(snapshotPath)) throw new InvalidOperationException("Refusing to overwrite a frozen snapshot.");
            var connection = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
                ?? throw new InvalidOperationException("Set KOR_OPPORTUNITIES_OPPORTUNITIESDB in the process environment.");
            var captured = await Capture(connection);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(snapshotPath))!);
            await File.WriteAllTextAsync(snapshotPath, JsonSerializer.Serialize(captured, Json));
        }
        var snapshot = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(snapshotPath), Json)!;
        var scorer = new RuleBasedOpportunityScoringService(new Options(snapshot.HasProfile ? snapshot.Options : ScoringOptions.KorDefaults()), new NoFacts(), new FrozenClock(snapshot.CapturedAtUtc));
        var results = snapshot.Rows.Select(r => new Movement(r, scorer.Explain(r.Opportunity with
            { ScoringContext = new(r.Kind, r.Description) }))).ToArray();
        WriteReport(snapshot, results, output);
        if ((args.Contains("--verify") || args.Contains("--apply")) && !CheckContract(results, output)) return 1;
        if (args.Contains("--check-store") || args.Contains("--apply"))
            await LiveRescore.Run(snapshot, results, output, args.Contains("--apply"), args.Contains("--rollback"));
        if (args.Contains("--verify-live")) await LiveRescore.Verify(snapshot, results, output);
        return 0;
    }

    private static string? Arg(string[] args, string key)
    {
        var index = Array.IndexOf(args, key);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static async Task<Snapshot> Capture(string connection)
    {
        var profile = await new SqlScoringProfileStore(connection).LoadAsync(CancellationToken.None);
        var options = profile ?? ScoringOptions.KorDefaults();
        var capturedAt = DateTimeOffset.UtcNow;
        var scorer = new RuleBasedOpportunityScoringService(new Options(options), new NoFacts(), new FrozenClock(capturedAt));
        await using var con = new SqlConnection(connection);
        await con.OpenAsync();
        // One connection and four set-based reads; no per-opportunity round trips.
        var opportunities = await ReadJson<Opportunity>(con, "SELECT o.* FROM opportunities.Opportunities o WHERE o.DismissedAtUtc IS NULL ORDER BY o.Id FOR JSON PATH;");
        var descriptions = (await ReadJson<Observation>(con, """
            SELECT o.Id, d.Id ObservationId, d.Description
            FROM opportunities.Opportunities o
            OUTER APPLY (SELECT TOP (1) ob.Id, ob.Description FROM opportunities.OpportunityObservations ob
                         WHERE ob.OpportunityId=o.Id ORDER BY ob.Id DESC) d
            WHERE o.DismissedAtUtc IS NULL FOR JSON PATH;
            """)).ToDictionary(r => r.Id);
        var sources = (await ReadJson<Membership>(con, """
            SELECT DISTINCT ob.OpportunityId, s.Name, s.SourceType, m.ValueJson Metadata
            FROM opportunities.OpportunityObservations ob
            JOIN opportunities.Opportunities o ON o.Id=ob.OpportunityId AND o.DismissedAtUtc IS NULL
            JOIN opportunities.OpportunitySources s ON s.Id=ob.OpportunitySourceId
            LEFT JOIN opportunities.OpportunitySourceMappings m ON m.OpportunitySourceId=s.Id AND m.[Key]='bd.kind'
            FOR JSON PATH;
            """)).GroupBy(r => r.OpportunityId).ToDictionary(g => g.Key, g => g.ToArray());
        var island = (await ReadJson<Island>(con, "SELECT OpportunityId, IsBigTicket FROM opportunities.vIslandApplications FOR JSON PATH;"))
            .GroupBy(r => r.OpportunityId).ToDictionary(g => g.Key, g => g.All(r => r.IsBigTicket));
        var snapshot = new Snapshot { CapturedAtUtc = capturedAt, Options = options, HasProfile = profile is not null };
        foreach (var opportunity in opportunities)
        {
            var memberships = sources.GetValueOrDefault(opportunity.Id) ?? [];
            var kind = OpportunityKinds.Combine(memberships.Select(s => OpportunityKinds.FromSource(s.SourceType, s.Metadata)));
            var description = descriptions.GetValueOrDefault(opportunity.Id)?.Description;
            snapshot.Rows.Add(new CorpusRow
            {
                Opportunity = opportunity,
                Description = description,
                ObservationId = descriptions.GetValueOrDefault(opportunity.Id)?.ObservationId,
                Sources = memberships,
                Kind = kind,
                IsBigTicket = island.TryGetValue(opportunity.Id, out var big) ? big : null,
                Baseline = scorer.Explain(opportunity with { ScoringContext = new(kind, description) }),
            });
        }
        return snapshot;
    }

    private static async Task<List<T>> ReadJson<T>(SqlConnection con, string sql)
    {
        await using var command = new SqlCommand(sql, con) { CommandTimeout = 120 };
        await using var reader = await command.ExecuteReaderAsync();
        var json = new StringBuilder();
        while (await reader.ReadAsync()) json.Append(reader.GetString(0));
        return JsonSerializer.Deserialize<List<T>>(json.ToString(), Json)!;
    }

    private static void WriteReport(Snapshot snapshot, Movement[] rows, string directory)
    {
        var beforeRank = rows.OrderByDescending(r => r.Row.Baseline.Score).ThenBy(r => r.Row.Opportunity.Id)
            .Select((r, i) => (r.Row.Opportunity.Id, Rank: i + 1)).ToDictionary(r => r.Id, r => r.Rank);
        var afterRank = rows.OrderByDescending(r => r.After.Score).ThenBy(r => r.Row.Opportunity.Id)
            .Select((r, i) => (r.Row.Opportunity.Id, Rank: i + 1)).ToDictionary(r => r.Id, r => r.Rank);
        var summary = new StringBuilder($"# ScoringDiff — {snapshot.CapturedAtUtc:O}\n\n");
        summary.AppendLine("Compares scores, tiers, ranks and factors for the frozen corpus. Does not validate missing/incorrect source descriptions, rejected/absent rows, UI order, or Deltek ODBC facts. A multi-unit project with no form in its latest title/description can be ranked wrongly in both arms without this diff catching it.\n");
        summary.AppendLine($"Rows: {rows.Length}; descriptions absent: {rows.Count(r => string.IsNullOrWhiteSpace(r.Row.Description))}; baseline vs stored score/tier drift: {rows.Count(r => r.Row.Opportunity.RelevanceScore != r.Row.Baseline.Score || r.Row.Opportunity.RelevanceTier != r.Row.Baseline.Tier)}.");
        summary.AppendLine($"Score increases: {rows.Count(r => r.After.Score > r.Row.Baseline.Score)}; decreases: {rows.Count(r => r.After.Score < r.Row.Baseline.Score)}. Tier promotions: {rows.Count(r => (int)r.After.Tier > (int)r.Row.Baseline.Tier)}; demotions: {rows.Count(r => (int)r.After.Tier < (int)r.Row.Baseline.Tier)}.\n");
        summary.AppendLine("Source groups count an opportunity once per source; cross-posts can appear in multiple source groups. Kind groups partition IDs. Ranks are over the complete corpus, descending score then ID.\n");
        summary.AppendLine("| Group | Rows | Before avg | After avg | Before max | After max | Before mean rank | After mean rank | Score up/down | Tier up/down | Missing description |");
        summary.AppendLine("|---|---:|---:|---:|---:|---:|---:|---:|---|---|---:|");
        var groups = rows.GroupBy(r => "Kind: " + r.Row.Kind).Select(g => (g.Key, Rows: g.ToArray()))
            .Concat(rows.SelectMany(r => r.Row.Sources.Select(s => (s.Name, Row: r)))
                .GroupBy(r => r.Name).Select(g => (Key: "Source: " + g.Key, Rows: g.Select(r => r.Row).DistinctBy(r => r.Row.Opportunity.Id).ToArray())))
            .OrderBy(g => g.Key).ToArray();
        foreach (var (name, group) in groups)
        {
            summary.AppendLine(FormattableString.Invariant($"| {name} | {group.Length} | {group.Average(r => r.Row.Baseline.Score):F2} | {group.Average(r => r.After.Score):F2} | {group.Max(r => r.Row.Baseline.Score):F2} | {group.Max(r => r.After.Score):F2} | {group.Average(r => beforeRank[r.Row.Opportunity.Id]):F1} | {group.Average(r => afterRank[r.Row.Opportunity.Id]):F1} | {group.Count(r => r.After.Score > r.Row.Baseline.Score)}/{group.Count(r => r.After.Score < r.Row.Baseline.Score)} | {group.Count(r => (int)r.After.Tier > (int)r.Row.Baseline.Tier)}/{group.Count(r => (int)r.After.Tier < (int)r.Row.Baseline.Tier)} | {group.Count(r => string.IsNullOrWhiteSpace(r.Row.Description))} |"));
        }
        var small = rows.Where(r => r.Row.IsBigTicket == false).ToArray();
        summary.AppendLine($"\nExisting Island view, IsBigTicket=0: {small.Length}; score increases {small.Count(r => r.After.Score > r.Row.Baseline.Score)}, decreases {small.Count(r => r.After.Score < r.Row.Baseline.Score)}. This cohort is NOT a new single-family classifier; it includes TI/accessory/alteration rows and the view's documented ambiguous cases.");
        File.WriteAllText(Path.Combine(directory, "summary.md"), summary.ToString());
        const string header = "Id,Kind,Sources,Title,StoredScore,StoredTier,BeforeScore,AfterScore,BeforeTier,AfterTier,BeforeRank,AfterRank,IsBigTicket,Description,BeforeFactors,AfterFactors";
        var all = new List<string> { header };
        var changedTiers = new List<string> { header };
        foreach (var r in rows)
        {
            var row = string.Join(',', new object?[] { r.Row.Opportunity.Id, r.Row.Kind, string.Join(';', r.Row.Sources.Select(s => s.Name)), r.Row.Opportunity.Name,
                r.Row.Opportunity.RelevanceScore, r.Row.Opportunity.RelevanceTier, r.Row.Baseline.Score, r.After.Score, r.Row.Baseline.Tier, r.After.Tier,
                beforeRank[r.Row.Opportunity.Id], afterRank[r.Row.Opportunity.Id], r.Row.IsBigTicket, r.Row.Description,
                string.Join(';', r.Row.Baseline.Factors.Select(f => $"{f.Label}={f.Delta}")), string.Join(';', r.After.Factors.Select(f => $"{f.Label}={f.Delta}")) }.Select(Csv));
            all.Add(row);
            if (r.Row.Baseline.Tier != r.After.Tier || r.Row.Opportunity.RelevanceTier != r.After.Tier) changedTiers.Add(row);
        }
        File.WriteAllLines(Path.Combine(directory, "rows.csv"), all);
        File.WriteAllLines(Path.Combine(directory, "tier-changes.csv"), changedTiers);
        File.WriteAllText(Path.Combine(directory, "results.json"), JsonSerializer.Serialize(rows.Select(r => new { r.Row.Opportunity.Id, r.After }), Json));
        WriteStoredMovement(rows, directory);
        WriteChart(groups, directory);
        Console.WriteLine($"Rows={rows.Length}; score up={rows.Count(r => r.After.Score > r.Row.Baseline.Score)}, down={rows.Count(r => r.After.Score < r.Row.Baseline.Score)}; changed tiers={rows.Count(r => r.After.Tier != r.Row.Baseline.Tier)}; stored-tier changes={rows.Count(r => r.After.Tier != r.Row.Opportunity.RelevanceTier)}");
        Console.WriteLine($"Reports: {Path.GetFullPath(directory)}");
    }

    private static string Csv(object? value) => "\"" + (Convert.ToString(value, CultureInfo.InvariantCulture) ?? "").Replace("\"", "\"\"") + "\"";

    private static void WriteStoredMovement(Movement[] rows, string directory)
    {
        // The rule diff above recomputes both arms. The live write deliberately
        // leaves non-applications at their STORED values, even if old deadline /
        // profile drift would change them on a fresh legacy-rule calculation.
        decimal? Score(Movement r) => r.Row.Kind == OpportunityKind.Application ? r.After.Score : r.Row.Opportunity.RelevanceScore;
        RelevanceTier? Tier(Movement r) => r.Row.Kind == OpportunityKind.Application ? r.After.Tier : r.Row.Opportunity.RelevanceTier;
        var beforeRank = rows.OrderByDescending(r => r.Row.Opportunity.RelevanceScore).ThenBy(r => r.Row.Opportunity.Id)
            .Select((r, i) => (r.Row.Opportunity.Id, Rank: i + 1)).ToDictionary(r => r.Id, r => r.Rank);
        var afterRank = rows.OrderByDescending(Score).ThenBy(r => r.Row.Opportunity.Id)
            .Select((r, i) => (r.Row.Opportunity.Id, Rank: i + 1)).ToDictionary(r => r.Id, r => r.Rank);
        const string header = "Id,Kind,StoredBeforeScore,PersistedAfterScore,StoredBeforeTier,PersistedAfterTier,StoredBeforeRank,PersistedAfterRank";
        string Line(Movement r) => string.Join(',', new object?[] { r.Row.Opportunity.Id, r.Row.Kind,
            r.Row.Opportunity.RelevanceScore, Score(r), r.Row.Opportunity.RelevanceTier, Tier(r),
            beforeRank[r.Row.Opportunity.Id], afterRank[r.Row.Opportunity.Id] }.Select(Csv));
        File.WriteAllLines(Path.Combine(directory, "stored-movement.csv"), new[] { header }.Concat(rows.Select(Line)));
        File.WriteAllLines(Path.Combine(directory, "persisted-tier-changes.csv"), new[] { header }
            .Concat(rows.Where(r => r.Row.Opportunity.RelevanceTier != Tier(r)).Select(Line)));
        File.AppendAllText(Path.Combine(directory, "summary.md"),
            $"\nPlanned live write (applications only; preserves stored bids/awards/other): {rows.Count(r => Score(r) != r.Row.Opportunity.RelevanceScore || Tier(r) != r.Row.Opportunity.RelevanceTier)} rows; "
            + $"tier promotions {rows.Count(r => r.Row.Opportunity.RelevanceTier.HasValue && Tier(r) > r.Row.Opportunity.RelevanceTier)}, "
            + $"demotions {rows.Count(r => r.Row.Opportunity.RelevanceTier.HasValue && Tier(r) < r.Row.Opportunity.RelevanceTier)}, "
            + $"newly assigned tiers {rows.Count(r => !r.Row.Opportunity.RelevanceTier.HasValue && Tier(r).HasValue)}. "
            + "See stored-movement.csv for every stored-score rank change and persisted-tier-changes.csv for every tier write. The broader tier-changes.csv ALSO exposes existing stored-versus-recomputed drift; it is not the update list.\n");
    }

    // Evaluation cohorts only: positive form evidence from the gate vocabulary.
    // Never used to admit, hide, classify as small, or score a production row.
    private static readonly Regex NamedForm = new(@"\b(?:multi[- ]family|multifamily|multi[- ]residential|mixed[- ]uses?|apartments?|condominiums?|townhouses?|townhomes?|rowhouses?|row houses?|fourplex|sixplex|houseplex|multiplex|m-(?:cg|c1|c2|g|1|2|h1|h2|h3|x1|x2)|mu-[12]|h-go)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool HasForm(CorpusRow row) => NamedForm.IsMatch(row.Opportunity.Name + " " + row.Description);

    private static bool CheckContract(Movement[] rows, string directory)
    {
        var targets = rows.Where(r => r.Row.Kind == OpportunityKind.Application && HasForm(r.Row)).ToArray();
        var controls = rows.Where(r => r.Row.IsBigTicket == false
            && r.Row.Sources.Any(s => s.Name == "Saanich_DevelopmentApplications") && !HasForm(r.Row)).ToArray();
        // vIslandApplications itself warns that IsBigTicket=0 contains genuine
        // multi-unit proposals. Report all its gains, but do not pretend those
        // positively named forms are single-family controls.
        var changedBids = rows.Count(r => r.Row.Kind is OpportunityKind.Bid or OpportunityKind.Award
            && (r.After.Score != r.Row.Baseline.Score || r.After.Tier != r.Row.Baseline.Tier));
        var controlGains = controls.Count(r => r.After.Score > r.Row.Baseline.Score);
        var ceiling = controls.Max(r => r.After.Score);
        var under = targets.Count(r => r.After.Score < ceiling);
        var calgary = rows.Where(r => r.Row.Sources.Any(s => s.Name == "Calgary_LandUseRedesignation")).ToArray();
        var saanich = rows.Where(r => r.Row.Sources.Any(s => s.Name == "Saanich_DevelopmentApplications")).ToArray();
        var summary = $"Named multi-unit/mixed-form targets: {targets.Length}; Saanich controls (existing view small, no named multi-unit form): {controls.Length}.\n"
            + $"Bids/awards changed: {changedBids}; control score increases: {controlGains}.\n"
            + $"Control maximum: {ceiling}; target minimum: {targets.Min(r => r.After.Score)}; targets below control maximum: {under}.\n"
            + $"Calgary mean: {calgary.Average(r => r.Row.Baseline.Score):F2} -> {calgary.Average(r => r.After.Score):F2}; Saanich mean: {saanich.Average(r => r.Row.Baseline.Score):F2} -> {saanich.Average(r => r.After.Score):F2}.\n"
            + "These are corpus/ranking gates, not a new small-project filter. Both controls and the complete Island-small gains are in the row ledger.\n";
        File.WriteAllText(Path.Combine(directory, "contract.txt"), summary);
        File.WriteAllLines(Path.Combine(directory, "contract-cohorts.csv"), new[] { "Id,Cohort" }
            .Concat(targets.Select(r => $"{r.Row.Opportunity.Id},named-form"))
            .Concat(controls.Select(r => $"{r.Row.Opportunity.Id},saanich-view-small-no-named-form")));
        Console.Write(summary);
        return changedBids == 0 && controlGains == 0 && under == 0
            && calgary.Average(r => r.After.Score) > saanich.Average(r => r.After.Score);
    }

    private static void WriteChart((string Key, Movement[] Rows)[] groups, string directory)
    {
        var selected = groups.Where(g => g.Key.Contains("Calgary_") || g.Key.Contains("Saanich_") || g.Key.Contains("Esquimalt_") || g.Key.Contains("Vancouver_") || g.Key.Contains("PortMoody_")).ToArray();
        var html = new StringBuilder("<!doctype html><meta charset='utf-8'><title>ScoringDiff</title><style>body{font:18px Segoe UI;margin:40px;background:#f5f7fa;color:#203747}table{border-collapse:collapse}td,th{padding:12px;text-align:left}.bar{height:16px;background:#6b8292;margin:4px 0}.after{background:#007f72}</style><h1>Early-signal scoring</h1><p>Frozen corpus. Grey: before. Green: proposed. Bar scale: 10 pixels per score point.</p><table><tr><th>Source</th><th>Mean score</th><th>Before → after</th></tr>");
        foreach (var group in selected)
        {
            var before = group.Rows.Average(r => r.Row.Baseline.Score);
            var after = group.Rows.Average(r => r.After.Score);
            html.Append(CultureInfo.InvariantCulture, $"<tr><td>{WebUtility.HtmlEncode(group.Key.Replace("Source: ", ""))}</td><td><div class='bar' style='width:{Math.Max(0, before) * 10:F0}px'></div><div class='bar after' style='width:{Math.Max(0, after) * 10:F0}px'></div></td><td>{before:F2} → {after:F2}</td></tr>");
        }
        html.Append("</table><p>Missing descriptions and errors shared by both arms remain limits. See summary.md and the complete row/tier-change CSVs.</p>");
        File.WriteAllText(Path.Combine(directory, "chart.html"), html.ToString());
    }
}

internal sealed class Snapshot
{
    public DateTimeOffset CapturedAtUtc { get; set; }
    public bool HasProfile { get; set; }
    public ScoringOptions Options { get; set; } = new();
    public List<CorpusRow> Rows { get; set; } = [];
}
internal sealed class CorpusRow
{
    public Opportunity Opportunity { get; set; } = new();
    public OpportunityKind Kind { get; set; }
    public string? Description { get; set; }
    public long? ObservationId { get; set; }
    public Membership[] Sources { get; set; } = [];
    public bool? IsBigTicket { get; set; }
    public OpportunityScoreExplanation Baseline { get; set; } = new(0, RelevanceTier.Low, [], false);
}
internal sealed record Membership(long OpportunityId, string Name, OpportunitySourceType SourceType, string? Metadata);
internal sealed record Observation(long Id, long? ObservationId, string? Description);
internal sealed record Island(long OpportunityId, bool IsBigTicket);
internal sealed record Movement(CorpusRow Row, OpportunityScoreExplanation After);
internal sealed class Options(ScoringOptions options) : IScoringOptionsAccessor
{
    public ScoringOptions GetCurrent() => options;
    public ScoringOptions Update(ScoringOptions value) => throw new NotSupportedException("Frozen harness options.");
}
internal sealed class NoFacts : IDeltekClientFactsAccessor { public DeltekClientFactsSnapshot? GetFacts(string? clientId) => null; }
internal sealed class FrozenClock(DateTimeOffset timestamp) : TimeProvider { public override DateTimeOffset GetUtcNow() => timestamp; }
