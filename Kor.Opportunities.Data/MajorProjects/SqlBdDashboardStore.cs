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

    private static int ClampTake(int take)
        => Math.Clamp(take, 1, 1000);
}
