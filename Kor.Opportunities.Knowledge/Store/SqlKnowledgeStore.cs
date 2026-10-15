using System.Data;
using System.Globalization;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Readers;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Knowledge.Store;

/// <summary>
/// Permanent writes are confined to Source and Passage. A book is replaced atomically;
/// claim rows are read only. Repair uses one session-local staging table and one joined
/// passage update per batch, with no per-passage SQL calls.
/// </summary>
public sealed class SqlKnowledgeStore : IKnowledgeStore
{
    private readonly string _connectionString;
    private const string SourceSelect = """
        SELECT s.Id, s.Kind, s.Title, s.Author, s.[Year], s.Origin, s.Sha256, s.Licence,
               s.EmbeddingModel, s.Passages, s.Tokens, s.IngestedAtUtc, s.IngestedBy,
               (SELECT COUNT(*) FROM knowledge.Claim c WHERE c.SourceId = s.Id) AS Claims
        FROM knowledge.Source s
        """;
    private const string PassageColumns = "Id, SourceId, Ordinal, Ref, [Text], Tokens";

    public SqlKnowledgeStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
    }

    public async Task<KnowledgeSource?> FindBySha256Async(string sha256, CancellationToken ct = default)
    {
        await using var con = await OpenAsync(ct);
        await using var cmd = new SqlCommand(SourceSelect + " WHERE s.Sha256 = @sha;", con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@sha", SqlDbType.Char, 64).Value = sha256;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? ReadSource(reader) : null;
    }

    public async Task<IReadOnlyList<KnowledgeSource>> ListSourcesAsync(CancellationToken ct = default)
    {
        await using var con = await OpenAsync(ct);
        await using var cmd = new SqlCommand(SourceSelect + " ORDER BY s.Title, s.Id;", con) { CommandTimeout = 30 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var result = new List<KnowledgeSource>();
        while (await reader.ReadAsync(ct)) result.Add(ReadSource(reader));
        return result;
    }

    public async Task<long> IngestAsync(KnowledgeSource source,
        IReadOnlyList<(KnowledgePassage Passage, byte[]? Embedding)> passages, bool force, CancellationToken ct = default)
    {
        Validate(source, passages);
        await using var con = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        long? existingId = null;
        var existingPassages = 0;
        // Lock the unique hash range, including an absent hash, before deciding whether to insert.
        await using (var find = new SqlCommand("""
            SELECT s.Id, (SELECT COUNT(*) FROM knowledge.Passage p WHERE p.SourceId = s.Id)
            FROM knowledge.Source s WITH (UPDLOCK, HOLDLOCK) WHERE s.Sha256 = @sha;
            """, con, tx) { CommandTimeout = 120 })
        {
            find.Parameters.Add("@sha", SqlDbType.Char, 64).Value = source.Sha256;
            await using var reader = await find.ExecuteReaderAsync(ct);
            if (await reader.ReadAsync(ct)) { existingId = reader.GetInt64(0); existingPassages = reader.GetInt32(1); }
        }
        if (existingId is { } duplicate && existingPassages > 0 && !force)
            throw new AlreadyIngestedException(duplicate);

        if (force && existingId is { } replaceId)
        {
            await using var guard = new SqlCommand(
                "SELECT COUNT(*) FROM knowledge.Claim WITH (HOLDLOCK) WHERE SourceId = @sourceId;", con, tx) { CommandTimeout = 120 };
            guard.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = replaceId;
            if (Convert.ToInt32(await guard.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture) > 0)
                throw new InvalidOperationException($"Source {replaceId} has claims; --force is refused.");
            await using var remove = new SqlCommand("DELETE FROM knowledge.Passage WHERE SourceId = @sourceId;", con, tx) { CommandTimeout = 120 };
            remove.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = replaceId;
            await remove.ExecuteNonQueryAsync(ct);
        }

        long sourceId;
        if (existingId is { } reuseId) sourceId = reuseId;
        else
        {
            await using var insert = new SqlCommand("""
                INSERT INTO knowledge.Source (Kind, Title, Author, [Year], Origin, Sha256, Licence, IngestedBy)
                OUTPUT INSERTED.Id VALUES (@kind, @title, @author, @year, @origin, @sha, @licence, @by);
                """, con, tx) { CommandTimeout = 120 };
            Metadata(insert, source);
            insert.Parameters.Add("@sha", SqlDbType.Char, 64).Value = source.Sha256;
            sourceId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }

        using var table = new DataTable();
        table.Columns.Add("SourceId", typeof(long));
        table.Columns.Add("Ordinal", typeof(int));
        table.Columns.Add("Ref", typeof(string));
        table.Columns.Add("Text", typeof(string));
        table.Columns.Add("Tokens", typeof(int));
        table.Columns.Add("Embedding", typeof(byte[]));
        foreach (var (passage, embedding) in passages)
        {
            ct.ThrowIfCancellationRequested();
            table.Rows.Add(sourceId, passage.Ordinal, (object?)passage.Ref ?? DBNull.Value,
                passage.Text, passage.Tokens, (object?)embedding ?? DBNull.Value);
        }
        using (var bulk = new SqlBulkCopy(con, SqlBulkCopyOptions.CheckConstraints, tx)
        {
            DestinationTableName = "knowledge.Passage", BulkCopyTimeout = 120,
        })
        {
            foreach (DataColumn column in table.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulk.WriteToServerAsync(table, ct);
        }
        await using (var finish = new SqlCommand("""
            UPDATE knowledge.Source SET Kind = @kind, Title = @title, Author = @author, [Year] = @year,
                Origin = @origin, Licence = @licence, IngestedBy = @by, IngestedAtUtc = sysdatetimeoffset(),
                Passages = @passages, Tokens = @tokens, EmbeddingModel = @model
            WHERE Id = @sourceId;
            """, con, tx) { CommandTimeout = 120 })
        {
            Metadata(finish, source);
            finish.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = sourceId;
            finish.Parameters.Add("@passages", SqlDbType.Int).Value = passages.Count;
            finish.Parameters.Add("@tokens", SqlDbType.Int).Value = passages.Sum(p => p.Passage.Tokens);
            Text(finish, "@model", 40, passages.All(p => p.Embedding is not null) ? KnowledgeEmbedder.ModelName : null);
            await finish.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return sourceId;
    }

    public async Task<IReadOnlyList<KnowledgePassage>> ListUnembeddedAsync(long? sourceId = null, CancellationToken ct = default)
    {
        await using var con = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT " + PassageColumns +
            " FROM knowledge.Passage WHERE Embedding IS NULL AND (@sourceId IS NULL OR SourceId = @sourceId) ORDER BY SourceId, Ordinal;", con) { CommandTimeout = 120 };
        cmd.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = (object?)sourceId ?? DBNull.Value;
        return await ReadPassagesAsync(cmd, ct);
    }

    public async Task SetEmbeddingsAsync(IReadOnlyList<(long PassageId, byte[] Embedding)> embeddings, CancellationToken ct = default)
    {
        if (embeddings.Count == 0) return;
        if (embeddings.Count > 500) throw new ArgumentException("Embedding repair batches are limited to 500 passages.", nameof(embeddings));
        if (embeddings.Select(e => e.PassageId).Distinct().Count() != embeddings.Count)
            throw new ArgumentException("A repair batch cannot repeat a passage.", nameof(embeddings));
        using var table = new DataTable();
        table.Columns.Add("PassageId", typeof(long));
        table.Columns.Add("Embedding", typeof(byte[]));
        foreach (var (id, embedding) in embeddings)
        {
            CheckEmbedding(embedding);
            table.Rows.Add(id, embedding);
        }
        await using var con = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        await using (var create = new SqlCommand(
            "CREATE TABLE #KnowledgeEmbeddings (PassageId BIGINT NOT NULL PRIMARY KEY, Embedding VARBINARY(1536) NOT NULL);", con, tx) { CommandTimeout = 120 })
            await create.ExecuteNonQueryAsync(ct);
        // This session-local staging destination is the sole exception to permanent-table destinations.
        using (var bulk = new SqlBulkCopy(con, SqlBulkCopyOptions.CheckConstraints, tx)
        {
            DestinationTableName = "#KnowledgeEmbeddings", BulkCopyTimeout = 120,
        })
        {
            bulk.ColumnMappings.Add("PassageId", "PassageId");
            bulk.ColumnMappings.Add("Embedding", "Embedding");
            await bulk.WriteToServerAsync(table, ct);
        }
        await using (var update = new SqlCommand("""
            UPDATE knowledge.Passage SET Embedding = repair.Embedding
            FROM knowledge.Passage
            INNER JOIN #KnowledgeEmbeddings repair ON repair.PassageId = knowledge.Passage.Id
            WHERE knowledge.Passage.Embedding IS NULL;
            """, con, tx) { CommandTimeout = 120 })
            await update.ExecuteNonQueryAsync(ct);
        await tx.CommitAsync(ct);
        // The local table expires with the connection; no persistent staging table or cleanup DDL.
    }

    public async Task<IReadOnlyList<(long PassageId, long SourceId, float[] Vector)>> LoadVectorsAsync(CancellationToken ct = default)
    {
        await using var con = await OpenAsync(ct);
        await using var cmd = new SqlCommand(
            "SELECT Id, SourceId, Embedding FROM knowledge.Passage WHERE Embedding IS NOT NULL ORDER BY Id;", con) { CommandTimeout = 120 };
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<(long, long, float[])>();
        while (await reader.ReadAsync(ct))
            rows.Add((reader.GetInt64(0), reader.GetInt64(1), KnowledgeEmbedder.FromBytes(reader.GetFieldValue<byte[]>(2))));
        return rows;
    }

    public async Task<IReadOnlyList<KnowledgePassage>> GetPassagesAsync(IReadOnlyList<long> ids, CancellationToken ct = default)
    {
        var rows = new List<KnowledgePassage>();
        if (ids.Count == 0) return rows;
        await using var con = await OpenAsync(ct);
        foreach (var batch in ids.Distinct().Chunk(500))
        {
            await using var cmd = new SqlCommand { Connection = con, CommandTimeout = 120 };
            var names = batch.Select((id, i) =>
            {
                var name = "@id" + i.ToString(CultureInfo.InvariantCulture);
                cmd.Parameters.Add(name, SqlDbType.BigInt).Value = id;
                return name;
            }).ToArray();
            cmd.CommandText = "SELECT " + PassageColumns + " FROM knowledge.Passage WHERE Id IN (" + string.Join(",", names) + ");";
            rows.AddRange(await ReadPassagesAsync(cmd, ct));
        }
        return rows;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqlConnection(_connectionString);
        try { await connection.OpenAsync(ct); return connection; }
        catch { await connection.DisposeAsync(); throw; }
    }

    private static async Task<IReadOnlyList<KnowledgePassage>> ReadPassagesAsync(SqlCommand cmd, CancellationToken ct)
    {
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<KnowledgePassage>();
        while (await reader.ReadAsync(ct))
            rows.Add(new KnowledgePassage(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetString(3), reader.GetString(4), reader.GetInt32(5)));
        return rows;
    }

    private static KnowledgeSource ReadSource(SqlDataReader r) => new(r.GetInt64(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3), r.IsDBNull(4) ? null : r.GetInt16(4), r.GetString(5), r.GetString(6),
        r.GetString(7), r.IsDBNull(8) ? null : r.GetString(8), r.GetInt32(9), r.GetInt32(10),
        r.GetDateTimeOffset(11), r.GetString(12), r.GetInt32(13));

    private static void Metadata(SqlCommand cmd, KnowledgeSource s)
    {
        Text(cmd, "@kind", 20, s.Kind); Text(cmd, "@title", 300, s.Title); Text(cmd, "@author", 300, s.Author);
        cmd.Parameters.Add("@year", SqlDbType.SmallInt).Value = (object?)s.Year ?? DBNull.Value;
        Text(cmd, "@origin", 400, s.Origin); Text(cmd, "@licence", 40, s.Licence); Text(cmd, "@by", 150, s.IngestedBy);
    }

    private static void Text(SqlCommand cmd, string name, int length, string? value)
    {
        if (value?.Length > length) throw new ArgumentException($"Value for {name} exceeds {length} characters.");
        cmd.Parameters.Add(name, SqlDbType.NVarChar, length).Value = (object?)value ?? DBNull.Value;
    }

    private static void CheckEmbedding(byte[] bytes)
    {
        if (bytes.Length != KnowledgeEmbedder.Dimensions * sizeof(float))
            throw new ArgumentException("An embedding must contain exactly 1536 bytes.");
    }

    private static void Validate(KnowledgeSource source, IReadOnlyList<(KnowledgePassage Passage, byte[]? Embedding)> passages)
    {
        if (!KnowledgeKinds.All.Contains(source.Kind) || !KnowledgeLicences.All.Contains(source.Licence))
            throw new ArgumentException("Unknown knowledge kind or licence.");
        if (string.IsNullOrWhiteSpace(source.Title) || string.IsNullOrWhiteSpace(source.Origin) || string.IsNullOrWhiteSpace(source.IngestedBy))
            throw new ArgumentException("Title, original filename and ingested-by name are required.");
        if (source.Sha256.Length != 64 || source.Sha256.Any(c => c is not (>= '0' and <= '9') and not (>= 'A' and <= 'F')))
            throw new ArgumentException("SHA-256 must be 64 uppercase hexadecimal characters.");
        if (passages.Count == 0) throw new ArgumentException("No text found.");
        for (var i = 0; i < passages.Count; i++)
        {
            var (passage, embedding) = passages[i];
            if (passage.Ordinal != i + 1 || string.IsNullOrWhiteSpace(passage.Text) || passage.Text.Length > Chunking.MaxChars
                || passage.Ref?.Length > 200 || passage.Tokens != TokenEstimate.Of(passage.Text))
                throw new ArgumentException($"Passage {i + 1} has invalid text, reference, ordinal or token estimate.");
            if (embedding is not null) CheckEmbedding(embedding);
        }
    }
}
