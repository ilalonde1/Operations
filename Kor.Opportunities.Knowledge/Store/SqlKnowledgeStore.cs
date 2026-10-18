using System.Data;
using System.Globalization;
using Kor.Opportunities.Knowledge.Claims;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Readers;
using Microsoft.Data.SqlClient;

namespace Kor.Opportunities.Knowledge.Store;

/// <summary>
/// Permanent writes are confined to knowledge tables. Book replacement, embedding repair
/// and claims import each commit atomically. Reviewed claims are never superseded by import.
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

    public async Task<IReadOnlyList<(long Id, string Statement)>> ListUnembeddedClaimsAsync(int take, CancellationToken ct = default)
    {
        if (take is < 1 or > 500) throw new ArgumentOutOfRangeException(nameof(take));
        await using var con = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT TOP (@take) Id, Statement FROM knowledge.Claim WHERE RetiredAtUtc IS NULL AND Embedding IS NULL ORDER BY Id;", con) { CommandTimeout = 120 };
        cmd.Parameters.Add("@take", SqlDbType.Int).Value = take;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<(long, string)>();
        while (await reader.ReadAsync(ct)) rows.Add((reader.GetInt64(0), reader.GetString(1)));
        return rows;
    }

    public async Task SetClaimEmbeddingsAsync(IReadOnlyList<(long ClaimId, byte[] Embedding)> embeddings, string model, CancellationToken ct = default)
    {
        if (embeddings.Count == 0) return;
        if (embeddings.Count > 500 || embeddings.Select(e => e.ClaimId).Distinct().Count() != embeddings.Count)
            throw new ArgumentException("Use at most 500 distinct claims per batch.", nameof(embeddings));
        if (model != KnowledgeEmbedder.ModelName) throw new ArgumentException("Use the configured claim embedding model.", nameof(model));
        using var table = new DataTable();
        table.Columns.Add("ClaimId", typeof(long)); table.Columns.Add("Embedding", typeof(byte[]));
        foreach (var (id, embedding) in embeddings) { CheckEmbedding(embedding); table.Rows.Add(id, embedding); }
        await using var con = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(ct);
        await using (var create = new SqlCommand("CREATE TABLE #KnowledgeClaimEmbeddings (ClaimId BIGINT NOT NULL PRIMARY KEY, Embedding VARBINARY(1536) NOT NULL);", con, tx) { CommandTimeout = 120 })
            await create.ExecuteNonQueryAsync(ct);
        using (var bulk = new SqlBulkCopy(con, SqlBulkCopyOptions.CheckConstraints, tx)
        { DestinationTableName = "#KnowledgeClaimEmbeddings", BulkCopyTimeout = 120 })
        {
            bulk.ColumnMappings.Add("ClaimId", "ClaimId"); bulk.ColumnMappings.Add("Embedding", "Embedding");
            await bulk.WriteToServerAsync(table, ct);
        }
        await using (var update = new SqlCommand("""
            UPDATE knowledge.Claim SET Embedding = repair.Embedding, EmbeddingModel = @model
            FROM knowledge.Claim INNER JOIN #KnowledgeClaimEmbeddings repair ON repair.ClaimId = knowledge.Claim.Id
            WHERE knowledge.Claim.Embedding IS NULL AND knowledge.Claim.RetiredAtUtc IS NULL;
            """, con, tx) { CommandTimeout = 120 })
        {
            Text(update, "@model", 40, model);
            await update.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    public async Task<IReadOnlyList<(long ClaimId, long SourceId, float[] Vector)>> LoadClaimVectorsAsync(CancellationToken ct = default)
    {
        await using var con = await OpenAsync(ct);
        await using var cmd = new SqlCommand("SELECT Id, SourceId, Embedding FROM knowledge.Claim WHERE RetiredAtUtc IS NULL AND Embedding IS NOT NULL AND EmbeddingModel = @model ORDER BY Id;", con) { CommandTimeout = 120 };
        Text(cmd, "@model", 40, KnowledgeEmbedder.ModelName);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<(long, long, float[])>();
        while (await reader.ReadAsync(ct)) rows.Add((reader.GetInt64(0), reader.GetInt64(1), KnowledgeEmbedder.FromBytes(reader.GetFieldValue<byte[]>(2))));
        return rows;
    }

    public async Task<IReadOnlyList<KnowledgeClaim>> GetClaimsAsync(IReadOnlyList<long> ids, CancellationToken ct = default)
    {
        var rows = new List<KnowledgeClaim>();
        if (ids.Count == 0) return rows;
        await using var con = await OpenAsync(ct);
        foreach (var batch in ids.Distinct().Chunk(500))
        {
            await using var cmd = new SqlCommand { Connection = con, CommandTimeout = 120 };
            var names = batch.Select((id, i) =>
            {
                var name = "@id" + i.ToString(CultureInfo.InvariantCulture);
                cmd.Parameters.Add(name, SqlDbType.BigInt).Value = id; return name;
            });
            // Recheck retirement when resolving a cached vector index.
            cmd.CommandText = "SELECT c.Id, c.SourceId, s.Title, c.PassageId, c.Kind, c.Statement, c.Confidence FROM knowledge.Claim c JOIN knowledge.Source s ON s.Id = c.SourceId WHERE c.RetiredAtUtc IS NULL AND c.Id IN (" + string.Join(",", names) + ");";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) rows.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetString(2), reader.GetInt64(3), reader.GetString(4), reader.GetString(5), reader.GetDecimal(6)));
        }
        return rows;
    }

    public async Task<IReadOnlyList<KnowledgeClaimRelation>> ListRelationsAsync(IReadOnlyCollection<long> claimIds, CancellationToken ct = default)
    {
        if (claimIds.Count == 0) return Array.Empty<KnowledgeClaimRelation>();
        var rows = new Dictionary<(long From, long To, string Kind), KnowledgeClaimRelation>();
        await using var con = await OpenAsync(ct);
        foreach (var batch in claimIds.Distinct().Order().Chunk(500))
        {
            await using var cmd = new SqlCommand { Connection = con, CommandTimeout = 120 };
            var names = batch.Select((id, i) =>
            {
                var name = "@id" + i.ToString(CultureInfo.InvariantCulture);
                cmd.Parameters.Add(name, SqlDbType.BigInt).Value = id;
                return name;
            }).ToArray();
            var parameters = string.Join(",", names);
            cmd.CommandText = $"""
                SELECT r.FromClaimId, f.Statement, fs.Title, r.ToClaimId, t.Statement, ts.Title, r.Kind, r.Note
                FROM knowledge.ClaimRelation r
                JOIN knowledge.Claim f ON f.Id = r.FromClaimId
                JOIN knowledge.Source fs ON fs.Id = f.SourceId
                JOIN knowledge.Claim t ON t.Id = r.ToClaimId
                JOIN knowledge.Source ts ON ts.Id = t.SourceId
                WHERE (r.FromClaimId IN ({parameters}) OR r.ToClaimId IN ({parameters}))
                  AND f.RetiredAtUtc IS NULL AND t.RetiredAtUtc IS NULL;
                """;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var row = new KnowledgeClaimRelation(reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7));
                // Either end may occur in another batch; emit each directed relation only once.
                rows.TryAdd((row.FromClaimId, row.ToClaimId, row.Kind), row);
            }
        }
        return rows.Values.OrderBy(r => r.FromClaimId).ThenBy(r => r.ToClaimId).ThenBy(r => r.Kind, StringComparer.Ordinal).ToArray();
    }

    public async Task<(int Written, int Rejected)> ImportRelationsAsync(RelationFile file, string by, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(by) || by.Length > 100) throw new ArgumentException("Created-by name must contain 1–100 characters.", nameof(by));
        if (file.Relations is null) throw new ArgumentException("relations must be an array.");
        await using var con = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var active = new HashSet<long>();
        var ids = file.Relations.Where(r => r is not null).SelectMany(r => new[] { r.From, r.To }).Distinct().Order().ToArray();
        foreach (var batch in ids.Chunk(500))
        {
            await using var read = new SqlCommand { Connection = con, Transaction = tx, CommandTimeout = 120 };
            var names = batch.Select((id, i) =>
            {
                var name = "@id" + i.ToString(CultureInfo.InvariantCulture); read.Parameters.Add(name, SqlDbType.BigInt).Value = id; return name;
            });
            read.CommandText = "SELECT Id FROM knowledge.Claim WITH (UPDLOCK, HOLDLOCK) WHERE RetiredAtUtc IS NULL AND Id IN (" + string.Join(",", names) + ");";
            await using var reader = await read.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) active.Add(reader.GetInt64(0));
        }
        var validation = RelationValidation.Validate(file, active);
        var written = 0;
        foreach (var row in validation.Accepted.OrderBy(r => r.From).ThenBy(r => r.To).ThenBy(r => r.Kind, StringComparer.Ordinal))
        {
            await using var cmd = new SqlCommand("""
                IF NOT EXISTS (SELECT 1 FROM knowledge.ClaimRelation WITH (UPDLOCK, HOLDLOCK)
                    WHERE FromClaimId = @from AND ToClaimId = @to AND Kind = @kind)
                BEGIN
                    INSERT INTO knowledge.ClaimRelation (FromClaimId, ToClaimId, Kind, Confidence, LinkedBy, Note, CreatedBy)
                    VALUES (@from, @to, @kind, @confidence, N'Model', @note, @by);
                    SELECT 1;
                END
                ELSE SELECT 0;
                """, con, tx) { CommandTimeout = 120 };
            cmd.Parameters.Add("@from", SqlDbType.BigInt).Value = row.From; cmd.Parameters.Add("@to", SqlDbType.BigInt).Value = row.To;
            Text(cmd, "@kind", 12, row.Kind); Text(cmd, "@note", 500, row.Note); Text(cmd, "@by", 100, by);
            DecimalConfidence(cmd, row.Confidence!.Value);
            written += Convert.ToInt32(await cmd.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
        }
        await tx.CommitAsync(ct);
        return (written, validation.Rejected.Count);
    }

    public async Task<ClaimFile> ExportAsync(long sourceId, CancellationToken ct = default)
    {
        await using var con = await OpenAsync(ct);
        await using var cmd = new SqlCommand("""
            SELECT Title, Author FROM knowledge.Source WHERE Id = @sourceId;
            SELECT Slug, Name FROM knowledge.Concept ORDER BY Slug;
            SELECT Id, Ordinal, Ref, [Text] FROM knowledge.Passage WHERE SourceId = @sourceId ORDER BY Ordinal;
            """, con) { CommandTimeout = 120 };
        cmd.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = sourceId;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) throw new ArgumentException($"Source {sourceId} was not found.");
        var title = reader.GetString(0);
        var author = reader.IsDBNull(1) ? null : reader.GetString(1);
        await reader.NextResultAsync(ct);
        var concepts = new List<ClaimFileConcept>();
        while (await reader.ReadAsync(ct)) concepts.Add(new(reader.GetString(0), reader.GetString(1)));
        await reader.NextResultAsync(ct);
        var passages = new List<ClaimFilePassage>();
        while (await reader.ReadAsync(ct)) passages.Add(new()
        {
            Id = reader.GetInt64(0), Ordinal = reader.GetInt32(1),
            Ref = reader.IsDBNull(2) ? null : reader.GetString(2), Text = reader.GetString(3),
        });
        return new ClaimFile { SourceId = sourceId, Title = title, Author = author,
            ExportedAtUtc = DateTimeOffset.UtcNow, KnownConcepts = concepts, Passages = passages };
    }

    public async Task<(int PassagesCovered, int ClaimsWritten, int ClaimsRejected)> ImportClaimsAsync(
        ClaimFile file, bool force, string by, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(by) || by.Length > 100) throw new ArgumentException("Importer must be 1–100 characters.", nameof(by));
        if (string.IsNullOrWhiteSpace(file.FileName) || file.FileName.Length > 260 || file.FileSha256 is not { Length: 64 })
            throw new ArgumentException("Read the original claims file with ClaimFile.Parse before importing.", nameof(file));
        await using var con = await OpenAsync(ct);
        await using var tx = (SqlTransaction)await con.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        // Serialize imports for a source, and protect passage identity/stamps until commit.
        await using (var source = new SqlCommand(
            "SELECT Id FROM knowledge.Source WITH (UPDLOCK, HOLDLOCK) WHERE Id = @sourceId;", con, tx) { CommandTimeout = 120 })
        {
            source.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = file.SourceId;
            if (await source.ExecuteScalarAsync(ct) is null) throw new ArgumentException($"Source {file.SourceId} was not found.");
        }
        var stored = new List<StoredClaimPassage>();
        await using (var passages = new SqlCommand("""
            SELECT Id, SourceId, Ordinal, ClaimsExtractedAtUtc
            FROM knowledge.Passage WITH (UPDLOCK, HOLDLOCK) WHERE SourceId = @sourceId;
            """, con, tx) { CommandTimeout = 120 })
        {
            passages.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = file.SourceId;
            await using var reader = await passages.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct)) stored.Add(new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt32(2),
                reader.IsDBNull(3) ? null : reader.GetDateTimeOffset(3)));
        }
        var plan = ClaimValidation.PrepareImport(file, stored, force);
        if (plan.SupersedeUnreviewedModelClaims)
        {
            // LinkedBy is the schema's model/human provenance; ExtractedBy is a free-form writer name.
            // Preserve unlinked claims and any human-linked claim as well as all reviewed/retired claims.
            await using var retire = new SqlCommand("""
                UPDATE knowledge.Claim SET RetiredAtUtc = sysdatetimeoffset(), RetiredReason = N'Superseded by re-import'
                WHERE SourceId = @sourceId AND ReviewedAtUtc IS NULL AND RetiredAtUtc IS NULL
                    AND EXISTS (SELECT 1 FROM knowledge.ClaimConcept link WHERE link.ClaimId = knowledge.Claim.Id AND link.LinkedBy = N'Model')
                    AND NOT EXISTS (SELECT 1 FROM knowledge.ClaimConcept link WHERE link.ClaimId = knowledge.Claim.Id AND link.LinkedBy = N'Human');
                """, con, tx) { CommandTimeout = 120 };
            retire.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = file.SourceId;
            await retire.ExecuteNonQueryAsync(ct);
        }

        // Consistent slug order avoids taking shared concept locks in opposite orders across books.
        // GroupBy preserves the first accepted name seen for each normalized slug.
        var conceptIds = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var concept in plan.Validation.Accepted.SelectMany(c => c.Claim.Concepts)
                     .GroupBy(c => c.Slug, StringComparer.Ordinal).Select(g => g.First()).OrderBy(c => c.Slug, StringComparer.Ordinal))
        {
            await using var upsert = new SqlCommand("""
                DECLARE @id BIGINT;
                SELECT @id = Id FROM knowledge.Concept WITH (UPDLOCK, HOLDLOCK) WHERE Slug = @slug;
                IF @id IS NULL
                BEGIN
                    INSERT INTO knowledge.Concept (Slug, Name, CreatedBy) VALUES (@slug, @name, @by);
                    SET @id = CONVERT(BIGINT, SCOPE_IDENTITY());
                END;
                SELECT @id;
                """, con, tx) { CommandTimeout = 120 };
            Text(upsert, "@slug", 80, concept.Slug); Text(upsert, "@name", 150, concept.Name); Text(upsert, "@by", 100, by);
            conceptIds.Add(concept.Slug, Convert.ToInt64(await upsert.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture));
        }
        foreach (var accepted in plan.Validation.Accepted)
        {
            var claim = accepted.Claim;
            await using var insert = new SqlCommand("""
                INSERT INTO knowledge.Claim (SourceId, PassageId, Kind, Statement, Confidence, ExtractedBy)
                OUTPUT INSERTED.Id VALUES (@sourceId, @passageId, @kind, @statement, @confidence, @writer);
                """, con, tx) { CommandTimeout = 120 };
            insert.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = file.SourceId;
            insert.Parameters.Add("@passageId", SqlDbType.BigInt).Value = accepted.PassageId;
            Text(insert, "@kind", 20, claim.Kind); Text(insert, "@statement", 1000, claim.Statement);
            DecimalConfidence(insert, claim.Confidence!.Value); Text(insert, "@writer", 100, file.WrittenBy);
            var claimId = Convert.ToInt64(await insert.ExecuteScalarAsync(ct), CultureInfo.InvariantCulture);
            foreach (var concept in claim.Concepts)
            {
                await using var link = new SqlCommand("""
                    INSERT INTO knowledge.ClaimConcept (ClaimId, ConceptId, Confidence, LinkedBy)
                    VALUES (@claimId, @conceptId, @confidence, N'Model');
                    """, con, tx) { CommandTimeout = 120 };
                link.Parameters.Add("@claimId", SqlDbType.BigInt).Value = claimId;
                link.Parameters.Add("@conceptId", SqlDbType.BigInt).Value = conceptIds[concept.Slug];
                DecimalConfidence(link, claim.Confidence!.Value);
                await link.ExecuteNonQueryAsync(ct);
            }
        }
        // Stamp every listed passage, including empty/rejected claim lists, in bounded parameter batches.
        foreach (var batch in file.Passages.Chunk(500))
        {
            await using var stamp = new SqlCommand { Connection = con, Transaction = tx, CommandTimeout = 120 };
            var ids = batch.Select((passage, i) =>
            {
                var name = "@p" + i.ToString(CultureInfo.InvariantCulture);
                stamp.Parameters.Add(name, SqlDbType.BigInt).Value = passage.Id;
                return name;
            });
            stamp.CommandText = """
                UPDATE knowledge.Passage SET ClaimsExtractedAtUtc = sysdatetimeoffset(), ClaimsExtractedBy = @writer
                WHERE SourceId = @sourceId AND Id IN (
                """ + string.Join(",", ids) + ");";
            Text(stamp, "@writer", 100, file.WrittenBy);
            stamp.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = file.SourceId;
            await stamp.ExecuteNonQueryAsync(ct);
        }
        await using (var audit = new SqlCommand("""
            INSERT INTO knowledge.ClaimImport
                (SourceId, FileName, FileSha256, ImportedBy, PassagesCovered, ClaimsWritten, ClaimsRejected, Forced)
            VALUES (@sourceId, @fileName, @sha, @by, @covered, @written, @rejected, @forced);
            """, con, tx) { CommandTimeout = 120 })
        {
            audit.Parameters.Add("@sourceId", SqlDbType.BigInt).Value = file.SourceId;
            Text(audit, "@fileName", 260, file.FileName); Text(audit, "@by", 100, by);
            audit.Parameters.Add("@sha", SqlDbType.Char, 64).Value = file.FileSha256;
            audit.Parameters.Add("@covered", SqlDbType.Int).Value = file.Passages.Count;
            audit.Parameters.Add("@written", SqlDbType.Int).Value = plan.Validation.Accepted.Count;
            audit.Parameters.Add("@rejected", SqlDbType.Int).Value = plan.Validation.Rejected.Count;
            audit.Parameters.Add("@forced", SqlDbType.Bit).Value = force;
            await audit.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return (file.Passages.Count, plan.Validation.Accepted.Count, plan.Validation.Rejected.Count);
    }

    private static void DecimalConfidence(SqlCommand cmd, decimal confidence)
    {
        var parameter = cmd.Parameters.Add("@confidence", SqlDbType.Decimal);
        parameter.Precision = 4; parameter.Scale = 3; parameter.Value = confidence;
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
