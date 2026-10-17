#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// The BD consigliere (migration 362): questions opened in Claude Code from the Director, and doctrine cards that only reach
/// later prompts once Ian accepts them — the NetworkOps Prompt Library method.
/// WHAT IT COVERS: where a card applies (the closed vocabulary, refused typos, matching); a report's validation (every fault
/// reported, nothing accepted with one); what a question and a synthesis prompt carry (the rules, the facts, the cards that
/// apply in full and the rest by title, the report command with its run and token); that the app stores only hashes, the
/// prompt hash of the text Claude reads; deciding cards; 362 guarded; the window's screens; no API marker in the consigliere.
/// WHAT IT DOES NOT COVER: Claude's answers (a session's quality is Ian's to judge, card by card); the SQL against a live
/// database (the store's statements are read, not run); Windows Terminal actually opening.
/// A same-class fault it would NOT catch: a card whose citations name a passage that does not exist.
/// </summary>
public sealed class BdDoctrineRegressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    private static DoctrineCardRow Card(long id, string title, string appliesTo, string status = "accepted", long? amends = null) =>
        new(id, "BD", title, "Plain " + title, appliesTo, "Principle " + id, "Recognise " + id, "Play " + id, null, "Maister, ch. " + id, null,
            1, amends, status, Now, null, null);

    [Fact]
    public void AppliesTo_RefusesTypos_AndMatchesOnlyItsContext()
    {
        Assert.Null(BdDoctrine.Invalid("situation:going-cold, moment:pursuit"));
        Assert.Contains("situation 'gone-cold'", BdDoctrine.Invalid("situation:gone-cold"), StringComparison.Ordinal);
        Assert.Contains("not one of", BdDoctrine.Invalid("device:KOR-217"), StringComparison.Ordinal);
        Assert.Contains("must be a company id", BdDoctrine.Invalid("org:Bird"), StringComparison.Ordinal);
        Assert.NotNull(BdDoctrine.Invalid(""));
        var cold = new DoctrineContext("pursuit", new[] { "going-cold", "relationship-pursuit" }, "Pending", "Alberta", "ishabana@korstructural.com", 141, "health care");
        Assert.True(BdDoctrine.AppliesTo("situation:Going-Cold", cold));
        Assert.True(BdDoctrine.AppliesTo("org:141", cold));
        Assert.True(BdDoctrine.AppliesTo("sector:health", cold));
        Assert.True(BdDoctrine.AppliesTo("any", cold));
        Assert.False(BdDoctrine.AppliesTo("situation:late-action, moment:hour, region:USA", cold));
    }

    [Fact]
    public void Report_ReportsEveryFault_AndAcceptsAGoodOne()
    {
        const string good = """
        { "outcome": "answered", "answer": "Call Bryson first.", // comments allowed
          "cards": [ { "plain": "p", "title": "t", "appliesTo": "situation:going-cold", "principle": "x", "recognise": "y",
                       "play": "z", "citations": "Maister ch. 3", "amends": 4 } ] }
        """;
        var (report, errors) = BdDoctrine.ParseReport(good);
        Assert.Empty(errors);
        Assert.Equal(4L, Assert.Single(report!.Cards).Amends);
        var (bad, why) = BdDoctrine.ParseReport("""{ "outcome": "solved", "cards": [ { "title": "t", "appliesTo": "situation:nope" } ] }""");
        Assert.Null(bad);
        foreach (var expected in new[] { "outcome must be one of", "answer is required", "card 1: plain is required", "situation 'nope'", "card 1: principle is required", "card 1: citations is required" })
            Assert.True(why.Any(e => e.Contains(expected, StringComparison.Ordinal)), $"EXPECTED '{expected}'; ACTUAL: {string.Join(" | ", why)}");
        Assert.StartsWith("Not valid JSON", BdDoctrine.ParseReport("{ nope").Errors.Single(), StringComparison.Ordinal);
    }

    [Fact]
    public void AskPrompt_CarriesRulesFactsApplyingCardsLibraryAndTheReportCommand()
    {
        var context = new DoctrineContext("pursuit", new[] { "going-cold" });
        var prompt = BdPromptComposer.Ask(new BdAskInput("How do I restart Northland?", "ian", "Northland Revelstoke", context,
            new[] { "Held by John Bryson.", "Last touch 3 Sep." },
            new[] { Card(5, "Rescue a quiet relationship", "situation:going-cold") }, new[] { Card(6, "Price on value", "situation:fee-undercut") }, Now),
            new BdPromptHandle(42, "TOKEN-abcdefghijklmnopqrstu"));
        foreach (var expected in new[]
        {
            "# How do I restart Northland?", "Never call the Anthropic API", "addressed to Ian", "- Held by John Bryson.", "- Situations: going-cold",
            "### #5 Rescue a quiet relationship", "- The play: Play 5", "- #6 Price on value", "doctrine show <id>",
            "prompt report 42 <report.json> --token TOKEN-abcdefghijklmnopqrstu", "situation:going-cold|never-touched",
        })
            Assert.Contains(expected, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("### #6", prompt, StringComparison.Ordinal);
    }

    [Fact]
    public void SynthesisPrompt_ListsTheBooks_BuildsOnAcceptedCards_AndNamesWhatIsWaiting()
    {
        var prompt = BdPromptComposer.Synthesis(new BdSynthesisInput("BD", "ian", new[] { "#1 Managing the professional service firm (Maister): 639 claims" },
            new[] { Card(5, "Rescue a quiet relationship", "any") }, new[] { Card(9, "Waiting card", "any", "proposed") }, "pricing", Now,
            Array.Empty<DoctrineCardRecord>(), Array.Empty<ClosedPursuit>()),
            new BdPromptHandle(7, "TOKEN-abcdefghijklmnopqrstu"));
        foreach (var expected in new[] { "# Synthesize the BD doctrine from the books", "This run's focus: pricing", "- #1 Managing the professional service firm",
                     "claims search \"<theme>\" --take 30", "### #5 Rescue a quiet relationship", "- #9 Waiting card", "`amends`", "prompt report 7" })
            Assert.Contains(expected, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Asking_StoresOnlyHashes_ThePromptHashIsOfWhatClaudeReads_AndOnlyAcceptedCardsTravel()
    {
        var store = new Mock<IBdDoctrineStore>();
        store.Setup(s => s.ListCardsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRow>());
        store.Setup(s => s.ListRecordsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRecord>());
        store.Setup(s => s.ListCardsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            Card(5, "Rescue a quiet relationship", "situation:going-cold"), Card(6, "Price on value", "situation:fee-undercut"),
            Card(7, "Not yet accepted", "any", "proposed"),
        });
        store.Setup(s => s.ListRunsAsync("BD", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdPromptRunRow>());
        byte[]? tokenSha = null, promptSha = null;
        store.Setup(s => s.StartRunAsync("ask", "BD", "Northland", "How?", "ian", It.IsAny<byte[]>(), It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, string?, string, byte[], byte[], CancellationToken>((_, _, _, _, _, _, t, _) => tokenSha = t).ReturnsAsync(42);
        store.Setup(s => s.SetPromptShaAsync(42, It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .Callback<long, byte[], CancellationToken>((_, p, _) => promptSha = p).Returns(Task.CompletedTask);
        var vm = new BdDoctrineViewModel(store.Object, () => Now);
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(new long[] { 7 }, vm.Proposed.Select(c => c.Id));
        Assert.Equal(2, vm.Accepted.Count);

        var prompt = await vm.PrepareAskAsync("How?", "Northland", new DoctrineContext("pursuit", new[] { "going-cold" }), new[] { "fact" }, "ian", CancellationToken.None);
        Assert.Equal("bd-ask-42.md", prompt.FileName);
        Assert.Equal(BdDoctrine.Sha(prompt.Markdown), promptSha);
        var token = Regex.Match(prompt.Markdown, @"--token (\S+)").Groups[1].Value;
        Assert.True(token.Length >= 20);
        Assert.Equal(BdDoctrine.Sha(token), tokenSha);
        Assert.Contains("### #5", prompt.Markdown, StringComparison.Ordinal);
        Assert.DoesNotContain("Not yet accepted", prompt.Markdown, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => vm.PrepareAskAsync(" ", "x", new DoctrineContext("hour", Array.Empty<string>()), Array.Empty<string>(), "ian", CancellationToken.None));
    }

    [Fact]
    public async Task Deciding_GoesThroughTheStore_AndSaysWhenItWasAlreadyDecided()
    {
        var store = new Mock<IBdDoctrineStore>();
        store.Setup(s => s.ListCardsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRow>());
        store.Setup(s => s.ListRecordsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRecord>());
        store.Setup(s => s.ListCardsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRow>());
        store.Setup(s => s.ListRunsAsync("BD", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdPromptRunRow>());
        store.Setup(s => s.DecideCardAsync(8, true, "ian", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.DecideCardAsync(9, false, "ian", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var vm = new BdDoctrineViewModel(store.Object, () => Now);
        Assert.True(await vm.DecideAsync(Card(8, "Amend", "any", "proposed", amends: 3), true, "ian", CancellationToken.None));
        Assert.Equal("Accepted: Amend (card #3 retired)", vm.Status);
        Assert.False(await vm.DecideAsync(Card(9, "Old", "any", "proposed"), false, "ian", CancellationToken.None));
        Assert.Contains("already decided", vm.Status, StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyOneSynthesisAtATime_ButAnAbandonedOneDoesNotBlockForever()
    {
        BdPromptRunRow Run(long id, string kind, string? outcome, double hoursAgo) =>
            new(id, kind, "BD", "s", null, "ian", Now.AddHours(-hoursAgo), outcome is null ? null : Now, outcome, null, 0);
        Assert.Equal(7, BdDoctrineViewModel.OpenSynthesis(new[] { Run(7, "synthesis", null, 1), Run(6, "synthesis", "answered", 3) }, Now)!.Id);
        Assert.Null(BdDoctrineViewModel.OpenSynthesis(new[] { Run(8, "ask", null, 1), Run(6, "synthesis", "answered", 3) }, Now));
        Assert.Null(BdDoctrineViewModel.OpenSynthesis(new[] { Run(5, "synthesis", null, 72) }, Now));
    }

    [Fact]
    public async Task AcceptAll_AcceptsOnlyWaitingCards_OldestFirst_SoAnAmendmentFollowsWhatItAmends()
    {
        var store = new Mock<IBdDoctrineStore>();
        store.Setup(s => s.ListCardsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRow>());
        store.Setup(s => s.ListRecordsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRecord>());
        store.SetupSequence(s => s.ListCardsAsync("BD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Card(12, "Amends 5", "any", "proposed", amends: 5), Card(5, "First", "any", "proposed"), Card(3, "Kept", "any") })
            .ReturnsAsync(new[] { Card(3, "Kept", "any"), Card(5, "First", "any"), Card(12, "Amends 5", "any") });
        store.Setup(s => s.ListRunsAsync("BD", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdPromptRunRow>());
        var order = new List<long>();
        store.Setup(s => s.DecideCardAsync(It.IsAny<long>(), true, "ian", It.IsAny<CancellationToken>()))
            .Callback<long, bool, string, CancellationToken>((id, _, _, _) => order.Add(id)).ReturnsAsync(true);
        var vm = new BdDoctrineViewModel(store.Object, () => Now);
        await vm.LoadAsync(CancellationToken.None);
        Assert.True(vm.HasProposed);

        Assert.Equal(2, await vm.AcceptAllAsync("ian", CancellationToken.None));

        Assert.Equal(new long[] { 5, 12 }, order);
        Assert.False(vm.HasProposed);
        Assert.Equal("Accepted 2 card(s).", vm.Status);
    }

    [Fact]
    public void Store_ChecksTheTokenHash_ReplacesOnlyProposedCards_AndRetiresOnlyAnAcceptedAmendedCard()
    {
        var sql = Source("Kor.Opportunities.Data/Crm/BdDoctrine.cs");
        Assert.Contains("WHERE Id = @id AND TokenSha256 = @token", sql, StringComparison.Ordinal);
        Assert.Contains("DELETE FROM opportunities.DoctrineCard WHERE SourceRunId = @id AND Status = N'proposed'", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE Id = @id AND Status = N'proposed'", sql, StringComparison.Ordinal);
        Assert.Contains("WHERE Id = @amends AND Status = N'accepted'", sql, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(sql, @"DELETE FROM"));
    }

    [Fact]
    public void Migration362_CreatesGuarded_ChangesNoData()
    {
        var sql = Source("Kor.Opportunities.Data/Schema/362_BdDoctrineAndPromptRuns.sql");
        foreach (var table in new[] { "BdPromptRun", "DoctrineCard" })
            Assert.Contains($"IF OBJECT_ID(N'opportunities.{table}', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (Status IN (N'proposed', N'accepted', N'rejected', N'retired'))", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\b(DELETE|DROP|TRUNCATE|UPDATE|INSERT)\b", Regex.Replace(sql, @"--[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline));
    }

    [Fact]
    public void Window_HasTheDoctrineScreen_AndAQuestionBoxOnEachOfTheThreeScreens_AndTheConsigliereCallsNoApi()
    {
        var xaml = Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml");
        foreach (var expected in new[] { "x:Name=\"DoctrineTab\"", "x:Name=\"AskTodayBox\"", "x:Name=\"AskPersonBox\"", "x:Name=\"AskPursuitBox\"",
                     "Click=\"AskClaudeToday_Click\"", "Click=\"AskClaudePerson_Click\"", "Click=\"AskClaudePursuit_Click\"", "Click=\"Synthesize_Click\"",
                     "Click=\"AcceptCard_Click\"", "Click=\"RejectCard_Click\"" })
            Assert.Contains(expected, xaml, StringComparison.Ordinal);
        foreach (var file in new[] { "Kor.Operations.App/BusinessDevelopment/Director/BdDoctrineViewModel.cs", "Kor.Opportunities.Data/Crm/BdDoctrine.cs",
                     "Kor.Opportunities.Data/Crm/BdPromptComposer.cs", "Kor.Opportunities.Data/Crm/DoctrineTrackRecord.cs",
                     "Kor.Operations.App/Crm/PursuitCloseReason.cs" })
            Assert.DoesNotMatch(@"api\.anthropic\.com|AskService|HttpClient|x-api-key", Source(file));
        Assert.Contains("PromptLauncher.OpenInClaude(", Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml.cs"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The CLI refuses any option not on its verb's allowed list; the first end-to-end report (16 Oct) failed with "--token does not
    /// apply to prompt" while every unit test passed. A pin on the source, narrow by design: it would not catch a new option added
    /// to a verb's usage line but left off that verb's list.
    /// </summary>
    [Fact]
    public void Cli_AllowsTheOptionsThePromptsTellClaudeToUse()
    {
        var cli = Source("tools/BdKnowledgeIngest/Program.cs");
        Assert.Contains("\"prompt\" => promptAction == \"synthesis\" ? new[] { \"--by\", \"--focus\", \"--role\" } : new[] { \"--token\", \"--checked\" },", cli, StringComparison.Ordinal);
        Assert.Contains("\"--token\", \"--role\", \"--focus\", \"--exclude-run\", \"--after\" }", cli, StringComparison.Ordinal);
        Assert.Contains("\"--replace\", \"--checked\" }", cli, StringComparison.Ordinal);
        Assert.Contains("\"doctrine\" => new[] { \"--role\", \"--exclude-run\" },", cli, StringComparison.Ordinal);
        Assert.Contains("or \"doctrine\" or \"prompt\"))", cli, StringComparison.Ordinal);
    }

    [Fact]
    public void HourImport_RefusesNonAcceptedOrWrongRoleCards_WithMoveRankAndId()
    {
        const string json = """
        { "forDate":"2026-10-16", "kind":"BD", "title":"Hour", "plan":[],
          "moves":[{"rank":1,"title":"Call","minutes":15,"whyNow":["Due"],"cards":[5,6,7,8,9,999]}] }
        """;
        var content = DirectorHourFile.Parse(json).Content!;
        var errors = DirectorHourFile.ValidateCards(content, new[] { Card(5, "Yes", "any"), Card(6, "Waiting", "any", "proposed"),
            Card(7, "No", "any", "rejected"), Card(8, "Retired", "any", "retired"), Card(9, "Other role", "any") with { Role = "Operations" } });
        Assert.Equal(5, errors.Count);
        foreach (var id in new[] { 6, 7, 8, 9, 999 }) Assert.Contains($"move 1: card {id} is not an accepted BD card.", errors);
        Assert.Empty(DirectorHourFile.ValidateCards(content with { Moves = content.Moves.Select(m => m with { Cards = new long[] { 5 } }).ToArray() },
            new[] { Card(5, "Yes", "any") }));
        // The CLI is not referenced by this test assembly. Pin that it executes this gate before writing the hour.
        var cli = Source("tools/BdKnowledgeIngest/Program.cs");
        var gate = cli.IndexOf("DirectorHourFile.ValidateCards(content, cards)", StringComparison.Ordinal);
        var write = cli.IndexOf("rhythm.ImportHourAsync", StringComparison.Ordinal);
        Assert.True(gate >= 0 && write > gate);
        Assert.Contains("return 1;", cli[gate..write], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(CrmEngagementStage.Won)]
    [InlineData(CrmEngagementStage.Lost)]
    [InlineData(CrmEngagementStage.Archived)]
    public void Closing_RequiresOneLine_AndCancelLeavesThePursuitOpen(CrmEngagementStage stage)
    {
        var current = new CrmEngagement { Id = 12, Stage = CrmEngagementStage.Submitted, OutcomeReason = "stale reason" };
        var calls = 0;
        Assert.Null(PursuitCloseReason.Prepare(current, stage, _ => { calls++; return null; }));
        Assert.Equal(1, calls);
        Assert.Equal(CrmEngagementStage.Submitted, current.Stage);
        Assert.Throws<ArgumentException>(() => PursuitCloseReason.Prepare(current, stage, _ => "  "));
        Assert.Throws<ArgumentException>(() => PursuitCloseReason.Prepare(current, stage, _ => "first\nsecond"));
        var saved = PursuitCloseReason.Prepare(current, stage, _ => "  Buyer changed the scope.  ")!;
        Assert.Equal(stage, saved.Stage);
        Assert.Equal("Buyer changed the scope.", saved.OutcomeReason);
        var reopened = PursuitCloseReason.Prepare(saved, CrmEngagementStage.Drafting, _ => throw new InvalidOperationException("Must not prompt"));
        Assert.Null(reopened!.OutcomeReason);
        Assert.Equal("stale reason", current.OutcomeReason);
    }

    [Fact]
    public void CloseDoors_UseTheSharedGate_BeforeSaving_AndArchivedKeepsReasonAndDate()
    {
        var menu = Source("Kor.Operations.App/Crm/CrmView.xaml.cs");
        var editor = Source("Kor.Operations.App/Crm/CrmEngagementDialog.xaml.cs");
        Assert.Contains("PursuitCloseReason.Prompt(Window.GetWindow(this), _vm.Selected.Engagement, newStage)", menu, StringComparison.Ordinal);
        Assert.Contains("CancellationToken.None, prepared.OutcomeReason)", menu, StringComparison.Ordinal);
        Assert.Contains("PursuitCloseReason.Prompt(this, _original, stage)", editor, StringComparison.Ordinal);
        Assert.Contains("Result = prepared with", editor, StringComparison.Ordinal);
        foreach (var source in new[] { menu, editor }) Assert.Contains("if (prepared is null) return;", source, StringComparison.Ordinal);
        var helper = Source("Kor.Operations.App/Crm/PursuitCloseReason.cs");
        Assert.Contains("required: true, singleLine: true", helper, StringComparison.Ordinal);
        var sql = Source("Kor.Opportunities.Data/Crm/SqlCrmEngagementStore.cs");
        Assert.Contains("OutcomeReason         = CASE WHEN @stage IN (6, 7, 8) THEN @outcomeReason ELSE NULL END", sql, StringComparison.Ordinal);
        Assert.Contains("ClosedAtUtc           = CASE WHEN @stage IN (6, 7, 8)", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordScreen_FlagsOnlyAcceptedCards_RetiresAsIan_AndKeepsRetiredCitationsNavigable()
    {
        var accepted = Card(5, "Quiet relationship", "any");
        var record = new DoctrineCardRecord(5, 3, 0, 0, 3, 0, 0, 0, 0, new DateOnly(2026, 10, 16), new[] { "Skipped" });
        var store = new Mock<IBdDoctrineStore>();
        store.Setup(s => s.ListCardsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRow>());
        store.SetupSequence(s => s.ListCardsAsync("BD", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { accepted, Card(6, "Rejected", "any", "rejected") })
            .ReturnsAsync(new[] { accepted with { Status = "retired" }, Card(6, "Rejected", "any", "rejected") });
        store.Setup(s => s.ListRecordsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { record, record with { CardId = 6 } });
        store.Setup(s => s.ListRunsAsync("BD", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdPromptRunRow>());
        store.Setup(s => s.RetireCardAsync(5, "Ian", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var vm = new BdDoctrineViewModel(store.Object, () => Now);
        await vm.LoadAsync(CancellationToken.None);
        var flagged = Assert.Single(vm.NeedsLook);
        Assert.Equal("Skipped the last 3 times it came up", flagged.FlagText);
        Assert.Contains("card #5", BdDoctrineViewModel.AmendmentQuestion(flagged), StringComparison.Ordinal);
        Assert.Contains(record.Summary, BdDoctrineViewModel.AmendmentFacts(flagged));
        Assert.True(await vm.RetireAsync(flagged, CancellationToken.None));
        Assert.Empty(vm.NeedsLook);
        Assert.Empty(vm.Accepted);
        vm.FocusCard(5);
        Assert.Equal("retired", vm.FocusedCard!.Card.Status);
        Assert.Equal(3, vm.FocusedCard.Record.Suggested);
        var move = new HourMove(1, "Call", 15, new[] { "Due" }, null, Array.Empty<HourDraft>(), Array.Empty<string>(), Array.Empty<HourLink>())
            { Cards = new long[] { 5, 6, 7 } };
        var view = new HourMoveView(move, null);
        view.SetCards(vm.Cards.Append(Card(7, "Proposed", "any", "proposed")).ToArray());
        Assert.True(Assert.Single(view.Cards).IsRetired);
        store.Verify(s => s.RetireCardAsync(5, "Ian", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Synthesis_ReadsFreshRecordsAndNinetyDaysOfReasons_AndRequiresTwoOutcomesForExperience()
    {
        var records = new[] { DoctrineCardRecord.Empty(8), new DoctrineCardRecord(5, 4, 4, 0, 0, 0, 0, 0, 0,
            new DateOnly(2026, 10, 16), new[] { "NoEffect" }) };
        var closed = new[] { new ClosedPursuit(101, "Example One", 7, "Scope changed", Now.AddDays(-1)),
            new ClosedPursuit(102, "Example Two", 8, "Scope changed", Now.AddDays(-90)),
            new ClosedPursuit(103, "Too old", 6, "Do not include", Now.AddDays(-91)),
            new ClosedPursuit(104, "Future", 6, "Do not include", Now.AddDays(1)) };
        var store = new Mock<IBdDoctrineStore>();
        store.Setup(s => s.ListCardsAsync("Operations", It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<DoctrineCardRow>());
        store.Setup(s => s.ListCardsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Card(5, "Quiet relationship", "any") });
        store.Setup(s => s.ListRunsAsync("BD", It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdPromptRunRow>());
        store.Setup(s => s.ListRecordsAsync("BD", It.IsAny<CancellationToken>())).ReturnsAsync(records);
        store.Setup(s => s.ListClosedPursuitsAsync(Now.AddDays(-90), Now, It.IsAny<CancellationToken>())).ReturnsAsync(closed);
        var vm = new BdDoctrineViewModel(store.Object, () => Now);
        var prompt = (await vm.PrepareSynthesisAsync(new[] { "Book" }, null, "Ian", CancellationToken.None)).Markdown;
        foreach (var expected in new[] { "What KOR's own record shows", "Cards needing a look", "| #5 | 4 | 4 |", "Pursuit #101", "Pursuit #102",
            "Scope changed", "Propose amendments or retirements", "only when two or more outcomes share a reason", "citing those pursuits by id", DoctrineTrackRecord.Rule })
            Assert.Contains(expected, prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Pursuit #103", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Pursuit #104", prompt, StringComparison.Ordinal);
        Assert.True(prompt.IndexOf("Card #5:", StringComparison.Ordinal) < prompt.IndexOf("| Card |", StringComparison.Ordinal));
        Assert.True(prompt.IndexOf("| #5 |", StringComparison.Ordinal) < prompt.IndexOf("| #8 |", StringComparison.Ordinal));
        store.Verify(s => s.ListClosedPursuitsAsync(Now.AddDays(-90), Now, It.IsAny<CancellationToken>()), Times.Once);
        var cli = Source("tools/BdKnowledgeIngest/Program.cs");
        Assert.Contains("store.ListRecordsAsync(role, ct)", cli, StringComparison.Ordinal);
        Assert.Contains("var role = args.Value(\"--role\") ?? \"BD\";", cli, StringComparison.Ordinal);
        Assert.Contains("store.ListClosedPursuitsAsync(now.AddDays(-90), now, ct)", cli, StringComparison.Ordinal);
    }

    [Fact]
    public void Retirement_IsGuardedByAcceptedStatus_AndReturnsTheUpdateCount()
    {
        var sql = Source("Kor.Opportunities.Data/Crm/BdDoctrine.cs");
        var start = sql.IndexOf("public async Task<bool> RetireCardAsync", StringComparison.Ordinal);
        var end = sql.IndexOf("public async Task<IReadOnlyList<DoctrineCardRecord>> ListRecordsAsync", start, StringComparison.Ordinal);
        var retirement = sql[start..end];
        Assert.Contains("WHERE Id = @id AND Status = N'accepted';", retirement, StringComparison.Ordinal);
        Assert.Contains("SELECT @@ROWCOUNT;", retirement, StringComparison.Ordinal);
        Assert.Contains("DecidedBy = @by", retirement, StringComparison.Ordinal);
    }

    private static string Source(string path) => File.ReadAllText(Path.Combine(RepoRoot(), path));

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("EXPECTED repository root; ACTUAL not found");
    }
}
