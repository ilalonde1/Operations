#nullable enable
using System.Globalization;
using System.Text;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Worker.Services;

internal sealed partial class DataHealthAuditJob
{
    // Reuse Q9's SourceRuns CTE verbatim: the evaluated IDs and the exclusion
    // inventory come from the same source universe, not a second WHERE policy.
    // Q9 itself remains unchanged. Unknown enum values are also surfaced as
    // unevaluated: their counter/storage contract has not been established.
    internal static readonly string SourceCoverageSql = BuildSourceCoverageSql();

    private static string BuildSourceCoverageSql()
    {
        var boundary = Q9SourceStalenessSql.IndexOf("SourceProduction AS (", StringComparison.Ordinal);
        if (boundary < 0)
            throw new InvalidOperationException("Q9 SourceRuns CTE boundary not found.");
        return Q9SourceStalenessSql[..boundary].TrimEnd().TrimEnd(',') + "\n" + """
            SELECT s.Name, s.SourceType, s.IsHistorical,
                   CAST(CASE WHEN sr.Id IS NOT NULL THEN 1 ELSE 0 END AS bit) AS EvaluatedByQ9
            FROM opportunities.OpportunitySources s
            LEFT JOIN SourceRuns sr ON sr.Id = s.Id
            WHERE s.IsEnabled = 1
            ORDER BY s.Name;
            """;
    }

    // Section J compares MAX(FirstSeenAtUtc) (new production) and
    // MAX(LastSeenAtUtc) (rows re-seen), independently for each enabled type-18
    // feed, including retired records. CA production: 21 days = three weekly
    // cycles. AB/BC: 92 days = longest calendar quarter. Fetch: 14 days = two
    // weekly scheduled runs for both families. All stale verdicts prompt review,
    // not an error or proof that a legitimately quiet publisher is defective.
    //
    // NOT COVERED: publisher completeness, field quality, changed content on
    // existing projects, or downstream relevance/retirement correctness.
    // Same-class miss: a feed dropping half its columns while still adding rows
    // passes. MAX timestamps also cannot detect partial pagination. Attribution
    // uses provider-owned SourceKeys; name-merged records retaining another
    // importer's key are not credited to this feed. UpdatedAtUtc is not fetch
    // evidence because enrichment can change it. Unknown feeds remain visible
    // as UNEVALUATED instead of inheriting a province-wide freshness timestamp.
    internal const string Q10MpiFreshnessSql = """
        WITH FeedPolicies AS (
            SELECT * FROM (VALUES
                (N'AB_MajorProjectsInventory', N'AB', N'AB-', 92, 14),
                (N'BC_MajorProjectsInventory', N'BC', N'BC-', 92, 14),
                (N'CA_CEQAnet', N'CA', N'ceqa:', 21, 14),
                (N'CA_SocrataSF', N'CA', NULL, 21, 14),
                (N'CA_SocrataSanDiego', N'CA', NULL, 21, 14),
                (N'CA_SanJoseCkan', N'CA', NULL, 21, 14)
            ) p(Name, Province, FixedPrefix, ProductionDays, FetchDays)
        ), EnabledMpi AS (
            SELECT s.Name, s.IsHistorical, p.Province, p.ProductionDays, p.FetchDays,
                   CASE WHEN p.Name IS NULL THEN NULL ELSE COALESCE(p.FixedPrefix,
                       COALESCE(NULLIF(LTRIM(RTRIM(COALESCE(sm.ValueJson,
                           JSON_VALUE(CASE WHEN ISJSON(s.ConfigJson) = 1 THEN s.ConfigJson ELSE N'{}' END,
                               '$.sourceKeyPrefix')))), N''), s.Name) + N':') END AS KeyPrefix
            FROM opportunities.OpportunitySources s
            LEFT JOIN FeedPolicies p ON p.Name = s.Name
            LEFT JOIN opportunities.OpportunitySourceMappings sm
                ON sm.OpportunitySourceId = s.Id AND sm.[Key] = N'sourceKeyPrefix'
            WHERE s.IsEnabled = 1 AND s.SourceType = 18
        )
        SELECT s.Name, s.Province, s.KeyPrefix, s.ProductionDays, s.FetchDays, s.IsHistorical,
               m.Rows_, m.LiveRows, m.LastProducedUtc, m.LastSeenUtc,
               DATEDIFF(day, m.LastProducedUtc, SYSDATETIMEOFFSET()) AS DaysSinceProduced,
               DATEDIFF(day, m.LastSeenUtc, SYSDATETIMEOFFSET()) AS DaysSinceSeen
        FROM EnabledMpi s
        OUTER APPLY (
            SELECT COUNT_BIG(*) AS Rows_,
                   COUNT_BIG(CASE WHEN RetiredAtUtc IS NULL THEN 1 END) AS LiveRows,
                   MAX(FirstSeenAtUtc) AS LastProducedUtc, MAX(LastSeenAtUtc) AS LastSeenUtc
            FROM opportunities.MajorProjectsInventory
            WHERE Province = s.Province AND s.IsHistorical = 0
              AND LEFT(SourceKey, LEN(s.KeyPrefix)) = s.KeyPrefix
        ) m
        ORDER BY s.Name;
        """;

    internal static string? SourceExclusion(SourceCoverageRow row)
    {
        if (row.IsHistorical)
            return "Historical archive: uses historical observation tables; not evaluated here.";
        if (!Enum.IsDefined(typeof(OpportunitySourceType), row.SourceType))
            return $"UNEVALUATED: unrecognized source type {row.SourceType}; freshness storage contract requires review.";
        if (row.EvaluatedByQ9)
            return null;
        return row.SourceType switch
        {
            0 => "Unknown source type: no established freshness contract.",
            7 => "Manual BD outreach: no automated polling.",
            18 => "Internal MPI upserts record zero ingestion counters; evaluated separately in §J where supported.",
            99 => "Manual source: no automated polling.",
            _ => "UNEVALUATED: absent from Q9's source universe; requires review."
        };
    }

    internal static void AppendSourceCoverage(StringBuilder sb, IReadOnlyList<SourceCoverageRow> rows)
    {
        var excluded = rows.Select(r => (Row: r, Reason: SourceExclusion(r)))
            .Where(r => r.Reason is not null).ToList();
        sb.AppendLine($"{rows.Count - excluded.Count} of {rows.Count} enabled sources evaluated by §I; {excluded.Count} excluded or unevaluated.");
        sb.AppendLine();
        if (excluded.Count > 0)
        {
            sb.AppendLine("| Excluded / unevaluated source | Reason |");
            sb.AppendLine("|---|---|");
            foreach (var row in excluded)
                sb.AppendLine($"| {Md(row.Row.Name)} | {Md(row.Reason)} |");
            sb.AppendLine();
        }
        sb.AppendLine("DEAD-GREEN, NEVER-PRODUCED and GATED are review prompts, not proof of a provider defect.");
        sb.AppendLine();
    }

    internal static string MpiVerdict(MpiFreshnessRow row)
    {
        if (row.IsHistorical)
            return "UNEVALUATED — historical archive";
        if (row.ProductionDays is null || row.FetchDays is null || string.IsNullOrWhiteSpace(row.KeyPrefix))
            return "UNEVALUATED — no source attribution/cadence policy";
        if (row.LastProducedUtc is null)
            return "NEVER-PRODUCED — review";
        if (row.DaysSinceSeen is null || row.DaysSinceSeen > row.FetchDays)
            return "REVIEW — fetch overdue";
        if (row.DaysSinceProduced > row.ProductionDays)
            return "DEAD-GREEN — review (fetch current, no new projects)";
        return "Within windows";
    }

    internal static void AppendMpiFreshness(StringBuilder sb, IReadOnlyList<MpiFreshnessRow> rows)
    {
        sb.AppendLine();
        sb.AppendLine("## J. MPI source freshness (type 18)");
        var evaluated = rows.Count(r => !MpiVerdict(r).StartsWith("UNEVALUATED", StringComparison.Ordinal));
        sb.AppendLine($"{evaluated} of {rows.Count} enabled type-18 sources evaluated; {rows.Count - evaluated} unevaluated (named below with the reason).");
        sb.AppendLine();
        sb.AppendLine("Production = MAX(FirstSeenAtUtc); fetch = MAX(LastSeenAtUtc), per provider-owned SourceKey, including retired rows. Dates are UTC.");
        sb.AppendLine("Production windows: California 21 days (three weekly cycles); AB/BC MPI 92 days (one full calendar quarter). Fetch window: 14 days (two weekly scheduled runs). Gaps are review prompts, not errors; a quiet publisher can be complete.");
        sb.AppendLine();
        sb.AppendLine("| Source | Scope | Rows / live | Newest project | Days since new | Last seen | Days since seen | Production / fetch days | Verdict |");
        sb.AppendLine("|---|---|---:|---|---:|---|---:|---|---|");
        foreach (var row in rows)
        {
            var scope = string.IsNullOrWhiteSpace(row.KeyPrefix) ? "unmapped" : $"{row.Province}: {row.KeyPrefix}*";
            sb.AppendLine($"| {Md(row.Name)} | {Md(scope)} | {row.Rows} / {row.LiveRows} | {Stamp(row.LastProducedUtc)} | {Days(row.DaysSinceProduced)} | {Stamp(row.LastSeenUtc)} | {Days(row.DaysSinceSeen)} | {Days(row.ProductionDays)} / {Days(row.FetchDays)} | {Md(MpiVerdict(row))} |");
        }
        sb.AppendLine();
        sb.AppendLine("Coverage limits: measures row arrival and re-seeing, not publisher completeness, field quality, updates to existing projects, or retirement correctness. A feed losing columns or pages while still inserting rows can pass. Name-merged rows retaining another importer's key are not attributed to this feed. UpdatedAtUtc is not used because enrichment can refresh it.");
    }

    private static string Stamp(DateTimeOffset? value) => value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "never";
    private static string Days(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "—";

    private static SourceCoverageRow ReadSourceCoverageRow(SqlDataReader r) =>
        new(GetString(r, 0), Convert.ToInt32(r.GetValue(1), CultureInfo.InvariantCulture), r.GetBoolean(2), r.GetBoolean(3));

    private static MpiFreshnessRow ReadMpiFreshnessRow(SqlDataReader r) =>
        new(GetString(r, 0), GetString(r, 1), GetString(r, 2),
            r.IsDBNull(3) ? null : r.GetInt32(3), r.IsDBNull(4) ? null : r.GetInt32(4), r.GetBoolean(5),
            GetInt64(r, 6), GetInt64(r, 7), r.IsDBNull(8) ? null : r.GetDateTimeOffset(8),
            r.IsDBNull(9) ? null : r.GetDateTimeOffset(9), r.IsDBNull(10) ? null : r.GetInt32(10),
            r.IsDBNull(11) ? null : r.GetInt32(11));

    internal sealed record SourceCoverageRow(string Name, int SourceType, bool IsHistorical, bool EvaluatedByQ9);
    internal sealed record MpiFreshnessRow(string Name, string Province, string KeyPrefix,
        int? ProductionDays, int? FetchDays, bool IsHistorical, long Rows, long LiveRows,
        DateTimeOffset? LastProducedUtc, DateTimeOffset? LastSeenUtc, int? DaysSinceProduced, int? DaysSinceSeen);
}
