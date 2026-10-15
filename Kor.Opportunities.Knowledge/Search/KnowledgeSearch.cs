using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Store;

namespace Kor.Opportunities.Knowledge.Search;

/// <summary>
/// JoeBrain's in-memory cosine ranking and per-source cap for passages and claims.
/// An instance snapshots the index once; create a new instance after ingest or repair.
/// </summary>
public sealed class KnowledgeSearch(IKnowledgeStore store, IKnowledgeEmbedder embedder) : IDisposable
{
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private IReadOnlyList<(long PassageId, long SourceId, float[] Vector)>? _vectors;
    private IReadOnlyList<(long ClaimId, long SourceId, float[] Vector)>? _claimVectors;
    private IReadOnlyDictionary<long, string> _titles = new Dictionary<long, string>();

    public async Task<IReadOnlyList<KnowledgeHit>> SearchAsync(string query, int take = 10, float minScore = 0f,
        int perSource = 2, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfNegative(perSource);
        if (!float.IsFinite(minScore) || minScore is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(minScore));
        ct.ThrowIfCancellationRequested();
        var q = embedder.Embed(query);
        await LoadIndexAsync(ct);
        var ranked = new List<(long PassageId, long SourceId, float Score)>();
        foreach (var (id, sourceId, vector) in _vectors!)
        {
            ct.ThrowIfCancellationRequested();
            var score = Cosine(q, vector);
            if (float.IsFinite(score) && score >= minScore) ranked.Add((id, sourceId, score));
        }
        var chosen = new List<(long PassageId, long SourceId, float Score)>();
        var counts = new Dictionary<long, int>();
        foreach (var row in ranked.OrderByDescending(r => r.Score).ThenBy(r => r.PassageId))
        {
            var count = counts.GetValueOrDefault(row.SourceId);
            if (perSource > 0 && count >= perSource) continue;
            chosen.Add(row);
            counts[row.SourceId] = count + 1;
            if (chosen.Count == take) break;
        }
        var passages = (await store.GetPassagesAsync(chosen.Select(r => r.PassageId).ToArray(), ct)).ToDictionary(p => p.Id);
        return chosen.Where(r => passages.ContainsKey(r.PassageId))
            .Select(r => new KnowledgeHit(passages[r.PassageId], _titles.GetValueOrDefault(r.SourceId, "(unknown source)"), r.Score)).ToArray();
    }

    public async Task<IReadOnlyList<KnowledgeClaimHit>> SearchClaimsAsync(string query, int take = 12, float minScore = 0f,
        int perSource = 3, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfNegative(perSource);
        if (!float.IsFinite(minScore) || minScore is < -1 or > 1) throw new ArgumentOutOfRangeException(nameof(minScore));
        ct.ThrowIfCancellationRequested();
        await _indexLock.WaitAsync(ct);
        IReadOnlyList<(long ClaimId, long SourceId, float[] Vector)> vectors;
        try { vectors = _claimVectors ??= await store.LoadClaimVectorsAsync(ct); }
        finally { _indexLock.Release(); }
        if (vectors.Count == 0) return Array.Empty<KnowledgeClaimHit>();
        var queryVector = embedder.Embed(query);
        var ranked = vectors.Select(v => (v.ClaimId, v.SourceId, Score: Cosine(queryVector, v.Vector)))
            .Where(v => float.IsFinite(v.Score) && v.Score >= minScore).OrderByDescending(v => v.Score).ThenBy(v => v.ClaimId);
        var hits = new List<KnowledgeClaimHit>();
        var counts = new Dictionary<long, int>();
        // Resolve in rank order, rechecking retirement, and backfill past newly retired hits.
        foreach (var batch in ranked.Chunk(100))
        {
            ct.ThrowIfCancellationRequested();
            var claims = (await store.GetClaimsAsync(batch.Select(r => r.ClaimId).ToArray(), ct)).ToDictionary(c => c.Id);
            foreach (var row in batch)
            {
                if (!claims.TryGetValue(row.ClaimId, out var claim)) continue;
                var count = counts.GetValueOrDefault(claim.SourceId);
                if (perSource > 0 && count >= perSource) continue;
                hits.Add(new(claim.Id, claim.SourceId, claim.SourceTitle, claim.PassageId, claim.Kind, claim.Statement, row.Score));
                counts[claim.SourceId] = count + 1;
                if (hits.Count == take) return hits;
            }
        }
        return hits;
    }

    private async Task LoadIndexAsync(CancellationToken ct)
    {
        await _indexLock.WaitAsync(ct);
        try
        {
            if (_vectors is not null) return;
            var vectors = await store.LoadVectorsAsync(ct);
            var sources = await store.ListSourcesAsync(ct);
            _titles = sources.ToDictionary(s => s.Id, s => s.Title);
            _vectors = vectors;
        }
        finally { _indexLock.Release(); }
    }

    // Equivalent cosine to Semantic's TensorPrimitives call, without another package.
    public static float Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != KnowledgeEmbedder.Dimensions || b.Length != KnowledgeEmbedder.Dimensions)
            throw new ArgumentException("Cosine requires two 384-dimensional vectors.");
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += (double)a[i] * b[i]; aa += (double)a[i] * a[i]; bb += (double)b[i] * b[i]; }
        return aa == 0 || bb == 0 ? 0 : (float)(dot / Math.Sqrt(aa * bb));
    }

    public void Dispose() => _indexLock.Dispose();
}
