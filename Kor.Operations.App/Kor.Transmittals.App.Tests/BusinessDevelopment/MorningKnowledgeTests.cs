using System.Text.RegularExpressions;
using Kor.MorningBrief;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.Financials;
using Kor.Operations.PMTools;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Knowledge.Embedding;
using Kor.Opportunities.Knowledge.Store;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>10e/10f gates, authored and not run. Synthetic fixtures never measure semantic match quality.</summary>
public sealed class MorningKnowledgeTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 17, 15, 0, 0, TimeSpan.Zero);

    /// <summary>Checks field-derived meaning, known-name/identifier removal and doctrine terms for six fixtures;
    /// not live rows or semantic quality. An unlabelled third-party name in free text is not detected.</summary>
    [Fact]
    public void Query_builders_use_each_kinds_fields_without_printed_identifiers()
    {
        const string identifiers = "Jane Smith jane@example.com #123 12345-67 20261017 2026-10-17 $12,345.67";
        var pursuit = new MorningFacts.Pursuit(123, "jane@example.com", "BC", identifiers, null, Now.AddDays(-140),
            "Developer", Now.AddDays(-140));
        var stale = Assert.Single(MorningFacts.PursuitFacts(new[] { pursuit }, Now)[1].Situations);
        var action = Action(123, "jane@example.com", "Jane Smith", Now.AddDays(-7)) with { Action = "Discuss fee recovery with " + identifiers };
        var late = Assert.Single(MorningFacts.ActionFacts(new[] { action }, Now)[1].Situations);
        var invoice = new KpiArInvoiceRow("12345-67", identifiers, "Jane Smith", "20261017", "#123", identifiers,
            Now.Date.AddDays(-90), Now.Date.AddDays(-60), 60, 12345.67);
        var project = new KpiProjectDrilldownRow("12345-67", identifiers, "Jane Smith", 20, 1.2, .5, .6, true);
        var executive = MorningFacts.ExecutiveFacts(new ExecutiveSummaryResult(Now, new List<ExecutiveKpi>
        {
            new("AR > 60 Days", "", "", "", ArInvoiceRows: new[] { invoice }),
            new("Projects Over Budget", "", "", "", ProjectDrilldownRows: new[] { project }),
        }, new List<ExecutiveTrend>(), new List<ExecutiveAlert>()), 1);
        var ar = Assert.Single(executive[1].Situations);
        var over = Assert.Single(executive[2].Situations);
        var week = new DateTime(2026, 10, 11);
        var partial = Assert.Single(MorningFacts.EmployeeWeekFacts(new[]
        {
            new EmployeeWeeklyHours { EmployeeId = "A", WeekStart = week.AddDays(-7), TotalHrs = 40 },
            new EmployeeWeeklyHours { EmployeeId = "B", WeekStart = week.AddDays(-7), TotalHrs = 40 },
            new EmployeeWeeklyHours { EmployeeId = "A", WeekStart = week, TotalHrs = 20 },
        }, week)[0].Situations);
        var it = MorningFacts.FindingSituation("NAS-01", 2, 1, "disk-missing: A drive has disappeared on NAS-01 " + identifiers);

        var cases = new[]
        {
            (Situation: stale, Kind: "stale pursuit", Phrase: "no contact for 140 days, no proposed fee"),
            (Situation: late, Kind: "late action", Phrase: "promised follow-up is overdue by 7 days: Discuss fee recovery"),
            (Situation: ar, Kind: "AR invoice", Phrase: "60 days unpaid (5–25k). Collect old receivables"),
            (Situation: over, Kind: "over budget", Phrase: "20% over its engineering-hours budget with 50% of the fee billed"),
            (Situation: partial, Kind: "partial timesheets", Phrase: "Timesheets are incomplete for recent weeks"),
            (Situation: it, Kind: "IT finding", Phrase: "A drive has disappeared"),
        };
        foreach (var (situation, kind, phrase) in cases)
        {
            Assert.Equal(kind, situation.Kind);
            Assert.Contains(phrase, situation.Query, StringComparison.Ordinal);
            Assert.NotEqual(situation.Printed, situation.Query);
            Assert.DoesNotMatch(@"\S+@\S+|#\d+|\b\d{5}-\d{2}\b|\b\d{8}\b|\b\d{4}-\d{2}-\d{2}\b|\$[\d,]+", situation.Query);
        }
        Assert.DoesNotContain("Jane Smith", late.Query, StringComparison.Ordinal);
        Assert.DoesNotContain("NAS-01", it.Query, StringComparison.Ordinal);
        Assert.Equal("Critical / NAS-01: 1 open findings: disk-missing: A drive has disappeared on NAS-01 " + identifiers, it.Printed);
        Assert.Equal(new[] { "going-cold", "never-touched" }, stale.Situations);
        Assert.Equal(new[] { "late-action" }, late.Situations);
        Assert.All(new[] { ar, over, partial, it }, s => Assert.Empty(s.Situations));
        var touched = Assert.Single(MorningFacts.PursuitFacts(new[] { pursuit with { Value = 100, OpenedAtUtc = Now.AddDays(-150) } }, Now)[1].Situations);
        Assert.Equal(new[] { "going-cold" }, touched.Situations);
        Assert.DoesNotContain("no proposed fee", touched.Query, StringComparison.Ordinal);
        var unknownOpened = Assert.Single(MorningFacts.PursuitFacts(new[] { pursuit with { OpenedAtUtc = null } }, Now)[1].Situations);
        Assert.Equal(new[] { "going-cold" }, unknownOpened.Situations);
    }

    /// <summary>Checks the pure scrubber's specified identifiers and supplied names; not arbitrary name recognition.</summary>
    [Fact]
    public void Identifier_removal_preserves_action_meaning_and_numeric_context()
    {
        var scrubbed = MorningKnowledge.WithoutIdentifiers(
            "Follow up with JANE SMITH jane.smith+bd@example.com #123 12345-67 20261017 2026-10-17T15:30:00Z CAD $12,345.67 USD 500 600 CAD €700 £800 after 7 days about fee recovery",
            "Jane Smith");
        Assert.Equal("Follow up with after 7 days about fee recovery", scrubbed);
        Assert.Equal("Follow up with", MorningKnowledge.WithoutIdentifiers("Follow up with Jane jane@example.com", "Jane"));
    }

    /// <summary>Checks balance-band boundaries; not whether the source's aging or balance is correct.</summary>
    [Theory]
    [InlineData(4999.99, "under 5k")]
    [InlineData(5000, "5–25k")]
    [InlineData(25000, "5–25k")]
    [InlineData(25000.01, "over 25k")]
    public void Invoice_queries_use_balance_bands(double balance, string band)
    {
        var invoice = new KpiArInvoiceRow("12345-67", "Project", "PM", "20261017", "Client", "Developer", Now.Date, null, 90, balance);
        var facts = MorningFacts.ExecutiveFacts(new ExecutiveSummaryResult(Now, new List<ExecutiveKpi>
        {
            new("AR > 60 Days", "", "", "", ArInvoiceRows: new[] { invoice }),
        }, new List<ExecutiveTrend>(), new List<ExecutiveAlert>()), 1);
        Assert.Contains("90 days unpaid (" + band + ")", Assert.Single(facts[1].Situations).Query, StringComparison.Ordinal);
    }

    /// <summary>Checks doctrine priority, cosine backfill and printed order using fake embeddings; not real match quality.</summary>
    [Fact]
    public void Doctrine_matches_precede_higher_cosine_any_cards_and_survive_formatting()
    {
        var cards = new[]
        {
            Card(1), Card(2) with { AppliesTo = "situation:going-cold" },
            Card(3), Card(4) with { AppliesTo = "any, situation:GOING-COLD" },
        };
        var embedder = new FixedEmbedder(new Dictionary<string, float[]>
        {
            [MorningKnowledge.CardText(cards[0])] = CosineVector(.70f),
            [MorningKnowledge.CardText(cards[1])] = CosineVector(.50f),
            [MorningKnowledge.CardText(cards[2])] = CosineVector(.65f),
            [MorningKnowledge.CardText(cards[3])] = CosineVector(.40f),
        });
        var embedded = MorningKnowledge.EmbedCards(cards, "BD", embedder);
        var cold = MorningKnowledge.RankCards(Vector(1, 0), embedded, new[] { "going-cold" });
        Assert.Equal(new long[] { 2, 4, 1 }, cold.Select(c => c.Id));
        Assert.Equal(.50f, cold[0].Score, 5);
        var formatted = MorningKnowledge.Format(new[]
        {
            new MorningSituationMatch("Quiet pursuit", cold, Array.Empty<KnowledgeClaimHit>(), Array.Empty<KnowledgeClaimRelation>()),
        });
        Assert.Contains("cards #2 (0.50), #4 (0.40), #1 (0.70)", Assert.Single(formatted.Lines), StringComparison.Ordinal);
        var ar = MorningKnowledge.RankCards(Vector(1, 0), embedded, Array.Empty<string>());
        // Card 2 applies only to situation:going-cold, so it never reaches an AR row; card 4 also carries "any".
        Assert.Equal(new long[] { 1, 3, 4 }, ar.Select(c => c.Id));
        var hour = MorningKnowledge.RankCards(Vector(1, 0), new[]
        {
            embedded[0], embedded[1] with { AppliesTo = "moment:hour" },
        }, Array.Empty<string>());
        // moment:hour holds for every morning situation, so it is not specific: cosine decides.
        Assert.Equal(new long[] { 1, 2 }, hour.Select(c => c.Id));
        var hourCold = MorningKnowledge.RankCards(Vector(1, 0), new[]
        {
            embedded[0], embedded[1] with { AppliesTo = "moment:hour" }, embedded[3] with { AppliesTo = "moment:hour, situation:going-cold" },
        }, new[] { "going-cold" });
        Assert.Equal(new long[] { 4, 1, 2 }, hourCold.Select(c => c.Id));
        // A card for another moment never reaches the hour, however close its text.
        var otherMoment = MorningKnowledge.RankCards(Vector(1, 0), new[]
        {
            embedded[0] with { AppliesTo = "moment:synthesis" }, embedded[1] with { AppliesTo = "moment:one-to-one" }, embedded[2],
        }, Array.Empty<string>());
        Assert.Equal(new long[] { 3 }, otherMoment.Select(c => c.Id));
    }

    /// <summary>Checks six-candidate backfill, three-per-situation/role caps, printed failures and query-only embedding;
    /// not live claim relevance or repeated quotations stored under different claim IDs.</summary>
    [Fact]
    public async Task Reader_caps_each_claim_at_three_situations_and_backfills_within_each_role()
    {
        var claims = Enumerable.Range(1, 7).Select(i => new KnowledgeClaim(i, i, "Book " + i, i, "Rule", "Claim " + i, 1)).ToArray();
        var store = ClaimStore(claims, claims.Select(c => (c.Id, c.SourceId, CosineVector(1 - c.Id * .05f))).ToArray());
        var requests = new List<long[]>();
        store.Setup(s => s.ListRelationsAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyCollection<long> ids, CancellationToken _) =>
            {
                requests.Add(ids.ToArray());
                if (requests.Count == 1) throw new InvalidOperationException("Fixture relation failure");
                return Task.FromResult<IReadOnlyList<KnowledgeClaimRelation>>(Array.Empty<KnowledgeClaimRelation>());
            });
        var situations = Enumerable.Range(1, 5).Select(i => Situation("Printed #" + i)).ToArray();
        var situationFact = new MorningFact("Situations", situations.Select(s => s.Printed).ToArray(), 5, 5) { Situations = situations };
        var embedder = new FixedEmbedder();
        using var reader = new MorningKnowledgeReader(() => store.Object, embedder);
        var result = await reader.MatchAsync(new[] { situationFact, new MorningFact("Accepted BD cards", Array.Empty<string>(), 0, 0) }, "BD", CancellationToken.None);
        Assert.Equal(4, result.Shown);
        Assert.Equal(5, result.Total);
        Assert.All(result.Lines.Take(3), line => Assert.Contains("[Book 1] \"Claim 1\"", line, StringComparison.Ordinal));
        Assert.All(result.Lines.Skip(3), line =>
        {
            Assert.DoesNotContain("[Book 1]", line, StringComparison.Ordinal);
            foreach (var id in new[] { 4, 5, 6 }) Assert.Contains($"[Book {id}] \"Claim {id}\"", line, StringComparison.Ordinal);
        });
        Assert.All(requests.Take(3), ids => Assert.Equal(new long[] { 1, 2, 3 }, ids));
        Assert.All(requests.Skip(3), ids => Assert.Equal(new long[] { 4, 5, 6 }, ids));
        Assert.All(requests.SelectMany(ids => ids).GroupBy(id => id), group => Assert.InRange(group.Count(), 1, 3));
        foreach (var situation in situations)
        {
            Assert.Contains(situation.Query, embedder.Inputs);
            Assert.DoesNotContain(situation.Printed, embedder.Inputs);
        }
        var it = await reader.MatchAsync(new[] { situationFact, new MorningFact("Accepted IT cards", Array.Empty<string>(), 0, 0) }, "IT", CancellationToken.None);
        Assert.Contains("[Book 1] \"Claim 1\"", it.Lines[0], StringComparison.Ordinal);
        store.Verify(s => s.LoadClaimVectorsAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Checks inclusive 0.55 floor, explicit empty text and coverage with fake vectors; not semantic accuracy.</summary>
    [Theory]
    [InlineData(.50f, false)]
    [InlineData(.55f, true)]
    public async Task Reader_applies_claim_floor_and_prints_when_no_claim_qualifies(float score, bool qualifies)
    {
        var claim = new KnowledgeClaim(1, 1, "Book", 1, "Rule", "Claim", 1);
        var store = ClaimStore(new[] { claim }, new[] { (claim.Id, claim.SourceId, CosineVector(score)) });
        using var reader = new MorningKnowledgeReader(() => store.Object, new FixedEmbedder());
        var result = await reader.MatchAsync(new[]
        {
            new MorningFact("Situations", new[] { "Printed" }, 1, 1) { Situations = new[] { Situation("Printed") } },
            new MorningFact("Accepted BD cards", Array.Empty<string>(), 0, 0),
        }, "BD", CancellationToken.None);
        Assert.Equal(qualifies ? 1 : 0, result.Shown);
        Assert.Equal(1, result.Total);
        Assert.Null(result.CouldNotRead);
        Assert.Contains(qualifies ? "[Book] \"Claim\" (0.55)" : "claims: no claim above 0.55", Assert.Single(result.Lines), StringComparison.Ordinal);
        store.Verify(s => s.ListRelationsAsync(It.Is<IReadOnlyCollection<long>>(ids => ids.Count == (qualifies ? 1 : 0)), It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Checks formatting, direction, notes, top three, truncation and coverage; not match quality or live data.</summary>
    [Fact]
    public void Formatter_prints_three_ranked_cards_claims_relations_and_coverage()
    {
        var situation = new string('x', 180);
        var cards = new[] { new MorningCardMatch(210, .71f), new MorningCardMatch(209, .64f), new MorningCardMatch(213, .58f), new MorningCardMatch(999, .1f) };
        var claim = new KnowledgeClaimHit(10, 1, "Book A", 100, "Rule", "Original claim", .78f);
        var relations = new[]
        {
            new KnowledgeClaimRelation(20, "Disagrees", "Book B", 10, "Original claim", "Book A", "Contradicts", "Use in a downturn"),
            new KnowledgeClaimRelation(30, "Adds detail", "Book C", 10, "Original claim", "Book A", "Refines", "For small firms"),
            new KnowledgeClaimRelation(10, "Original claim", "Book A", 40, "Broader rule", "Book D", "Refines", "Narrower scope"),
            new KnowledgeClaimRelation(99, "Unrelated", "Book X", 98, "Unrelated", "Book Y", "Contradicts", "Ignore"),
        };
        var fact = MorningKnowledge.Format(new[]
        {
            new MorningSituationMatch(situation, cards, new[] { claim }, relations),
            new MorningSituationMatch("No candidates", Array.Empty<MorningCardMatch>(), Array.Empty<KnowledgeClaimHit>(), Array.Empty<KnowledgeClaimRelation>()),
            new MorningSituationMatch("Read failure", cards, new[] { claim }, Array.Empty<KnowledgeClaimRelation>(), "Fixture failure"),
        });
        Assert.Equal(1, fact.Shown);
        Assert.Equal(3, fact.Total);
        Assert.Equal("Fixture failure", fact.CouldNotRead);
        var line = fact.Lines[0];
        Assert.Equal(new string('x', 160), line.Split(" → ")[0]);
        Assert.Contains("cards #210 (0.71), #209 (0.64), #213 (0.58);", line, StringComparison.Ordinal);
        Assert.DoesNotContain("#999", line, StringComparison.Ordinal);
        Assert.Contains("claims: [Book A] \"Original claim\" (0.78)", line, StringComparison.Ordinal);
        Assert.Contains("contested: [Book B] \"Disagrees\" (Use in a downturn; matched claim #10)", line, StringComparison.Ordinal);
        Assert.Contains("refined by: [Book C] \"Adds detail\" (For small firms; matched claim #10)", line, StringComparison.Ordinal);
        Assert.Contains("refines: [Book D] \"Broader rule\" (Narrower scope; matched claim #10)", line, StringComparison.Ordinal);
        Assert.DoesNotContain("Unrelated", line, StringComparison.Ordinal);
        var prompt = MorningPromptComposer.Compose(new MorningFacts(Now, new[] { fact }, Array.Empty<MorningFact>(), Array.Empty<MorningFact>()), new DateOnly(2026, 10, 17));
        Assert.Contains("Knowledge matched to today's situations · 1 of 3", prompt, StringComparison.Ordinal);
    }

    /// <summary>Fixes cosine order and exact card input with fake vectors; not ONNX execution or match quality.</summary>
    [Fact]
    public void Card_embedding_uses_accepted_role_text_once_and_cosine_has_no_threshold()
    {
        var cards = new[] { Card(1), Card(2), Card(3), Card(4), Card(1), Card(5, "IT"), Card(6, status: "proposed") };
        var embedder = new FixedEmbedder(new Dictionary<string, float[]>
        {
            ["Card 1. Plain. Recognise."] = Vector(1, 0),
            ["Card 2. Plain. Recognise."] = Vector(0, 1),
            ["Card 3. Plain. Recognise."] = Vector(-1, 0),
            ["Card 4. Plain. Recognise."] = Vector(80, 60),
        });
        var embedded = MorningKnowledge.EmbedCards(cards, "BD", embedder);
        var ranked = MorningKnowledge.RankCards(Vector(1, 0), embedded);
        Assert.Equal(new long[] { 1, 4, 2 }, ranked.Select(c => c.Id));
        Assert.Equal(.8f, ranked[1].Score, 5);
        Assert.Equal(4, embedder.Inputs.Count);
        Assert.All(embedder.Inputs, text => Assert.Equal(1, embedder.Inputs.Count(value => value == text)));
        Assert.Equal(-1f, Assert.Single(MorningKnowledge.RankCards(Vector(1, 0), embedded.Where(c => c.Id == 3).ToArray())).Score);
        Assert.Contains("KnowledgeSearch.Cosine", Source("tools/MorningBrief/MorningKnowledge.cs"), StringComparison.Ordinal);
    }

    /// <summary>Checks caps, row reuse and Ian-first priority; not source freshness, identity resolution or match quality.</summary>
    [Fact]
    public void Situation_selection_reuses_printed_rows_with_each_cap()
    {
        var pursuits = Enumerable.Range(1, 12).Select(i => new MorningFacts.Pursuit(i, "Owner", "BC", "Pursuit " + i, i, Now.AddDays(-20))).ToArray();
        var stale = MorningFacts.PursuitFacts(pursuits, Now)[1];
        Assert.Equal(10, stale.Situations.Count);
        Assert.Equal(stale.Lines, stale.Situations.Select(s => s.Printed));
        var actions = Enumerable.Range(1, 12).Select(i => Action(i, "someone", "Other", Now.AddDays(-10)))
            .Append(Action(20, "ilalonde@korstructural.com", "Ian Lalonde", Now.AddDays(-1)))
            .Append(Action(21, "ian", "Ian", Now.AddDays(-2)))
            .Append(Action(22, "ian", "Ian", Now.AddDays(1)))
            .Append(Action(23, "ian", "Ian", Now.AddDays(-20)) with { Status = "Closed" }).ToArray();
        var late = MorningFacts.ActionFacts(actions, Now)[1];
        Assert.Equal(14, late.Total);
        Assert.Equal(10, late.Situations.Count);
        Assert.Equal(late.Lines, late.Situations.Select(s => s.Printed));
        Assert.StartsWith("Ian: #21", late.Situations[0].Printed, StringComparison.Ordinal);
        Assert.StartsWith("Ian Lalonde: #20", late.Situations[1].Printed, StringComparison.Ordinal);
        Assert.StartsWith("Other: #1 ", late.Situations[2].Printed, StringComparison.Ordinal);

        var invoices = Enumerable.Range(1, 12).Select(i => new KpiArInvoiceRow("P" + i, "Project", "PM", "INV" + i,
            "Client", "Client name", Now.Date, Now.Date.AddDays(-90), 90, 100)).ToArray();
        var projects = Enumerable.Range(1, 7).Select(i => new KpiProjectDrilldownRow("P" + i, "Project", "PM", 20, 1.2, .5, .6, true)).ToArray();
        var executive = new ExecutiveSummaryResult(Now, new List<ExecutiveKpi>
        {
            new("AR > 60 Days", "1200", "", "", ArInvoiceRows: invoices),
            new("Projects Over Budget", "7", "", "", ProjectDrilldownRows: projects),
        }, new List<ExecutiveTrend>(), new List<ExecutiveAlert>());
        var facts = MorningFacts.ExecutiveFacts(executive, 20);
        Assert.Equal(10, facts[1].Situations.Count);
        Assert.Equal(5, facts[2].Situations.Count);
        foreach (var fact in facts)
            Assert.All(fact.Situations, situation => Assert.Contains(situation.Printed, fact.Lines));
        Assert.StartsWith("P1 ", facts[1].Situations[0].Printed, StringComparison.Ordinal);
        Assert.StartsWith("P5 ", facts[2].Situations[^1].Printed, StringComparison.Ordinal);

        var week = new DateTime(2026, 10, 11);
        var weeks = new[]
        {
            new EmployeeWeeklyHours { EmployeeId = "A", WeekStart = week.AddDays(-7), TotalHrs = 40 },
            new EmployeeWeeklyHours { EmployeeId = "B", WeekStart = week.AddDays(-7), TotalHrs = 40 },
            new EmployeeWeeklyHours { EmployeeId = "A", WeekStart = week, TotalHrs = 20 },
        };
        var partial = MorningFacts.EmployeeWeekFacts(weeks, week)[0];
        Assert.Contains("partial: week 2026-10-11", Assert.Single(partial.Situations).Printed, StringComparison.Ordinal);
        Assert.Contains(partial.Situations[0].Printed, partial.Lines);
        Assert.Empty(MorningFacts.EmployeeWeekFacts(weeks.Take(2).ToArray(), week)[0].Situations);
        var source = Source("tools/MorningBrief/MorningFacts.cs");
        Assert.Contains("ORDER BY f.Severity DESC, d.Name", source, StringComparison.Ordinal);
        Assert.Contains("Situations = rows.Lines.Take(10).ToArray()", source, StringComparison.Ordinal);
    }

    /// <summary>Checks claim cap, per-book diversity, shared index and failure isolation; not SQL, ONNX or match quality.</summary>
    [Fact]
    public async Task Reader_reuses_cards_and_index_and_keeps_failed_relations_visible()
    {
        var store = new Mock<IKnowledgeStore>(MockBehavior.Strict);
        var claims = Enumerable.Range(1, 5).Select(i => new KnowledgeClaim(i, i == 2 ? 1 : i, "Book " + i, i, "Rule", "Claim " + i, 1)).ToArray();
        store.Setup(s => s.LoadClaimVectorsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(claims.Select(c => (c.Id, c.SourceId, Vector(1, 0))).ToArray());
        store.Setup(s => s.GetClaimsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<long> ids, CancellationToken _) => claims.Where(c => ids.Contains(c.Id)).ToArray());
        var requests = new List<long[]>();
        store.Setup(s => s.ListRelationsAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .Returns((IReadOnlyCollection<long> ids, CancellationToken _) =>
            {
                requests.Add(ids.ToArray());
                if (requests.Count == 1) throw new InvalidOperationException("Secret must not be printed");
                return Task.FromResult<IReadOnlyList<KnowledgeClaimRelation>>(Array.Empty<KnowledgeClaimRelation>());
            });
        var embedder = new FixedEmbedder();
        using var reader = new MorningKnowledgeReader(() => store.Object, embedder);
        var facts = new[] { new MorningFact("Situations", new[] { "First", "Second" }, 2, 2)
            { Situations = new[] { Situation("First"), Situation("Second") } },
            new MorningFact("Accepted BD cards", Array.Empty<string>(), 1, 1) { AcceptedCards = new[] { Card(1) } } };
        var result = await reader.MatchAsync(facts, "BD", CancellationToken.None);
        Assert.Equal(1, result.Shown);
        Assert.Equal(2, result.Total);
        Assert.NotNull(result.CouldNotRead);
        Assert.DoesNotContain("Secret", result.CouldNotRead!, StringComparison.Ordinal);
        Assert.Equal(1, embedder.Inputs.Count(text => text == "Card 1. Plain. Recognise."));
        Assert.All(requests, ids => Assert.Equal(new long[] { 1, 3, 4 }, ids));
        var it = await reader.MatchAsync(new[] { facts[0], new MorningFact("Accepted IT cards", Array.Empty<string>(), 0, 0) }, "IT", CancellationToken.None);
        Assert.Null(it.CouldNotRead);
        Assert.Equal(2, it.Shown);
        store.Verify(s => s.LoadClaimVectorsAsync(It.IsAny<CancellationToken>()), Times.Once);
        var empty = await reader.MatchAsync(Array.Empty<MorningFact>(), "IT", CancellationToken.None);
        Assert.Equal(0, empty.Total);
    }

    /// <summary>Checks typed SQL placeholders, both ends, chunking and joins as text; not SQL execution or relation quality.</summary>
    [Fact]
    public void Relations_query_uses_bigint_parameters_for_both_ends()
    {
        var source = Source("Kor.Opportunities.Knowledge/Store/SqlKnowledgeStore.cs");
        var query = source[source.IndexOf("public async Task<IReadOnlyList<KnowledgeClaimRelation>> ListRelationsAsync", StringComparison.Ordinal)
            ..source.IndexOf("public async Task<(int Written, int Rejected)> ImportRelationsAsync", StringComparison.Ordinal)];
        foreach (var expected in new[] { "claimIds.Distinct().Order().Chunk(500)", "cmd.Parameters.Add(name, SqlDbType.BigInt).Value = id",
            "string.Join(\",\", names)", "r.FromClaimId IN ({parameters}) OR r.ToClaimId IN ({parameters})",
            "f.Statement, fs.Title", "t.Statement, ts.Title", "rows.TryAdd", "if (claimIds.Count == 0)" })
            Assert.Contains(expected, query, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"string\.Join\([^\r\n]*(?:claimIds|batch|ids)\)", query);
        Assert.DoesNotContain("{claimIds}", query, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\b(INSERT|UPDATE|DELETE|MERGE|EXEC)\b", query);
    }

    /// <summary>Checks schema guards and CLI/prompt wiring as text; not applying SQL, executing the CLI or live doctrine.</summary>
    [Fact]
    public void IT_schema_cli_and_prompt_allow_the_third_role()
    {
        var migration = Source("Kor.Opportunities.Data/Schema/367_ItDoctrineRole.sql");
        Assert.Equal(2, Regex.Matches(migration, Regex.Escape("CHECK (Role IN (N'BD', N'Operations', N'IT'))")).Count);
        foreach (var table in new[] { "DoctrineCard", "BdPromptRun" })
        {
            Assert.Contains($"IF OBJECT_ID(N'opportunities.{table}', N'U') IS NOT NULL", migration, StringComparison.Ordinal);
            Assert.Contains($"IF OBJECT_ID(N'opportunities.CK_{table}_Role', N'C') IS NOT NULL", migration, StringComparison.Ordinal);
            Assert.Contains($"DROP CONSTRAINT CK_{table}_Role", migration, StringComparison.Ordinal);
        }
        Assert.Contains("BEGIN TRANSACTION", migration, StringComparison.Ordinal);
        Assert.Contains("COMMIT TRANSACTION", migration, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\b(INSERT|UPDATE|DELETE|MERGE)\b", migration);
        var cli = Source("tools/BdKnowledgeIngest/Program.cs");
        Assert.Contains("role is not (\"BD\" or \"Operations\" or \"IT\")", cli, StringComparison.Ordinal);
        Assert.Contains("synthesisRole is not (\"BD\" or \"Operations\" or \"IT\")", cli, StringComparison.Ordinal);
        Assert.Contains(".Concat(await doctrine.ListCardsAsync(\"IT\", ct))", cli, StringComparison.Ordinal);
        Assert.Contains("new[] { \"BD\", \"Operations\", \"IT\" }.Where", cli, StringComparison.Ordinal);
        var prompt = Source("tools/MorningBrief/MorningPromptComposer.cs");
        Assert.DoesNotContain("there is no IT doctrine", prompt, StringComparison.Ordinal);
        foreach (var expected in new[] { "IT moves may cite only accepted IT cards below", "--role IT", "candidates found by meaning", "books disagree", "Other accepted cards of the role" })
            Assert.Contains(expected, prompt, StringComparison.Ordinal);
        Assert.Contains("a systems and IT director for a sixty-person engineering firm", Source("Kor.Opportunities.Data/Crm/BdPromptComposer.cs"), StringComparison.Ordinal);
        Assert.Contains("await CardsAsync(facts, \"IT\", ct);", Source("tools/MorningBrief/MorningFacts.cs"), StringComparison.Ordinal);
    }

    /// <summary>Checks selection and duplicate-pool coverage for all roles; not WPF rendering, persistence or match quality.</summary>
    [Theory]
    [InlineData("BD")]
    [InlineData("Operations")]
    [InlineData("IT")]
    public async Task Doctrine_view_loads_both_other_roles(string role)
    {
        var store = new Mock<IBdDoctrineStore>();
        var roles = new[] { "BD", "Operations", "IT" };
        for (var i = 0; i < roles.Length; i++)
            store.Setup(s => s.ListCardsAsync(roles[i], It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Card(i + 1, roles[i]) });
        store.Setup(s => s.ListRecordsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRecord>());
        store.Setup(s => s.ListRunsAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdPromptRunRow>());
        var vm = new BdDoctrineViewModel(store.Object, () => Now);
        if (role == "BD") await vm.LoadAsync(CancellationToken.None);
        else await vm.SelectRoleAsync(role, CancellationToken.None);
        Assert.Equal(roles, vm.Roles);
        Assert.Equal(role, Assert.Single(vm.Accepted).Card.Role);
        Assert.Equal(3, vm.CitationCards.Count);
        foreach (var expected in roles)
            store.Verify(s => s.ListCardsAsync(expected, It.IsAny<CancellationToken>()), Times.Once);
    }

    /// <summary>Checks fallback order, SELECT-only statements and variable-name coverage; not credentials, access or live reads.</summary>
    [Fact]
    public void Network_reads_use_watch_then_service_connection_and_only_SELECT()
    {
        var source = Source("tools/MorningBrief/MorningFacts.cs");
        Assert.Contains("var watchConnection = OptionalConnection(\"KOR_NETWORKOPS_WATCHDB\");", source, StringComparison.Ordinal);
        Assert.Contains("var connection = watchConnection ?? Connection(\"KOR_NETWORKOPS_DB\");", source, StringComparison.Ordinal);
        Assert.Contains("watchConnection is not null ? \"KOR_NETWORKOPS_WATCHDB\" : \"KOR_NETWORKOPS_DB\"", source, StringComparison.Ordinal);
        Assert.Contains("connection variable {rows.ConnectionVariable}", source, StringComparison.Ordinal);
        var statements = Regex.Matches(source, "NetworkRowsAsync\\(\"\"\"(?<sql>.*?)\"\"\"", RegexOptions.Singleline);
        Assert.Equal(3, statements.Count);
        foreach (System.Text.RegularExpressions.Match statement in statements)
        {
            var sql = statement.Groups["sql"].Value.Trim();
            Assert.StartsWith("SELECT", sql, StringComparison.Ordinal);
            Assert.DoesNotMatch(@"(?i)\b(INSERT|UPDATE|DELETE|MERGE|EXEC)\b", sql);
        }
    }

    private static DoctrineCardRow Card(long id, string role = "BD", string status = "accepted") => new(id, role, "Card " + id,
        "Plain", "any", "Principle", "Recognise", "Play", null, "Synthetic", null, null, null, status, Now, null, null);
    private static MorningSituation Situation(string printed) => new("fixture", "Meaning of " + printed, printed, Array.Empty<string>());
    private static BdActionRow Action(long id, string key, string owner, DateTimeOffset due) => new(id, key, owner, "Action " + id,
        null, 20, due, "Open", null, null, null, null, null, null, null, null, null, null);
    private static Mock<IKnowledgeStore> ClaimStore(IReadOnlyList<KnowledgeClaim> claims, IReadOnlyList<(long ClaimId, long SourceId, float[] Vector)> vectors)
    {
        var store = new Mock<IKnowledgeStore>(MockBehavior.Strict);
        store.Setup(s => s.LoadClaimVectorsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(vectors);
        store.Setup(s => s.GetClaimsAsync(It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<long> ids, CancellationToken _) => claims.Where(c => ids.Contains(c.Id)).ToArray());
        store.Setup(s => s.ListRelationsAsync(It.IsAny<IReadOnlyCollection<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<KnowledgeClaimRelation>());
        return store;
    }
    private static float[] CosineVector(float score) => Vector(score, MathF.Sqrt(1 - score * score));
    private static float[] Vector(float x, float y)
    {
        var vector = new float[KnowledgeEmbedder.Dimensions];
        vector[0] = x; vector[1] = y;
        return vector;
    }
    private sealed class FixedEmbedder(IReadOnlyDictionary<string, float[]>? vectors = null) : IKnowledgeEmbedder
    {
        public List<string> Inputs { get; } = new();
        public float[] Embed(string text)
        {
            Inputs.Add(text);
            return vectors is null ? Vector(1, 0) : vectors[text];
        }
    }
    private static string Source(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md"))) return File.ReadAllText(Path.Combine(dir.FullName, path));
        throw new DirectoryNotFoundException("Repository root not found.");
    }
}
