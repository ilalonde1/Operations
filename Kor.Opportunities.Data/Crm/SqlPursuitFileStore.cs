#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Data.Crm;

/// <summary>
/// Attachment metadata only. Content is streamed separately; LocalPath is legacy-only.
/// </summary>
public sealed record PursuitFile(
    long Id,
    long EngagementId,
    long? OpportunityId,
    string FileName,
    string? LocalPath,
    byte[]? Sha256,
    long? SizeBytes,
    string? ContentType,
    DateTimeOffset UploadedAtUtc,
    string UploadedBy,
    bool HasContent = false);

/// <summary>
/// Index of pursuit attachments in opportunities.OpportunityFiles (wired for
/// use by migration 275). Keyed on EngagementId — every pursuit is a
/// CrmEngagement, including BD-tracking ones with no parent Opportunity. The
/// store streams bytes into the existing row. Lists return metadata only.
/// </summary>
public interface IPursuitFileStore
{
    Task<IReadOnlyList<PursuitFile>> ListByEngagementAsync(long engagementId, CancellationToken ct);

    Task<PursuitFile> AddAsync(long engagementId, long? opportunityId, string fileName,
        Stream content, long declaredLength, string? contentType, string uploadedBy, CancellationToken ct);

    /// <summary>The caller must dispose the returned stream, which owns its SQL reader and connection.</summary>
    Task<Stream?> OpenContentAsync(long id, CancellationToken ct);

    /// <summary>Obsolete path-based overload retained for legacy callers. New attachments must supply a stream.</summary>
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
    public const long MaxAttachmentBytes = 250L * 1024 * 1024;

    public static void ValidateLength(long declaredLength)
    {
        if (declaredLength < 0) throw new ArgumentOutOfRangeException(nameof(declaredLength));
        if (declaredLength > MaxAttachmentBytes)
            throw new InvalidOperationException("Attachments must be 250 MB or smaller.");
    }

    private const string AllColumns =
        "Id, EngagementId, OpportunityId, FileName, LocalPath, Sha256, SizeBytes, ContentType, UploadedAtUtc, UploadedBy";
    private const string MetadataColumns = AllColumns + ", CAST(CASE WHEN Content IS NULL THEN 0 ELSE 1 END AS bit) AS HasContent";

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
SELECT {MetadataColumns}
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

    public async Task<PursuitFile> AddAsync(long engagementId, long? opportunityId, string fileName,
        Stream content, long declaredLength, string? contentType, string uploadedBy, CancellationToken ct)
    {
        ValidateLength(declaredLength);
        ArgumentNullException.ThrowIfNull(content);
        if (!content.CanRead) throw new ArgumentException("The attachment stream must be readable.", nameof(content));
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 500)
            throw new ArgumentException("The attachment needs a file name of 500 characters or fewer.", nameof(fileName));

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct).ConfigureAwait(false);
        using var measured = new MeasuredReadStream(content, declaredLength);
        const string sql = @"
INSERT INTO opportunities.OpportunityFiles
    (EngagementId, OpportunityId, FileName, Content, ContentType, UploadedBy)
OUTPUT inserted.Id, inserted.UploadedAtUtc
VALUES (@eng, @opp, @name, @content, @type, @by);";
        await using var cmd = new SqlCommand(sql, con, tx) { CommandTimeout = 300 };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@opp", SqlDbType.BigInt).Value = (object?)opportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 500).Value = fileName;
        cmd.Parameters.Add("@content", SqlDbType.VarBinary, -1).Value = measured;
        cmd.Parameters.Add("@type", SqlDbType.NVarChar, 200).Value = (object?)contentType ?? DBNull.Value;
        cmd.Parameters.Add("@by", SqlDbType.NVarChar, 150).Value = uploadedBy;
        long id;
        DateTimeOffset uploadedAt;
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            if (!await reader.ReadAsync(ct).ConfigureAwait(false)) throw new InvalidOperationException("INSERT did not return a row.");
            id = reader.GetInt64(0);
            uploadedAt = reader.GetDateTimeOffset(1);
        }
        var sha256 = measured.Complete();
        // The fingerprint is known only after SqlClient consumes the stream. Both
        // statements commit together; a failed/short upload leaves no visible row.
        await using var metadata = new SqlCommand(@"
UPDATE opportunities.OpportunityFiles SET Sha256 = @sha, SizeBytes = @size WHERE Id = @id;", con, tx);
        metadata.Parameters.Add("@sha", SqlDbType.VarBinary, 32).Value = sha256;
        metadata.Parameters.Add("@size", SqlDbType.BigInt).Value = measured.BytesRead;
        metadata.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await metadata.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new PursuitFile(id, engagementId, opportunityId, fileName, null, sha256, measured.BytesRead,
            contentType, uploadedAt, uploadedBy, HasContent: true);
    }

    public async Task<Stream?> OpenContentAsync(long id, CancellationToken ct)
    {
        var con = new SqlConnection(_connectionString);
        var cmd = new SqlCommand("SELECT Content FROM opportunities.OpportunityFiles WHERE Id = @id;", con)
            { CommandTimeout = 300 };
        SqlDataReader? reader = null;
        try
        {
            await con.OpenAsync(ct).ConfigureAwait(false);
            cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
            reader = await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess, ct).ConfigureAwait(false);
            if (await reader.ReadAsync(ct).ConfigureAwait(false) && !await reader.IsDBNullAsync(0, ct).ConfigureAwait(false))
                return new ContentReadStream(reader.GetStream(0), reader, cmd, con);
        }
        catch
        {
            if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
            await cmd.DisposeAsync().ConfigureAwait(false);
            await con.DisposeAsync().ConfigureAwait(false);
            throw;
        }
        if (reader is not null) await reader.DisposeAsync().ConfigureAwait(false);
        await cmd.DisposeAsync().ConfigureAwait(false);
        await con.DisposeAsync().ConfigureAwait(false);
        return null;
    }

    /// <summary>Obsolete path-based overload for legacy callers; new uploads use streams.</summary>
    public async Task<PursuitFile> AddAsync(
        long engagementId, long? opportunityId, string fileName, string localPath,
        byte[]? sha256, long? sizeBytes, string? contentType, string uploadedBy, CancellationToken ct)
    {
        var sql = $@"
INSERT INTO opportunities.OpportunityFiles
    (EngagementId, OpportunityId, FileName, LocalPath, Sha256, SizeBytes, ContentType, UploadedBy)
OUTPUT {PrefixInserted(AllColumns)}, CAST(0 AS bit) AS HasContent
VALUES (@eng, @opp, @name, @path, @sha, @size, @type, @by);";

        await using var con = new SqlConnection(_connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = CommandTimeoutSeconds };
        cmd.Parameters.Add("@eng", SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@opp", SqlDbType.BigInt).Value = (object?)opportunityId ?? DBNull.Value;
        cmd.Parameters.Add("@name", SqlDbType.NVarChar, 500).Value = fileName;
        cmd.Parameters.Add("@path", SqlDbType.NVarChar, 4000).Value = localPath;
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
        LocalPath: r.IsDBNull(4) ? null : r.GetString(4),
        Sha256: r.IsDBNull(5) ? null : (byte[])r.GetValue(5),
        SizeBytes: r.IsDBNull(6) ? null : r.GetInt64(6),
        ContentType: r.IsDBNull(7) ? null : r.GetString(7),
        UploadedAtUtc: r.GetDateTimeOffset(8),
        UploadedBy: r.GetString(9),
        HasContent: r.GetBoolean(10));

    private abstract class ReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class MeasuredReadStream(Stream source, long declaredLength) : ReadStream
    {
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        private bool _ended;
        public long BytesRead { get; private set; }

        private int Measure(ReadOnlySpan<byte> bytes)
        {
            BytesRead += bytes.Length;
            ValidateLength(BytesRead);
            if (BytesRead > declaredLength) throw new IOException("The attachment exceeded its declared length.");
            if (bytes.Length == 0)
            {
                _ended = true;
                if (BytesRead != declaredLength) throw new EndOfStreamException("The attachment ended before its declared length.");
            }
            _hash.AppendData(bytes);
            return bytes.Length;
        }

        public byte[] Complete()
        {
            if (!_ended || BytesRead != declaredLength) throw new EndOfStreamException("The attachment was not fully streamed.");
            return _hash.GetHashAndReset();
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer) => buffer.Length == 0 ? 0 : Measure(buffer[..source.Read(buffer)]);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.Length == 0) return 0;
            var count = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            return Measure(buffer.Span[..count]);
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) _hash.Dispose(); // source belongs to the caller
            base.Dispose(disposing);
        }
    }

    private sealed class ContentReadStream(Stream source, SqlDataReader reader, SqlCommand cmd, SqlConnection con) : ReadStream
    {
        private bool _disposed;
        public override int Read(byte[] buffer, int offset, int count) => source.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => source.Read(buffer);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct)
            => source.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => source.ReadAsync(buffer, ct);
        protected override void Dispose(bool disposing)
        {
            if (disposing && !_disposed)
            {
                _disposed = true;
                try { source.Dispose(); }
                finally { try { reader.Dispose(); } finally { try { cmd.Dispose(); } finally { con.Dispose(); } } }
            }
            base.Dispose(disposing);
        }
        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                _disposed = true;
                try { await source.DisposeAsync().ConfigureAwait(false); }
                finally
                {
                    try { await reader.DisposeAsync().ConfigureAwait(false); }
                    finally { try { await cmd.DisposeAsync().ConfigureAwait(false); } finally { await con.DisposeAsync().ConfigureAwait(false); } }
                }
            }
            GC.SuppressFinalize(this);
        }
    }
}
