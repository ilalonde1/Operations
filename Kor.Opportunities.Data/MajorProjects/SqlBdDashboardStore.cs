#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.MajorProjects;

public sealed class SqlBdDashboardStore : IBdDashboardStore
{
    private const int CommandTimeoutSeconds = 60;
    private readonly string _connectionString;

    public SqlBdDashboardStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<BdOpenStructuralSeatRow>> GetOpenStructuralSeatsAsync(int take, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@take)
       co.Id,
       co.DisplayName,
       JSON_VALUE(e.ResultJson,'$.market') AS Market,
       JSON_VALUE(e.ResultJson,'$.structuralPartnerStatus') AS Status,
       JSON_VALUE(e.ResultJson,'$.korPriority') AS Priority,
       JSON_VALUE(e.ResultJson,'$.korDisplacementRead') AS DisplacementRead
FROM opportunities.CanonicalOrgEnrichment e
JOIN opportunities.CanonicalOrg co ON co.Id = e.CanonicalOrgId
WHERE e.ProviderName = N'StructuralPartnerMap'
  AND JSON_VALUE(e.ResultJson,'$.structuralPartnerStatus') IN (N'open', N'rotating')
  AND JSON_VALUE(e.ResultJson,'$.korPriority') = N'high'
ORDER BY co.DisplayName;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = ClampTake(take);

        var rows = new List<BdOpenStructuralSeatRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new BdOpenStructuralSeatRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetString(5)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<BdCompetitorWatchRow>> GetCompetitorWatchAsync(int take, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@take)
       co.Id,
       co.DisplayName,
       JSON_VALUE(e.ResultJson,'$.capacityRead') AS CapacityRead
FROM opportunities.CanonicalOrgEnrichment e
JOIN opportunities.CanonicalOrg co ON co.Id = e.CanonicalOrgId
WHERE e.ProviderName = N'CompetitorSignals'
ORDER BY co.DisplayName;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = ClampTake(take);

        var rows = new List<BdCompetitorWatchRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new BdCompetitorWatchRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<BdForwardPipelineRow>> GetForwardPipelineAsync(int take, CancellationToken ct)
    {
        const string sql = @"
SELECT TOP (@take)
       Id,
       ProjectName,
       ProponentName,
       Province,
       Sector,
       EstimatedCostCad,
       Stage
FROM opportunities.MajorProjectsInventory
WHERE ProjectStage IN (N'CapitalPlan', N'FacilityRenewal')
ORDER BY EstimatedCostCad DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = ClampTake(take);

        var rows = new List<BdForwardPipelineRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new BdForwardPipelineRow(
                r.GetInt64(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.IsDBNull(4) ? null : r.GetString(4),
                r.IsDBNull(5) ? null : r.GetDecimal(5),
                r.IsDBNull(6) ? null : r.GetString(6)));
        }

        return rows;
    }

    public async Task<PipelineFunnel> GetPipelineFunnelAsync(CancellationToken ct)
    {
        const string sql = @"
WITH Base AS
(
    SELECT Stage, SeatStatus
    FROM opportunities.MajorProjectsInventory
    WHERE RetiredAtUtc IS NULL
      AND (
            Sector LIKE N'%school%' OR Sector LIKE N'%hospital%' OR Sector LIKE N'%health%'
         OR Sector LIKE N'%recreation%' OR Sector LIKE N'%civic%' OR Sector LIKE N'%cultural%'
         OR Sector LIKE N'%universit%' OR Sector LIKE N'%college%' OR Sector LIKE N'%library%'
         OR Sector LIKE N'%communit%' OR Sector LIKE N'%education%' OR Sector LIKE N'%housing%'
         OR Sector LIKE N'%institution%' OR Sector LIKE N'%care%'
         OR Sector LIKE N'%fire%' OR Sector LIKE N'%police%'
         OR Sector IN (N'Civic', N'Tourism / Recreation', N'Government', N'Mixed-use')
          )
)
SELECT
    COUNT_BIG(*) AS RadarCount,
    SUM(CASE
            WHEN Stage LIKE N'%procure%'
              OR Stage LIKE N'%RFP%'
              OR Stage LIKE N'%RFQ%'
              OR Stage LIKE N'%tender%'
              OR Stage LIKE N'%design%' THEN 1
            ELSE 0
        END) AS BidWindowCount,
    SUM(CASE WHEN SeatStatus = N'likely-open' THEN 1 ELSE 0 END) AS OpenSeatCount
FROM Base;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };

        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await r.ReadAsync(ct).ConfigureAwait(false))
        {
            return new PipelineFunnel(0, 0, 0);
        }

        return new PipelineFunnel(
            ToInt32(r.GetInt64(0)),
            r.IsDBNull(1) ? 0 : ToInt32(Convert.ToInt64(r.GetValue(1), System.Globalization.CultureInfo.InvariantCulture)),
            r.IsDBNull(2) ? 0 : ToInt32(Convert.ToInt64(r.GetValue(2), System.Globalization.CultureInfo.InvariantCulture)));
    }

    public async Task<IReadOnlyList<DataHealthRow>> GetDataHealthAsync(CancellationToken ct)
    {
        const string sql = @"
SELECT N'Orgs' AS Category, Kind AS Label, COUNT_BIG(*) AS CountValue
FROM opportunities.CanonicalOrg
WHERE Kind IN (N'Architect', N'Competitor', N'GC', N'Subcontractor', N'Developer', N'Buyer', N'KorClient')
GROUP BY Kind

UNION ALL

SELECT N'Enrichment' AS Category, ProviderName AS Label, COUNT_BIG(DISTINCT CanonicalOrgId) AS CountValue
FROM opportunities.CanonicalOrgEnrichment
GROUP BY ProviderName

UNION ALL

SELECT N'MPI (BC)' AS Category, N'architect-resolved' AS Label, COUNT_BIG(*) AS CountValue
FROM opportunities.MajorProjectsInventory
WHERE Province = N'BC' AND ArchitectCanonicalOrgId IS NOT NULL

UNION ALL

SELECT N'MPI (BC)' AS Category, N'structural-resolved' AS Label, COUNT_BIG(*) AS CountValue
FROM opportunities.MajorProjectsInventory
WHERE Province = N'BC' AND StructuralEngineerCanonicalOrgId IS NOT NULL

UNION ALL

SELECT N'MPI (BC)' AS Category, N'total' AS Label, COUNT_BIG(*) AS CountValue
FROM opportunities.MajorProjectsInventory
WHERE Province = N'BC'

ORDER BY Category, Label;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };

        var rows = new List<DataHealthRow>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new DataHealthRow(
                r.GetString(0),
                r.GetString(1),
                r.GetInt64(2)));
        }

        return rows;
    }

    private static int ClampTake(int take)
        => Math.Clamp(take, 1, 1000);

    private static int ToInt32(long value)
        => value > int.MaxValue ? int.MaxValue : (int)value;
}
