using System.Security.Cryptography;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Readers;
using Kor.Opportunities.Knowledge.Store;

namespace Kor.Opportunities.Knowledge;

public sealed record KnowledgeIngestOptions(string Kind = KnowledgeKinds.Book,
    string Licence = KnowledgeLicences.OwnedCopyPrivateStudy, string? Title = null,
    string? Author = null, short? Year = null, string? By = null, bool Force = false, bool Dry = false);
public sealed record KnowledgeIngestResult(long? SourceId, bool Skipped, int Passages, int Tokens,
    string? FirstRef = null, string? FirstLine = null);

/// <summary>
/// Hash/read/embedding orchestration shared by the CLI and the original-file hash gate.
/// Hashes the purchased file before reader selection; conversion cannot change provenance.
/// A dry run performs no store calls and never invokes the embedder.
/// </summary>
public sealed class KnowledgeIngestor(ReaderPicker readers, IKnowledgeStore? store, IKnowledgeEmbedder? embedder)
{
    public async Task<KnowledgeIngestResult> IngestAsync(string path, KnowledgeIngestOptions options, CancellationToken ct = default)
    {
        if (!KnowledgeKinds.All.Contains(options.Kind) || !KnowledgeLicences.All.Contains(options.Licence))
            throw new ArgumentException("Unknown knowledge kind or licence.");
        // Keep the original open for shared reading only through extraction, preventing changes
        // between hashing and conversion on Windows. No original file is copied.
        await using var original = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var sha = Convert.ToHexString(await SHA256.HashDataAsync(original, ct));
        if (!options.Dry)
        {
            if (store is null || embedder is null) throw new InvalidOperationException("Ingest requires a store and embedder.");
            var existing = await store.FindBySha256Async(sha, ct);
            if (existing is { Passages: > 0 } && !options.Force)
                return new KnowledgeIngestResult(existing.Id, true, existing.Passages, existing.Tokens);
            if (options.Force && existing is { Claims: > 0 })
                throw new InvalidOperationException($"Source {existing.Id} has claims; --force is refused.");
        }
        var chunks = await readers.Pick(path).ReadAsync(path, ct);
        if (chunks.Count == 0) throw new InvalidDataException("No text found.");
        var tokens = chunks.Sum(c => c.Tokens);
        if (options.Dry)
            return new KnowledgeIngestResult(null, false, chunks.Count, tokens, chunks[0].Ref,
                chunks[0].Text.Split('\n')[0]);
        var metadata = FileNameParser.Parse(path);
        var source = new KnowledgeSource(0, options.Kind, options.Title ?? metadata.Title, options.Author ?? metadata.Author,
            options.Year ?? metadata.Year, Path.GetFileName(path), sha, options.Licence, KnowledgeEmbedder.ModelName,
            chunks.Count, tokens, DateTimeOffset.UtcNow, options.By ?? Environment.UserName, 0);
        var passages = new List<(KnowledgePassage Passage, byte[]? Embedding)>(chunks.Count);
        for (var i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chunk = chunks[i];
            passages.Add((new KnowledgePassage(0, 0, i + 1, chunk.Ref, chunk.Text, chunk.Tokens),
                KnowledgeEmbedder.ToBytes(embedder!.Embed(chunk.Text))));
        }
        try
        {
            var id = await store!.IngestAsync(source, passages, options.Force, ct);
            return new KnowledgeIngestResult(id, false, chunks.Count, tokens);
        }
        catch (AlreadyIngestedException ex)
        {
            return new KnowledgeIngestResult(ex.SourceId, true, chunks.Count, tokens);
        }
    }
}
