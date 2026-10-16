#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Operations.Converters;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

// In-memory behavior gates plus source contracts. No SQL/ODBC, clipboard, dialogs,
// or authentication is exercised here. The render suite consumes the same fixture.
// Same-class miss: SQL join fanout can satisfy a source gate while returning more
// rows than this fake store. The follow-up SQL and render checks remain necessary.
public sealed class BdDirectorMyDayRegressionTests
{
    [Fact]
    public async Task CallSheet_OrdersUrgencyThenAskThenMissingActionThenAccount_AndCapsAtTwelve()
    {
        var f = new DirectorMyDayFixture();
        using var vm = f.Create();
        await vm.LoadMyDayAsync(CancellationToken.None, f.Now);
        Assert.Equal(new[] { "Overdue buyer", "Today buyer", "Ask buyer", "Missing action buyer", "Untracked client" }, vm.MyDay.Select(r => r.Item.Buyer));
        f.Director.Verify(s => s.GetCallSheetAsync(DirectorMyDayFixture.Actor, f.Now, It.IsAny<CancellationToken>()), Times.Once);
        var withinWeek = f.Items.Single(i => i.EngagementId == 2) with { Buyer = "Next week", NextActionDueUtc = f.Now.AddDays(6) };
        Assert.Equal("Next week", CallSheetOrdering.Order(f.Items.Append(withinWeek), f.Now)[2].Buyer);
        Assert.Equal(12, CallSheetOrdering.Order(Enumerable.Range(0, 20).Select(i => new CallSheetItem { Kind = CallSheetKind.Account, Buyer = i.ToString(CultureInfo.InvariantCulture) }), f.Now).Count);
    }

    [Fact]
    public void Draft_DropsWholeMissingSentences_UsesStoredFacts_AndRemovesBraces()
    {
        var f = new DirectorMyDayFixture();
        var item = f.Items.Single(i => i.EngagementId == 1);
        var full = BdMyDayRules.Draft(item, f.History);
        Assert.Contains("Hi Pat,", full);
        Assert.Contains("KOR has worked with Overdue buyer on 3 projects, most recently Community centre (2024).", full);
        Assert.Contains("Priya Engineer led that work.", full);
        var missing = BdMyDayRules.Draft(item with { ContactName = null, NextActionNote = null }, f.History with { LatestProjectName = null, KnownBy = Array.Empty<DeltekKnownBy>() });
        Assert.Equal("Would you have twenty minutes in the next two weeks?", missing);
        var braces = BdMyDayRules.Draft(item with { ContactName = "{Pat}", NextActionNote = "Discuss {scope}." }, f.History);
        Assert.DoesNotContain("{", braces);
        Assert.DoesNotContain("}", braces);
        Assert.DoesNotContain("Unknown person", full);
    }

    [Fact]
    public void OldSignal_HasAnExplicitBdDayDate_AndNeverEntersTheDraft()
    {
        var f = new DirectorMyDayFixture();
        var item = new CallSheetItem { Kind = CallSheetKind.Account, Buyer = "Example", SignalSubject = "Old tender", SignalLastSeenAtUtc = f.Now.AddDays(-45) };
        var row = new BdDirectorMyDayRow(item, f.Now, null);
        Assert.True(row.HasSignal);
        var day = new BdDayConverter().Convert(row.Item.SignalLastSeenAtUtc!, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.False(string.IsNullOrWhiteSpace(day.ToString()));
        Assert.DoesNotContain("recently", row.DraftedNote, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Old tender", row.DraftedNote);
        var xaml = XDocument.Parse(Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml"));
        var signalLine = xaml.Descendants().Single(e => e.Name.LocalName == "TextBlock"
            && ((string?)e.Attribute("Visibility"))?.Contains("HasSignal", StringComparison.Ordinal) == true);
        Assert.Contains(signalLine.Descendants(), e => (string?)e.Attribute("Text") == "Last signal ");
        Assert.Contains(signalLine.Descendants(), e => ((string?)e.Attribute("Text"))?.Contains("Item.SignalLastSeenAtUtc, Mode=OneWay, Converter={StaticResource BdDay}", StringComparison.Ordinal) == true);
    }

    [Theory]
    [InlineData(CrmActivityType.Call, 2, "Call")]
    [InlineData(CrmActivityType.EmailOut, 4, "Email")]
    [InlineData(CrmActivityType.Meeting, 3, "Meeting")]
    public async Task LogTouch_AppendsOnceWithActorAndPursuit_AccountCannotLog(CrmActivityType type, int number, string label)
    {
        var f = new DirectorMyDayFixture();
        using var vm = f.Create();
        await vm.LoadMyDayAsync(CancellationToken.None, f.Now);
        var account = vm.MyDay.Single(r => r.IsAccount);
        Assert.False(account.CanLog);
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.LogMyDayAsync(account, type, CancellationToken.None));
        await vm.LogMyDayAsync(vm.MyDay.First(), type, CancellationToken.None);
        f.Activities.Verify(s => s.AppendAsync(It.Is<CrmActivity>(a => (int)a.ActivityType == number && a.EngagementId == 1
            && a.CreatedBy == DirectorMyDayFixture.Actor && a.Subject == "My day: " + label), DirectorMyDayFixture.Actor, It.IsAny<CancellationToken>()), Times.Once);
        f.Director.Verify(s => s.GetCallSheetAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Candidates_RequireEvidence_DeduplicateAliases_AndNormalizeRegions()
    {
        var f = new DirectorMyDayFixture();
        using var vm = f.Create();
        await vm.LoadMyDayAsync(CancellationToken.None, f.Now);
        var row = vm.MyDay.First();
        Assert.Equal(new[] { "Priya", "Omar" }, row.Candidates.Select(c => c.Staff.StaffKey));
        Assert.Equal("Led 3 jobs for Overdue buyer", row.Candidates[0].Reason);
        Assert.Equal("Holds 1 live pursuits in Vancouver Island", row.Candidates[1].Reason);
        Assert.DoesNotContain(row.Candidates, c => c.Staff.StaffKey is "Accountant" or "Gmail");
        Assert.Equal(BdMyDayRules.RegionKey("Vancouver Island"), BdMyDayRules.RegionKey("VancouverIsland"));
        Assert.Equal(BdMyDayRules.RegionKey("Okanagan"), BdMyDayRules.RegionKey("Okanagan-BcInterior"));
        Assert.False(BdMyDayRules.IsActor("unknown@korstructural.com", "unknown@korstructural.com", f.Staff));
        Assert.Contains("unknown@korstructural.com", row.SecondSeats);
        Assert.Empty(BdMyDayRules.Candidates(f.Records[0] with { AssignedStaffIds = "omara@korstructural.com" }, DirectorMyDayFixture.Actor, f.Staff, f.Records, f.History, row.Item.Buyer));
        Assert.Empty(BdMyDayRules.Candidates(f.Records[0], "someoneelse@korstructural.com", f.Staff, f.Records, f.History, row.Item.Buyer));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChooseEngineer_SavesSeatThenAsk_ReportsPartialFailure(bool failAsk)
    {
        var f = new DirectorMyDayFixture { FailAsk = failAsk };
        using var vm = f.Create();
        await vm.LoadMyDayAsync(CancellationToken.None, f.Now);
        var row = vm.MyDay.First();
        var before = DateTimeOffset.UtcNow;
        await vm.ChooseEngineerAsync(row, row.Candidates[0], CancellationToken.None);
        Assert.Equal(new[] { "update", "ask" }, f.Writes);
        Assert.Equal("unknown@korstructural.com,Priya", f.Records[0].AssignedStaffIds);
        Assert.Equal(new byte[] { 1 }, f.Saved!.RowVersion);
        var ask = Assert.IsType<BdAsk>(f.Ask);
        Assert.Equal(1L, ask.EngagementId);
        Assert.Equal("Priya", ask.ToStaffKey);
        Assert.Equal(60, ask.Minutes);
        Assert.InRange(ask.DueUtc, before.AddDays(7), DateTimeOffset.UtcNow.AddDays(7));
        Assert.Equal("Technical lead on Community relationship: Discuss the next phase.", ask.Ask);
        Assert.Equal(failAsk ? "Second seat saved, but the technical-lead ask could not be created. Create the ask from Asks." : "Second seat saved and technical-lead ask created.", vm.StatusMessage);
        f.RecordsStore.Verify(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        f.Ledger.Verify(s => s.CreateAskAsync(It.IsAny<BdAsk>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SeatConflict_ReloadsAndNeverCreatesAsk()
    {
        var f = new DirectorMyDayFixture();
        f.RecordsStore.Setup(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CrmConcurrencyException(nameof(CrmEngagement), 1));
        using var vm = f.Create();
        await vm.LoadMyDayAsync(CancellationToken.None, f.Now);
        var row = vm.MyDay.First();
        await vm.ChooseEngineerAsync(row, row.Candidates[0], CancellationToken.None);
        Assert.Contains("reloaded; no seat or ask was saved", vm.StatusMessage);
        f.Ledger.Verify(s => s.CreateAskAsync(It.IsAny<BdAsk>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TrackedAccount_OffersJoin_NeverInserts_AndPreservesExistingSeats()
    {
        var f = new DirectorMyDayFixture();
        using var vm = f.Create();
        await vm.LoadMyDayAsync(CancellationToken.None, f.Now);
        var account = vm.MyDay.Single(r => r.IsAccount);
        // A different person starts tracking after the call sheet was read.
        f.Records.Add(new CrmEngagement { Id = 20, BuyerCanonicalOrgId = account.Item.BuyerOrgId, OwnerStaffId = "Omar", AssignedStaffIds = "legacy", RowVersion = new byte[] { 9 } });
        await vm.StartTrackingAsync(account, CancellationToken.None);
        Assert.True(account.CanJoin);
        Assert.Equal("Tracked by Omar Engineer", account.TrackedBy);
        f.RecordsStore.Verify(s => s.InsertAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        await vm.JoinSecondSeatAsync(account, CancellationToken.None);
        Assert.Equal("legacy,BDLead", f.Records.Single(r => r.Id == 20).AssignedStaffIds);
        Assert.Equal(new byte[] { 9 }, f.Saved!.RowVersion);
    }

    [Fact]
    public async Task UntrackedAccount_InsertsChosenRegionAndActor_AndReportsDuplicateRefusal()
    {
        var f = new DirectorMyDayFixture();
        using var vm = f.Create();
        await vm.LoadMyDayAsync(CancellationToken.None, f.Now);
        var account = vm.MyDay.Single(r => r.IsAccount);
        account.SelectedRegion = "vancouverisland";
        f.RecordsStore.Setup(s => s.InsertAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unique buyer, owner and region refused."));
        await vm.StartTrackingAsync(account, CancellationToken.None);
        f.RecordsStore.Verify(s => s.InsertAsync(It.Is<CrmEngagement>(e => e.OpportunityId == null && e.OwnerStaffId == "BDLead"
            && e.Stage == CrmEngagementStage.Drafting && e.BuyerCanonicalOrgId == 13 && e.Region == "vancouverisland"
            && e.ExternalSource == "BdDirector.MyDay"), DirectorMyDayFixture.Actor, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("Unique buyer, owner and region refused", vm.StatusMessage);
    }

    [Fact]
    public void SecondSeat_ResolvesAliasesOncePerLivePursuit_AndPeopleDisplaysTheCount()
    {
        var f = new DirectorMyDayFixture();
        var assigned = new[]
        {
            new CrmEngagement { AssignedStaffIds = "Omar,omara@korstructural.com" },
            new CrmEngagement { AssignedStaffIds = "omara@korstructural.com", Stage = CrmEngagementStage.Submitted },
            new CrmEngagement { AssignedStaffIds = "Omar", Stage = CrmEngagementStage.Won },
            new CrmEngagement { AssignedStaffIds = "unknown" },
        };
        Assert.Equal(2, assigned.Count(e => BdMyDayRules.IsLive(e) && BdMyDayRules.Assigned(e.AssignedStaffIds)
            .Any(key => BdMyDayRules.IsActor(key, "omara@korstructural.com", f.Staff))));
        var person = new BdDirectorPerson("omara@korstructural.com", "Omar", new[] { "Omar", "omara@korstructural.com" }, 1, 0, 1, 0, 0, 0, 0, 0, 0, SecondSeat: 2);
        Assert.Equal(2, new BdDirectorPersonRow(person).SecondSeat);
        // Cardinality is enforced by EXISTS, not a join that multiplies alias rows.
        var sql = Source("Kor.Opportunities.Data/Crm/SqlBdDirectorStore.cs");
        Assert.Contains("seat.Stage IN (1, 3) AND EXISTS", sql);
        Assert.Contains("staff.StaffKey = LTRIM(RTRIM(token.value)) OR staff.Mailbox = LTRIM(RTRIM(token.value))", sql);
        Assert.Contains("staff.Mailbox = p.PersonKey AND staff.Mailbox IS NOT NULL", sql);
    }

    [Fact]
    public void MyDayDates_UseBdDay_AndDateRatchetStaysZero()
    {
        var path = Path.Combine(Root(), "Kor.Operations.App/BusinessDevelopment/Director");
        foreach (var file in Directory.EnumerateFiles(path).Where(f => Path.GetExtension(f) is ".cs" or ".xaml"))
            Assert.DoesNotMatch("yyyy-MM-dd|MMM d, yyyy", File.ReadAllText(file));
        var xaml = XDocument.Parse(Source("Kor.Operations.App/BusinessDevelopment/Director/BdDirectorWindow.xaml"));
        var tab = xaml.Descendants().Single(e => e.Name.LocalName == "TabItem" && (string?)e.Attribute("Header") == "My day");
        var dates = tab.Descendants().Attributes().Where(a => Regex.IsMatch(a.Value, "Binding (WhyDate|Item.SignalLastSeenAtUtc|Item.LastActivityUtc)"));
        Assert.Equal(3, dates.Count());
        Assert.All(dates, a => Assert.Contains("Converter={StaticResource BdDay}", a.Value));
    }

    private static string Source(string path) => File.ReadAllText(Path.Combine(Root(), path));
    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

internal sealed class DirectorMyDayFixture
{
    internal const string Actor = "bdlead@korstructural.com";
    internal DateTimeOffset Now { get; } = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    internal Mock<IBdDirectorStore> Director { get; } = new();
    internal Mock<ICrmEngagementStore> RecordsStore { get; } = new();
    internal Mock<ICrmActivityStore> Activities { get; } = new();
    internal Mock<IBdDirectorLedgerStore> Ledger { get; } = new();
    internal Mock<IBdStaffDirectory> Directory { get; } = new();
    internal Mock<IDeltekClientContextService> Deltek { get; } = new();
    internal List<CrmEngagement> Records { get; } = new();
    internal List<CallSheetItem> Items { get; } = new();
    internal List<string> Writes { get; } = new();
    internal CrmEngagement? Saved { get; private set; }
    internal BdAsk? Ask { get; private set; }
    internal bool FailAsk { get; init; }
    internal IReadOnlyList<BdStaffRow> Staff { get; } = new[]
    {
        new BdStaffRow(1, "BDLead", Actor, "BD Lead", true, "fixture"),
        new BdStaffRow(2, "Priya", "priya@korstructural.com", "Priya Engineer", true, "fixture"),
        new BdStaffRow(3, "Omar", "omara@korstructural.com", "Omar Engineer", true, "fixture"),
        new BdStaffRow(4, "omara@korstructural.com", "omara@korstructural.com", "Omar Engineer", true, "fixture"),
        new BdStaffRow(5, "Accountant", "accounts@korstructural.com", "Accountant", true, "fixture"),
        new BdStaffRow(6, "Gmail", "engineer@gmail.com", "Outside engineer", true, "fixture"),
    };
    internal DeltekClientIntelligence History { get; } = new()
    {
        ClientId = "C11", ClientName = "Overdue buyer", ProjectCount = 3, LifetimeFee = 125000,
        LatestProjectName = "Community centre", LatestProjectStart = new DateTime(2024, 5, 1),
        KnownBy = new[] { new DeltekKnownBy("Priya Engineer", "priya@korstructural.com", 3, new DateTime(2024, 5, 1)) },
    };

    internal DirectorMyDayFixture()
    {
        Records.Add(new CrmEngagement { Id = 1, BuyerCanonicalOrgId = 11, OwnerStaffId = "BDLead", AssignedStaffIds = "unknown@korstructural.com",
            Region = "Vancouver Island", NextActionNote = "Discuss the next phase.", NextActionDueUtc = Now.AddDays(-1), RowVersion = new byte[] { 1 } });
        Records.Add(new CrmEngagement { Id = 2, BuyerCanonicalOrgId = 12, OwnerStaffId = Actor, Region = "VancouverIsland", NextActionDueUtc = Now });
        Records.Add(new CrmEngagement { Id = 4, BuyerCanonicalOrgId = 14, OwnerStaffId = "BDLead", Region = "Okanagan-BcInterior" });
        Records.Add(new CrmEngagement { Id = 6, BuyerCanonicalOrgId = 21, OwnerStaffId = "Omar", Region = "VancouverIsland" });
        Items.Add(new CallSheetItem { Kind = CallSheetKind.Account, BuyerOrgId = 13, Buyer = "Untracked client", DeltekClientId = "C13" });
        Items.Add(new CallSheetItem { Kind = CallSheetKind.Pursuit, EngagementId = 4, BuyerOrgId = 14, Buyer = "Missing action buyer", Seat = "BD lead" });
        Items.Add(new CallSheetItem { Kind = CallSheetKind.Ask, AskId = 8, BuyerOrgId = 14, Buyer = "Ask buyer", AskText = "Share the project scope.", AskDueUtc = Now.AddDays(1) });
        Items.Add(new CallSheetItem { Kind = CallSheetKind.Pursuit, EngagementId = 2, BuyerOrgId = 12, Buyer = "Today buyer", NextActionDueUtc = Now, Seat = "BD lead" });
        Items.Add(new CallSheetItem { Kind = CallSheetKind.Pursuit, EngagementId = 1, BuyerOrgId = 11, Buyer = "Overdue buyer", DeltekClientId = "C11",
            ContactName = "Pat Client", ContactEmail = "pat@example.invalid", NextActionNote = "Discuss the next phase.", NextActionDueUtc = Now.AddDays(-1),
            Region = "Vancouver Island", Seat = "BD lead", Pursuit = "Community relationship", SignalSubject = "Illustrative old signal", SignalLastSeenAtUtc = Now.AddDays(-45) });
        Directory.Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Staff);
        Director.Setup(s => s.GetCallSheetAsync(It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>())).ReturnsAsync(Items);
        Director.Setup(s => s.GetPeopleAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDirectorPerson>());
        RecordsStore.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Records);
        RecordsStore.Setup(s => s.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((long id, CancellationToken ct) => Records.SingleOrDefault(r => r.Id == id));
        RecordsStore.Setup(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CrmEngagement e, string actor, CancellationToken ct) =>
            {
                Writes.Add("update"); Saved = e; Records[Records.FindIndex(r => r.Id == e.Id)] = e; return e;
            });
        Ledger.Setup(s => s.CreateAskAsync(It.IsAny<BdAsk>(), It.IsAny<CancellationToken>())).Returns((BdAsk ask, CancellationToken ct) =>
        {
            Writes.Add("ask"); Ask = ask;
            return FailAsk ? Task.FromException<long>(new InvalidOperationException("Fixture ask failure.")) : Task.FromResult(1L);
        });
        Activities.Setup(s => s.AppendAsync(It.IsAny<CrmActivity>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CrmActivity a, string actor, CancellationToken ct) => a);
        Deltek.Setup(s => s.LoadAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync((string? client, CancellationToken ct) =>
            client == "C11" ? History : History with { ClientId = "C13", ClientName = "Untracked client", KnownBy = Array.Empty<DeltekKnownBy>() });
    }

    internal BdDirectorViewModel Create() => new(Director.Object, Mock.Of<IPursuitOverwatchStore>(), RecordsStore.Object,
        Mock.Of<IBdAttributionStore>(), Ledger.Object, Deltek.Object, Mock.Of<IKnowledgeClaimStore>(), DirectorMarketDefaults.Dashboard(),
        DirectorMarketDefaults.Opportunities(), DirectorMarketDefaults.Brief(), Mock.Of<IPursuitFileStore>(), Activities.Object, Directory.Object)
        { CurrentActorUpn = Actor };
}
