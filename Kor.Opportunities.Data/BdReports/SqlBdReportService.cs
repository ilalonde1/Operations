#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

            // Freshness buckets (decision 6: 30/90-day boundaries) over rows
            // that HAVE honing data — never-honed rows are NoVerdict, not stale.
            var freshnessSql = $@"
SELECT SUM(CASE WHEN e.LastRefreshAtUtc >= DATEADD(day, -30, sysdatetimeoffset()) THEN 1 ELSE 0 END),
       SUM(CASE WHEN e.LastRefreshAtUtc <  DATEADD(day, -30, sysdatetimeoffset())
                 AND e.LastRefreshAtUtc >= DATEADD(day, -90, sysdatetimeoffset()) THEN 1 ELSE 0 END),
       SUM(CASE WHEN e.LastRefreshAtUtc <  DATEADD(day, -90, sysdatetimeoffset())
                 OR e.LastRefreshAtUtc IS NULL THEN 1 ELSE 0 END),
       COALESCE(SUM(m.EstimatedCostCad), 0)
FROM opportunities.MajorProjectsInventory m
JOIN opportunities.MajorProjectEnrichment e
  ON e.MajorProjectsInventoryId = m.Id AND e.ProviderName = N'ProjectBriefHoning'
WHERE m.RetiredAtUtc IS NULL
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
        // sector filters by design).
        const string sql = @"
SELECT COUNT(*),
       SUM(CASE WHEN e.Id IS NOT NULL THEN 1 ELSE 0 END),
       COALESCE(SUM(CASE WHEN e.Id IS NOT NULL THEN m.EstimatedCostCad END), 0)
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
}
