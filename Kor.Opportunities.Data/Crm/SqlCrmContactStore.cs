#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

public sealed class SqlCrmContactStore : ICrmContactStore
{
    private const int CommandTimeoutSeconds = 30;

    private const string AllColumns = @"
Id, EngagementId, DisplayName, Role, Email, Phone, IsPrimary, Notes,
CreatedAtUtc, CreatedBy, UpdatedAtUtc, UpdatedBy, RowVersion";

    private readonly string _connectionString;

    public SqlCrmContactStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<CrmContact> InsertAsync(CrmContact contact, string actorDisplay, CancellationToken ct)
    {
        const string sql = @"
INSERT INTO opportunities.CrmContacts
    (EngagementId, DisplayName, Role, Email, Phone, IsPrimary, Notes, CreatedBy, UpdatedBy)
OUTPUT
    inserted.Id, inserted.EngagementId, inserted.DisplayName, inserted.Role, inserted.Email,
    inserted.Phone, inserted.IsPrimary, inserted.Notes,
    inserted.CreatedAtUtc, inserted.CreatedBy, inserted.UpdatedAtUtc, inserted.UpdatedBy, inserted.RowVersion
VALUES
    (@engId, @name, @role, @email, @phone, @isPrimary, @notes, @actor, @actor);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindContactParams(cmd, contact);
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("INSERT did not return a row.");
        }

        return MapReader(reader);
    }

    public async Task<CrmContact> UpdateAsync(CrmContact contact, string actorDisplay, CancellationToken ct)
    {
        const string sql = @"
UPDATE opportunities.CrmContacts
SET DisplayName = @name,
    Role = @role,
    Email = @email,
    Phone = @phone,
    IsPrimary = @isPrimary,
    Notes = @notes,
    UpdatedAtUtc = sysdatetimeoffset(),
    UpdatedBy = @actor
OUTPUT
    inserted.Id, inserted.EngagementId, inserted.DisplayName, inserted.Role, inserted.Email,
    inserted.Phone, inserted.IsPrimary, inserted.Notes,
    inserted.CreatedAtUtc, inserted.CreatedBy, inserted.UpdatedAtUtc, inserted.UpdatedBy, inserted.RowVersion
WHERE Id = @id AND RowVersion = @rv;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        BindContactParams(cmd, contact);
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = contact.Id;
        cmd.Parameters.Add("@rv", SqlDbType.Binary, 8).Value = contact.RowVersion;
        cmd.Parameters.Add("@actor", SqlDbType.NVarChar, 150).Value = actorDisplay;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new CrmConcurrencyException(nameof(CrmContact), contact.Id);
        }

        return MapReader(reader);
    }

    public async Task DeleteAsync(long contactId, byte[] rowVersion, CancellationToken ct)
    {
        // Guarded like Insert/Update: a stale UI can't delete a contact someone
        // just edited (audit 2026-07-01 n8 — this was the only unguarded hard
        // delete in the BD model).
        const string sql = @"
DELETE FROM opportunities.CrmContacts WHERE Id = @id AND RowVersion = @rv;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = contactId;
        cmd.Parameters.Add("@rv", SqlDbType.Timestamp).Value = rowVersion;
        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        if (affected == 0)
        {
            throw new CrmConcurrencyException(nameof(CrmContact), contactId);
        }
    }

    public async Task<IReadOnlyList<CrmContact>> ListByEngagementAsync(long engagementId, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.CrmContacts
WHERE EngagementId = @engId
ORDER BY IsPrimary DESC, DisplayName;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@engId", SqlDbType.BigInt).Value = engagementId;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<CrmContact>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(MapReader(reader));
        }

        return rows;
    }

    private static void BindContactParams(SqlCommand cmd, CrmContact c)
    {
        cmd.Parameters.Add("@engId", SqlDbType.BigInt).Value = c.EngagementId;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = c.DisplayName ?? string.Empty;
        cmd.Parameters.Add("@role", SqlDbType.NVarChar, 150).Value = (object?)c.Role ?? DBNull.Value;
        cmd.Parameters.Add("@email", SqlDbType.NVarChar, 300).Value = (object?)c.Email ?? DBNull.Value;
        cmd.Parameters.Add("@phone", SqlDbType.NVarChar, 60).Value = (object?)c.Phone ?? DBNull.Value;
        cmd.Parameters.Add("@isPrimary", SqlDbType.Bit).Value = c.IsPrimary;
        cmd.Parameters.Add("@notes", SqlDbType.NVarChar, -1).Value = (object?)c.Notes ?? DBNull.Value;
    }

    private static CrmContact MapReader(SqlDataReader r) => new()
    {
        Id = r.GetInt64(0),
        EngagementId = r.GetInt64(1),
        DisplayName = r.GetString(2),
        Role = r.IsDBNull(3) ? null : r.GetString(3),
        Email = r.IsDBNull(4) ? null : r.GetString(4),
        Phone = r.IsDBNull(5) ? null : r.GetString(5),
        IsPrimary = r.GetBoolean(6),
        Notes = r.IsDBNull(7) ? null : r.GetString(7),
        CreatedAtUtc = r.GetDateTimeOffset(8),
        CreatedBy = r.GetString(9),
        UpdatedAtUtc = r.GetDateTimeOffset(10),
        UpdatedBy = r.GetString(11),
        RowVersion = (byte[])r.GetValue(12),
    };
}
