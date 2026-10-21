// BdOpportunityDedup — merge opportunities that are ONE real RFP held under
// several sources, and refuse to merge anything it is not sure about.
//
// WHY THIS EXISTS. OpportunityKey is composed as
// <8-char source prefix>-<external reference>, so two portals carrying the same
// solicitation can never collide at ingest — by design, not by accident. The
// Leduc West Campus structural RFP was held twice, once from Alberta Purchasing
// Connection and once from leduc.bidsandtenders.ca, with the deadline on one
// copy and the higher relevance score on the other. Measured 2026-09-28: 244
// titles across 562 rows, about 7% of the active pipeline.
//
// 2026-10-20: the logic now lives in Kor.Opportunities.Data OpportunityDedup, shared with the Worker's nightly
// OpportunityDedupJob; this tool is its command line (dry run, plan file, --pair).
//
// OpportunityDuplicateScorer already exists and is tested, so this reuses it
// rather than growing a second matcher. That scorer guards MANUAL ENTRY in the
// app and is never called from IngestionService; this tool is the batch side of
// the same question.
//
//   BdOpportunityDedup                     dry run; writes a plan CSV
//   BdOpportunityDedup --commit            apply the plan
//   BdOpportunityDedup --min-similarity .95
//   BdOpportunityDedup --include-same-source   also group ONE source's repeats, but
//                                              only when buyer and deadline match
//
// DRY RUN IS THE DEFAULT. Nothing is written without --commit.
//
// WHAT IT WILL NOT TOUCH — a group is HELD, not merged, if any row in it:
//   * has a CrmEngagement (a live pursuit),
//   * has an owner assigned,
//   * has notes or a fee-proposal link (human work),
//   * disagrees with its siblings on Status — 16 of the 244 groups have one
//     copy New and one Lost, and merging those would either resurrect a dead
//     bid or bury a live one,
//   * scores below the similarity floor against the survivor.
// Held groups are listed in the plan with the reason, for a person to settle.
//
// WHAT IT DOES NOT COVER, and a person still has to look:
//   * it groups on EXACT normalised title. Two portals wording the same RFP
//     differently are not found. The floor check is belt-and-braces on top of
//     that, not a fuzzy search.
//   * it cannot tell a genuine re-issue from a duplicate. A city that cancels
//     and re-posts the same RFP under a new number looks identical here, and
//     the status gate is the only thing standing between that and a bad merge.
//   * PrimePipeline and vIslandApplications do not filter DismissedAtUtc, so
//     merging changes nothing for those two views. They were already counting
//     both copies and will carry on doing so until they are fixed separately.
using System.Data;
using System.Globalization;
using System.Text;
using Kor.Opportunities.Data.Opportunities;
using Microsoft.Data.SqlClient;

namespace Kor.BdOpportunityDedup;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var commit = false;
        var includeSameSource = false;
        var minSimilarity = 0.90;
        string? db = null;
        (long Keep, long Drop)? pair = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--commit":
                    commit = true;
                    break;
                case "--include-same-source":
                    includeSameSource = true;
                    break;
                case "--db" when i + 1 < args.Length:
                    db = args[++i];
                    break;
                case "--min-similarity" when i + 1 < args.Length:
                    minSimilarity = double.Parse(args[++i], CultureInfo.InvariantCulture);
                    break;
                case "--pair" when i + 2 < args.Length:
                    pair = (long.Parse(args[++i], CultureInfo.InvariantCulture), long.Parse(args[++i], CultureInfo.InvariantCulture));
                    break;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        db ??= Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
        if (string.IsNullOrWhiteSpace(db))
        {
            Console.Error.WriteLine("Set KOR_OPPORTUNITIES_OPPORTUNITIESDB or pass --db.");
            return 2;
        }

        Console.WriteLine($"Mode: {(commit ? "COMMIT" : "dry-run")}  |  similarity floor {minSimilarity:0.00}");
        Console.WriteLine();

        await using var con = new SqlConnection(db);
        await con.OpenAsync().ConfigureAwait(false);

        var rows = await OpportunityDedup.LoadAsync(con).ConfigureAwait(false);
        Console.WriteLine($"active opportunities considered: {rows.Count}");

        // --pair KEEP DROP (2026-10-20): a person has settled one held group, e.g. Leduc AB-2026-06617 carried by two
        // APC feeds where the kept copy holds the pursuit. The DROPPED row must still carry no pursuit, owner or human
        // work, and the two must share a title core; everything else is the person's call, named by Id.
        if (pair is { } p0)
        {
            var keep = rows.SingleOrDefault(r => r.Id == p0.Keep);
            var drop = rows.SingleOrDefault(r => r.Id == p0.Drop);
            var refusal = keep is null || drop is null ? "both Ids must be active opportunities"
                : drop.HasPursuit || drop.HasOwner || drop.HasHumanWork ? "the dropped row carries a pursuit, an owner or human work"
                : keep.NormName != drop.NormName ? $"titles differ: \"{keep.NormName}\" / \"{drop.NormName}\""
                : null;
            if (refusal is not null) { Console.Error.WriteLine($"--pair refused: {refusal}"); return 3; }
            Console.WriteLine($"PAIR: keep {keep!.OpportunityKey} <- drop {drop!.OpportunityKey}  {keep.Name}");
            if (!commit) { Console.WriteLine("Dry run. Re-run with --commit to apply."); return 0; }
            var error = await OpportunityDedup.MergeAsync(con, new OpportunityDedup.Plan(keep, drop), Actor).ConfigureAwait(false);
            Console.WriteLine(error is null ? "COMMITTED: 1 row(s) merged." : $"merge FAILED: {error}");
            return error is null ? 0 : 1;
        }

        var outcome = OpportunityDedup.BuildPlan(rows, includeSameSource, minSimilarity);
        Console.WriteLine(includeSameSource
            ? $"groups holding one title (several sources, or one source with a shared buyer+deadline): {outcome.GroupsConsidered}"
            : $"groups holding one title under several sources: {outcome.GroupsConsidered}");
        Console.WriteLine();

        Report(outcome.Plans, outcome.Held);
        WritePlanFile(outcome.Plans, outcome.Held);

        if (!commit)
        {
            Console.WriteLine();
            Console.WriteLine("Dry run. Nothing was written. Re-run with --commit to apply.");
            return 0;
        }

        var merged = 0;
        foreach (var p in outcome.Plans)
        {
            var error = await OpportunityDedup.MergeAsync(con, p, Actor).ConfigureAwait(false);
            if (error is null) merged++;
            else Console.Error.WriteLine($"  merge FAILED for {p.Loser.OpportunityKey}: {error}");
        }

        Console.WriteLine();
        Console.WriteLine($"COMMITTED: {merged} row(s) merged into {outcome.Plans.Select(p => p.Survivor.Id).Distinct().Count()} survivor(s).");
        return 0;
    }

    private const string Actor = "BdOpportunityDedup";

    private static void Report(IReadOnlyList<OpportunityDedup.Plan> plans, IReadOnlyList<OpportunityDedup.Held> held)
    {
        Console.WriteLine($"MERGE PLAN: {plans.Count} row(s) would be merged into "
                          + $"{plans.Select(p => p.Survivor.Id).Distinct().Count()} survivor(s).");
        foreach (var p in plans.Take(12))
        {
            Console.WriteLine($"   keep {p.Survivor.OpportunityKey,-28} <- drop {p.Loser.OpportunityKey,-28} "
                              + $"{Trim(p.Survivor.Name, 46)}");
        }

        if (plans.Count > 12)
        {
            Console.WriteLine($"   … and {plans.Count - 12} more (see the plan file)");
        }

        Console.WriteLine();
        Console.WriteLine($"HELD FOR A PERSON: {held.Count} group(s)");
        foreach (var h in held.GroupBy(x => x.Reason).OrderByDescending(x => x.Count()))
        {
            Console.WriteLine($"   {h.Count(),4}  {h.Key}");
        }
    }

    private static void WritePlanFile(IReadOnlyList<OpportunityDedup.Plan> plans, IReadOnlyList<OpportunityDedup.Held> held)
    {
        var dir = ResolveOutputDirectory();
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "opportunity-dedup-plan.csv");

        var sb = new StringBuilder();
        sb.AppendLine("action,survivorKey,loserKey,status,survivorScore,loserScore,title,reason");
        foreach (var p in plans)
        {
            sb.AppendLine(string.Join(',',
                "merge", Csv(p.Survivor.OpportunityKey), Csv(p.Loser.OpportunityKey),
                p.Survivor.Status.ToString(CultureInfo.InvariantCulture),
                (p.Survivor.RelevanceScore ?? 0).ToString(CultureInfo.InvariantCulture),
                (p.Loser.RelevanceScore ?? 0).ToString(CultureInfo.InvariantCulture),
                Csv(p.Survivor.Name), ""));
        }

        foreach (var h in held)
        {
            sb.AppendLine(string.Join(',', "hold", "", "", "", "", "", Csv(h.NormName), Csv(h.Reason)));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        Console.WriteLine();
        Console.WriteLine($"plan written: {path}");
    }

    private static string ResolveOutputDirectory()
    {
        var asmDir = Path.GetDirectoryName(typeof(Program).Assembly.Location) ?? Directory.GetCurrentDirectory();
        var probe = new DirectoryInfo(asmDir);
        while (probe is not null)
        {
            if (Directory.Exists(Path.Combine(probe.FullName, ".git")))
            {
                return Path.Combine(probe.FullName, "tools", "BdOpportunityDedup", "output");
            }

            probe = probe.Parent;
        }

        return asmDir;
    }

    private static string Csv(string s)
        => "\"" + (s ?? "").Replace("\"", "\"\"") + "\"";

    private static string Trim(string s, int n)
        => s.Length <= n ? s : s[..n];
}
