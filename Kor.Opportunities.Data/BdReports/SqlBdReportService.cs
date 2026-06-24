#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.BdReports.Generators;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.BdReports;

public sealed class SqlBdReportService : IBdReportService
{
    private const int CommandTimeoutSeconds = 120;

    // The repo-wide verdict recipe (tools/BdVerdictBackfill, MCP system prompt):
    // contract shape nests the verdict under honingPass; legacy shape (b) has it
    // at the root. Safe in SQL because verdicts are short tokens (JSON_VALUE's
    // 4000-char limit only matters for prose fields, which are parsed in C#).
    private const string VerdictExpr =
        "COALESCE(JSON_VALUE(e.ResultJson, '$.honingPass.verdict'), JSON_VALUE(e.ResultJson, '$.verdict'))";

    private readonly string _connectionString;

    public SqlBdReportService(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<SectorVerdictSummary>> GetSectorSummariesAsync(CancellationToken ct)
    {
        var summaries = new List<SectorVerdictSummary>(SectorReportDefinitionCatalog.All.Count);

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        foreach (var def in SectorReportDefinitionCatalog.All)
        {
            // LEFT JOIN so never-honed MPIs land in the NoVerdict bucket.
            var sql = $@"
SELECT {VerdictExpr} AS Verdict, COUNT(*) AS N
FROM opportunities.MajorProjectsInventory m
LEFT JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND {def.MpiWhere}
GROUP BY {VerdictExpr};";

            int pursueUrgent = 0, pursue = 0, monitor = 0, discover = 0, dead = 0, duplicate = 0, noVerdict = 0;

            // Explicit scopes: the freshness query below reuses this connection,
            // so this reader must be disposed first (no MARS).
            await using (var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds })
            await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                while (await r.ReadAsync(ct).ConfigureAwait(false))
                {
                    var verdict = r.IsDBNull(0) ? null : r.GetString(0);
                    var n = r.GetInt32(1);

                    // Summary buckets are by raw verdict: PURSUE rows whose korAngle
                    // marks them urgent stay in Pursue here (IsUrgent is a row-level
                    // flag on PursuitBriefRow). Out-of-vocabulary verdict strings
                    // count as NoVerdict rather than being dropped.
                    switch (verdict)
                    {
                        case BdVerdicts.PursueUrgent: pursueUrgent += n; break;
                        case BdVerdicts.Pursue: pursue += n; break;
                        case BdVerdicts.Monitor: monitor += n; break;
                        case BdVerdicts.Discover: discover += n; break;
                        case BdVerdicts.Dead: dead += n; break;
                        case BdVerdicts.Duplicate: duplicate += n; break;
                        default: noVerdict += n; break;
                    }
                }
            }

            // Freshness buckets (decision 6: 30/90-day boundaries) over
            // VERDICT-BEARING rows only — queued/failed enrichment rows
            // (Status='pending', NULL ResultJson) are NotHoned on the card and
            // must not surface as "stale" here (adversarial review C2). The
            // cost rollup counts live verdicts only (PURSUE/MONITOR/DISCOVER):
            // DEAD is locked work and DUPLICATEs would double-count (M1).
            var freshnessSql = $@"
SELECT SUM(CASE WHEN e.LastRefreshAtUtc >= DATEADD(day, -30, sysdatetimeoffset()) THEN 1 ELSE 0 END),
       SUM(CASE WHEN e.LastRefreshAtUtc <  DATEADD(day, -30, sysdatetimeoffset())
                 AND e.LastRefreshAtUtc >= DATEADD(day, -90, sysdatetimeoffset()) THEN 1 ELSE 0 END),
       SUM(CASE WHEN e.LastRefreshAtUtc <  DATEADD(day, -90, sysdatetimeoffset())
                 OR e.LastRefreshAtUtc IS NULL THEN 1 ELSE 0 END),
       COALESCE(SUM(CASE WHEN {VerdictExpr} IN (N'{BdVerdicts.PursueUrgent}', N'{BdVerdicts.Pursue}', N'{BdVerdicts.Monitor}', N'{BdVerdicts.Discover}')
                          THEN m.EstimatedCostCad END), 0)
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND {VerdictExpr} IS NOT NULL
  AND {def.MpiWhere};";

            int fresh = 0, aging = 0, stale = 0;
            decimal honedCost = 0;
            await using (var freshnessCmd = new SqlCommand(freshnessSql, con) { CommandTimeout = CommandTimeoutSeconds })
            await using (var fr = await freshnessCmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                if (await fr.ReadAsync(ct).ConfigureAwait(false) && !fr.IsDBNull(0))
                {
                    fresh = fr.GetInt32(0);
                    aging = fr.GetInt32(1);
                    stale = fr.GetInt32(2);
                    honedCost = fr.GetDecimal(3);
                }
            }

            var total = pursueUrgent + pursue + monitor + discover + dead + duplicate + noVerdict;
            summaries.Add(new SectorVerdictSummary(
                def.Key, def.Title, pursueUrgent, pursue, monitor, discover, dead, duplicate, noVerdict, total,
                fresh, aging, stale, honedCost));
        }

        return summaries;
    }

    public async Task<IReadOnlyList<PursuitBriefRow>> GetSectorPursuitsAsync(string sectorKey, CancellationToken ct)
    {
        var def = SectorReportDefinitionCatalog.All
            .FirstOrDefault(d => string.Equals(d.Key, sectorKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"Unknown sector key '{sectorKey}'. Valid keys: " +
                string.Join(", ", SectorReportDefinitionCatalog.All.Select(d => d.Key)) + ".",
                nameof(sectorKey));

        var sql = $@"
SELECT m.Id, m.ProjectName, m.Province, m.Sector, m.SubSector, m.Stage, m.ProponentName,
       m.EstimatedCostCad, m.EstimatedCostText, m.MunicipalityName, m.RegionName,
       e.ResultJson, e.LastRefreshAtUtc
FROM opportunities.MajorProjectsInventory m
LEFT JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND {def.MpiWhere};";

        var rows = new List<PursuitBriefRow>();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(Hydrate(r));
        }

        return rows
            .OrderBy(x => BdVerdicts.Rank(x.Verdict))
            .ThenByDescending(x => x.EstimatedCostCad ?? decimal.MinValue)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SectorIntelSignalRow>> GetSectorIntelSignalsAsync(string sectorKey, CancellationToken ct)
    {
        var def = SectorReportDefinitionCatalog.All
            .FirstOrDefault(d => string.Equals(d.Key, sectorKey, StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException(
                $"Unknown sector key '{sectorKey}'. Valid keys: " +
                string.Join(", ", SectorReportDefinitionCatalog.All.Select(d => d.Key)) + ".",
                nameof(sectorKey));

        // A signal surfaces in this sector if EITHER its org is on one of the
        // sector's projects (def.MpiWhere) OR — when the sector defines a topic
        // bridge — the signal TEXT is about the sector (def.SignalTopicWhere). The
        // org path keeps cross-sector firms from flooding the list; the topic path
        // catches sector-relevant signals attached to a firm NOT FK-linked to the
        // sector's MPIs (e.g. a civil teaming partner on a defense pursuit). Both
        // fragments are catalog constants (never user input).
        var topicMatch = string.IsNullOrWhiteSpace(def.SignalTopicWhere) ? "1 = 0" : def.SignalTopicWhere;
        var sql = $@"
SELECT TOP 30 co.DisplayName, s.SignalType, s.Subject, s.Detail, s.OccurredAtApprox
FROM opportunities.IntelSignal s
JOIN opportunities.CanonicalOrg co ON co.Id = s.CanonicalOrgId
WHERE s.RetiredAtUtc IS NULL
  AND co.RetiredAtUtc IS NULL
  AND s.SignalType IN (N'SE_LOCKED', N'SE_SOFT', N'SE_OPEN', N'KOR_OPPORTUNITY',
      N'LeadershipChange', N'PipelineUpdate')
  AND (EXISTS (
      SELECT 1
      FROM opportunities.MajorProjectsInventory m
      CROSS APPLY (VALUES
          (m.ArchitectCanonicalOrgId),
          (m.ProponentCanonicalOrgId),
          (m.StructuralEngineerCanonicalOrgId),
          (m.GeneralContractorCanonicalOrgId)
      ) v(CanonicalOrgId)
      WHERE m.RetiredAtUtc IS NULL
        AND {def.MpiWhere}
        AND v.CanonicalOrgId = s.CanonicalOrgId)
      OR ({topicMatch}))
ORDER BY CASE s.SourceConfidence WHEN N'High' THEN 3 WHEN N'Medium' THEN 2 ELSE 1 END DESC,
         s.CreatedAtUtc DESC;";

        var rows = new List<SectorIntelSignalRow>();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new SectorIntelSignalRow(
                r.GetString(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<PursuitBriefRow>> GetCallSheetPoolAsync(CancellationToken ct)
    {
        var sql = $@"
SELECT m.Id, m.ProjectName, m.Province, m.Sector, m.SubSector, m.Stage, m.ProponentName,
       m.EstimatedCostCad, m.EstimatedCostText, m.MunicipalityName, m.RegionName,
       e.ResultJson, e.LastRefreshAtUtc
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND {VerdictExpr} IN (N'{BdVerdicts.PursueUrgent}', N'{BdVerdicts.Pursue}');";

        var rows = new List<PursuitBriefRow>();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(Hydrate(r));
        }

        return rows
            .OrderByDescending(x => x.IsUrgent)
            .ThenByDescending(x => x.EstimatedCostCad ?? decimal.MinValue)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<BdExecHeadline> GetExecHeadlineAsync(CancellationToken ct)
    {
        // DISTINCT active MPIs — per-sector totals double-count (overlapping
        // sector filters by design). "Honed" = verdict-bearing, NOT merely
        // enrichment-row-exists: queued drains create Status='pending' rows
        // with NULL ResultJson that must not count (adversarial review C2).
        // $ rollup = live verdicts only (DEAD locked / DUPLICATE double-counts).
        var sql = $@"
SELECT COUNT(*),
       COALESCE(SUM(CASE WHEN {VerdictExpr} IS NOT NULL THEN 1 ELSE 0 END), 0),
       COALESCE(SUM(CASE WHEN {VerdictExpr} IN (N'{BdVerdicts.PursueUrgent}', N'{BdVerdicts.Pursue}', N'{BdVerdicts.Monitor}', N'{BdVerdicts.Discover}')
                          THEN m.EstimatedCostCad END), 0)
FROM opportunities.MajorProjectsInventory m
LEFT JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        await r.ReadAsync(ct).ConfigureAwait(false);
        return new BdExecHeadline(r.GetInt32(0), r.GetInt32(1), r.GetDecimal(2));
    }

    public async Task<IReadOnlyList<ArchitectLeverageRow>> GetArchitectLeverageAsync(int take, CancellationToken ct)
    {
        // Architect project membership = MPI ArchitectCanonicalOrgId FK rows
        // UNION the org's IntelWork architect-role portfolio edges (Role LIKE
        // '%architect%'), deduped by normalized project name (MPI wins on a
        // tie). Mirrors the dossier-footprint unification (commit 9776a15d) so
        // architects whose teaming lives only in IntelWork enter the ranking.
        const string rankingSql = @"
WITH ArchMpi AS (
    SELECT m.ArchitectCanonicalOrgId AS OrgId, m.EstimatedCostCad AS Cost, 0 AS SrcRank,
           LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(m.ProjectName)),N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')) AS DedupKey
    FROM opportunities.MajorProjectsInventory m
    WHERE m.RetiredAtUtc IS NULL AND m.ArchitectCanonicalOrgId IS NOT NULL
),
ArchIntel AS (
    SELECT iw.CanonicalOrgId AS OrgId, iw.EstimatedValueCad AS Cost, 1 AS SrcRank,
           COALESCE(NULLIF(LTRIM(RTRIM(iw.NormalizedProjectName)),N''),
               LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(iw.ProjectName)),N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N''))) AS DedupKey
    FROM opportunities.IntelWork iw
    WHERE iw.RetiredAtUtc IS NULL AND LOWER(ISNULL(iw.Role,N'')) LIKE N'%architect%'
),
Combined AS (
    SELECT OrgId, Cost, DedupKey,
           ROW_NUMBER() OVER (PARTITION BY OrgId, DedupKey ORDER BY SrcRank) AS rn
    FROM (SELECT OrgId, Cost, SrcRank, DedupKey FROM ArchMpi
          UNION ALL
          SELECT OrgId, Cost, SrcRank, DedupKey FROM ArchIntel) u
    WHERE DedupKey IS NOT NULL AND DedupKey <> N''
),
Deduped AS (SELECT OrgId, Cost FROM Combined WHERE rn = 1)
SELECT TOP (@take) co.Id, co.DisplayName,
       COUNT(*) AS ActiveProjects,
       COALESCE(SUM(d.Cost), 0) AS PipelineCostCad,
       (SELECT COUNT(*) FROM opportunities.IntelSignal s
         WHERE s.CanonicalOrgId = co.Id AND s.RetiredAtUtc IS NULL) AS Signals,
       (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation pa
         JOIN opportunities.IntelPerson p ON p.Id = pa.IntelPersonId AND p.RetiredAtUtc IS NULL
         WHERE pa.CanonicalOrgId = co.Id AND pa.RetiredAtUtc IS NULL) AS People,
       (SELECT COUNT(*) FROM opportunities.IntelAction a
         WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL AND a.Status = N'Open') AS OpenActions
FROM Deduped d
JOIN opportunities.CanonicalOrg co ON co.Id = d.OrgId AND co.RetiredAtUtc IS NULL
GROUP BY co.Id, co.DisplayName
ORDER BY COUNT(*) DESC, COALESCE(SUM(d.Cost), 0) DESC, co.DisplayName;";

        // Top-8 projects per ranked architect: MPI FK rows UNION IntelWork
        // portfolio rows (negative Id distinguishes IntelWork-only), deduped by
        // normalized name (MPI preferred), then top-8 per org.
        const string projectsSql = @"
WITH SrcProjects AS (
    SELECT m.ArchitectCanonicalOrgId AS OrgId, m.Id AS ProjId, m.ProjectName, m.Province,
           COALESCE(m.EstimatedCostText, CAST(m.EstimatedCostCad AS NVARCHAR(64)), N'TBD') AS CostDisplay,
           CASE WHEN m.EstimatedCostCad IS NULL THEN 1 ELSE 0 END AS NoCost, 0 AS SrcRank,
           LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(m.ProjectName)),N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')) AS DedupKey
    FROM opportunities.MajorProjectsInventory m
    WHERE m.RetiredAtUtc IS NULL AND m.ArchitectCanonicalOrgId IS NOT NULL
    UNION ALL
    SELECT iw.CanonicalOrgId, -iw.Id, iw.ProjectName, N'',
           COALESCE(iw.EstimatedValueText, CAST(iw.EstimatedValueCad AS NVARCHAR(64)), N'TBD'),
           CASE WHEN iw.EstimatedValueCad IS NULL THEN 1 ELSE 0 END, 1,
           COALESCE(NULLIF(LTRIM(RTRIM(iw.NormalizedProjectName)),N''),
               LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(iw.ProjectName)),N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')))
    FROM opportunities.IntelWork iw
    JOIN opportunities.CanonicalOrg ico ON ico.Id = iw.CanonicalOrgId AND ico.RetiredAtUtc IS NULL
    WHERE iw.RetiredAtUtc IS NULL AND LOWER(ISNULL(iw.Role,N'')) LIKE N'%architect%'
      AND iw.ProjectName IS NOT NULL AND LTRIM(RTRIM(iw.ProjectName)) <> N''
),
Ded AS (
    SELECT *, ROW_NUMBER() OVER (PARTITION BY OrgId, DedupKey ORDER BY SrcRank) AS rdup FROM SrcProjects
),
Ranked AS (
    SELECT OrgId, ProjId, ProjectName, Province, CostDisplay,
           ROW_NUMBER() OVER (PARTITION BY OrgId ORDER BY NoCost, SrcRank, ProjId DESC) AS rn
    FROM Ded WHERE rdup = 1
)
SELECT OrgId, ProjId, ProjectName, Province, CostDisplay FROM Ranked WHERE rn <= 8;";

        var ranking = new List<(long OrgId, string Name, int Projects, decimal Cost, int Signals, int People, int Actions)>();
        var projectsByOrg = new Dictionary<long, List<string>>();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        await using (var cmd = new SqlCommand(rankingSql, con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@take", System.Data.SqlDbType.Int).Value = Math.Clamp(take, 1, 100);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                ranking.Add((r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetDecimal(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6)));
            }
        }

        await using (var cmd = new SqlCommand(projectsSql, con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var orgId = r.GetInt64(0);
                if (!projectsByOrg.TryGetValue(orgId, out var list))
                {
                    projectsByOrg[orgId] = list = new List<string>();
                }

                list.Add($"{r.GetInt64(1)}: {r.GetString(2)} ({r.GetString(3)}) — {r.GetString(4)}");
            }
        }

        return ranking
            .Select(x => new ArchitectLeverageRow(
                x.OrgId, x.Name, x.Projects, x.Cost, x.Signals, x.People, x.Actions,
                projectsByOrg.TryGetValue(x.OrgId, out var projects) ? projects : Array.Empty<string>()))
            .ToList();
    }

    public async Task<IReadOnlyList<CompetitorFootprintRow>> GetCompetitorFootprintAsync(int take, CancellationToken ct)
    {
        // The verified recipe behind the shipped competitor report (its
        // closing note: "IntelSignal + IntelPersonAffiliation + IntelAction
        // tables on CanonicalOrg.Kind = Competitor"). Counts confirmed against
        // .bc-rivals.json on 2026-06-09 (Aspect 37/6/18 exact match).
        // LinkedProjects = a competitor's SE footprint. MPI StructuralEngineer
        // FK rows UNION the org's IntelWork structural/engineer-role portfolio
        // edges, deduped by normalized project name (UNION collapses ties).
        // Signals/People/Actions are unchanged (verified Aspect 37/6/18).
        const string rankingSql = @"
SELECT TOP (@take) x.Id, x.DisplayName, x.Signals, x.People, x.Actions,
       (SELECT COUNT(*) FROM (
            SELECT LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(m.ProjectName)),N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')) AS dk
            FROM opportunities.MajorProjectsInventory m
            WHERE m.StructuralEngineerCanonicalOrgId = x.Id AND m.RetiredAtUtc IS NULL
            UNION
            SELECT COALESCE(NULLIF(LTRIM(RTRIM(iw.NormalizedProjectName)),N''),
                LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(iw.ProjectName)),N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')))
            FROM opportunities.IntelWork iw
            WHERE iw.CanonicalOrgId = x.Id AND iw.RetiredAtUtc IS NULL
              AND (LOWER(ISNULL(iw.Role,N'')) LIKE N'%structural%' OR LOWER(ISNULL(iw.Role,N'')) LIKE N'%engineer%')
        ) lp WHERE lp.dk IS NOT NULL AND lp.dk <> N'') AS LinkedProjects
FROM (
    SELECT co.Id, co.DisplayName,
           (SELECT COUNT(*) FROM opportunities.IntelSignal s
             WHERE s.CanonicalOrgId = co.Id AND s.RetiredAtUtc IS NULL) AS Signals,
           (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation pa
             WHERE pa.CanonicalOrgId = co.Id AND pa.RetiredAtUtc IS NULL) AS People,
           (SELECT COUNT(*) FROM opportunities.IntelAction a
             WHERE a.CanonicalOrgId = co.Id AND a.RetiredAtUtc IS NULL) AS Actions
    FROM opportunities.CanonicalOrg co
    WHERE co.RetiredAtUtc IS NULL AND co.Kind = N'Competitor'
) x
WHERE x.Signals + x.People + x.Actions > 0
ORDER BY x.Signals + x.People + x.Actions DESC, x.DisplayName;";

        // Top-6 SE projects per competitor: MPI FK rows UNION IntelWork
        // structural-role rows (negative Id marks IntelWork-only), deduped by
        // normalized name (MPI preferred), then top-6 per org.
        const string projectsSql = @"
WITH SrcProjects AS (
    SELECT m.StructuralEngineerCanonicalOrgId AS OrgId, m.Id AS ProjId, m.ProjectName, m.Province,
           COALESCE(m.EstimatedCostText, CAST(m.EstimatedCostCad AS NVARCHAR(64)), N'TBD') AS CostDisplay,
           CASE WHEN m.EstimatedCostCad IS NULL THEN 1 ELSE 0 END AS NoCost, 0 AS SrcRank,
           LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(m.ProjectName)),N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')) AS DedupKey
    FROM opportunities.MajorProjectsInventory m
    WHERE m.RetiredAtUtc IS NULL AND m.StructuralEngineerCanonicalOrgId IS NOT NULL
    UNION ALL
    SELECT iw.CanonicalOrgId, -iw.Id, iw.ProjectName, N'',
           COALESCE(iw.EstimatedValueText, CAST(iw.EstimatedValueCad AS NVARCHAR(64)), N'TBD'),
           CASE WHEN iw.EstimatedValueCad IS NULL THEN 1 ELSE 0 END, 1,
           COALESCE(NULLIF(LTRIM(RTRIM(iw.NormalizedProjectName)),N''),
               LOWER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(LTRIM(RTRIM(iw.ProjectName)),N' ',N''),N'-',N''),N'.',N''),N',',N''),N'''',N'')))
    FROM opportunities.IntelWork iw
    JOIN opportunities.CanonicalOrg ico ON ico.Id = iw.CanonicalOrgId AND ico.RetiredAtUtc IS NULL
    WHERE iw.RetiredAtUtc IS NULL
      AND (LOWER(ISNULL(iw.Role,N'')) LIKE N'%structural%' OR LOWER(ISNULL(iw.Role,N'')) LIKE N'%engineer%')
      AND iw.ProjectName IS NOT NULL AND LTRIM(RTRIM(iw.ProjectName)) <> N''
),
Ded AS (
    SELECT *, ROW_NUMBER() OVER (PARTITION BY OrgId, DedupKey ORDER BY SrcRank) AS rdup FROM SrcProjects
),
Ranked AS (
    SELECT OrgId, ProjId, ProjectName, Province, CostDisplay,
           ROW_NUMBER() OVER (PARTITION BY OrgId ORDER BY NoCost, SrcRank, ProjId DESC) AS rn
    FROM Ded WHERE rdup = 1
)
SELECT OrgId, ProjId, ProjectName, Province, CostDisplay FROM Ranked WHERE rn <= 6;";

        var ranking = new List<(long OrgId, string Name, int Signals, int People, int Actions, int Linked)>();
        var projectsByOrg = new Dictionary<long, List<string>>();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        await using (var cmd = new SqlCommand(rankingSql, con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@take", System.Data.SqlDbType.Int).Value = Math.Clamp(take, 1, 100);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                ranking.Add((r.GetInt64(0), r.GetString(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5)));
            }
        }

        await using (var cmd = new SqlCommand(projectsSql, con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var orgId = r.GetInt64(0);
                if (!projectsByOrg.TryGetValue(orgId, out var list))
                {
                    projectsByOrg[orgId] = list = new List<string>();
                }

                list.Add($"{r.GetInt64(1)}: {r.GetString(2)} ({r.GetString(3)}) — {r.GetString(4)}");
            }
        }

        return ranking
            .Select(x => new CompetitorFootprintRow(
                x.OrgId, x.Name, x.Signals, x.People, x.Actions, x.Linked,
                projectsByOrg.TryGetValue(x.OrgId, out var projects) ? projects : Array.Empty<string>()))
            .ToList();
    }

    public async Task<IReadOnlyList<string>> GetProjectsMentioningAsync(string orgPattern, int take, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(orgPattern))
        {
            return Array.Empty<string>();
        }

        // Catalog patterns can carry OR-alternatives ('Sharon Petty / VCH',
        // 'Wesgroup|Bosa|...'). The PS builder bound these as one literal LIKE
        // — which could never match, so those targets silently always rendered
        // the no-match fallback. Splitting on / and | implements the intent.
        var terms = orgPattern
            .Split(new[] { '|', '/' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3)
            .Take(8)
            .ToList();
        if (terms.Count == 0)
        {
            return Array.Empty<string>();
        }

        var likeClauses = string.Join(" OR ", terms.Select((_, i) => $"e.ResultJson LIKE @p{i}"));
        var sql = $@"
SELECT TOP (@take) m.Id, m.ProjectName, m.Province,
       COALESCE(m.EstimatedCostText, CAST(m.EstimatedCostCad AS NVARCHAR(64)), N'TBD') AS CostDisplay
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
  AND ({likeClauses})
ORDER BY CASE WHEN m.EstimatedCostCad IS NULL THEN 1 ELSE 0 END, m.EstimatedCostCad DESC, m.ProjectName;";

        var lines = new List<string>();
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", System.Data.SqlDbType.Int).Value = Math.Clamp(take, 1, 50);
        for (var i = 0; i < terms.Count; i++)
        {
            // T-SQL LIKE treats % _ [ as metacharacters — bind each term as a
            // literal (BD-Audit-2026-06-09 Minor 10 lesson).
            var escaped = terms[i].Replace("[", "[[]").Replace("%", "[%]").Replace("_", "[_]");
            cmd.Parameters.Add($"@p{i}", System.Data.SqlDbType.NVarChar, 256).Value = $"%{escaped}%";
        }
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            lines.Add($"{r.GetInt64(0)}: {r.GetString(1)} ({r.GetString(2)}) — {r.GetString(3)}");
        }

        return lines;
    }

    public async Task<IReadOnlyList<PrimeConsultantRow>> GetPrimeConsultantsAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT m.Id, m.ProjectName, m.Province, m.ProponentName,
       COALESCE(m.EstimatedCostText, CAST(m.EstimatedCostCad AS NVARCHAR(64)), N'TBD') AS CostDisplay,
       COALESCE(m.EstimatedCostCad, 0) AS CostNumeric,
       e.ResultJson
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'PrimeConsultantResearch'
WHERE m.RetiredAtUtc IS NULL
  AND e.ResultJson IS NOT NULL;";

        var rows = new List<PrimeConsultantRow>();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var research = PrimeConsultantParser.Parse(r.GetString(6));
            rows.Add(new PrimeConsultantRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetString(4),
                r.GetDecimal(5),
                research.FirmName,
                research.PrincipalInCharge,
                research.ContactEmail,
                research.KnownToKor,
                research.KorRelationshipNotes,
                research.KorAngle,
                research.Confidence));
        }

        return rows
            .OrderByDescending(x => x.CostNumeric)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<PursuitDossierRow>> GetPursuitDossiersAsync(CancellationToken ct)
    {
        // Verdict filter stays in SQL (VerdictExpr is fine for the short
        // verdict enum); korAngle/description hydrate client-side via
        // HoningResultParser per its 4000-char JSON_VALUE caveat. Edge org
        // joins keep retired orgs out — a merged-away org must read as a
        // graph gap, not a stale name. SE-is-KOR comes from Kind (canonical
        // 38918 KOR Structural Ltd.), not a hardcoded id.
        var sql = $@"
SELECT m.Id, m.ProjectName, m.Province, m.Sector, m.Stage, m.MunicipalityName, m.ProponentName,
       m.EstimatedCostCad,
       COALESCE(m.EstimatedCostText, CAST(m.EstimatedCostCad AS NVARCHAR(64)), N'TBD') AS CostDisplay,
       e.ResultJson,
       arch.Id AS ArchitectOrgId, arch.DisplayName AS ArchitectName,
       (SELECT COUNT(*) FROM opportunities.IntelSignal s
         WHERE s.CanonicalOrgId = arch.Id AND s.RetiredAtUtc IS NULL) AS ArchitectSignals,
       (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation pa
         JOIN opportunities.IntelPerson p ON p.Id = pa.IntelPersonId AND p.RetiredAtUtc IS NULL
         WHERE pa.CanonicalOrgId = arch.Id AND pa.RetiredAtUtc IS NULL) AS ArchitectPeople,
       (SELECT COUNT(*) FROM opportunities.IntelAction a
         WHERE a.CanonicalOrgId = arch.Id AND a.RetiredAtUtc IS NULL AND a.Status = N'Open') AS ArchitectOpenActions,
       se.Id AS SeOrgId, se.DisplayName AS SeName,
       CASE WHEN se.Kind = N'KorStructural' THEN 1 ELSE 0 END AS SeIsKor,
       gc.Id AS GcOrgId, gc.DisplayName AS GcName
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
LEFT JOIN opportunities.CanonicalOrg arch
  ON arch.Id = m.ArchitectCanonicalOrgId AND arch.RetiredAtUtc IS NULL
LEFT JOIN opportunities.CanonicalOrg se
  ON se.Id = m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
LEFT JOIN opportunities.CanonicalOrg gc
  ON gc.Id = m.GeneralContractorCanonicalOrgId AND gc.RetiredAtUtc IS NULL
WHERE m.RetiredAtUtc IS NULL
  AND {VerdictExpr} IN (N'{BdVerdicts.PursueUrgent}', N'{BdVerdicts.Pursue}');";

        var rows = new List<PursuitDossierRow>();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var honing = HoningResultParser.Parse(r.IsDBNull(9) ? null : r.GetString(9));
            var hasArchitect = !r.IsDBNull(10);

            rows.Add(new PursuitDossierRow(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6),
                r.IsDBNull(7) ? null : r.GetDecimal(7),
                r.GetString(8),
                honing.Verdict,
                honing.KorAngle,
                BdVerdicts.IsUrgent(honing.Verdict, honing.KorAngle),
                hasArchitect ? r.GetInt64(10) : null,
                hasArchitect ? r.GetString(11) : null,
                hasArchitect ? r.GetInt32(12) : 0,
                hasArchitect ? r.GetInt32(13) : 0,
                hasArchitect ? r.GetInt32(14) : 0,
                r.IsDBNull(15) ? null : r.GetInt64(15),
                r.IsDBNull(16) ? null : r.GetString(16),
                !r.IsDBNull(17) && r.GetInt32(17) == 1,
                r.IsDBNull(18) ? null : r.GetInt64(18),
                r.IsDBNull(19) ? null : r.GetString(19)));
        }

        // Read-time attach of project-named live signals (no schema, no backfill).
        // Most signals are org-level (surfaced via the org dossier); the few that
        // name a specific pursuit by project are the "live intel on THIS deal" the
        // dossier wants. Name >= 14 chars guards against trivial substring hits.
        var signalsByMpi = new Dictionary<long, List<string>>();
        if (rows.Count > 0)
        {
            var sigSql = $@"
SELECT m.Id, s.SignalType, s.Subject
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
JOIN opportunities.IntelSignal s
  ON s.RetiredAtUtc IS NULL
 AND (s.Subject LIKE N'%' + m.ProjectName + N'%' OR s.Detail LIKE N'%' + m.ProjectName + N'%')
WHERE m.RetiredAtUtc IS NULL
  AND LEN(LTRIM(RTRIM(m.ProjectName))) >= 14
  AND {VerdictExpr} IN (N'{BdVerdicts.PursueUrgent}', N'{BdVerdicts.Pursue}')
ORDER BY m.Id;";
            // Separate connection: the pursuit reader above is still open on `con`.
            await using var sigCon = new SqlConnection(_connectionString);
            await sigCon.OpenAsync(ct).ConfigureAwait(false);
            await using var sigCmd = new SqlCommand(sigSql, sigCon) { CommandTimeout = CommandTimeoutSeconds };
            await using var sr = await sigCmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await sr.ReadAsync(ct).ConfigureAwait(false))
            {
                var mpiId = sr.GetInt64(0);
                if (!signalsByMpi.TryGetValue(mpiId, out var list))
                {
                    signalsByMpi[mpiId] = list = new List<string>();
                }

                if (list.Count < 4)
                {
                    list.Add($"{sr.GetString(1)}: {sr.GetString(2)}");
                }
            }
        }

        return rows
            .Select(x => signalsByMpi.TryGetValue(x.MpiId, out var sl) ? x with { LiveSignals = sl } : x)
            .OrderByDescending(x => x.IsUrgent)
            .ThenByDescending(x => x.EstimatedCostCad ?? decimal.MinValue)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<AwardProgramReportRow>> GetUpcomingAwardProgramsAsync(int take, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@take)
    ap.Id,
    ap.AwardingBody,
    ap.ProgramName,
    ap.CycleYear,
    ap.Category,
    ap.Discipline,
    ap.Region,
    ap.EligibilitySummary,
    ap.SubmissionDeadline,
    ap.EntryFee,
    ap.Url,
    projectMatch.MpiId,
    projectMatch.ProjectName
FROM opportunities.AwardProgram ap
OUTER APPLY (
    SELECT TOP 1 m.Id AS MpiId, m.ProjectName
    FROM opportunities.MajorProjectsInventory m
    WHERE m.RetiredAtUtc IS NULL
      AND (
          ap.Region IS NULL
          OR ap.Region IN (N'Canada', N'International', N'NorthAmerica', N'US')
          OR (ap.Region = N'BC' AND m.Province = N'BC')
          OR (ap.Region = N'CA' AND m.Province = N'CA')
      )
      AND (
          ap.Discipline IS NULL
          OR ap.Discipline IN (N'Mixed', N'Engineering', N'Structural')
          OR (ap.Discipline = N'Architecture' AND (m.ArchitectCanonicalOrgId IS NOT NULL OR m.Sector LIKE N'%Residential%' OR m.Sector LIKE N'%Institutional%'))
          OR (ap.Discipline = N'UrbanDesign' AND (m.Sector LIKE N'%Residential%' OR m.Sector LIKE N'%Mixed%'))
          OR (ap.Discipline = N'Sustainability' AND (m.ProjectDescription LIKE N'%sustain%' OR m.ProjectDescription LIKE N'%mass timber%' OR m.GreenBuildingInd = 1))
      )
    ORDER BY m.UpdatedAtUtc DESC, m.Id DESC
) projectMatch
WHERE ap.RetiredAtUtc IS NULL
  AND (ap.SubmissionDeadline IS NULL OR ap.SubmissionDeadline >= CONVERT(date, sysdatetimeoffset()))
ORDER BY CASE WHEN ap.SubmissionDeadline IS NULL THEN 1 ELSE 0 END,
         ap.SubmissionDeadline,
         ap.AwardingBody,
         ap.ProgramName;";

        var rows = new List<AwardProgramReportRow>();
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", System.Data.SqlDbType.Int).Value = Math.Max(1, take);
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new AwardProgramReportRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : DateOnly.FromDateTime(reader.GetDateTime(8)),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                reader.IsDBNull(11) ? null : reader.GetInt64(11),
                reader.IsDBNull(12) ? null : reader.GetString(12)));
        }

        return rows;
    }

    public async Task<BdActionRollup> GetActionRollupAsync(int topOpen, CancellationToken ct)
    {
        const string countsSql = @"
SELECT a.Status, a.ActionType, COUNT(*)
FROM opportunities.IntelAction a
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId AND co.RetiredAtUtc IS NULL
WHERE a.RetiredAtUtc IS NULL
GROUP BY a.Status, a.ActionType
ORDER BY a.Status, a.ActionType;";

        const string openSql = @"
SELECT TOP (@take) a.Id, co.DisplayName, a.ActionType, a.Recommendation, a.TargetPersonName, a.TimingNotes
FROM opportunities.IntelAction a
JOIN opportunities.CanonicalOrg co ON co.Id = a.CanonicalOrgId AND co.RetiredAtUtc IS NULL
WHERE a.RetiredAtUtc IS NULL AND a.Status = N'Open'
ORDER BY a.UpdatedAtUtc DESC;";

        var counts = new List<BdActionStatusCount>();
        var open = new List<BdOpenActionRow>();

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        await using (var cmd = new SqlCommand(countsSql, con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                counts.Add(new BdActionStatusCount(r.GetString(0), r.GetString(1), r.GetInt32(2)));
            }
        }

        await using (var cmd = new SqlCommand(openSql, con) { CommandTimeout = CommandTimeoutSeconds })
        {
            cmd.Parameters.Add("@take", System.Data.SqlDbType.Int).Value = Math.Clamp(topOpen, 1, 100);
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var recommendation = r.GetString(3);
                open.Add(new BdOpenActionRow(
                    r.GetInt64(0),
                    r.GetString(1),
                    r.GetString(2),
                    recommendation.Length <= 300 ? recommendation : recommendation[..300],
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.IsDBNull(5) ? null : r.GetString(5)));
            }
        }

        return new BdActionRollup(counts, open);
    }

    public async Task LogReportGeneratedAsync(
        string category,
        string format,
        string generatedByUser,
        int? recordCount,
        string? notes,
        CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.BdReportAuditLog (Category, Format, GeneratedByUser, RecordCount, Notes)
VALUES (@category, @format, @user, @recordCount, @notes);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@category", System.Data.SqlDbType.NVarChar, 64).Value = category;
        cmd.Parameters.Add("@format", System.Data.SqlDbType.NVarChar, 16).Value = format;
        cmd.Parameters.Add("@user", System.Data.SqlDbType.NVarChar, 256).Value = generatedByUser;
        cmd.Parameters.Add("@recordCount", System.Data.SqlDbType.Int).Value = (object?)recordCount ?? DBNull.Value;
        cmd.Parameters.Add("@notes", System.Data.SqlDbType.NVarChar, 500).Value = (object?)notes ?? DBNull.Value;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Both pursuit queries SELECT the same 13 columns in the same order;
    /// honing prose fields come from the C# parser, not JSON_VALUE (4000-char
    /// truncation).
    /// </summary>
    private static PursuitBriefRow Hydrate(SqlDataReader r)
    {
        var honing = HoningResultParser.Parse(r.IsDBNull(11) ? null : r.GetString(11));

        return new PursuitBriefRow(
            r.GetInt64(0),
            r.GetString(1),
            r.GetString(2),
            r.IsDBNull(3) ? null : r.GetString(3),
            r.IsDBNull(4) ? null : r.GetString(4),
            r.IsDBNull(5) ? null : r.GetString(5),
            r.IsDBNull(6) ? null : r.GetString(6),
            r.IsDBNull(7) ? null : r.GetDecimal(7),
            r.IsDBNull(8) ? null : r.GetString(8),
            r.IsDBNull(9) ? null : r.GetString(9),
            r.IsDBNull(10) ? null : r.GetString(10),
            honing.Verdict,
            honing.KorAngle,
            honing.Status,
            honing.Description,
            honing.OverallConfidence,
            r.IsDBNull(12) ? null : r.GetDateTimeOffset(12),
            BdVerdicts.IsUrgent(honing.Verdict, honing.KorAngle));
    }

    // ===================== Live BD visuals (teaming graph / treemap / cards) =====================
    // These re-query the live BD graph on every call, so each enrichment pass +
    // pursuit/verdict change is reflected the next time the report is opened.
    // The HTML is shared with the BdHeatGraph CLI via BdVisualHtml; here we just
    // run the same queries and serialize the data in. Rendered in the WebView2
    // preview (Chromium), exported via screenshot like the other visual reports.

    // The live "attack spine": every org sitting on a PURSUE/PURSUE_URGENT pursuit,
    // carrying that pursuit's $ and urgency so heat = OPPORTUNITY VALUE (pursuit cost
    // x urgency), not dossier completeness. Cheap — no vDossierCompleteness join
    // (that 6-CTE rubric view costs ~120s; this is sub-second). Warmth (do we know
    // people there) is a separate channel via IntelPersonAffiliation.
    private string AttackSpineCte =>
        $@"WITH HotMpi AS (
    SELECT m.Id, m.EstimatedCostCad,
           CASE WHEN {VerdictExpr} = N'PURSUE_URGENT' THEN 2.0 ELSE 1.0 END AS Urg
    FROM opportunities.MajorProjectsInventory m
    JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
    WHERE m.RetiredAtUtc IS NULL AND {VerdictExpr} IN (N'PURSUE', N'PURSUE_URGENT')),
HotOrg AS (
    SELECT DISTINCT x.OrgId, h.Id AS MpiId, h.EstimatedCostCad, h.Urg
    FROM opportunities.MajorProjectsInventory m
    JOIN HotMpi h ON h.Id = m.Id
    CROSS APPLY (VALUES (m.ArchitectCanonicalOrgId),(m.GeneralContractorCanonicalOrgId),(m.StructuralEngineerCanonicalOrgId),(m.ProponentCanonicalOrgId)) x(OrgId)
    WHERE x.OrgId IS NOT NULL)";

    public async Task<string> GetTeamingGraphHtmlAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var nodes = new List<object>();
        double maxAttack = 1; // millions of CAD x urgency
        await using (var cmd = new SqlCommand(AttackSpineCte + @"
SELECT co.Id, co.DisplayName, co.Kind,
       COUNT(DISTINCT ho.MpiId) AS Pursuits,
       CAST(SUM(ISNULL(ho.EstimatedCostCad,0)*ho.Urg) AS DECIMAL(18,2)) AS Attack,
       MAX(CASE WHEN aff.C>0 THEN 1 ELSE 0 END) AS Warm
FROM HotOrg ho
JOIN opportunities.CanonicalOrg co ON co.Id=ho.OrgId AND co.RetiredAtUtc IS NULL
OUTER APPLY (SELECT COUNT(*) C FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId=co.Id AND a.RetiredAtUtc IS NULL) aff
GROUP BY co.Id, co.DisplayName, co.Kind;", con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var attackM = (double)r.GetDecimal(4) / 1_000_000d;
                if (attackM > maxAttack) maxAttack = attackM;
                nodes.Add(new
                {
                    id = r.GetInt64(0),
                    name = r.GetString(1),
                    kind = r.GetString(2),
                    pursuits = r.GetInt32(3),
                    attack = Math.Round(attackM, 1),
                    warm = r.GetInt32(5) == 1,
                });
            }
        }

        var links = new List<object>();
        await using (var cmd = new SqlCommand(AttackSpineCte + @"
SELECT a.OrgId AS Src, b.OrgId AS Dst, COUNT(DISTINCT a.MpiId) AS W FROM HotOrg a
JOIN HotOrg b ON a.MpiId=b.MpiId AND a.OrgId<b.OrgId GROUP BY a.OrgId,b.OrgId;", con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
                links.Add(new { source = r.GetInt64(0), target = r.GetInt64(1), w = r.GetInt32(2) });
        }

        var json = JsonSerializer.Serialize(new { nodes, links });
        return BdVisualHtml.Graph(json, maxAttack, nodes.Count, links.Count);
    }

    public async Task<string> GetPriorityTreemapHtmlAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var rows = new List<object>();
        await using (var cmd = new SqlCommand(AttackSpineCte + @"
SELECT TOP 160 co.DisplayName, co.Kind,
       COUNT(DISTINCT ho.MpiId) AS Pursuits,
       CAST(SUM(ISNULL(ho.EstimatedCostCad,0)*ho.Urg) AS DECIMAL(18,2)) AS Attack,
       MAX(CASE WHEN aff.C>0 THEN 1 ELSE 0 END) AS Warm
FROM HotOrg ho
JOIN opportunities.CanonicalOrg co ON co.Id=ho.OrgId AND co.RetiredAtUtc IS NULL
OUTER APPLY (SELECT COUNT(*) C FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId=co.Id AND a.RetiredAtUtc IS NULL) aff
GROUP BY co.Id, co.DisplayName, co.Kind
ORDER BY SUM(ISNULL(ho.EstimatedCostCad,0)*ho.Urg) DESC;", con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                rows.Add(new
                {
                    name = r.GetString(0),
                    kind = r.GetString(1),
                    pursuits = r.GetInt32(2),
                    attack = Math.Round((double)r.GetDecimal(3) / 1_000_000d, 1),
                    warm = r.GetInt32(4) == 1,
                });
            }
        }

        return BdVisualHtml.Treemap(JsonSerializer.Serialize(rows), rows.Count);
    }

    public async Task<string> GetAttackCardsHtmlAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);

        var cards = new List<object>();
        var sql = $@"
SELECT m.Id, m.ProjectName, m.Province, {VerdictExpr} AS Verdict, m.EstimatedCostCad,
  arch.DisplayName AS Arch,
  (SELECT COUNT(*) FROM opportunities.IntelPersonAffiliation a WHERE a.CanonicalOrgId=arch.Id AND a.RetiredAtUtc IS NULL) AS ArchPeople,
  se.DisplayName AS Se, CASE WHEN se.Kind=N'KorStructural' THEN 1 ELSE 0 END AS SeIsKor,
  gc.DisplayName AS Gc, COALESCE(prop.DisplayName, m.ProponentName) AS Owner
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e ON e.MajorProjectsInventoryId=m.Id AND e.ProviderName=N'ProjectBriefHoning'
LEFT JOIN opportunities.CanonicalOrg arch ON arch.Id=m.ArchitectCanonicalOrgId AND arch.RetiredAtUtc IS NULL
LEFT JOIN opportunities.CanonicalOrg se ON se.Id=m.StructuralEngineerCanonicalOrgId AND se.RetiredAtUtc IS NULL
LEFT JOIN opportunities.CanonicalOrg gc ON gc.Id=m.GeneralContractorCanonicalOrgId AND gc.RetiredAtUtc IS NULL
LEFT JOIN opportunities.CanonicalOrg prop ON prop.Id=m.ProponentCanonicalOrgId AND prop.RetiredAtUtc IS NULL
WHERE m.RetiredAtUtc IS NULL AND {VerdictExpr} IN (N'PURSUE', N'PURSUE_URGENT')
ORDER BY CASE WHEN {VerdictExpr}=N'PURSUE_URGENT' THEN 0 ELSE 1 END, m.EstimatedCostCad DESC;";

        await using (var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds })
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                cards.Add(new
                {
                    id = r.GetInt64(0),
                    project = r.GetString(1),
                    province = r.IsDBNull(2) ? "" : r.GetString(2),
                    verdict = r.IsDBNull(3) ? "" : r.GetString(3),
                    cost = r.IsDBNull(4) ? (decimal?)null : r.GetDecimal(4),
                    arch = r.IsDBNull(5) ? null : r.GetString(5),
                    archPeople = r.GetInt32(6),
                    se = r.IsDBNull(7) ? null : r.GetString(7),
                    seIsKor = r.GetInt32(8) == 1,
                    gc = r.IsDBNull(9) ? null : r.GetString(9),
                    owner = r.IsDBNull(10) ? null : r.GetString(10),
                });
            }
        }

        return BdVisualHtml.Cards(JsonSerializer.Serialize(cards), cards.Count);
    }

}
