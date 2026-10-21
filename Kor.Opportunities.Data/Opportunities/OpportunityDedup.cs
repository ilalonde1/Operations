#nullable enable
using System.Data;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Opportunities;

/// <summary>
/// Merges opportunities that are ONE real tender held under several sources, and refuses anything it is not sure about.
/// Moved here from tools/BdOpportunityDedup on 2026-10-20 so the Worker's nightly job and the command-line tool run the
/// same code. History and the measurements behind each rule are in the tool's header.
///
/// A group is HELD, not merged, if any row in it has a pursuit, an owner, notes or a fee-proposal link; if the copies
/// disagree on Status; if one portal carries it under different reference numbers without a shared buyer and deadline;
/// or if the title cores score below the similarity floor. What it does not cover: two portals wording one tender
/// differently beyond reference numbers and CivicInfo's "(X (City))" tag, and a cancelled-and-re-posted tender with the
/// same deadline, which looks identical to a duplicate.
/// </summary>
public static class OpportunityDedup
{
    private const int CommandTimeoutSeconds = 180;

    public sealed record Row(
        long Id, string OpportunityKey, string Name, string BuyerName, int Status,
        decimal? RelevanceScore, bool HasDeadline, bool HasOwner, bool HasPursuit, bool HasHumanWork,
        string NormName, string SourcePrefix, DateTime? DeadlineDate);

    public sealed record Plan(Row Survivor, Row Loser);

    public sealed record Held(string NormName, int Members, string Reason);

    public sealed record Outcome(int GroupsConsidered, IReadOnlyList<Plan> Plans, IReadOnlyList<Held> Held);

    // The title a person reads, without the decorations portals add: "26-010 Building Condition Assessment…" on one
    // feed and the same title without the number on another, or with CivicInfo's "(Vernon (City))" appended.
    private static readonly Regex CityTail = new(@"\s*\([^()]*\((city|district|town|village)\)\)\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LeadingRef = new(@"^\s*(rfp|rfq|rfb|itt|rft|nrfp|rfsq|rfpq|noi|tender)?\s*[#:]?\s*[a-z]{0,4}[-\s]?\d[\w./-]*\s*[-–:|]*\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NonWord = new(@"[^a-z0-9]+", RegexOptions.Compiled);

    public static string CoreTitle(string? name)
    {
        var t = CityTail.Replace(name ?? "", "");
        var stripped = LeadingRef.Replace(t, "");
        if (stripped.Length >= 12) t = stripped;  // never strip a title down to almost nothing
        return NonWord.Replace(t.ToLowerInvariant(), " ").Trim();
    }

    /// <summary>The source is the key's text before its first dash ("BCBID-229472" -> "BCBID").</summary>
    public static string SourceOf(string key) => key.IndexOf('-') is > 0 and var dash ? key[..dash] : key;

    // BC Bid's engineering feed (BCBIDENG) is the same portal as BCBID: one reference number, two feeds.
    private static string PortalOf(string sourcePrefix) =>
        sourcePrefix.EndsWith("ENG", StringComparison.OrdinalIgnoreCase) ? sourcePrefix[..^3] : sourcePrefix;

    private static string RefOf(string key) => key.IndexOf('-') is > 0 and var dash ? key[(dash + 1)..] : key;

    public static async Task<List<Row>> LoadAsync(SqlConnection con, CancellationToken ct = default)
    {
        const string Sql = @"
SELECT o.Id, o.OpportunityKey, o.Name, ISNULL(o.BuyerName,'') AS BuyerName, o.Status,
       o.RelevanceScore,
       CASE WHEN o.SubmissionDeadlineUtc IS NULL THEN 0 ELSE 1 END AS HasDeadline,
       CONVERT(date, o.SubmissionDeadlineUtc) AS DeadlineDate,
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
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var name = r.GetString(2);
            // Ordinals: 0 Id, 1 Key, 2 Name, 3 Buyer, 4 Status, 5 Score, 6 HasDeadline, 7 DeadlineDate, 8 HasOwner,
            // 9 HasPursuit, 10 HasHumanWork.
            list.Add(new Row(
                r.GetInt64(0), r.GetString(1), name, r.GetString(3), r.GetInt32(4),
                r.IsDBNull(5) ? null : r.GetDecimal(5),
                r.GetInt32(6) == 1, r.GetInt32(8) == 1, r.GetInt32(9) == 1, r.GetInt32(10) == 1,
                CoreTitle(name), SourceOf(r.GetString(1)),
                r.IsDBNull(7) ? null : r.GetDateTime(7)));
        }

        return list;
    }

    /// <summary>
    /// Groups the rows by title core and gates each group. A group is considered when one title arrives under several
    /// sources, or (includeSameSource) one source repeats it with one buyer and one non-null deadline: BC Bid re-posts
    /// under a fresh number, and the deadline is what separates a re-post from a genuine re-issue.
    /// </summary>
    public static Outcome BuildPlan(IReadOnlyList<Row> rows, bool includeSameSource, double minSimilarity)
    {
        static bool SameSourceIsSafe(IGrouping<string, Row> g)
            => g.Select(r => r.BuyerName.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count() == 1
               && g.All(r => r.DeadlineDate.HasValue)
               && g.Select(r => r.DeadlineDate!.Value).Distinct().Count() == 1;

        var groups = rows
            .GroupBy(r => r.NormName, StringComparer.Ordinal)
            .Where(g => g.Select(r => r.SourcePrefix).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1
                        || (includeSameSource && g.Count() > 1 && SameSourceIsSafe(g)))
            .ToList();

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

            plans.AddRange(members.Where(m => m.Id != survivor!.Id).Select(loser => new Plan(survivor!, loser)));
        }

        return new Outcome(groups.Count, plans, held);
    }

    /// <summary>Null when the group is safe to merge (and sets survivor), or the reason it is held.</summary>
    public static string? GateGroup(List<Row> members, double minSimilarity, out Row? survivor)
    {
        survivor = null;
        if (members.Any(m => m.HasPursuit)) return "a pursuit (CrmEngagement) is linked";
        if (members.Any(m => m.HasOwner)) return "an owner is assigned";
        if (members.Any(m => m.HasHumanWork)) return "notes or a fee-proposal link exist";
        if (members.Select(m => m.Status).Distinct().Count() > 1) return "copies disagree on Status — one may be live and one dead";

        // Two reference numbers on the SAME portal are two solicitations sharing a title ("Notice to Vendors of upcoming
        // procurement opportunities", BCBID-229472 / 232044 / 234181) unless they share buyer and deadline (a re-post).
        var sharesBuyerAndDeadline = members.Select(m => m.BuyerName.Trim().ToLowerInvariant()).Distinct(StringComparer.Ordinal).Count() == 1
            && members.All(m => m.DeadlineDate.HasValue) && members.Select(m => m.DeadlineDate!.Value).Distinct().Count() == 1;
        var refs = members.Select(m => (Portal: PortalOf(m.SourcePrefix), Ref: RefOf(m.OpportunityKey))).ToList();
        if (!sharesBuyerAndDeadline && refs.GroupBy(r => r.Portal, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Select(r => r.Ref).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            return "same portal, different reference numbers — separate solicitations sharing a title";

        // Survivor: highest relevance, then the one carrying a deadline, then the oldest row.
        var pick = members
            .OrderByDescending(m => m.RelevanceScore ?? decimal.MinValue)
            .ThenByDescending(m => m.HasDeadline)
            .ThenBy(m => m.Id)
            .First();
        survivor = pick;

        foreach (var other in members.Where(m => m.Id != pick.Id))
        {
            var sim = OpportunityDuplicateScorer.NameSimilarity(CoreTitle(pick.Name), CoreTitle(other.Name));
            if (sim < minSimilarity) return $"similarity {sim:0.00} below the {minSimilarity:0.00} floor";
        }

        return null;
    }

    /// <summary>Merges one loser into its survivor in a single transaction. Returns null on success, or the error.</summary>
    public static async Task<string?> MergeAsync(SqlConnection con, Plan p, string actor, CancellationToken ct = default)
    {
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        try
        {
            // 1. Fill the survivor's gaps from the loser. COALESCE only: a value already on the survivor is never overwritten.
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
    UpdatedBy             = @actor
FROM opportunities.Opportunities k
CROSS JOIN opportunities.Opportunities d
WHERE k.Id = @keep AND d.Id = @drop;", p, actor, ct).ConfigureAwait(false);

            // 2. Move everything that points at the loser. Three child tables carry a unique index on
            //    (OpportunityId, X) and two copies of one tender routinely hold the same X, so the loser's colliding
            //    row (identical URL, firm or proposal, already on the survivor) is deleted rather than carried across.
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
              WHERE k.OpportunityId = @keep AND k.{uniqueCol} = d.{uniqueCol});", p, actor, ct).ConfigureAwait(false);
            }

            foreach (var t in new[]
                     {
                         "OpportunityObservations", "OpportunityDocuments", "OpportunityNotes",
                         "OpportunityFiles", "OpportunityInterestedFirms", "OpportunityFeeProposalLinks",
                     })
            {
                await Exec(con, tx, $"UPDATE opportunities.{t} SET OpportunityId = @keep WHERE OpportunityId = @drop;", p, actor, ct)
                    .ConfigureAwait(false);
            }

            // 3. Retire the loser. Dismissed, never deleted.
            await Exec(con, tx, @"
UPDATE opportunities.Opportunities
SET DismissedAtUtc  = SYSDATETIMEOFFSET(),
    DismissedBy     = @actor,
    DismissedReason = N'Duplicate of ' + @keepKey + N', the record kept. Its postings and history were merged there.',
    OwnerStaffId    = NULL,
    UpdatedAtUtc    = SYSDATETIMEOFFSET(),
    UpdatedBy       = @actor
WHERE Id = @drop;", p, actor, ct).ConfigureAwait(false);

            await tx.CommitAsync(ct).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            return ex.Message;
        }
    }

    private static async Task Exec(SqlConnection con, SqlTransaction tx, string sql, Plan p, string actor, CancellationToken ct)
    {
        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@keep", SqlDbType.BigInt).Value = p.Survivor.Id;
        cmd.Parameters.Add("@drop", SqlDbType.BigInt).Value = p.Loser.Id;
        cmd.Parameters.Add("@keepKey", SqlDbType.NVarChar, 64).Value = p.Survivor.OpportunityKey;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 100).Value = actor;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
}
