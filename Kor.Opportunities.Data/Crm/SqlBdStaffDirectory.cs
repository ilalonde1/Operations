#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <inheritdoc cref="IBdStaffDirectory"/>
public sealed class SqlBdStaffDirectory : IBdStaffDirectory
{
    private readonly string _cs;

    public SqlBdStaffDirectory(string connectionString)
    {
        _cs = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
    }

    public async Task<string?> ResolveMailboxAsync(string staffKey, CancellationToken ct)
    {
        var key = staffKey?.Trim();
        if (string.IsNullOrEmpty(key)) return null;

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT Mailbox FROM opportunities.BdStaff WHERE StaffKey = @k AND IsActive = 1 AND Mailbox IS NOT NULL;",
            con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 150).Value = key;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is string s && !string.IsNullOrWhiteSpace(s) ? s : null;
    }

    public async Task<bool> IsKnownAsync(string staffKey, CancellationToken ct)
    {
        var key = staffKey?.Trim();
        if (string.IsNullOrEmpty(key)) return false;

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT TOP 1 1 FROM opportunities.BdStaff WHERE StaffKey = @k;", con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 150).Value = key;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result is not null;
    }

    public async Task<IReadOnlyList<BdStaffRow>> GetAllAsync(CancellationToken ct)
    {
        var rows = new List<BdStaffRow>();
        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "SELECT Id, StaffKey, Mailbox, DisplayName, IsActive, Source FROM opportunities.BdStaff ORDER BY StaffKey;",
            con) { CommandTimeout = 30 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new BdStaffRow(
                r.GetInt32(0),
                r.GetString(1),
                r.IsDBNull(2) ? null : r.GetString(2),
                r.IsDBNull(3) ? null : r.GetString(3),
                r.GetBoolean(4),
                r.GetString(5)));
        }

        return rows;
    }

    public async Task UpsertAsync(string staffKey, string? mailbox, string? displayName, bool isActive, string source, CancellationToken ct)
    {
        var key = staffKey?.Trim();
        if (string.IsNullOrEmpty(key)) return;

        const string sql = @"
SET XACT_ABORT ON;
BEGIN TRAN;

UPDATE opportunities.BdStaff WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET Mailbox      = @mailbox,
    DisplayName  = COALESCE(@displayName, DisplayName),
    IsActive     = @isActive,
    Source       = @source,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE StaffKey = @k;

IF @@ROWCOUNT = 0
    INSERT INTO opportunities.BdStaff (StaffKey, Mailbox, DisplayName, IsActive, Source)
    VALUES (@k, @mailbox, @displayName, @isActive, @source);

COMMIT TRAN;";

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 150).Value = key;
        cmd.Parameters.Add("@mailbox", SqlDbType.NVarChar, 256).Value = (object?)Trim(mailbox) ?? DBNull.Value;
        cmd.Parameters.Add("@displayName", SqlDbType.NVarChar, 200).Value = (object?)Trim(displayName) ?? DBNull.Value;
        cmd.Parameters.Add("@isActive", SqlDbType.Bit).Value = isActive;
        cmd.Parameters.Add("@source", SqlDbType.NVarChar, 40).Value = string.IsNullOrWhiteSpace(source) ? "sync" : source.Trim();
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> FillMailboxIfEmptyAsync(string staffKey, string mailbox, string? displayName, string source, CancellationToken ct)
    {
        var key = staffKey?.Trim();
        var mail = mailbox?.Trim();
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(mail)) return false;

        await using var con = new SqlConnection(_cs);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(@"
UPDATE opportunities.BdStaff
SET Mailbox      = @mailbox,
    DisplayName  = COALESCE(DisplayName, @displayName),
    Source       = @source,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE StaffKey = @k AND Mailbox IS NULL;", con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@k", SqlDbType.NVarChar, 150).Value = key;
        cmd.Parameters.Add("@mailbox", SqlDbType.NVarChar, 256).Value = mail;
        cmd.Parameters.Add("@displayName", SqlDbType.NVarChar, 200).Value = (object?)Trim(displayName) ?? DBNull.Value;
        cmd.Parameters.Add("@source", SqlDbType.NVarChar, 40).Value = string.IsNullOrWhiteSpace(source) ? "sync" : source.Trim();
        var affected = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        return affected > 0;
    }

    private static string? Trim(string? v)
    {
        var t = v?.Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }
}
