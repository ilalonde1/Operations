using System.Globalization;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Search;
using Kor.Opportunities.Knowledge.Store;

namespace Kor.MorningBrief;

public sealed record MorningCardVector(long Id, float[] Vector);
public sealed record MorningCardMatch(long Id, float Score);
public sealed record MorningSituationMatch(string Situation, IReadOnlyList<MorningCardMatch> Cards,
    IReadOnlyList<KnowledgeClaimHit> Claims, IReadOnlyList<KnowledgeClaimRelation> Relations, string? CouldNotRead = null);

/// <summary>Local card embedding, pure ranking and pure formatting; no store reads.</summary>
public static class MorningKnowledge
{
    public const string FactName = "Knowledge matched to today's situations";

    public static string CardText(DoctrineCardRow card) => $"{card.Title}. {card.Plain}. {card.Recognise}.";

    public static IReadOnlyList<MorningCardVector> EmbedCards(IEnumerable<DoctrineCardRow> cards, string role,
        IKnowledgeEmbedder embedder) => cards.Where(c => c.Role == role && c.Status == "accepted").DistinctBy(c => c.Id)
        .Select(c => new MorningCardVector(c.Id, embedder.Embed(CardText(c)))).ToArray();

    public static IReadOnlyList<MorningCardMatch> RankCards(float[] situation, IReadOnlyList<MorningCardVector> cards) =>
        cards.Select(c => new MorningCardMatch(c.Id, KnowledgeSearch.Cosine(situation, c.Vector)))
            .OrderByDescending(c => c.Score).ThenBy(c => c.Id).Take(3).ToArray();

    // X counts situations with at least one candidate and no failed knowledge read; Y includes every selected situation.
    public static MorningFact Format(IReadOnlyList<MorningSituationMatch> matches)
    {
        var failures = matches.Where(m => m.CouldNotRead is not null).Select(m => m.CouldNotRead!).Distinct().ToArray();
        return new MorningFact(FactName, matches.Select(Line).ToArray(),
            matches.Count(m => m.CouldNotRead is null && (m.Cards.Count > 0 || m.Claims.Count > 0)), matches.Count,
            failures.Length == 0 ? null : string.Join("; ", failures));
    }

    private static string Line(MorningSituationMatch match)
    {
        var situation = OneLine(match.Situation);
        if (situation.Length > 160) situation = situation[..160];
        var cards = match.Cards.OrderByDescending(c => c.Score).ThenBy(c => c.Id).Take(3).ToArray();
        var line = situation + " → cards " + (cards.Length == 0 ? "none" : string.Join(", ", cards.Select(c => $"#{c.Id} ({Score(c.Score)})")))
            + "; claims: " + (match.Claims.Count == 0 ? "none" : string.Join("; ", match.Claims.Select(c => $"{Quote(c.SourceTitle, c.Statement)} ({Score(c.Score)})")));
        var ids = match.Claims.Select(c => c.ClaimId).ToHashSet();
        foreach (var relation in match.Relations.Distinct().OrderBy(r => r.FromClaimId).ThenBy(r => r.ToClaimId).ThenBy(r => r.Kind, StringComparer.Ordinal))
        {
            if (relation.Kind is not ("Contradicts" or "Refines")) continue;
            if (ids.Contains(relation.ToClaimId))
                line += Related(relation.Kind == "Contradicts" ? "contested" : "refined by", relation.FromSourceTitle,
                    relation.FromStatement, relation.Note, relation.ToClaimId);
            else if (ids.Contains(relation.FromClaimId))
                line += Related(relation.Kind == "Contradicts" ? "contested" : "refines", relation.ToSourceTitle,
                    relation.ToStatement, relation.Note, relation.FromClaimId);
        }
        if (match.CouldNotRead is { } error) line += "; Could not read: " + OneLine(error);
        return line;
    }

    private static string Related(string kind, string title, string statement, string? note, long matchedId) =>
        $"; {kind}: {Quote(title, statement)} ({OneLine(note ?? "no note")}; matched claim #{matchedId})";
    private static string Quote(string title, string statement) => $"[{OneLine(title)}] \"{OneLine(statement)}\"";
    private static string Score(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);
    private static string OneLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
}

/// <summary>One run shares the local embedder and claim index. Failures stay in the affected role's fact.</summary>
public sealed class MorningKnowledgeReader(Func<IKnowledgeStore> createStore, IKnowledgeEmbedder embedder) : IDisposable
{
    private IKnowledgeStore? _store;
    private KnowledgeSearch? _search;

    public async Task<MorningFact> MatchAsync(IReadOnlyList<MorningFact> facts, string role, CancellationToken ct)
    {
        var situations = facts.SelectMany(f => f.Situations).ToArray();
        if (situations.Length == 0) return MorningKnowledge.Format(Array.Empty<MorningSituationMatch>());
        var matches = new List<MorningSituationMatch>();
        IReadOnlyList<MorningCardVector> cards;
        try
        {
            var cardFact = facts.Single(f => f.Name == "Accepted " + role + " cards");
            if (cardFact.CouldNotRead is not null) throw new InvalidOperationException("Accepted cards unavailable.");
            cards = MorningKnowledge.EmbedCards(cardFact.AcceptedCards, role, embedder);
            _store ??= createStore();
            _search ??= new KnowledgeSearch(_store, embedder);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return MorningKnowledge.Format(situations.Select(s => new MorningSituationMatch(s,
                Array.Empty<MorningCardMatch>(), Array.Empty<KnowledgeClaimHit>(), Array.Empty<KnowledgeClaimRelation>(), Failure(ex))).ToArray());
        }
        foreach (var situation in situations)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<MorningCardMatch> ranked = Array.Empty<MorningCardMatch>();
            IReadOnlyList<KnowledgeClaimHit> claims = Array.Empty<KnowledgeClaimHit>();
            try
            {
                ranked = MorningKnowledge.RankCards(embedder.Embed(situation), cards);
                claims = await _search.SearchClaimsAsync(situation, take: 3, perSource: 1, ct: ct);
                var relations = await _store.ListRelationsAsync(claims.Select(c => c.ClaimId).ToArray(), ct);
                matches.Add(new(situation, ranked, claims, relations));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                matches.Add(new(situation, ranked, claims, Array.Empty<KnowledgeClaimRelation>(), Failure(ex)));
            }
        }
        return MorningKnowledge.Format(matches);
    }

    // Exception messages can contain configuration or source text; report only the type.
    private static string Failure(Exception ex) => $"Knowledge read failed ({ex.GetType().Name}); check the local model and knowledge source.";
    public void Dispose() => _search?.Dispose();
}
