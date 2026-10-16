#nullable enable
using System.Data;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

public sealed class SqlPursuitRequirementStore(string connectionString) : IPursuitRequirementStore
{
    public async Task<IReadOnlyList<PursuitRequirement>> ListAsync(long engagementId, CancellationToken ct)
    {
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            SELECT Id, EngagementId, Ordinal, Requirement, SourceRef, CheckKind, CheckParameter,
                Status, StatusNote, CheckedBy, CheckedAtUtc, CreatedBy, CreatedAtUtc
            FROM opportunities.PursuitRequirement WHERE EngagementId = @eng ORDER BY Ordinal, Id;
            """, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var rows = new List<PursuitRequirement>();
        while (await r.ReadAsync(ct).ConfigureAwait(false))
            rows.Add(new(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetString(3), r.GetString(4),
                r.GetString(5), Text(r, 6), r.GetString(7), Text(r, 8), Text(r, 9),
                r.IsDBNull(10) ? null : r.GetDateTimeOffset(10), r.GetString(11), r.GetDateTimeOffset(12)));
        return rows;
    }

    public Task AddAsync(long engagementId, IReadOnlyList<PursuitRequirementInput> items, string by, string? note, CancellationToken ct)
        => WriteAsync(engagementId, items, by, note, import: false, replace: false, ct);

    public Task ImportAsync(long engagementId, IReadOnlyList<PursuitRequirementInput> items, string by, bool replace, CancellationToken ct)
        => WriteAsync(engagementId, items, by, null, import: true, replace, ct);

    private async Task WriteAsync(long engagementId, IReadOnlyList<PursuitRequirementInput> items, string by,
        string? note, bool import, bool replace, CancellationToken ct)
    {
        // Snapshot the caller's list and validate everything before connecting or starting the single transaction.
        var validated = items.ToArray();
        PursuitRequirementValidation.Validate(validated);
        PursuitRequirementValidation.Identity(engagementId, by);
        ValidateNote(note);
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        try
        {
            // Lock the parent even for an empty checklist, serializing add/import for this pursuit.
            await using var parent = new SqlCommand("SELECT Id FROM opportunities.CrmEngagements WITH (UPDLOCK, HOLDLOCK) WHERE Id = @eng;", con, tx) { CommandTimeout = 30 };
            parent.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
            if (await parent.ExecuteScalarAsync(ct).ConfigureAwait(false) is null)
                throw new InvalidOperationException("The pursuit no longer exists.");
            await using var count = new SqlCommand("SELECT COALESCE(MAX(Ordinal), 0), COUNT(*) FROM opportunities.PursuitRequirement WITH (UPDLOCK, HOLDLOCK) WHERE EngagementId = @eng;", con, tx) { CommandTimeout = 30 };
            count.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
            int ordinal, existing;
            await using (var r = await count.ExecuteReaderAsync(ct).ConfigureAwait(false))
            {
                await r.ReadAsync(ct).ConfigureAwait(false);
                ordinal = r.GetInt32(0); existing = r.GetInt32(1);
            }
            if (import && existing > 0 && !replace)
                throw new InvalidOperationException("The checklist is not empty. Use --replace to replace it.");
            if (import && replace)
            {
                await using var delete = new SqlCommand("DELETE FROM opportunities.PursuitRequirement WHERE EngagementId = @eng;", con, tx) { CommandTimeout = 30 };
                delete.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
                await delete.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                ordinal = 0;
            }
            foreach (var item in validated)
            {
                await using var cmd = new SqlCommand("""
                    INSERT INTO opportunities.PursuitRequirement
                        (EngagementId, Ordinal, Requirement, SourceRef, CheckKind, CheckParameter, StatusNote, CreatedBy)
                    VALUES (@eng, @ordinal, @requirement, @source, @kind, @parameter, @note, @by);
                    """, con, tx) { CommandTimeout = 30 };
                cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
                cmd.Parameters.Add("@ordinal", SqlDbType.Int).Value = ++ordinal;
                AddText(cmd, "@requirement", 1000, item.Requirement);
                AddText(cmd, "@source", 200, item.SourceRef);
                AddText(cmd, "@kind", 20, item.CheckKind);
                AddText(cmd, "@parameter", 400, item.CheckParameter);
                AddText(cmd, "@note", 500, note);
                AddText(cmd, "@by", 150, by);
                await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            }
            await tx.CommitAsync(ct).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }
    }

    public async Task UpdateStatusAsync(long engagementId, long id, string status, string? note, string by,
        bool automatic, CancellationToken ct)
    {
        PursuitRequirementValidation.Identity(engagementId, by);
        if (!PursuitRequirementValidation.Statuses.Contains(status)) throw new ArgumentException("Unknown checklist status.");
        if (automatic && status == "NotApplicable") throw new ArgumentException("Only a person can mark an item not applicable.");
        ValidateNote(note);
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("""
            UPDATE opportunities.PursuitRequirement
            SET Status = @status, StatusNote = @note, CheckedBy = @by, CheckedAtUtc = SYSDATETIMEOFFSET()
            WHERE Id = @id AND EngagementId = @eng AND (@automatic = 0 OR CheckKind <> N'Manual');
            """, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@automatic", SqlDbType.Bit).Value = automatic;
        AddText(cmd, "@status", 20, status); AddText(cmd, "@note", 500, note); AddText(cmd, "@by", 150, by);
        if (await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) != 1)
            throw new InvalidOperationException("The checklist item changed or no longer exists. Reload the checklist.");
    }

    private static void ValidateNote(string? note)
    {
        if (note?.Length > 500) throw new ArgumentException("The status note must be at most 500 characters.");
    }
    private static void AddText(SqlCommand cmd, string name, int length, string? value)
        => cmd.Parameters.Add(name, SqlDbType.NVarChar, length).Value = (object?)value ?? DBNull.Value;
    private static string? Text(SqlDataReader r, int index) => r.IsDBNull(index) ? null : r.GetString(index);
}
