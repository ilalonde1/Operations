#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

public sealed class SqlCrmActivityStore : ICrmActivityStore
{
    private const int CommandTimeoutSeconds = 30;

    private const string AllColumns = @"
Id, EngagementId, ActivityType, OccurredAtUtc, Subject, Body, ContactId,
CreatedAtUtc, CreatedBy";

    private readonly string _connectionString;

    public SqlCrmActivityStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<CrmActivity> AppendAsync(CrmActivity activity, string actorDisplay, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.CrmActivities
    (EngagementId, ActivityType, OccurredAtUtc, Subject, Body, ContactId, CreatedBy)
OUTPUT
    inserted.Id, inserted.EngagementId, inserted.ActivityType, inserted.OccurredAtUtc,
    inserted.Subject, inserted.Body, inserted.ContactId,
    inserted.CreatedAtUtc, inserted.CreatedBy
VALUES
    (@engId, @type, @occurredAt, @subject, @body, @contactId, @actor);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@engId", SqlDbType.BigInt).Value = activity.EngagementId;
        cmd.Parameters.Add("@type", SqlDbType.Int).Value = (int)activity.ActivityType;
        cmd.Parameters.Add("@occurredAt", SqlDbType.DateTimeOffset).Value = activity.OccurredAtUtc;
        cmd.Parameters.Add("@subject", SqlDbType.NVarChar, 300).Value = activity.Subject ?? string.Empty;
        cmd.Parameters.Add("@body", SqlDbType.NVarChar, -1).Value = (object?)activity.Body ?? DBNull.Value;
        cmd.Parameters.Add("@contactId", SqlDbType.BigInt).Value = (object?)activity.ContactId ?? DBNull.Value;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("INSERT did not return a row.");
        }

        return MapReader(reader);
    }

    public async Task<IReadOnlyList<CrmActivity>> ListByEngagementAsync(long engagementId, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.CrmActivities
WHERE EngagementId = @engId
ORDER BY OccurredAtUtc DESC, Id DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@engId", SqlDbType.BigInt).Value = engagementId;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<CrmActivity>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(reader));
        }

        return rows;
    }

    private static CrmActivity MapReader(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        EngagementId = r.GetInt64(1),
        ActivityType = (CrmActivityType)r.GetInt32(2),
        OccurredAtUtc = r.GetDateTimeOffset(3),
        Subject = r.GetString(4),
        Body = r.IsDBNull(5) ? null : r.GetString(5),
        ContactId = r.IsDBNull(6) ? null : r.GetInt64(6),
        CreatedAtUtc = r.GetDateTimeOffset(7),
        CreatedBy = r.GetString(8),
    };
}
