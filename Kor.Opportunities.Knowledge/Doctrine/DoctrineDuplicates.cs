using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Search;

namespace Kor.Opportunities.Knowledge.Doctrine;

/// <summary>An incoming card that reads like one already in the doctrine.</summary>
public sealed record DoctrineNearMatch(int IncomingIndex, string IncomingTitle, long ExistingId, string ExistingTitle, string ExistingStatus, float Score);

/// <summary>
/// Finds incoming doctrine cards that say what an existing card already says, by meaning rather than wording, with the local
/// model the books are searched with (no outside service). Compares against every card of the role whatever its status: an
/// accepted card should be amended, not restated; a rejected one should not come back; a waiting one is already in front of Ian.
/// Covers: the same idea in different words. Does NOT cover: two cards that share wording but give opposite advice (they can
/// score high and are reported for the session to judge), or a card that merges two existing ideas (each scores moderately).
/// </summary>
public static class DoctrineDuplicates
{
    /// <summary>
    /// Calibrated 2026-10-16: the 21 cards removed by hand as duplicates when the doctrine was seeded scored 0.67–0.91 against
    /// what they repeated; the 62 kept as distinct scored at most 0.78 against the pricing run. At 0.80, 14 of 21 duplicates
    /// and 0 of 62 distinct cards; from 0.72, 19 of 21 duplicates and 9 of 62 distinct cards.
    /// </summary>
    public const float RefuseAt = 0.80f;

    /// <summary>Close enough that the session must look and confirm before the report is accepted.</summary>
    public const float ReviewAt = 0.72f;

    public const float DefaultThreshold = ReviewAt;

    public static string TextOf(string title, string plain, string principle) => $"{title}. {principle} {plain}";

    public static IReadOnlyList<DoctrineNearMatch> Find(
        IReadOnlyList<DoctrineCardDraft> incoming,
        IReadOnlyList<DoctrineCardRow> existing,
        IKnowledgeEmbedder embedder,
        float threshold = DefaultThreshold,
        long? excludeRunId = null)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(embedder);
        var pool = existing
            .Where(c => c.Status is "accepted" or "proposed" or "rejected")
            // A resend replaces its own still-proposed cards, so they are not duplicates of themselves.
            .Where(c => !(excludeRunId is { } run && c.SourceRunId == run && c.Status == "proposed"))
            .Select(c => (Card: c, Vector: embedder.Embed(TextOf(c.Title, c.Plain, c.Principle))))
            .ToList();
        var matches = new List<DoctrineNearMatch>();
        for (var i = 0; i < incoming.Count; i++)
        {
            var d = incoming[i];
            var v = embedder.Embed(TextOf(d.Title, d.Plain, d.Principle));
            foreach (var (card, vector) in pool)
            {
                if (d.Amends == card.Id) continue; // an amendment is meant to resemble what it amends
                var score = KnowledgeSearch.Cosine(v, vector);
                if (score >= threshold) matches.Add(new DoctrineNearMatch(i, d.Title, card.Id, card.Title, card.Status, score));
            }
        }

        return matches.OrderByDescending(m => m.Score).ToList();
    }

    /// <summary>Every incoming card's closest existing card, for calibrating the threshold.</summary>
    public static IReadOnlyList<DoctrineNearMatch> Closest(IReadOnlyList<DoctrineCardDraft> incoming, IReadOnlyList<DoctrineCardRow> existing, IKnowledgeEmbedder embedder)
    {
        var pool = existing.Select(c => (Card: c, Vector: embedder.Embed(TextOf(c.Title, c.Plain, c.Principle)))).ToList();
        return incoming.Select((d, i) =>
        {
            var v = embedder.Embed(TextOf(d.Title, d.Plain, d.Principle));
            var best = pool.Select(p => (p.Card, Score: KnowledgeSearch.Cosine(v, p.Vector))).OrderByDescending(p => p.Score).First();
            return new DoctrineNearMatch(i, d.Title, best.Card.Id, best.Card.Title, best.Card.Status, best.Score);
        }).ToList();
    }
}
