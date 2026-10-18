using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Search;
using Kor.Opportunities.Knowledge.Store;

namespace Kor.MorningBrief;

public sealed record MorningSituation(string Kind, string Query, string Printed, IReadOnlyCollection<string> Situations);
public sealed record MorningCardVector(long Id, float[] Vector, string AppliesTo = "any", double Weight = 1.0);
/// <summary>Score is relevance (cosine); Weight is the card's own record (DoctrineTrackRecord.RankWeight). They rank by Score × Weight.</summary>
public sealed record MorningCardMatch(long Id, float Score, double Weight = 1.0);
public sealed record MorningSituationMatch(string Situation, IReadOnlyList<MorningCardMatch> Cards,
    IReadOnlyList<KnowledgeClaimHit> Claims, IReadOnlyList<KnowledgeClaimRelation> Relations, string? CouldNotRead = null);

/// <summary>Local card embedding, pure ranking and pure formatting; no store reads.</summary>
public static class MorningKnowledge
{
    public const string FactName = "Knowledge matched to today's situations";
    public const float ClaimFloor = .55f;

    /// <summary>Removes structured identifiers and supplied names, without guessing names from ordinary words.</summary>
    public static string WithoutIdentifiers(string text, params string?[] names)
    {
        // Remove whole mailboxes before a supplied name can consume their local part.
        text = Regex.Replace(text, @"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        foreach (var name in names.Where(n => !string.IsNullOrWhiteSpace(n)).OrderByDescending(n => n!.Length))
            text = Regex.Replace(text, @"(?<!\w)" + Regex.Escape(name!) + @"(?!\w)", " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        const string identifiers = @"#\d+\b|\b\d{5}-\d{2}\b|\b\d{8}\b"
            + @"|\b\d{4}-\d{2}-\d{2}(?:T\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(?:Z|[+-]\d{2}:\d{2})?)?\b"
            + @"|(?:(?:\b(?:CAD|USD|EUR|GBP)\s*)?[$€£]\s*|\b(?:CAD|USD|EUR|GBP)\s+)\d[\d,]*(?:\.\d+)?(?:\s*[kKmM]\b)?"
            + @"|\b\d[\d,]*(?:\.\d+)?\s*(?:CAD|USD|EUR|GBP)\b";
        text = Regex.Replace(text, identifiers, " ", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    public static string CardText(DoctrineCardRow card) => $"{card.Title}. {card.Plain}. {card.Recognise}.";

    public static IReadOnlyList<MorningCardVector> EmbedCards(IEnumerable<DoctrineCardRow> cards, string role,
        IKnowledgeEmbedder embedder, IReadOnlyList<DoctrineCardRecord>? records = null)
    {
        var byCard = (records ?? Array.Empty<DoctrineCardRecord>()).DistinctBy(r => r.CardId).ToDictionary(r => r.CardId);
        return cards.Where(c => c.Role == role && c.Status == "accepted").DistinctBy(c => c.Id)
            .Select(c => new MorningCardVector(c.Id, embedder.Embed(CardText(c)), c.AppliesTo,
                DoctrineTrackRecord.RankWeight(byCard.GetValueOrDefault(c.Id)))).ToArray();
    }

    public static IReadOnlyList<MorningCardMatch> RankCards(float[] situation, IReadOnlyList<MorningCardVector> cards,
        IReadOnlyCollection<string>? situations = null)
    {
        var context = new DoctrineContext("hour", situations ?? Array.Empty<string>());
        // A card written for another moment (synthesis, one-to-one) never reaches the hour: live 19 Oct, #524 (staff wages,
        // moment:synthesis) topped every unpaid-client-invoice row on cosine alone.
        return cards.Where(c => BdDoctrine.AppliesTo(c.AppliesTo, context)).Select(c => new
            {
                Match = new MorningCardMatch(c.Id, KnowledgeSearch.Cosine(situation, c.Vector), c.Weight),
                // Only a situation term is specific. "any" and "moment:hour" hold for every morning situation, so letting them
                // count put a moment:hour card first on every row (live 19 Oct: #287 premortem above #210 receivables on all AR rows).
                Specific = BdDoctrine.AppliesTo(string.Join(",", c.AppliesTo.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Where(term => term.StartsWith("situation:", StringComparison.OrdinalIgnoreCase))), context),
            })
            // Within each group the card's own record moves it at most a quarter either way; relevance still decides.
            .OrderByDescending(c => c.Specific).ThenByDescending(c => c.Match.Score * c.Match.Weight).ThenBy(c => c.Match.Id)
            .Take(3).Select(c => c.Match).ToArray();
    }

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
        var cards = match.Cards.Take(3).ToArray();
        var line = situation + " → cards " + (cards.Length == 0 ? "none" : string.Join(", ", cards.Select(c => $"#{c.Id} ({Score(c.Score)}{Weight(c.Weight)})")))
            + "; claims: " + (match.Claims.Count == 0 ? "no claim above 0.55" : string.Join("; ", match.Claims.Select(c => $"{Quote(c.SourceTitle, c.Statement)} ({Score(c.Score)})")));
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
    // Printed only when the record has moved the card, so an unrecorded card reads exactly as before.
    private static string Weight(double value) => Math.Abs(value - 1.0) < 0.005 ? "" : " ×" + value.ToString("0.00", CultureInfo.InvariantCulture);
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
            cards = MorningKnowledge.EmbedCards(cardFact.AcceptedCards, role, embedder, cardFact.CardRecords);
            _store ??= createStore();
            _search ??= new KnowledgeSearch(_store, embedder);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            return MorningKnowledge.Format(situations.Select(s => new MorningSituationMatch(s.Printed,
                Array.Empty<MorningCardMatch>(), Array.Empty<KnowledgeClaimHit>(), Array.Empty<KnowledgeClaimRelation>(), Failure(ex))).ToArray());
        }
        // One call is one role's situations; other roles share the index, not their repeat allowance.
        var claimCounts = new Dictionary<long, int>();
        foreach (var situation in situations)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<MorningCardMatch> ranked = Array.Empty<MorningCardMatch>();
            IReadOnlyList<KnowledgeClaimHit> claims = Array.Empty<KnowledgeClaimHit>();
            try
            {
                ranked = MorningKnowledge.RankCards(embedder.Embed(situation.Query), cards, situation.Situations);
                var candidates = await _search.SearchClaimsAsync(situation.Query, take: 6, perSource: 1, ct: ct);
                claims = candidates.Where(c => c.Score >= MorningKnowledge.ClaimFloor && claimCounts.GetValueOrDefault(c.ClaimId) < 3)
                    .Take(3).ToArray();
                // These claims remain printed even if the relation read fails.
                foreach (var claim in claims) claimCounts[claim.ClaimId] = claimCounts.GetValueOrDefault(claim.ClaimId) + 1;
                var relations = await _store.ListRelationsAsync(claims.Select(c => c.ClaimId).ToArray(), ct);
                matches.Add(new(situation.Printed, ranked, claims, relations));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                matches.Add(new(situation.Printed, ranked, claims, Array.Empty<KnowledgeClaimRelation>(), Failure(ex)));
            }
        }
        return MorningKnowledge.Format(matches);
    }

    // Exception messages can contain configuration or source text; report only the type.
    private static string Failure(Exception ex) => $"Knowledge read failed ({ex.GetType().Name}); check the local model and knowledge source.";
    public void Dispose() => _search?.Dispose();
}
