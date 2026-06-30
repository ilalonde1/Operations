#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>Wins (and won fee) for one source feed.</summary>
public sealed record AttributionSourceRow(string Source, int Wins, decimal WonFee);

/// <summary>Wins (and won fee) for one owner.</summary>
public sealed record AttributionOwnerRow(string Owner, int Wins, decimal WonFee);

/// <summary>
/// BD attribution scorecard, CRM-sourced. Honest by construction: it reports
/// only what the CRM actually carries (wins, won fee, active/submitted/lost
/// counts, and the breakdowns that have data). Owner / source / win-rate
/// dimensions fill in as grabbed pursuits convert to wins carrying an owner
/// and a source feed — see the BD pursuit-lifecycle design §9.
/// </summary>
public sealed record BdAttributionSummary(
    int Wins,
    decimal WonFee,
    int ActivePursuits,
    int Submitted,
    int Lost,
    IReadOnlyList<AttributionSourceRow> BySource,
    IReadOnlyList<AttributionOwnerRow> ByOwner);

public interface IBdAttributionStore
{
    Task<BdAttributionSummary> GetSummaryAsync(CancellationToken ct);
}

public sealed class SqlBdAttributionStore : IBdAttributionStore
{
    private const int CommandTimeoutSeconds = 30;

    // CrmEngagementStage: Drafting=1, Submitted=3, Won=6, Lost=7.
    private const string SummarySql = @"
SELECT
    SUM(CASE WHEN Stage = 6 THEN 1 ELSE 0 END)                                          AS Wins,
    SUM(CASE WHEN Stage = 6 THEN CAST(ISNULL(ProposedFee, 0) AS decimal(18,2)) ELSE 0 END) AS WonFee,
    SUM(CASE WHEN Stage IN (1, 3) THEN 1 ELSE 0 END)                                    AS ActivePursuits,
    SUM(CASE WHEN Stage = 3 THEN 1 ELSE 0 END)                                          AS Submitted,
    SUM(CASE WHEN Stage = 7 THEN 1 ELSE 0 END)                                          AS Lost
FROM opportunities.CrmEngagements;

SELECT
    COALESCE(NULLIF(LTRIM(RTRIM(ExternalSource)), N''), N'(unrecorded)') AS Source,
    COUNT(*)                                                            AS Wins,
    SUM(CAST(ISNULL(ProposedFee, 0) AS decimal(18,2)))                  AS WonFee
FROM opportunities.CrmEngagements
WHERE Stage = 6
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(ExternalSource)), N''), N'(unrecorded)')
ORDER BY Wins DESC;

SELECT
    COALESCE(NULLIF(LTRIM(RTRIM(OwnerStaffId)), N''), N'(unattributed)') AS Owner,
    COUNT(*)                                                            AS Wins,
    SUM(CAST(ISNULL(ProposedFee, 0) AS decimal(18,2)))                  AS WonFee
FROM opportunities.CrmEngagements
WHERE Stage = 6
GROUP BY COALESCE(NULLIF(LTRIM(RTRIM(OwnerStaffId)), N''), N'(unattributed)')
ORDER BY Wins DESC;";

    private readonly string _connectionString;

    public SqlBdAttributionStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<BdAttributionSummary> GetSummaryAsync(CancellationToken ct)
    {
        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(SummarySql, con) { CommandTimeout = CommandTimeoutSeconds };
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        // Result 1: scalar summary.
        int wins = 0, active = 0, submitted = 0, lost = 0;
        decimal wonFee = 0m;
        if (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            wins = reader.IsDBNull(0) ? 0 : reader.GetInt32(0);
            wonFee = reader.IsDBNull(1) ? 0m : reader.GetDecimal(1);
            active = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
            submitted = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
            lost = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
        }

        // Result 2: wins by source feed.
        var bySource = new List<AttributionSourceRow>();
        await reader.NextResultAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            bySource.Add(new AttributionSourceRow(reader.GetString(0), reader.GetInt32(1), reader.GetDecimal(2)));
        }

        // Result 3: wins by owner.
        var byOwner = new List<AttributionOwnerRow>();
        await reader.NextResultAsync(ct).ConfigureAwait(false);
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            byOwner.Add(new AttributionOwnerRow(reader.GetString(0), reader.GetInt32(1), reader.GetDecimal(2)));
        }

        return new BdAttributionSummary(wins, wonFee, active, submitted, lost, bySource, byOwner);
    }
}
