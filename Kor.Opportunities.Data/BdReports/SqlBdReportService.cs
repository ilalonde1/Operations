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

            await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
            await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
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

            var total = pursueUrgent + pursue + monitor + discover + dead + duplicate + noVerdict;
            summaries.Add(new SectorVerdictSummary(
                def.Key, def.Title, pursueUrgent, pursue, monitor, discover, dead, duplicate, noVerdict, total));
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
            var honing = HoningResultParser.Parse(r.IsDBNull(11) ? null : r.GetString(11));

            rows.Add(new PursuitBriefRow(
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
                BdVerdicts.IsUrgent(honing.Verdict, honing.KorAngle)));
        }

        return rows
            .OrderBy(x => BdVerdicts.Rank(x.Verdict))
            .ThenByDescending(x => x.EstimatedCostCad ?? decimal.MinValue)
            .ThenBy(x => x.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
