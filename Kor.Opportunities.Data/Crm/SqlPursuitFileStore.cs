#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// One attachment on a pursuit — the DB row. Bytes live on the LAN share at
/// <see cref="LocalPath"/>; this record is just the index entry.
/// </summary>
public sealed record PursuitFile(
    long Id,
    long EngagementId,
    long? OpportunityId,
    string FileName,
    string LocalPath,
    byte[]? Sha256,
    long? SizeBytes,
    string? ContentType,
    DateTimeOffset UploadedAtUtc,
    string UploadedBy);

/// <summary>
/// Index of pursuit attachments in opportunities.OpportunityFiles (wired for
/// use by migration 275). Keyed on EngagementId — every pursuit is a
/// CrmEngagement, including BD-tracking ones with no parent Opportunity. The
/// store never touches the file bytes; the app-side storage service owns the
/// share copy/delete.
/// </summary>
public interface IPursuitFileStore
{
    Task<IReadOnlyList<PursuitFile>> ListByEngagementAsync(long engagementId, CancellationToken ct);

    Task<PursuitFile> AddAsync(
        long engagementId, long? opportunityId, string fileName, string localPath,
        byte[]? sha256, long? sizeBytes, string? contentType, string uploadedBy, CancellationToken ct);

    /// <summary>Returns the removed row's <see cref="PursuitFile.LocalPath"/> so
    /// the caller can delete the physical file, or null if the row was gone.</summary>
    Task<string?> RemoveAsync(long id, CancellationToken ct);
}

public sealed class SqlPursuitFileStore : IPursuitFileStore
{
    private const int CommandTimeoutSeconds = 30;

    private const string AllColumns =
        "Id, EngagementId, OpportunityId, FileName, LocalPath, Sha256, SizeBytes, ContentType, UploadedAtUtc, UploadedBy";

    private readonly string _connectionString;

    public SqlPursuitFileStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string is required.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    public async Task<IReadOnlyList<PursuitFile>> ListByEngagementAsync(long engagementId, CancellationToken ct)
    {
        var sql = $@"
SELECT {AllColumns}
FROM opportunities.OpportunityFiles
WHERE EngagementId = @eng
ORDER BY UploadedAtUtc DESC, Id DESC;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);

        var rows = new List<PursuitFile>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(Map(reader));
        }

        return rows;
    }

    public async Task<PursuitFile> AddAsync(
        long engagementId, long? opportunityId, string fileName, string localPath,
        byte[]? sha256, long? sizeBytes, string? contentType, string uploadedBy, CancellationToken ct)
    {
        var sql = $@"
INSERT INTO opportunities.OpportunityFiles
    (EngagementId, OpportunityId, FileName, LocalPath, Sha256, SizeBytes, ContentType, UploadedBy)
OUTPUT {PrefixInserted(AllColumns)}
VALUES (@eng, @opp, @name, @path, @sha, @size, @type, @by);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@opp", SqlDbType.BigInt).Value = (object?)opportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 500).Value = fileName;
        cmd.Parameters.Add("@path", SqlDbType.NVarChar, 2000).Value = localPath;
        cmd.Parameters.Add("@sha", SqlDbType.VarBinary, 32).Value = (object?)sha256 ?? DBNull.Value;
        cmd.Parameters.Add("@size", SqlDbType.BigInt).Value = (object?)sizeBytes ?? DBNull.Value;
        cmd.Parameters.Add("@type", SqlDbType.NVarChar, 200).Value = (object?)contentType ?? DBNull.Value;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = uploadedBy;

        await using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        if (!await reader.ReadAsync(ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("INSERT did not return a row.");
        }

        return Map(reader);
    }

    public async Task<string?> RemoveAsync(long id, CancellationToken ct)
    {
        const string sql = @"
DELETE FROM opportunities.OpportunityFiles
OUTPUT deleted.LocalPath
WHERE Id = @id;";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        var result = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        return result as string;
    }

    private static string PrefixInserted(string columns)
    {
        var parts = columns.Split(',', StringSplitOptions.TrimEntries);
        return string.Join(", ", Array.ConvertAll(parts, c => "inserted." + c));
    }

    private static PursuitFile Map(SqlDataReader r) => new(
        Id: r.GetInt64(0),
        EngagementId: r.GetInt64(1),
        OpportunityId: r.IsDBNull(2) ? null : r.GetInt64(2),
        FileName: r.GetString(3),
        LocalPath: r.GetString(4),
        Sha256: r.IsDBNull(5) ? null : (byte[])r.GetValue(5),
        SizeBytes: r.IsDBNull(6) ? null : r.GetInt64(6),
        ContentType: r.IsDBNull(7) ? null : r.GetString(7),
        UploadedAtUtc: r.GetDateTimeOffset(8),
        UploadedBy: r.GetString(9));
}
