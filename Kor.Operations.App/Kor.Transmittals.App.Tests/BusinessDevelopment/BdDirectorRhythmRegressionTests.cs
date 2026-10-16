#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.MajorProjects;
using Moq;
using Links = Kor.Opportunities.Data.BdReports.Generators.KorReportLinks;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// BD Director version 2: the prepared hour, the action register, meetings and references (migrations 359 and 360).
/// WHAT IT COVERS: the hour file's validation reports every fault and accepts a good file; the email carries every move,
/// encodes what it is given and links into the app; agendas and Monday lists are built from the register as specified;
/// the view model writes only through its store and only for the selected row; the store writes only the four rhythm
/// tables; 359 refuses a second run and 360 creates guarded.
/// WHAT IT DOES NOT COVER: the SQL against a live database (the store's statements are read, not run); the Worker's
/// 6 a.m. send; anything the person sees (BdScreensRenderTests renders the four tabs).
/// A same-class fault it would NOT catch: an hour whose links name a real kind and a positive id that does not exist.
/// </summary>
public sealed class BdDirectorRhythmRegressionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 15, 0, 0, TimeSpan.Zero);

    private const string GoodHour = """
    {
      "forDate": "2026-10-16", "kind": "BD", "title": "Your BD hour",
      "plan": [ { "status": "off", "label": "Owed", "text": "The recap." } ],
      "moves": [
        { "rank": 1, "title": "Send <the> recap", "minutes": 15, "whyNow": ["Promised."],
          "drafts": [ { "to": "Ian", "subject": "Recap", "body": "Body & more" } ], "steps": [],
          "links": [ { "label": "Actions", "kind": "director", "id": 2 }, { "label": "Org", "kind": "org", "id": 141 } ] },
        { "rank": 2, "title": "Call the client", "minutes": 20, "whyNow": ["Quiet months."], "drafts": [], "steps": [], "links": [] }
      ],
      "decisions": [], "meetings": []
    }
    """;

    [Fact]
    public void HourFile_AcceptsAGoodFile_AndTotalsItsMinutes()
    {
        var (content, errors) = DirectorHourFile.Parse(GoodHour);
        Assert.Empty(errors);
        Assert.NotNull(content);
        Assert.Equal(35, DirectorHourFile.TotalMinutes(content!));
        Assert.Equal(Kor.Opportunities.Data.BdReports.Generators.KorReportLinks.Director(2), content!.Moves[0].Links[0].Url);
        Assert.Equal(Kor.Opportunities.Data.BdReports.Generators.KorReportLinks.Org(141), content.Moves[0].Links[1].Url);
    }

    [Fact]
    public void HourFile_ReportsEveryFault_AndAcceptsNothing()
    {
        var bad = GoodHour
            .Replace("\"kind\": \"BD\"", "\"kind\": \"Sales\"", StringComparison.Ordinal)
            .Replace("\"status\": \"off\"", "\"status\": \"red\"", StringComparison.Ordinal)
            .Replace("\"rank\": 2", "\"rank\": 3", StringComparison.Ordinal)
            .Replace("\"minutes\": 20", "\"minutes\": 3", StringComparison.Ordinal)
            .Replace("\"kind\": \"director\", \"id\": 2", "\"kind\": \"director\", \"id\": 9", StringComparison.Ordinal)
            .Replace("\"kind\": \"org\"", "\"kind\": \"website\"", StringComparison.Ordinal);
        var (content, errors) = DirectorHourFile.Parse(bad);
        Assert.Null(content);
        foreach (var expected in new[] { "kind must be BD or Operations", "plan status 'red'", "ranks must run", "minutes must be 5 to 90", "has no valid id", "link kind 'website'" })
            Assert.True(errors.Any(e => e.Contains(expected, StringComparison.Ordinal)), $"EXPECTED an error containing '{expected}'; ACTUAL: {string.Join(" | ", errors)}");
        Assert.Equal("Not valid JSON", DirectorHourFile.Parse("{ not json").Errors.Single()[..14]);
    }

    [Fact]
    public void HourEmail_CarriesEveryMove_EncodesText_AndLinksIntoTheApp()
    {
        var content = DirectorHourFile.Parse(GoodHour).Content!;
        var html = DirectorHourEmail.Render(content);
        Assert.Contains("Send &lt;the&gt; recap", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<the>", html, StringComparison.Ordinal);
        Assert.Contains("Call the client", html, StringComparison.Ordinal);
        Assert.Contains("Body &amp; more", html, StringComparison.Ordinal);
        Assert.Contains(Kor.Opportunities.Data.BdReports.Generators.KorReportLinks.Director(1), html, StringComparison.Ordinal);
        Assert.Equal("Your BD hour · about 35 minutes", DirectorHourEmail.Subject(content));
    }

    private static BdActionRow Action(long id, string owner, string text, int dueDays, string status = "Open", int closedDaysAgo = 1) =>
        new(id, owner + "@korstructural.com", owner, text, "why", 15, Now.AddDays(dueDays), status,
            status == "Open" ? null : Now.AddDays(-closedDaysAgo), null, status == "Open" ? null : "it went", null, null, null, null, null, null, null);

    [Fact]
    public void OneToOne_ListsOverdueSoonAndDone_CountsLater_AndNeverSaysNothingForTheDiscussion()
    {
        var doc = BdAgenda.ForOneToOne("Omar", new[]
        {
            Action(1, "Omar", "Overdue thing.", -2), Action(2, "Omar", "Soon thing", 5), Action(3, "Omar", "Later thing", 30),
            Action(4, "Omar", "Closed thing", -9, "Done", 3), Action(5, "Omar", "Closed long ago", -40, "Done", 40),
        }, Now, Now.AddDays(-14));
        Assert.Equal(20, doc.Minutes);
        Assert.Equal(20, doc.Sections.Sum(s => s.Minutes));
        Assert.Equal(new[] { "Overdue thing (due 14 Oct)" }, doc.Sections[0].Lines);
        Assert.Equal(new[] { "Soon thing (due 21 Oct)", "Also open, due later: 1" }, doc.Sections[1].Lines);
        Assert.Equal(new[] { "Closed thing (Done: it went)" }, doc.Sections[2].Lines);
        var text = doc.ToText();
        Assert.DoesNotContain("Nothing.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AnEmptySectionIsLeftOutOfTheInvite_NotPrintedAsNothing()
    {
        var doc = new BdAgendaDoc("1:1", 20, new[]
        {
            new BdAgendaSection("Overdue", 5, Array.Empty<string>()),
            new BdAgendaSection("Coming up", 5, new[] { "Soon thing" }),
        });
        var text = doc.ToText();
        Assert.DoesNotContain("Overdue", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Nothing", text, StringComparison.Ordinal);
        Assert.Contains("Coming up (5 min)", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2026-10-16", "2026-10-19")] // Friday -> Monday
    [InlineData("2026-10-17", "2026-10-19")] // Saturday -> Monday
    [InlineData("2026-10-18", "2026-10-19")] // Sunday -> Monday
    [InlineData("2026-10-19", "2026-10-20")] // Monday -> Tuesday
    public void DueNextIsTheNextWorkingDay(string today, string expected)
    {
        Assert.Equal(DateTime.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            BdRhythmViewModel.NextWorkingDay(DateTime.Parse(today, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void AllHands_Is27Minutes_LateGroupedByOwner_DoneSinceLastMeeting()
    {
        var doc = BdAgenda.ForAllHands(new[]
        {
            Action(1, "Omar", "A", -2), Action(2, "Omar", "B", -1), Action(3, "Jim", "C", -3), Action(4, "Jim", "D", 4),
            Action(5, "Rory", "E", -9, "Done", 2), Action(6, "Rory", "F", -9, "Declined", 2),
        }, Now, Now.AddDays(-7));
        Assert.Equal(27, doc.Minutes);
        Assert.Equal(27, doc.Sections.Sum(s => s.Minutes));
        Assert.Equal(new[] { "Rory: E" }, doc.Sections[0].Lines);
        Assert.Equal(new[] { "Omar: 2 overdue: A; B", "Jim: 1 overdue: C" }, doc.Sections[1].Lines);
    }

    [Fact]
    public void MondayLists_AreDraftsToIan_OpenOnly_OverdueMarked_AndEncoded()
    {
        var html = BdLeadLists.RenderDraftsEmail(new[]
        {
            Action(1, "Omar", "Call <Bob>", -2), Action(2, "Omar", "Later", 3), Action(3, "Jim", "Closed", -2, "Done"),
        }, Now);
        Assert.Contains("Nothing has been sent to anyone", html, StringComparison.Ordinal);
        Assert.Contains("Overdue since Wed 14 Oct", html, StringComparison.Ordinal);
        Assert.Contains("Due Mon 19 Oct", html, StringComparison.Ordinal);
        Assert.Contains("Call &lt;Bob&gt;", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Closed", html, StringComparison.Ordinal);
        Assert.DoesNotContain(">Jim<", html, StringComparison.Ordinal);
        Assert.Single(BdLeadLists.Group(new[] { Action(1, "Omar", "x", 1), Action(2, "Omar", "y", 2) }));
    }

    private static (Mock<IBdRhythmStore> Store, BdRhythmViewModel Vm) Rhythm()
    {
        var store = new Mock<IBdRhythmStore>(MockBehavior.Strict);
        store.Setup(s => s.GetHourAsync(It.IsAny<string>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync((DirectorHourRow?)null);
        store.Setup(s => s.ListActionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[] { Action(1, "Omar", "Open one", -2), Action(2, "Jim", "Closed one", -2, "Done") });
        store.Setup(s => s.ListMeetingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdMeetingRow>());
        store.Setup(s => s.ListReferencesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdReferenceRow(7, "Fraser Health", "School", "Architect", "Firm", null, null, null, null, null, null, null, "NotCalled", null),
        });
        store.Setup(s => s.ListLeadsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdLeadRow("Ian@korstructural.com", "Ian", "Manager", null, 10),
            new BdLeadRow("Omar@korstructural.com", "Omar", "Lead", "Vancouver/LowerMainland", 20),
            new BdLeadRow("Jim@korstructural.com", "Jim", "Lead", "USA", 60),
        });
        return (store, new BdRhythmViewModel(store.Object, () => Now));
    }

    [Fact]
    public async Task Today_RecordsAMoveOnce_FoldsIt_AndSaysWhenTheHourIsDone()
    {
        var (store, vm) = Rhythm();
        store.Setup(s => s.GetHourAsync("BD", DateOnly.FromDateTime(Now.Date), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DirectorHourRow(9, DateOnly.FromDateTime(Now.Date), "BD", GoodHour, "claude", "ian", Now, null));
        store.Setup(s => s.ListMoveResultsAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { new HourMoveResult(1, "Done", null, Now) });
        store.Setup(s => s.RecordMoveResultAsync(9, 2, "NotDoing", null, "ian", It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        await vm.LoadAsync(CancellationToken.None);

        Assert.Equal(new[] { "Done", "20 min" }, vm.Moves.Select(m => m.SideText));
        Assert.Equal("1 of 2 moves done · about 20 minutes left", vm.MovesProgressText);
        Assert.False(vm.IsHourFinished);
        await vm.RecordMoveAsync(vm.Moves[1], "NotDoing", "ian", CancellationToken.None);
        Assert.True(vm.Moves[1].IsResolved);
        Assert.Equal("Not doing", vm.Moves[1].SideText);
        Assert.True(vm.IsHourFinished);
        Assert.Equal("2 of 2 moves done · about 0 minutes left", vm.MovesProgressText);
        store.Verify(s => s.RecordMoveResultAsync(It.IsAny<long>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("3 people", new HourDraftView(new HourDraft("a@x; b@x; c@x", "s", "b")).ToText);
        Assert.Equal("a@x; b@x", new HourDraftView(new HourDraft("a@x; b@x", "s", "b")).ToText);
    }

    [Fact]
    public async Task People_OneCardEach_LeadSelectedFirst_YourListIsTheSignedInPerson()
    {
        var (store, vm) = Rhythm();
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(new[] { "Ian", "Omar", "Jim" }, vm.PeopleCards.Select(p => p.Name));
        Assert.Equal("Omar", vm.SelectedPerson?.Name);
        var omar = vm.PeopleCards[1];
        Assert.Equal((1, 1, 0), (omar.Open, omar.Late, omar.ThisWeek));
        Assert.Equal("Next one-to-one: not booked", omar.NextText);
        Assert.Equal("Vancouver and the Lower Mainland", omar.RoleText);
        Assert.StartsWith("One-to-one: Omar (20 minutes)", vm.PersonAgenda, StringComparison.Ordinal);
        Assert.Equal("2 days late", vm.PersonActions[0].DueText);
        Assert.Empty(vm.MyActions); // the manager, Ian, owns nothing here
        vm.SetMe("omar@korstructural.com"); // keys compare without case
        Assert.Equal(new long[] { 1 }, vm.MyActions.Select(a => a.Row.Id));
        vm.SelectedPerson = vm.PeopleCards[0];
        Assert.False(vm.PersonIsLead);
        Assert.Equal("", vm.PersonAgenda);
        Assert.Contains("Your own list", vm.PersonNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Ticking_ClosesThatAction_AndRecountsEveryScreen()
    {
        var (store, vm) = Rhythm();
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(1, vm.TeamLate);
        store.Setup(s => s.CloseActionAsync(1, "Done", "sent", "ian", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.ListActionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Action(1, "Omar", "Open one", -2, "Done", 0), Action(2, "Jim", "Closed one", -2, "Done") });
        Assert.True(await vm.CloseActionAsync(vm.PersonActions[0], "Done", "sent", "ian", CancellationToken.None));
        Assert.Equal(0, vm.TeamLate);
        Assert.Equal(0, vm.PeopleCards.Single(p => p.Name == "Omar").Late);
        Assert.False(await vm.CloseActionAsync(vm.PersonActions[0], "Done", "again", "ian", CancellationToken.None));
    }

    [Fact]
    public void PursuitLine_GoesColdAfter14Days_AndOpensThePursuit()
    {
        PursuitOverwatchRow P(long id, int? touchedDaysAgo, int openedDaysAgo) => new(id, null, "omara@korstructural.com", 1, "Tower", "Buyer", "Vancouver",
            touchedDaysAgo is { } t ? Now.AddDays(-t) : null, Now.AddDays(-openedDaysAgo), 0, null, Now.AddDays(-openedDaysAgo));
        Assert.Equal("", new BdPursuitLine(P(1, 3, 40), "Omar", Now).Gap);
        Assert.Equal("Going cold: no touch in 20 days", new BdPursuitLine(P(2, 20, 40), "Omar", Now).Gap);
        Assert.Equal("Never touched in 30 days", new BdPursuitLine(P(3, null, 30), "Omar", Now).Gap);
        Assert.Equal("", new BdPursuitLine(P(4, null, 5), "Omar", Now).Gap);
        Assert.Equal("Omar · Buyer · Vancouver", new BdPursuitLine(P(1, 3, 40), "Omar", Now).Line);
        Assert.Equal(Links.Pursuit(2), BdDirectorRowLinks.For(new BdPursuitLine(P(2, 20, 40), "Omar", Now)));
    }

    [Fact]
    public void Migration361_CreatesGuarded_SeedsOnlyMissingActiveStaff_AndDeletesNothing()
    {
        var sql = Source("Kor.Opportunities.Data/Schema/361_DirectorPeopleAndMoves.sql");
        foreach (var table in new[] { "BdLead", "DirectorHourMove" })
            Assert.Contains($"IF OBJECT_ID(N'opportunities.{table}', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("AND NOT EXISTS (SELECT 1 FROM opportunities.BdLead l WHERE l.StaffKey = v.StaffKey)", sql, StringComparison.Ordinal);
        Assert.Contains("s.IsActive = 1", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (Result IN (N'Done', N'NotToday', N'NotDoing'))", sql, StringComparison.Ordinal);
        Assert.Equal(BdRhythmValues.MoveResults, new[] { "Done", "NotToday", "NotDoing" });
        Assert.DoesNotMatch(@"(?i)\b(DELETE|DROP|TRUNCATE|UPDATE)\b", Regex.Replace(sql, @"--[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline));
    }

    [Fact]
    public async Task ViewModel_WithNoHourInSevenDays_SaysSo_AndShowsOpenActionsOnly()
    {
        var (store, vm) = Rhythm();
        await vm.LoadAsync(CancellationToken.None);
        Assert.False(vm.HasHour);
        Assert.Equal("No hour has been prepared in the last seven days.", vm.HourMessage);
        store.Verify(s => s.GetHourAsync("BD", It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()), Times.Exactly(7));
        Assert.Equal(new long[] { 1 }, vm.Actions.Select(a => a.Row.Id));
        Assert.True(vm.Actions[0].IsOverdue);
        vm.ActionFilter = "All";
        Assert.Equal(2, vm.Actions.Count);
    }

    [Fact]
    public async Task ViewModel_WritesOnlyTheSelectedRow_AndNothingWithoutASelection()
    {
        var (store, vm) = Rhythm();
        await vm.LoadAsync(CancellationToken.None);
        Assert.False(await vm.CloseSelectedActionAsync("Done", null, "ian", CancellationToken.None));
        await vm.RecordSelectedReferenceCallAsync("Confirmed", "Pat", null, "ian", CancellationToken.None);

        store.Setup(s => s.CloseActionAsync(1, "Done", "sent", "ian", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        store.Setup(s => s.RecordReferenceCallAsync(7, "Confirmed", "Pat", null, "ian", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        vm.SelectedAction = vm.Actions[0];
        Assert.True(await vm.CloseSelectedActionAsync("Done", "sent", "ian", CancellationToken.None));
        vm.SelectedReference = vm.References[0];
        await vm.RecordSelectedReferenceCallAsync("Confirmed", "Pat", null, "ian", CancellationToken.None);
        store.Verify(s => s.CloseActionAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        store.Verify(s => s.RecordReferenceCallAsync(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Store_WritesOnlyTheFiveRhythmTables()
    {
        var sql = Source("Kor.Opportunities.Data/Crm/BdRhythmStore.cs");
        var targets = Regex.Matches(sql, @"\b(?:INSERT INTO|UPDATE|DELETE FROM|MERGE(?: INTO)?)\s+([A-Za-z_.]+)")
            .Select(m => m.Groups[1].Value).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "opportunities.BdAsk", "opportunities.BdMeeting", "opportunities.BdReference", "opportunities.DirectorHour", "opportunities.DirectorHourMove" }, targets);
        Assert.DoesNotMatch(@"DELETE FROM", sql);
    }

    [Fact]
    public void Migration359_RefusesASecondRun_BeforeItsTransaction()
    {
        var sql = Source("Kor.Opportunities.Data/Schema/359_CleanStartArchive.sql");
        var guard = sql.IndexOf("WHERE ByStaffId = N'migration-359')", StringComparison.Ordinal);
        var tran = sql.IndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal);
        Assert.True(guard > 0 && guard < tran, "EXPECTED the run-once guard before BEGIN TRANSACTION");
        Assert.Contains("THROW 50359", sql, StringComparison.Ordinal);
        Assert.Contains("CHECK (Stage IN (1, 3, 6, 7, 8))", sql, StringComparison.Ordinal);
        Assert.Contains("AND Stage <> 8;", sql, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO opportunities.CrmEngagementStageHistory", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\bDELETE\b", Regex.Replace(sql, @"--[^\r\n]*", ""));
    }

    [Fact]
    public void Migration360_CreatesEachTableGuarded_AndSeedsOnce()
    {
        var sql = Source("Kor.Opportunities.Data/Schema/360_BdOperatingRhythm.sql");
        foreach (var table in new[] { "BdMeeting", "BdReference", "DirectorHour" })
            Assert.Contains($"IF OBJECT_ID(N'opportunities.{table}', N'U') IS NULL", sql, StringComparison.Ordinal);
        Assert.Contains("IF NOT EXISTS (SELECT 1 FROM opportunities.BdMeeting WHERE Title = N'BD Updates'", sql, StringComparison.Ordinal);
        Assert.Contains("ISJSON", sql, StringComparison.Ordinal);
        Assert.DoesNotMatch(@"(?i)\b(DELETE|DROP TABLE|TRUNCATE)\b", Regex.Replace(sql, @"--[^\r\n]*", ""));
    }

    [Fact]
    public void Rows_OpenTheScreenForWhatTheyAreAbout()
    {
        Assert.Equal(Links.Org(5), BdDirectorRowLinks.For(new BdDirectorSignal(5, "Org", "Hiring", "s", Now, 1)));
        Assert.Equal(Links.Mpi(9), BdDirectorRowLinks.For(new BdForwardPipelineRow(9, "Tower", null, null, null, null, null)));
        Assert.Equal(Links.Org(6), BdDirectorRowLinks.For(new BdOpenStructuralSeatRow(6, "Org", null, null, null, null)));
        Assert.Equal(Links.Opportunity(7), BdDirectorRowLinks.For(new Kor.Operations.App.BusinessDevelopment.Workspace.DashboardViewModel.DeadlineRow(7, "RFP", "Buyer", null, null, "")));
        Assert.Equal(Links.Pursuit(8), BdDirectorRowLinks.For(new BdDirectorDeadlineRow(8, "P", "B", "O", Now)));
        var tied = Action(1, "Omar", "x", 1) with { EngagementId = 11, CanonicalOrgId = 12 };
        Assert.Equal(Links.Pursuit(11), BdDirectorRowLinks.For(new BdActionView(tied, false)));
        Assert.Equal(Links.Org(12), BdDirectorRowLinks.For(new BdActionView(tied with { EngagementId = null }, false)));
        Assert.Null(BdDirectorRowLinks.For(new BdActionView(Action(1, "Omar", "x", 1), false)));
        Assert.Null(BdDirectorRowLinks.For("anything else"));
    }

    [Fact]
    public void EveryGridWithSomethingBehindItsRows_OpensOnDoubleClick()
    {
        var doc = System.Xml.Linq.XDocument.Parse(Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml"));
        var grids = doc.Descendants().Where(e => e.Name.LocalName == "DataGrid")
            .Select(g => (Source: ((string?)g.Attribute("ItemsSource") ?? "").Replace("{Binding ", "").Split(new[] { '}', ',' })[0],
                          Opens: (string?)g.Attribute("MouseDoubleClick") == "Row_DoubleClick")).ToList();
        var linked = new[] { "Actions", "SelectedPursuits", "Undecided", "Accounts", "Asks", "Decisions", "MarketSignals", "MarketDeadlines", "ForwardPipeline", "OpenSeats", "PendingBids" };
        Assert.Equal(linked.OrderBy(s => s, StringComparer.Ordinal), grids.Where(g => g.Opens).Select(g => g.Source).OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void Worker_SendsTheHourOnce_SaysWhenThereIsNone_AndAddsMondayDraftsOnlyOnMonday()
    {
        var friday = new DateOnly(2026, 10, 16);
        var fridayAt6 = new DateTimeOffset(2026, 10, 16, 6, 5, 0, TimeSpan.FromHours(-7));
        DirectorHourRow Row(string json, DateTimeOffset? emailed = null) => new(4, friday, "BD", json, "claude", "ian", Now, emailed);
        var compose = Kor.Opportunities.Worker.Services.Reporting.DirectorHourEmailJob.Compose;

        var sent = Assert.Single(compose(Row(GoodHour), Array.Empty<BdActionRow>(), friday, fridayAt6));
        Assert.Equal("Your BD hour · about 35 minutes", sent.Subject);
        Assert.Equal(4L, sent.StampHourId);
        Assert.Empty(compose(Row(GoodHour, Now), new[] { Action(1, "Omar", "x", -1) }, friday, fridayAt6));

        var none = Assert.Single(compose(null, Array.Empty<BdActionRow>(), friday, fridayAt6));
        Assert.Equal("No BD hour was prepared for Friday 16 October", none.Subject);
        Assert.Null(none.StampHourId);
        var broken = Assert.Single(compose(Row("{ not json"), Array.Empty<BdActionRow>(), friday, fridayAt6));
        Assert.Contains("could not be read", broken.Subject, StringComparison.Ordinal);
        Assert.Null(broken.StampHourId);

        var monday = new DateOnly(2026, 10, 19);
        var mails = compose(null, new[] { Action(1, "Omar", "Call", -1) }, monday, fridayAt6.AddDays(3));
        Assert.Equal(new[] { "No BD hour was prepared for Monday 19 October", "Monday lists, drafts for you · Monday 19 October" }, mails.Select(m => m.Subject));
        Assert.Single(compose(null, new[] { Action(1, "Omar", "Closed", -1, "Done") }, monday, fridayAt6.AddDays(3)));
    }

    [Fact]
    public void Worker_HasOneRecipient_Ian_AndIsOnTheVisibleSchedule()
    {
        var options = new Kor.Opportunities.Worker.Options.OpportunitiesWorkerOptions();
        Assert.Equal("ilalonde@korstructural.com", options.DirectorHourRecipient);
        var job = Source("Kor.Opportunities.Worker/Services/Reporting/DirectorHourEmailJob.cs");
        Assert.Single(Regex.Matches(job, @"SendHtmlAsync\("));
        Assert.Contains("opt.DirectorHourRecipient, mail.Subject", job, StringComparison.Ordinal);
        Assert.DoesNotContain("anthropic", job, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nameof(Reporting.DirectorHourEmailJob), \"DirectorHourEmailCronSchedule\", \"0 5 6 ? * MON-FRI\"",
            Source("Kor.Opportunities.Worker/Services/ScheduledJobDefinition.cs"), StringComparison.Ordinal);
        Assert.Contains("?? \"0 5 6 ? * MON-FRI\"", Source("Kor.Opportunities.Worker/Program.cs"), StringComparison.Ordinal);
    }

    private static string Source(string path) => File.ReadAllText(Path.Combine(RepoRoot(), path));

    private static string RepoRoot()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("EXPECTED repository root; ACTUAL not found");
    }
}
