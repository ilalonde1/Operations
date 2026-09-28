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
// OpportunityDuplicateScorer already exists and is tested, so this reuses it
// rather than growing a second matcher. That scorer guards MANUAL ENTRY in the
// app and is never called from IngestionService; this tool is the batch side of
// the same question.
//
//   BdOpportunityDedup                     dry run; writes a plan CSV
//   BdOpportunityDedup --commit            apply the plan
//   BdOpportunityDedup --min-similarity .95
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
    private const int CommandTimeoutSeconds = 180;

    private static async Task<int> Main(string[] args)
    {
        var commit = false;
        var minSimilarity = 0.90;
        string? db = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--commit":
                    commit = true;
                    break;
                case "--db" when i + 1 < args.Length:
                    db = args[++i];
                    break;
                case "--min-similarity" when i + 1 < args.Length:
                    minSimilarity = double.Parse(args[++i], CultureInfo.InvariantCulture);
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

        var rows = await LoadAsync(con).ConfigureAwait(false);
        Console.WriteLine($"active opportunities considered: {rows.Count}");

        var groups = rows
            .GroupBy(r => r.NormName, StringComparer.Ordinal)
            .Where(g => g.Select(r => r.SourcePrefix).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .ToList();
        Console.WriteLine($"groups holding one title under several sources: {groups.Count}");
        Console.WriteLine();

        var plans = new List<Plan>();
        var held = new List<Held>();

        foreach (var g in groups)
        {
            var members = g.ToList();

            var reason = GateGroup(members, minSimilarity, out var survivor);
            if (reason is not null)
            {
                held.Add(new Held(g.Key, members.Count, reason));
                continue;
            }

            foreach (var loser in members.Where(m => m.Id != survivor!.Id))
            {
                plans.Add(new Plan(survivor!, loser));
            }
        }

        Report(plans, held);
        WritePlanFile(plans, held);

        if (!commit)
        {
            Console.WriteLine();
            Console.WriteLine("Dry run. Nothing was written. Re-run with --commit to apply.");
            return 0;
        }

        var merged = 0;
        foreach (var p in plans)
        {
            merged += await MergeAsync(con, p).ConfigureAwait(false);
        }

        Console.WriteLine();
        Console.WriteLine($"COMMITTED: {merged} row(s) merged into {plans.Select(p => p.Survivor.Id).Distinct().Count()} survivor(s).");
        return 0;
    }

    /// <summary>
    /// Returns null when the group is safe to merge (and sets survivor), or the
    /// reason it is being held.
    /// </summary>
    private static string? GateGroup(List<Row> members, double minSimilarity, out Row? survivor)
    {
        survivor = null;

        if (members.Any(m => m.HasPursuit))
        {
            return "a pursuit (CrmEngagement) is linked";
        }

        if (members.Any(m => m.HasOwner))
        {
            return "an owner is assigned";
        }

        if (members.Any(m => m.HasHumanWork))
        {
            return "notes or a fee-proposal link exist";
        }

        if (members.Select(m => m.Status).Distinct().Count() > 1)
        {
            return "copies disagree on Status — one may be live and one dead";
        }

        // Survivor: highest relevance, then the one that actually carries a
        // deadline, then the oldest row. A deadline is the field most often
        // missing on one side and the one people act on.
        var pick = members
            .OrderByDescending(m => m.RelevanceScore ?? decimal.MinValue)
            .ThenByDescending(m => m.HasDeadline)
            .ThenBy(m => m.Id)
            .First();
        survivor = pick;

        foreach (var other in members.Where(m => m.Id != pick.Id))
        {
            var sim = OpportunityDuplicateScorer.NameSimilarity(pick.Name, other.Name);
            if (sim < minSimilarity)
            {
                return $"similarity {sim:0.00} below the {minSimilarity:0.00} floor";
            }
        }

        return null;
    }

    private static async Task<int> MergeAsync(SqlConnection con, Plan p)
    {
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync().ConfigureAwait(false);
        try
        {
            // 1. Fill the survivor's gaps from the loser. COALESCE only — a value
            //    already on the survivor is never overwritten.
            await Exec(con, tx, @"
UPDATE k SET
    SubmissionDeadlineUtc = COALESCE(k.SubmissionDeadlineUtc, d.SubmissionDeadlineUtc),
    ProjectCity           = COALESCE(k.ProjectCity,     d.ProjectCity),
    ProjectProvince       = COALESCE(k.ProjectProvince, d.ProjectProvince),
    EstimatedValue        = COALESCE(k.EstimatedValue,  d.EstimatedValue),
    RfpReleaseDate        = COALESCE(k.RfpReleaseDate,  d.RfpReleaseDate),
    ProjectAddress        = COALESCE(k.ProjectAddress,  d.ProjectAddress),
    BuyerContactName      = COALESCE(k.BuyerContactName,  d.BuyerContactName),
    BuyerContactEmail     = COALESCE(k.BuyerContactEmail, d.BuyerContactEmail),
    BuyerContactPhone     = COALESCE(k.BuyerContactPhone, d.BuyerContactPhone),
    UpdatedAtUtc          = SYSDATETIMEOFFSET(),
    UpdatedBy             = N'BdOpportunityDedup'
FROM opportunities.Opportunities k
CROSS JOIN opportunities.Opportunities d
WHERE k.Id = @keep AND d.Id = @drop;", p).ConfigureAwait(false);

            // 2. Move everything that points at the loser.
            //
            // ⚠ THREE CHILD TABLES CARRY A UNIQUE INDEX ON (OpportunityId, X),
            //   and two copies of one RFP routinely hold the SAME X — the same
            //   document URL, the same interested firm. A blind repoint then
            //   violates the index and the whole merge rolls back: 45 of 301 on
            //   the first commit run, every one of them on
            //   UX_Opp_Docs_Opp_Url. The colliding row on the loser is genuinely
            //   redundant (identical URL, identical firm, already present on the
            //   survivor), so it is deleted rather than carried across.
            foreach (var (table, uniqueCol) in new[]
                     {
                         ("OpportunityDocuments", "DocumentUrl"),
                         ("OpportunityInterestedFirms", "RawFirmName"),
                         ("OpportunityFeeProposalLinks", "FeeProposalId"),
                     })
            {
                await Exec(con, tx, $@"
DELETE d FROM opportunities.{table} d
WHERE d.OpportunityId = @drop
  AND EXISTS (SELECT 1 FROM opportunities.{table} k
              WHERE k.OpportunityId = @keep AND k.{uniqueCol} = d.{uniqueCol});", p).ConfigureAwait(false);
            }

            foreach (var t in new[]
                     {
                         "OpportunityObservations", "OpportunityDocuments", "OpportunityNotes",
                         "OpportunityFiles", "OpportunityInterestedFirms", "OpportunityFeeProposalLinks",
                     })
            {
                await Exec(con, tx,
                    $"UPDATE opportunities.{t} SET OpportunityId = @keep WHERE OpportunityId = @drop;",
                    p).ConfigureAwait(false);
            }

            // 3. Retire the loser. Dismissed, never deleted.
            await Exec(con, tx, @"
UPDATE opportunities.Opportunities
SET DismissedAtUtc  = SYSDATETIMEOFFSET(),
    DismissedBy     = N'BdOpportunityDedup',
    DismissedReason = N'Same RFP as ' + @keepKey + N'; held separately because OpportunityKey carries the source prefix. Observations and child rows merged onto that record.',
    OwnerStaffId    = NULL,
    UpdatedAtUtc    = SYSDATETIMEOFFSET(),
    UpdatedBy       = N'BdOpportunityDedup'
WHERE Id = @drop;", p).ConfigureAwait(false);

            await tx.CommitAsync().ConfigureAwait(false);
            return 1;
        }
        catch (Exception ex)
        {
            await tx.RollbackAsync().ConfigureAwait(false);
            Console.Error.WriteLine($"  merge FAILED for {p.Loser.OpportunityKey}: {ex.Message}");
            return 0;
        }
    }

    private static async Task Exec(SqlConnection con, SqlTransaction tx, string sql, Plan p)
    {
        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@keep", SqlDbType.BigInt).Value = p.Survivor.Id;
        cmd.Parameters.Add("@drop", SqlDbType.BigInt).Value = p.Loser.Id;
        cmd.Parameters.Add("@keepKey", SqlDbType.NVarChar, 64).Value = p.Survivor.OpportunityKey;
        await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
    }

    private static async Task<List<Row>> LoadAsync(SqlConnection con)
    {
        const string Sql = @"
SELECT o.Id, o.OpportunityKey, o.Name, ISNULL(o.BuyerName,'') AS BuyerName, o.Status,
       o.RelevanceScore,
       CASE WHEN o.SubmissionDeadlineUtc IS NULL THEN 0 ELSE 1 END AS HasDeadline,
       CASE WHEN o.OwnerStaffId IS NULL THEN 0 ELSE 1 END AS HasOwner,
       CASE WHEN EXISTS (SELECT 1 FROM opportunities.CrmEngagements e WHERE e.OpportunityId = o.Id)
            THEN 1 ELSE 0 END AS HasPursuit,
       CASE WHEN EXISTS (SELECT 1 FROM opportunities.OpportunityNotes n WHERE n.OpportunityId = o.Id)
              OR EXISTS (SELECT 1 FROM opportunities.OpportunityFeeProposalLinks f WHERE f.OpportunityId = o.Id)
            THEN 1 ELSE 0 END AS HasHumanWork
FROM opportunities.Opportunities o
WHERE o.DismissedAtUtc IS NULL AND LEN(o.Name) > 25;";

        var list = new List<Row>();
        await using var cmd = new SqlCommand(Sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await r.ReadAsync().ConfigureAwait(false))
        {
            var name = r.GetString(2);
            list.Add(new Row(
                r.GetInt64(0), r.GetString(1), name, r.GetString(3), r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetDecimal(5),
                r.GetInt32(6) == 1, r.GetInt32(7) == 1, r.GetInt32(8) == 1, r.GetInt32(9) == 1,
                name.Trim().ToLowerInvariant(),
                r.GetString(1).Length >= 8 ? r.GetString(1)[..8] : r.GetString(1)));
        }

        return list;
    }

    private static void Report(List<Plan> plans, List<Held> held)
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

    private static void WritePlanFile(List<Plan> plans, List<Held> held)
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

    private sealed record Row(
        long Id, string OpportunityKey, string Name, string BuyerName, int Status,
        decimal? RelevanceScore, bool HasDeadline, bool HasOwner, bool HasPursuit, bool HasHumanWork,
        string NormName, string SourcePrefix);

    private sealed record Plan(Row Survivor, Row Loser);

    private sealed record Held(string NormName, int Members, string Reason);
}
