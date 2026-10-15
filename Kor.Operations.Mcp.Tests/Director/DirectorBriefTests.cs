using System.Text.RegularExpressions;
using Kor.Operations.Mcp.Director;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Knowledge.Store;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kor.Operations.Mcp.Tests.Director;

/// <summary>
/// Covers prompt evidence/JSON contracts, offered-only citations, fenced parsing, per-section
/// failure isolation, facts bounds on synthetic populations and the COO-shaped SQL schema.
/// No model, database, scheduler, HTTP server or real knowledge is accessed. Same-class miss:
/// a correct-looking facts table could carry an incorrect store aggregate; SQL execution and
/// business correctness are outside these fixtures. Delegates exercise the production loop.
/// </summary>
public sealed class DirectorBriefTests
{
    private static readonly KnowledgeClaimHit[] Offered = { new(7, 1, "Synthetic book", 70, "Rule", "Illustrative advice.", 0.9f) };

    [Fact]
    public void Prompts_EverySectionCarriesFactsClaimsAndCitationContract()
    {
        foreach (var section in Enum.GetValues<DirectorSection>())
        {
            var prompt = DirectorBriefPrompts.Build(section, "synthetic facts", Offered);
            foreach (var expected in new[] { "FACTS:", "CLAIMS:", "synthetic facts", "[7] (Synthetic book)", "\"headline\"", "\"body\"", "\"recommendation\"", "\"citations\"", "Cite only ids offered. If no claim applies, citations is []." })
                Assert.Contains(expected, prompt, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(DirectorBriefPrompts.ClaimsQuery(section)));
        }
    }

    [Fact]
    public void Parser_FencedAnswerDropsUnofferedIdsAndDuplicates_AndDoesNotInventCitations()
    {
        var answer = new DirectorAnswer("""
            Narration before the final response.
            ```json
            {"headline":"Example","body":"Synthetic {evidence}.","recommendation":"Take a step.","citations":[7,999,7,"7"]}
            ```
            """, 10, 20, 2);
        Assert.True(DirectorBriefGenerator.TryParseAnswer(answer, DirectorSection.Accounts, Offered, out var row));
        Assert.Equal(new DirectorCitation(7, 70, "Illustrative advice.", "Synthetic book"), Assert.Single(row.Citations));
        Assert.Equal(10, row.InputTokens);
        Assert.True(DirectorBriefGenerator.TryParseAnswer(answer with { Answer = """{"headline":"Example","body":"Synthetic.","recommendation":"Uncited."}""" }, DirectorSection.Market, Offered, out var uncited));
        Assert.Empty(uncited.Citations);
        Assert.False(DirectorBriefGenerator.TryParseAnswer(answer with { Answer = "No JSON here." }, DirectorSection.Market, Offered, out _));
    }

    [Fact]
    public async Task Generator_RunsAllFiveSections_WhenThirdAskFails()
    {
        var calls = 0;
        var rows = new List<RichDirectorSection>();
        var log = new CapturedLog();
        var generator = new DirectorBriefGenerator((section, ct) => Task.FromResult("Synthetic facts."),
            (query, ct) => Task.FromResult<IReadOnlyList<KnowledgeClaimHit>>(Offered),
            (prompt, ct) => ++calls == 3 ? Task.FromException<DirectorAnswer>(new InvalidOperationException("Synthetic failure"))
                : Task.FromResult(new DirectorAnswer("""{"headline":"Example","body":"Synthetic.","recommendation":"Act.","citations":[7]}""", 1, 2, 0)),
            (week, row, ct) => { rows.Add(row); return Task.FromResult((long)rows.Count); }, log);
        await generator.GenerateForWeekAsync(new DateTime(2026, 10, 12), CancellationToken.None);
        Assert.Equal(5, calls); Assert.True(rows.Count == 4, "EXPECTED four rows after one failed section.");
        Assert.DoesNotContain(rows, r => r.Section == DirectorSection.Accounts);
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Error);
    }

    [Fact]
    public async Task Generator_NonJsonLogsWarningAndWritesNothing_EmptyClaimsRunOnFactsAlone()
    {
        var log = new CapturedLog();
        var rows = new List<RichDirectorSection>();
        var calls = 0;
        var generator = new DirectorBriefGenerator((section, ct) => Task.FromResult("Synthetic facts."),
            (query, ct) => Task.FromResult<IReadOnlyList<KnowledgeClaimHit>>(Array.Empty<KnowledgeClaimHit>()),
            (prompt, ct) => Task.FromResult(new DirectorAnswer(++calls == 1 ? "Not JSON" : """{"headline":"Example","body":"Synthetic.","recommendation":"Act.","citations":[7]}""", 1, 2, 0)),
            (week, row, ct) => { rows.Add(row); return Task.FromResult((long)rows.Count); }, log);
        await generator.GenerateForWeekAsync(DateTime.Today, CancellationToken.None);
        Assert.True(rows.Count == 4, "EXPECTED four rows after one non-JSON answer.");
        Assert.Contains(log.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("non-JSON", StringComparison.Ordinal));
        Assert.All(rows, row => { Assert.Empty(row.Citations); Assert.Contains("facts alone", row.Body, StringComparison.Ordinal); });
    }

    [Fact]
    public async Task Facts_AreBoundedFor101PursuitsAnd40Accounts()
    {
        var store = new FactsStore();
        var facts = new DirectorFacts(store, store, store);
        foreach (var section in Enum.GetValues<DirectorSection>())
        {
            var block = await facts.GetAsync(section, CancellationToken.None);
            Assert.InRange(block.Length, 1, 6000);
            Assert.DoesNotContain("NaN", block, StringComparison.Ordinal);
        }
        var accounts = await facts.GetAsync(DirectorSection.Accounts, CancellationToken.None);
        Assert.Contains("Additional rows omitted", accounts, StringComparison.Ordinal);
        var pursuits = await facts.GetAsync(DirectorSection.Pursuits, CancellationToken.None);
        Assert.Contains("101", pursuits, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectorTable_MirrorsCooTablePlusCitationsDefault()
    {
        var expected = Source("Kor.Operations.Mcp/Sql/CreateCooBriefTable.sql").Replace("CooBrief", "DirectorBrief", StringComparison.Ordinal);
        var actual = Source("Kor.Operations.Mcp/Sql/CreateDirectorBriefTable.sql");
        Assert.Contains("Citations       NVARCHAR(MAX)  NOT NULL DEFAULT N''[]''", actual, StringComparison.Ordinal);
        actual = Regex.Replace(actual, @",\s*Citations\s+NVARCHAR\(MAX\)\s+NOT NULL DEFAULT N''\[\]''", "");
        Assert.Equal(Regex.Replace(expected, @"\s+", " ").Trim(), Regex.Replace(actual, @"\s+", " ").Trim());
    }

    private sealed class CapturedLog : ILogger<DirectorBriefGenerator>
    {
        internal List<(LogLevel Level, string Message)> Entries { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private sealed class FactsStore : IBdDirectorStore, IBdDirectorLedgerStore, IBdDashboardStore
    {
        private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch;
        public Task<IReadOnlyList<BdDirectorPerson>> GetPeopleAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BdDirectorPerson>>(new[]
            { new BdDirectorPerson("engineer", "Synthetic engineer", new[] { "engineer" }, 101, 24, 74, 90, 5, 9, 3, 4, 1) });
        public Task<BdDirectorFunnel> GetFunnelAsync(CancellationToken ct) => Task.FromResult(new BdDirectorFunnel(new Dictionary<int, double> { [1] = 90 },
            Enumerable.Range(1, 101).Select(i => new BdDirectorUndecidedPursuit(i, "Synthetic pursuit " + i, "Buyer", "engineer", Now)).ToArray(), 0, 0, 0, 0));
        public Task<IReadOnlyList<BdDirectorAccount>> GetAccountsAsync(int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<BdDirectorAccount>>(
            Enumerable.Range(1, Math.Min(take, 40)).Select(i => new BdDirectorAccount(i, "Synthetic " + new string('a', 250), "Client", "client" + i, i, true, null, 2, Now, 1)).ToArray());
        public Task<BdTargetActuals> GetTargetActualsAsync(IReadOnlyList<BdTargetKey> keys, CancellationToken ct) => Task.FromResult(new BdTargetActuals(keys.ToDictionary(k => k, _ => 101m), 101, 0));
        public Task<IReadOnlyList<BdDirectorSignal>> GetRecentSignalsAsync(int days, int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<BdDirectorSignal>>(new[] { new BdDirectorSignal(1, "Synthetic firm", "RecentWin", "Synthetic signal", Now, 1) });
        public Task<IReadOnlyList<BdTarget>> ListTargetsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BdTarget>>(new[] { new BdTarget(1, "LivePursuits", "Firm", null, Now, Now.AddDays(90), 120, null, "Synthetic", Now) });
        public Task<IReadOnlyList<BdAsk>> ListAsksAsync(string? status, CancellationToken ct) => Task.FromResult<IReadOnlyList<BdAsk>>(new[] { new BdAsk(1, "engineer", 1, null, "Synthetic ask", null, 30, Now, "Synthetic", Now, "Open", null, null, null) });
        public Task<IReadOnlyList<BdDecision>> ListDecisionsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<BdDecision>>(Array.Empty<BdDecision>());
        public Task<IReadOnlyList<BdForwardPipelineRow>> GetForwardPipelineAsync(int take, CancellationToken ct) => Task.FromResult<IReadOnlyList<BdForwardPipelineRow>>(new[] { new BdForwardPipelineRow(1, "Synthetic project", "Proponent", "BC", "School", 100m, "Planning") });
        public Task<PipelineFunnel> GetPipelineFunnelAsync(CancellationToken ct) => Task.FromResult(new PipelineFunnel(3, 2, 1));
        public Task<IReadOnlyList<BdOpenStructuralSeatRow>> GetOpenStructuralSeatsAsync(int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BdCompetitorWatchRow>> GetCompetitorWatchAsync(int take, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<DataHealthRow>> GetDataHealthAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<long> SetTargetAsync(BdTarget target, CancellationToken ct) => throw new NotSupportedException();
        public Task RetireTargetAsync(long id, string by, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyList<BdAsk>> ListOpenAsksForAsync(string toStaffKey, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> CreateAskAsync(BdAsk ask, CancellationToken ct) => throw new NotSupportedException();
        public Task CloseAskAsync(long id, string status, string? note, string by, CancellationToken ct) => throw new NotSupportedException();
        public Task<long> RecordDecisionAsync(BdDecision decision, CancellationToken ct) => throw new NotSupportedException();
        public Task ScoreDecisionAsync(long id, string outcome, string? note, string by, CancellationToken ct) => throw new NotSupportedException();
    }
    private static string Source(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException(), relative));
    }
}
