#nullable enable

using System;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
internal sealed class DataHealthAuditJob : IJob
{
    private const string Q1KindDistributionSql = """
        SELECT Kind, COUNT(*) AS N,
          SUM(CASE WHEN Website IS NULL THEN 1 ELSE 0 END) AS NoWebsite,
          CAST(100.0 * SUM(CASE WHEN Website IS NULL THEN 1 ELSE 0 END) / COUNT(*) AS DECIMAL(5,1)) AS PctNoWebsite
        FROM opportunities.CanonicalOrg WHERE RetiredAtUtc IS NULL GROUP BY Kind ORDER BY N DESC
        """;

    private const string Q2MessAccumulationSql = """
        SELECT Kind, COUNT(*) AS Created7Days,
          SUM(CASE WHEN Website IS NULL THEN 1 ELSE 0 END) AS NoWeb
        FROM opportunities.CanonicalOrg
        WHERE CreatedAtUtc >= DATEADD(day, -7, sysdatetimeoffset())
          AND RetiredAtUtc IS NULL
        GROUP BY Kind ORDER BY Created7Days DESC
        """;

    private const string Q3BareTargetsSql = """
        SELECT TOP 25 co.Kind, co.Id, co.DisplayName,
          (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory WHERE RetiredAtUtc IS NULL AND (ArchitectCanonicalOrgId=co.Id OR ProponentCanonicalOrgId=co.Id OR GeneralContractorCanonicalOrgId=co.Id OR StructuralEngineerCanonicalOrgId=co.Id)) AS MpiRefs
        FROM opportunities.CanonicalOrg co
        WHERE co.Kind IN ('Architect','Buyer','Developer','GC','Competitor')
          AND co.RetiredAtUtc IS NULL
          AND co.Website IS NULL
          AND (co.Notes IS NULL OR co.Notes NOT LIKE 'WebSearchNotFound:%')
          AND (SELECT COUNT(*) FROM opportunities.MajorProjectsInventory WHERE RetiredAtUtc IS NULL AND (ArchitectCanonicalOrgId=co.Id OR ProponentCanonicalOrgId=co.Id OR GeneralContractorCanonicalOrgId=co.Id OR StructuralEngineerCanonicalOrgId=co.Id)) >= 5
        ORDER BY MpiRefs DESC
        """;

    private const string Q4DirtPatternsSql = """
        SELECT 'DBA prefix' AS Pattern, COUNT(*) AS N FROM opportunities.CanonicalOrg WHERE DisplayName LIKE '%DBA:%'
        UNION ALL SELECT 'o/a wrapper', COUNT(*) FROM opportunities.CanonicalOrg WHERE DisplayName LIKE '% o/a %'
        UNION ALL SELECT 'Procurement-noise suffix', COUNT(*) FROM opportunities.CanonicalOrg WHERE DisplayName LIKE '% Pre-Qualified%' OR DisplayName LIKE '% Bid Received%' OR DisplayName LIKE '% Successful Proponent%' OR DisplayName LIKE '% Awarded'
        UNION ALL SELECT 'All-caps DisplayName', COUNT(*) FROM opportunities.CanonicalOrg WHERE DisplayName = UPPER(DisplayName) AND DisplayName <> LOWER(DisplayName) AND LEN(DisplayName) >= 6
        UNION ALL SELECT 'Newline-corrupted', COUNT(*) FROM opportunities.CanonicalOrg WHERE DisplayName LIKE '%' + CHAR(10) + '%' OR DisplayName LIKE '%' + CHAR(13) + '%'
        UNION ALL SELECT 'Junk pattern (URL/address/n-a)', COUNT(*) FROM opportunities.CanonicalOrg WHERE DisplayName LIKE 'http%' OR DisplayName LIKE 'www.%' OR DisplayName LIKE '#%' OR DisplayName IN ('yes','no','none','None','N/A','NA','TBD','TBA','Unknown','-','--','***','test','Test')
        """;

    private const string Q5KindSuffixMismatchSql = """
        SELECT 'Vendor w/ Arch/Eng/Construction/Builders/Developments suffix' AS Lever, COUNT(*) AS N FROM opportunities.CanonicalOrg
        WHERE Kind='Vendor' AND RetiredAtUtc IS NULL AND (DisplayName LIKE '% Architects' OR DisplayName LIKE '% Architecture' OR DisplayName LIKE '% Engineering Ltd%' OR DisplayName LIKE '% Construction' OR DisplayName LIKE '% Construction Ltd%' OR DisplayName LIKE '% Builders' OR DisplayName LIKE '% Builders Ltd%' OR DisplayName LIKE '% Developments Ltd%')
        UNION ALL SELECT 'Unknown w/ strong Buyer/Architect suffix', COUNT(*) FROM opportunities.CanonicalOrg
        WHERE Kind='Unknown' AND RetiredAtUtc IS NULL AND (DisplayName LIKE 'Ministry of %' OR DisplayName LIKE 'City of %' OR DisplayName LIKE 'District of %' OR DisplayName LIKE 'School District %' OR DisplayName LIKE '% Architects' OR DisplayName LIKE '% Architecture')
        UNION ALL SELECT 'Buyer w/ Construction/Engineering suffix', COUNT(*) FROM opportunities.CanonicalOrg
        WHERE Kind='Buyer' AND RetiredAtUtc IS NULL AND (DisplayName LIKE '% Construction%' OR DisplayName LIKE '% Engineers%' OR DisplayName LIKE '% Architects%')
        """;

    private const string Q6FkOrphanRatesSql = """
        SELECT 'MPI.Architect' AS Col, COUNT(*) AS Total,
          SUM(CASE WHEN ArchitectCanonicalOrgId IS NULL THEN 1 ELSE 0 END) AS NullCount,
          CAST(100.0 * SUM(CASE WHEN ArchitectCanonicalOrgId IS NULL THEN 1 ELSE 0 END) / NULLIF(COUNT(*),0) AS DECIMAL(5,1)) AS PctNull
        FROM opportunities.MajorProjectsInventory
        UNION ALL SELECT 'MPI.GC', COUNT(*),
          SUM(CASE WHEN GeneralContractorCanonicalOrgId IS NULL THEN 1 ELSE 0 END),
          CAST(100.0 * SUM(CASE WHEN GeneralContractorCanonicalOrgId IS NULL THEN 1 ELSE 0 END) / NULLIF(COUNT(*),0) AS DECIMAL(5,1))
        FROM opportunities.MajorProjectsInventory
        UNION ALL SELECT 'Opportunities.Buyer', COUNT(*),
          SUM(CASE WHEN BuyerCanonicalOrgId IS NULL THEN 1 ELSE 0 END),
          CAST(100.0 * SUM(CASE WHEN BuyerCanonicalOrgId IS NULL THEN 1 ELSE 0 END) / NULLIF(COUNT(*),0) AS DECIMAL(5,1))
        FROM opportunities.Opportunities
        UNION ALL SELECT 'OpportunityAwards.AwardedTo', COUNT(*),
          SUM(CASE WHEN AwardedToCanonicalOrgId IS NULL THEN 1 ELSE 0 END),
          CAST(100.0 * SUM(CASE WHEN AwardedToCanonicalOrgId IS NULL THEN 1 ELSE 0 END) / NULLIF(COUNT(*),0) AS DECIMAL(5,1))
        FROM opportunities.OpportunityAwards
        """;

    private const string Q7EnrichmentCoverageSql = """
        SELECT co.Kind, COUNT(*) AS Total,
          COUNT(DISTINCT ce.CanonicalOrgId) AS WithAnyEnrich,
          CAST(100.0 * COUNT(DISTINCT ce.CanonicalOrgId) / NULLIF(COUNT(*),0) AS DECIMAL(5,1)) AS PctEnriched
        FROM opportunities.CanonicalOrg co
        LEFT JOIN opportunities.CanonicalOrgEnrichment ce ON ce.CanonicalOrgId = co.Id
        WHERE co.RetiredAtUtc IS NULL
        GROUP BY co.Kind ORDER BY Total DESC
        """;

    private const string Q8IdentityDriftSql = """
        WITH LiveNames AS (
            SELECT NormalizedName
            FROM opportunities.CanonicalOrg
            WHERE RetiredAtUtc IS NULL
              AND NormalizedName IS NOT NULL
              AND LEN(NormalizedName) >= 3
            GROUP BY NormalizedName
        ), RetiredNames AS (
            SELECT NormalizedName
            FROM opportunities.CanonicalOrg
            WHERE RetiredAtUtc IS NOT NULL
              AND NormalizedName IS NOT NULL
              AND LEN(NormalizedName) >= 3
            GROUP BY NormalizedName
        ), NameTwins AS (
            SELECT l.NormalizedName
            FROM LiveNames l
            JOIN RetiredNames r ON r.NormalizedName = l.NormalizedName
        ), LiveDomains AS (
            SELECT WebsiteDomain
            FROM opportunities.CanonicalOrg
            WHERE RetiredAtUtc IS NULL
              AND WebsiteDomain IS NOT NULL
              AND LEN(WebsiteDomain) >= 4
            GROUP BY WebsiteDomain
        ), RetiredDomains AS (
            SELECT WebsiteDomain
            FROM opportunities.CanonicalOrg
            WHERE RetiredAtUtc IS NOT NULL
              AND WebsiteDomain IS NOT NULL
              AND LEN(WebsiteDomain) >= 4
            GROUP BY WebsiteDomain
        ), DomainTwins AS (
            SELECT l.WebsiteDomain
            FROM LiveDomains l
            JOIN RetiredDomains r ON r.WebsiteDomain = l.WebsiteDomain
        )
        SELECT 'Identity' AS Category, 'exact-name live+retired collisions' AS Label, COUNT_BIG(*) AS N
        FROM NameTwins
        UNION ALL
        SELECT 'Identity' AS Category, 'domain live+retired collisions' AS Label, COUNT_BIG(*) AS N
        FROM DomainTwins
        """;

    // Source-staleness sentinel (completeness audit 2026-07-01): the pipeline's
    // only loud failure mode is a thrown provider — a source that keeps running
    // green while producing NOTHING (portal moved, filter broke, session died)
    // is invisible. APC was dead-green for 18 days. Verdicts:
    //   DEAD-GREEN      produced before, nothing in 14 days despite >=5 recent runs
    //   NEVER-PRODUCED  >=10 runs all-time, never produced, feed emitting nothing
    //   GATED           never produced BUT candidates are arriving and being
    //                   skipped/rejected — feed alive, review the relevance gate
    // MPI sources (type 18) are excluded — their providers upsert internally and
    // always record 0 in IngestionRuns by design.
    private const string Q9SourceStalenessSql = """
        WITH SourceRuns AS (
            SELECT s.Name,
                   MAX(CASE WHEN r.InsertedCount + r.DuplicateCount > 0 THEN r.EndedAtUtc END) AS LastProducedUtc,
                   MAX(r.EndedAtUtc) AS LastRunUtc,
                   SUM(CASE WHEN r.EndedAtUtc >= DATEADD(day, -7, SYSDATETIMEOFFSET()) THEN 1 ELSE 0 END) AS Runs7d,
                   SUM(CASE WHEN r.EndedAtUtc >= DATEADD(day, -7, SYSDATETIMEOFFSET()) THEN r.SkippedCount ELSE 0 END) AS Skipped7d,
                   COUNT_BIG(r.Id) AS RunsAllTime
            FROM opportunities.OpportunitySources s
            LEFT JOIN opportunities.IngestionRuns r
              ON r.ProviderName LIKE s.Name + N' (%'
              OR r.ProviderName LIKE N'Awards: ' + s.Name + N' (%'
            WHERE s.IsEnabled = 1
              AND s.IsHistorical = 0
              AND s.SourceType NOT IN (0, 7, 18, 99)
            GROUP BY s.Name
        )
        SELECT Name,
               CASE
                   WHEN LastProducedUtc IS NOT NULL THEN 'DEAD-GREEN'
                   WHEN Skipped7d > 0 THEN 'GATED (feed alive)'
                   ELSE 'NEVER-PRODUCED'
               END AS Verdict,
               CONVERT(varchar(19), LastProducedUtc, 120) AS LastProduced,
               CONVERT(varchar(19), LastRunUtc, 120) AS LastRun,
               Runs7d
        FROM SourceRuns
        WHERE (LastProducedUtc IS NULL AND RunsAllTime >= 10)
           OR (LastProducedUtc < DATEADD(day, -14, SYSDATETIMEOFFSET()) AND Runs7d >= 5)
        ORDER BY Name
        """;

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<DataHealthAuditJob> _logger;

    public DataHealthAuditJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<DataHealthAuditJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.DataHealthAuditEnabled)
        {
            _logger.LogInformation("{Job}: disabled by configuration.", nameof(DataHealthAuditJob));
            return;
        }

        var sw = Stopwatch.StartNew();
        var timestampUtc = DateTimeOffset.UtcNow;
        var latestPath = string.Empty;

        try
        {
            await using var connection = new SqlConnection(opt.OpportunitiesDb);
            await connection.OpenAsync(context.CancellationToken).ConfigureAwait(false);

            var kindDistribution = await QueryAsync(connection, Q1KindDistributionSql, ReadKindDistributionRow, context.CancellationToken).ConfigureAwait(false);
            var messAccumulation = await QueryAsync(connection, Q2MessAccumulationSql, ReadMessAccumulationRow, context.CancellationToken).ConfigureAwait(false);
            var bareTargets = await QueryAsync(connection, Q3BareTargetsSql, ReadBareTargetRow, context.CancellationToken).ConfigureAwait(false);
            var dirtPatterns = await QueryAsync(connection, Q4DirtPatternsSql, ReadDirtPatternRow, context.CancellationToken).ConfigureAwait(false);
            var kindMismatches = await QueryAsync(connection, Q5KindSuffixMismatchSql, ReadKindMismatchRow, context.CancellationToken).ConfigureAwait(false);
            var fkOrphanRates = await QueryAsync(connection, Q6FkOrphanRatesSql, ReadFkOrphanRateRow, context.CancellationToken).ConfigureAwait(false);
            var enrichmentCoverage = await QueryAsync(connection, Q7EnrichmentCoverageSql, ReadEnrichmentCoverageRow, context.CancellationToken).ConfigureAwait(false);
            var identityDrift = await QueryAsync(connection, Q8IdentityDriftSql, ReadIdentityDriftRow, context.CancellationToken).ConfigureAwait(false);
            var staleSources = await QueryAsync(connection, Q9SourceStalenessSql, ReadSourceStalenessRow, context.CancellationToken).ConfigureAwait(false);

            // The one loud path: a stale source is an operational incident, not a
            // report footnote — surface it at WARNING so it lands in the log review.
            foreach (var stale in staleSources)
            {
                _logger.LogWarning(
                    "Source-staleness sentinel: {Source} is {Verdict} (last produced: {LastProduced}; last run: {LastRun}; runs last 7d: {Runs7d}).",
                    stale.Name, stale.Verdict,
                    string.IsNullOrEmpty(stale.LastProduced) ? "never" : stale.LastProduced,
                    stale.LastRun, stale.Runs7d);
            }

            sw.Stop();

            var outputDir = string.IsNullOrWhiteSpace(opt.DataHealthAuditOutputDir)
                ? @"C:\ProgramData\KorOperations\DataHealthAudit"
                : opt.DataHealthAuditOutputDir;
            Directory.CreateDirectory(outputDir);

            latestPath = Path.Combine(outputDir, "latest.md");
            var timestampedPath = Path.Combine(outputDir, timestampUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".md");

            var markdown = BuildMarkdown(
                timestampUtc,
                Environment.MachineName,
                sw.ElapsedMilliseconds,
                latestPath,
                kindDistribution,
                messAccumulation,
                bareTargets,
                dirtPatterns,
                kindMismatches,
                fkOrphanRates,
                enrichmentCoverage,
                identityDrift,
                staleSources);

            await File.WriteAllTextAsync(timestampedPath, markdown, Encoding.UTF8, context.CancellationToken).ConfigureAwait(false);
            await File.WriteAllTextAsync(latestPath, markdown, Encoding.UTF8, context.CancellationToken).ConfigureAwait(false);

            var created7d = messAccumulation.Sum(x => x.Created7Days);
            var dirt = dirtPatterns.Sum(x => x.N);
            var summary = $"DataHealthAudit OK: created7d={created7d}; bareTop25={bareTargets.Count}; dirt={dirt}; staleSources={staleSources.Count}; report={latestPath}";
            if (summary.Length > 1000)
            {
                summary = summary[..1000];
            }

            context.Result = summary;
            _logger.LogInformation("{Job}: wrote report to {Path}. {Summary}", nameof(DataHealthAuditJob), latestPath, summary);
        }
        catch (Exception ex)
        {
            sw.Stop();
            var reportHint = string.IsNullOrWhiteSpace(latestPath) ? "(not written)" : latestPath;
            var failureSummary = $"DataHealthAudit failed: {ex.GetType().Name}: {ex.Message}; report={reportHint}";
            if (failureSummary.Length > 1000)
            {
                failureSummary = failureSummary[..1000];
            }

            context.Result = failureSummary;
            _logger.LogError(ex, "{Job}: failed after {ElapsedMs}ms.", nameof(DataHealthAuditJob), sw.ElapsedMilliseconds);
        }
    }

    private static async Task<List<T>> QueryAsync<T>(
        SqlConnection connection,
        string sql,
        Func<SqlDataReader, T> read,
        CancellationToken cancellationToken)
    {
        await using var cmd = new SqlCommand(sql, connection);
        cmd.CommandType = CommandType.Text;

        var rows = new List<T>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private static string BuildMarkdown(
        DateTimeOffset timestampUtc,
        string hostName,
        long elapsedMs,
        string reportPath,
        IReadOnlyList<KindDistributionRow> kindDistribution,
        IReadOnlyList<MessAccumulationRow> messAccumulation,
        IReadOnlyList<BareTargetRow> bareTargets,
        IReadOnlyList<DirtPatternRow> dirtPatterns,
        IReadOnlyList<KindMismatchRow> kindMismatches,
        IReadOnlyList<FkOrphanRateRow> fkOrphanRates,
        IReadOnlyList<EnrichmentCoverageRow> enrichmentCoverage,
        IReadOnlyList<IdentityDriftRow> identityDrift,
        IReadOnlyList<SourceStalenessRow> staleSources)
    {
        var sb = new StringBuilder();
        sb.Append("# Data Health Audit  ").AppendLine(timestampUtc.ToString("O", CultureInfo.InvariantCulture));
        sb.AppendLine();
        sb.Append("Generated by `DataHealthAuditJob` on ").Append(Md(hostName)).Append(". Elapsed: ").Append(elapsedMs.ToString(CultureInfo.InvariantCulture)).AppendLine("ms.");
        sb.AppendLine();

        sb.AppendLine("## A. Kind distribution + enrichment depth");
        sb.AppendLine("| Kind | Count | NoWebsite | PctNoWebsite |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var row in kindDistribution)
        {
            sb.Append("| ").Append(Md(row.Kind))
                .Append(" | ").Append(row.N.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.NoWebsite.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.PctNoWebsite.ToString("0.0", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## B. Mess accumulation rate  last 7 days");
        sb.AppendLine("| Kind | Created | NoWeb |");
        sb.AppendLine("|---|---:|---:|");
        foreach (var row in messAccumulation)
        {
            sb.Append("| ").Append(Md(row.Kind))
                .Append(" | ").Append(row.Created7Days.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.NoWeb.ToString("N0", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## C. Top 25 bare BD targets (high MPI, no website)");
        sb.AppendLine("| Kind | Id | DisplayName | MPI refs |");
        sb.AppendLine("|---|---:|---|---:|");
        foreach (var row in bareTargets)
        {
            sb.Append("| ").Append(Md(row.Kind))
                .Append(" | ").Append(row.Id.ToString(CultureInfo.InvariantCulture))
                .Append(" | ").Append(Md(row.DisplayName))
                .Append(" | ").Append(row.MpiRefs.ToString("N0", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## D. Recurring dirt patterns (intake re-mess risk)");
        sb.AppendLine("| Pattern | Count |");
        sb.AppendLine("|---|---:|");
        foreach (var row in dirtPatterns)
        {
            sb.Append("| ").Append(Md(row.Pattern))
                .Append(" | ").Append(row.N.ToString("N0", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## E. Kind/suffix mismatches");
        sb.AppendLine("| Lever | Count |");
        sb.AppendLine("|---|---:|");
        foreach (var row in kindMismatches)
        {
            sb.Append("| ").Append(Md(row.Lever))
                .Append(" | ").Append(row.N.ToString("N0", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## F. FK orphan rates by table");
        sb.AppendLine("| Column | Total | Null | %Null |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var row in fkOrphanRates)
        {
            sb.Append("| ").Append(Md(row.Col))
                .Append(" | ").Append(row.Total.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.NullCount.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(FormatDecimal(row.PctNull))
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## G. Enrichment coverage by Kind");
        sb.AppendLine("| Kind | Total | WithEnrichment | %Enriched |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var row in enrichmentCoverage)
        {
            sb.Append("| ").Append(Md(row.Kind))
                .Append(" | ").Append(row.Total.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(row.WithAnyEnrich.ToString("N0", CultureInfo.InvariantCulture))
                .Append(" | ").Append(FormatDecimal(row.PctEnriched))
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## H. Identity drift sentinels");
        sb.AppendLine("| Category | Label | Count |");
        sb.AppendLine("|---|---|---:|");
        foreach (var row in identityDrift)
        {
            sb.Append("| ").Append(Md(row.Category))
                .Append(" | ").Append(Md(row.Label))
                .Append(" | ").Append(row.N.ToString("N0", CultureInfo.InvariantCulture))
                .AppendLine(" |");
        }

        sb.AppendLine();
        sb.AppendLine("## I. Source staleness sentinels");
        if (staleSources.Count == 0)
        {
            sb.AppendLine("All enabled sources produced within their window. ✔");
        }
        else
        {
            sb.AppendLine("| Source | Verdict | Last produced | Last run | Runs 7d |");
            sb.AppendLine("|---|---|---|---|---:|");
            foreach (var row in staleSources)
            {
                sb.Append("| ").Append(Md(row.Name))
                    .Append(" | ").Append(Md(row.Verdict))
                    .Append(" | ").Append(Md(string.IsNullOrEmpty(row.LastProduced) ? "never" : row.LastProduced))
                    .Append(" | ").Append(Md(row.LastRun))
                    .Append(" | ").Append(row.Runs7d.ToString("N0", CultureInfo.InvariantCulture))
                    .AppendLine(" |");
            }
        }

        sb.AppendLine();
        sb.AppendLine("---");
        sb.Append("Report file: `").Append(reportPath.Replace("`", "'")).AppendLine("`");
        return sb.ToString();
    }

    private static KindDistributionRow ReadKindDistributionRow(SqlDataReader r) =>
        new(GetString(r, 0), GetInt64(r, 1), GetInt64(r, 2), GetDecimal(r, 3));

    private static MessAccumulationRow ReadMessAccumulationRow(SqlDataReader r) =>
        new(GetString(r, 0), GetInt64(r, 1), GetInt64(r, 2));

    private static BareTargetRow ReadBareTargetRow(SqlDataReader r) =>
        new(GetString(r, 0), GetInt64(r, 1), GetString(r, 2), GetInt64(r, 3));

    private static DirtPatternRow ReadDirtPatternRow(SqlDataReader r) =>
        new(GetString(r, 0), GetInt64(r, 1));

    private static KindMismatchRow ReadKindMismatchRow(SqlDataReader r) =>
        new(GetString(r, 0), GetInt64(r, 1));

    private static FkOrphanRateRow ReadFkOrphanRateRow(SqlDataReader r) =>
        new(GetString(r, 0), GetInt64(r, 1), GetInt64(r, 2), GetNullableDecimal(r, 3));

    private static EnrichmentCoverageRow ReadEnrichmentCoverageRow(SqlDataReader r) =>
        new(GetString(r, 0), GetInt64(r, 1), GetInt64(r, 2), GetNullableDecimal(r, 3));

    private static IdentityDriftRow ReadIdentityDriftRow(SqlDataReader r) =>
        new(GetString(r, 0), GetString(r, 1), GetInt64(r, 2));

    private static SourceStalenessRow ReadSourceStalenessRow(SqlDataReader r) =>
        new(GetString(r, 0), GetString(r, 1), GetString(r, 2), GetString(r, 3), GetInt64(r, 4));

    private static string GetString(SqlDataReader r, int ordinal) =>
        r.IsDBNull(ordinal) ? string.Empty : r.GetString(ordinal);

    private static long GetInt64(SqlDataReader r, int ordinal) =>
        r.IsDBNull(ordinal) ? 0L : Convert.ToInt64(r.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static decimal GetDecimal(SqlDataReader r, int ordinal) =>
        r.IsDBNull(ordinal) ? 0m : Convert.ToDecimal(r.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static decimal? GetNullableDecimal(SqlDataReader r, int ordinal) =>
        r.IsDBNull(ordinal) ? null : Convert.ToDecimal(r.GetValue(ordinal), CultureInfo.InvariantCulture);

    private static string FormatDecimal(decimal? value) =>
        value.HasValue ? value.Value.ToString("0.0", CultureInfo.InvariantCulture) : string.Empty;

    private static string Md(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private sealed record KindDistributionRow(string Kind, long N, long NoWebsite, decimal PctNoWebsite);

    private sealed record MessAccumulationRow(string Kind, long Created7Days, long NoWeb);

    private sealed record BareTargetRow(string Kind, long Id, string DisplayName, long MpiRefs);

    private sealed record DirtPatternRow(string Pattern, long N);

    private sealed record KindMismatchRow(string Lever, long N);

    private sealed record FkOrphanRateRow(string Col, long Total, long NullCount, decimal? PctNull);

    private sealed record EnrichmentCoverageRow(string Kind, long Total, long WithAnyEnrich, decimal? PctEnriched);

    private sealed record IdentityDriftRow(string Category, string Label, long N);

    private sealed record SourceStalenessRow(string Name, string Verdict, string LastProduced, string LastRun, long Runs7d);
}
