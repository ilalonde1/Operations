using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Store;

namespace Kor.Opportunities.Knowledge.Search;

/// <summary>
/// JoeBrain's in-memory cosine ranking and per-source cap, with one vector per passage.
/// An instance snapshots the index once; create a new instance after ingest or repair.
/// </summary>
public sealed class KnowledgeSearch(IKnowledgeStore store, IKnowledgeEmbedder embedder) : IDisposable
{
    private readonly SemaphoreSlim _indexLock = new(1, 1);
    private IReadOnlyList<(long PassageId, long SourceId, float[] Vector)>? _vectors;
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
