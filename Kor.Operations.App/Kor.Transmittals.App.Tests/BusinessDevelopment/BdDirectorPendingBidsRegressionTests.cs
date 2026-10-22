#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;
using Kor.Operations.App.BusinessDevelopment;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Operations.App.FeeProposal;
using Kor.Operations.App.Opportunities;
using Kor.Operations.Converters;
using Kor.Operations.Core.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Knowledge;
using Kor.Opportunities.Data.Opportunities;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// Covers stage labels, tabs/bindings, bid versus relationship fixture populations,
/// read frequency, Mine identity, local calendar weeks, urgency, submission fields,
/// cancellation, current-row concurrency, reassignment reuse and opportunity editing.
/// The render harness calls VerifySubmissionDialogs on its themed STA thread to
/// exercise real modal OK/Cancel through the window's production action path.
/// Does not execute SQL or prove real history/status writes, shell launching or
/// visual quality. Same-class miss: a SQL join that duplicates rows could pass the
/// source contract and never appear in this fake-store fixture. Render review is owed.
/// </summary>
public sealed class BdDirectorPendingBidsRegressionTests
{
    private const string Director = "Kor.Operations.App/BusinessDevelopment/Director/";

    [Fact]
    public void Window_HasThreeScreensAndData_ExactStaticCaption_AndRequiredColumns()
    {
        var xaml = Source(Director + "BdDirectorWindow.xaml");
        var tabs = XDocument.Parse(xaml).Descendants().Where(e => e.Name.LocalName == "TabItem").ToArray();
        // v3 (21 Oct 2026): five places on the rail; the tabbed screens live in Library, the old Today as "Prepared hour".
        Assert.Equal(new[] { "Prepared hour", "People", "Pursuits", "Doctrine", "Data", "Actions", "Meetings", "References", "The number", "By person", "Funnel", "Accounts", "Asks & decisions", "Knowledge", "Market", "This week", "Pending bids", "My day" },
            tabs.Select(t => (string?)t.Attribute("Header")));
        Assert.Equal(new[] { "today", "pursuits", "team", "plan", "library" },
            XDocument.Parse(xaml).Descendants().Where(e => e.Name.LocalName == "RadioButton" && (string?)e.Attribute("GroupName") == "nav").Select(e => (string?)e.Attribute("Tag")));
        Assert.Equal("Bids in flight: each pursuit that came from an RFP, who holds it, when it is due, and whether it went in. Pursuits without an RFP behind them are relationship work and stay on People and Accounts.", BdDirectorViewModel.PendingBidsCaption);
        Assert.Contains("{x:Static director:BdDirectorViewModel.PendingBidsCaption}", xaml, StringComparison.Ordinal);
        Assert.Equal(new[] { "Client", "Project", "RFP", "Assignee", "Due", "Time left", "Stage", "Submitted", "File", "Link" },
            tabs[16].Descendants().Where(e => e.Name.LocalName is "DataGridTextColumn" or "DataGridTemplateColumn").Select(e => (string?)e.Attribute("Header")));
        Assert.Contains("ConverterParameter=Time", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IsUrgent}\" Value=\"True\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void StageNames_KeepStoredValues_AndNoScreenLiteralSaysDrafting()
    {
        Assert.Equal(1, (int)CrmEngagementStage.Drafting);
        foreach (var pair in new[] { (CrmEngagementStage.Drafting, "Pending"), (CrmEngagementStage.Submitted, "Submitted"),
                     (CrmEngagementStage.Won, "Won"), (CrmEngagementStage.Lost, "Lost") })
        {
            Assert.Equal(pair.Item2, CrmStageNames.Display(pair.Item1));
            if (pair.Item1 != CrmEngagementStage.Drafting)
                Assert.Equal(pair.Item2, new CrmEngagementRowView(new CrmEngagement { Stage = pair.Item1 }, null).StageDisplay);
        }
        // A stage-1 pursuit row reads the way BD Director splits it (2026-10-21): no tender yet, or a tender out for bid.
        Assert.Equal("No RFP yet", new CrmEngagementRowView(new CrmEngagement { Stage = CrmEngagementStage.Drafting }, null).StageDisplay);
        Assert.Equal("Out for bid", new CrmEngagementRowView(new CrmEngagement { Stage = CrmEngagementStage.Drafting, OpportunityId = 1 },
            new Opportunity { SubmissionDeadlineUtc = DateTimeOffset.Now }).StageDisplay);
        // A rezoning signal is an opportunity row with no closing date: not out for bid.
        Assert.Equal("No RFP yet", new CrmEngagementRowView(new CrmEngagement { Stage = CrmEngagementStage.Drafting, OpportunityId = 1 },
            new Opportunity()).StageDisplay);
        foreach (var folder in new[] { Director, "Kor.Operations.App/Crm" })
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), folder), "*", SearchOption.AllDirectories)
                     .Where(f => Path.GetExtension(f) is ".cs" or ".xaml"))
        {
            var source = File.ReadAllText(file);
            var literals = Path.GetExtension(file) == ".xaml"
                ? XDocument.Parse(source).Descendants().Attributes().Select(a => a.Value)
                : Regex.Matches(Regex.Replace(source, @"//[^\r\n]*|/\*.*?\*/", "", RegexOptions.Singleline), "\"((?:\\\\.|[^\"\\\\])*)\"")
                    .Select(m => m.Groups[1].Value);
            Assert.All(literals, literal => Assert.DoesNotContain("Drafting", literal, StringComparison.OrdinalIgnoreCase));
        }
        var editor = Source("Kor.Operations.App/Crm/CrmEngagementDialog.xaml.cs");
        Assert.Contains("CrmStageNames.Display(stage)", editor, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BidsAndRelationshipCount_LoadOnce_RefreshAgain_NeverOnTabOrMineChange()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var vm = fixture.Create();
        vm.CurrentActorUpn = "ishabana@korstructural.com"; // Window resolves identity before its first load.
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal("Up to date.", vm.StatusMessage);
        Assert.False(vm.MineOnly);
        vm.CurrentActorUpn = "ISHABANA@korstructural.com";
        vm.MineOnly = true;
        vm.MineOnly = false;
        fixture.VerifyReads(Times.Once());
        var xaml = XDocument.Parse(Source(Director + "BdDirectorWindow.xaml"));
        Assert.All(xaml.Descendants().Where(e => e.Name.LocalName is "TabControl" or "TabItem"), e => Assert.Null(e.Attribute("SelectionChanged")));
        await vm.LoadAsync(CancellationToken.None);
        fixture.VerifyReads(Times.Exactly(2));
    }

    [Fact]
    public async Task SixPursuits_ShowOnlyThreeBids_OneRedRow_AndSubmittedTime()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var vm = fixture.Create();
        await vm.LoadAsync(CancellationToken.None);
        Assert.Equal(6, fixture.Records.Count);
        Assert.Equal(new long[] { 3, 1, 2 }, vm.PendingBids.Select(b => b.Bid.EngagementId));
        Assert.Equal(3, vm.RelationshipPursuits);
        Assert.Equal(1L, Assert.Single(vm.PendingBids, b => b.IsUrgent).Bid.EngagementId);
        Assert.Equal(fixture.Now.AddHours(-2), vm.PendingBids[0].SubmittedAtUtc);
        Assert.All(vm.PendingBids.Where(b => b.IsPending), b => Assert.Null(b.SubmittedAtUtc));
        Assert.DoesNotContain(vm.PendingBids, b => b.Bid.EngagementId >= 10);
        Assert.Contains("2 pending · 1 submitted · by assignee: Islam Shabana 2", vm.PendingBidsSummary, StringComparison.Ordinal);
        Assert.Contains("3 relationship pursuits not shown", vm.PendingBidsSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Mine_MatchesMailboxOrStaffKey_CaseInsensitively_AndBlankNeverMatches()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var vm = fixture.Create();
        await vm.LoadAsync(CancellationToken.None);
        vm.CurrentActorUpn = "  ISHABANA@korstructural.com  ";
        vm.MineOnly = true;
        Assert.Equal(new long[] { 1, 2 }, vm.PendingBids.Select(b => b.Bid.EngagementId));
        vm.CurrentActorUpn = "islam";
        Assert.Equal(1L, Assert.Single(vm.PendingBids).Bid.EngagementId);
        vm.CurrentActorUpn = "";
        Assert.Empty(vm.PendingBids);
        vm.MineOnly = false;
        Assert.Equal(3, vm.PendingBids.Count);
    }

    [Fact]
    public void Urgency_OnlyPendingWithin24HoursOrPast_AndCountdownIsShared()
    {
        var fixture = new DirectorPendingBidsFixture();
        var bid = fixture.Bids[0];
        foreach (var hours in new[] { -2, 0, 6, 24, 25, 72 })
        {
            var due = fixture.Now.AddHours(hours);
            var row = new BdDirectorPendingBidRow(bid with { DueUtc = due }, fixture.Now);
            Assert.Equal(hours <= 24, row.IsUrgent);
            Assert.Equal(OpportunityDeadlineDisplay.Countdown(due, fixture.Now), row.Countdown);
            Assert.False((row with { Bid = row.Bid with { Stage = CrmEngagementStage.Submitted } }).IsUrgent);
        }
        Assert.False(new BdDirectorPendingBidRow(bid with { DueUtc = null }, fixture.Now).IsUrgent);
    }

    [Fact]
    public void Summary_UsesLocalCalendarWeeks_NotSevenDayWindows_AndCountsPendingOnly()
    {
        var fixture = new DirectorPendingBidsFixture();
        var week = BdDateDisplay.WeekStart(fixture.Now);
        DateTimeOffset Local(DateTime day) => new(DateTime.SpecifyKind(day, DateTimeKind.Unspecified), TimeZoneInfo.Local.GetUtcOffset(day));
        var now = Local(week.AddDays(6).AddHours(23));
        var dates = new[] { now, Local(week.AddDays(7)), Local(week.AddDays(14)), Local(week.AddDays(-1)) };
        var rows = dates.Select(d => new BdDirectorPendingBidRow(fixture.Bids[0] with { DueUtc = d }, now)).ToList();
        rows.Add(new(fixture.Bids[0] with { DueUtc = null }, now));
        rows.Add(new(fixture.Bids[2] with { DueUtc = now }, now)); // Submitted never inflates pending due buckets.
        var summary = BdDirectorViewModel.SummarizePendingBids(rows, 3, now);
        Assert.Contains("5 pending · 1 submitted", summary, StringComparison.Ordinal);
        Assert.Contains("due this week 1, next week 1, later 1", summary, StringComparison.Ordinal);
    }

    [Fact]
    public void BdDay_TimeModePreservesClock_AndDirectorDateRatchetStaysZero()
    {
        var now = DateTimeOffset.UtcNow;
        var converter = new BdDayConverter();
        Assert.Equal(BdDateDisplay.DayAndTime(now), converter.Convert(now, typeof(string), "Time", CultureInfo.InvariantCulture));
        Assert.Equal(BdDateDisplay.Day(now), converter.Convert(now, typeof(string), "", CultureInfo.InvariantCulture));
        Assert.Equal("", converter.Convert(null!, typeof(string), "Time", CultureInfo.InvariantCulture));
        foreach (var file in Directory.EnumerateFiles(Path.Combine(Root(), Director)).Where(f => Path.GetExtension(f) is ".cs" or ".xaml"))
            Assert.False(Regex.IsMatch(File.ReadAllText(file), "yyyy-MM-dd|MMM d, yyyy"), file);
    }

    [Fact]
    public async Task Submission_UsesFreshRow_UpdatesOnceWithActorAndBothFields_ThenReloads()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var vm = fixture.Create();
        await vm.LoadAsync(CancellationToken.None);
        var row = vm.PendingBids.Single(b => b.Bid.EngagementId == 1);
        using var source = new AttachmentFixtureFile();
        fixture.Files.ReturnedFileName = "stored-proposal.pdf";
        fixture.Records[0] = fixture.Records[0] with { Notes = "Newer note", OwnerStaffId = "new owner", RowVersion = new byte[] { 8 } };
        await vm.MarkSubmittedAsync(row, new SubmittedDetails("  proposal.pdf  ", "  pasted link  ", source.Path), "actor", CancellationToken.None);
        fixture.Engagements.Verify(s => s.UpdateAsync(It.Is<CrmEngagement>(e => e.Id == 1 && e.Stage == CrmEngagementStage.Submitted
            && e.SubmittedFileName == "stored-proposal.pdf" && e.SubmittedFileId == 100 && e.FolderLink == "pasted link" && e.Notes == "Newer note"
            && e.OwnerStaffId == "new owner" && e.RowVersion[0] == 8), "actor", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal(new[] { "add", "update" }, fixture.Files.Operations);
        Assert.Single(fixture.Files.Rows);
        fixture.VerifyReads(Times.Exactly(2));
        Assert.Equal("Bid marked submitted.", vm.StatusMessage);
    }

    [Fact]
    public async Task Submission_CancelDoesNotReadOrWrite()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var vm = fixture.Create();
        await vm.MarkSubmittedAsync(new(fixture.Bids[0], fixture.Now), null, "actor", CancellationToken.None);
        fixture.Engagements.VerifyNoOtherCalls();
        fixture.VerifyReads(Times.Never());
    }

    [Theory]
    [InlineData(CrmEngagementStage.Submitted)]
    [InlineData(CrmEngagementStage.Won)]
    [InlineData(CrmEngagementStage.Lost)]
    public async Task Submission_DoesNotRestampOrReopenAnAlreadyMovedBid(CrmEngagementStage stage)
    {
        var fixture = new DirectorPendingBidsFixture();
        fixture.Records[0] = fixture.Records[0] with { Stage = stage };
        using var vm = fixture.Create();
        using var source = new AttachmentFixtureFile();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vm.MarkSubmittedAsync(new(fixture.Bids[0], fixture.Now),
            new SubmittedDetails("proposal.pdf", null, source.Path), "actor", CancellationToken.None));
        Assert.Empty(fixture.Files.Rows);
        fixture.Engagements.Verify(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void Submission_ValidationRequiresFile_AndEnforcesBothLimits()
    {
        Assert.NotNull(SubmittedDetails.Validate("file.pdf", null)); // a typed name is not an attachment
        Assert.NotNull(SubmittedDetails.Validate(" ", null, "source.pdf"));
        Assert.NotNull(SubmittedDetails.Validate(new string('a', 257) + ".pdf", null, "source.pdf"));
        Assert.NotNull(SubmittedDetails.Validate("file.pdf", new string('a', 1001), "source.pdf"));
        Assert.Null(SubmittedDetails.Validate(new string('a', 256) + ".pdf", new string('a', 1000), "source.pdf"));
        Assert.Null(SubmittedDetails.Validate("file.pdf", null, "source.pdf"));
        Assert.Null(SubmittedDetails.Validate("file.pdf", "any pasted path or URL", "source.pdf"));
        Assert.Null(SubmittedDetails.Validate("existing.docx", null, existingFileId: 5));
        Assert.NotNull(SubmittedDetails.Validate("file.docx", null, "source.docx"));
        Assert.NotNull(SubmittedDetails.Validate("../file.pdf", null, "source.pdf"));
        var xaml = Source(Director + "SubmittedDialog.xaml");
        Assert.Contains("MaxLength=\"260\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MaxLength=\"1000\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Store", Source(Director + "SubmittedDialog.xaml.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Submission_UpdateFailureKeepsAttachment_AndSurfacesThatFact()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var source = new AttachmentFixtureFile();
        using var vm = fixture.Create();
        fixture.Engagements.Setup(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("concurrent update"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => vm.MarkSubmittedAsync(new(fixture.Bids[0], fixture.Now),
            new SubmittedDetails("proposal.pdf", null, source.Path), "actor", CancellationToken.None));
        Assert.Single(fixture.Files.Rows);
        Assert.DoesNotContain("remove", fixture.Files.Operations);
        Assert.Contains("attachment remains", error.Message, StringComparison.Ordinal);
        Assert.Equal(error.Message, vm.StatusMessage);
        Assert.Equal(CrmEngagementStage.Drafting, fixture.Records[0].Stage);
        fixture.VerifyReads(Times.Never());
    }

    [Fact]
    public async Task Submission_ExistingAttachmentCanBeKeptOrReplaced_WithoutDeletingHistory()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var source = new AttachmentFixtureFile();
        using var content = File.OpenRead(source.Path);
        var old = await fixture.Files.AddAsync(1, 101, "old.pdf", content, content.Length, "application/pdf", "actor", CancellationToken.None);
        fixture.Records[0] = fixture.Records[0] with { Stage = CrmEngagementStage.Submitted, SubmittedFileId = old.Id, SubmittedFileName = old.FileName };
        var row = new BdDirectorPendingBidRow(fixture.Bids[0] with { Stage = CrmEngagementStage.Submitted, SubmittedFileId = old.Id }, fixture.Now);
        using var vm = fixture.Create();
        await vm.MarkSubmittedAsync(row, new SubmittedDetails("old.pdf", null), "actor", CancellationToken.None);
        Assert.Single(fixture.Files.Rows);
        Assert.Equal(old.Id, fixture.Records[0].SubmittedFileId);
        await vm.MarkSubmittedAsync(row, new SubmittedDetails("replacement.pdf", null, source.Path), "actor", CancellationToken.None);
        Assert.Equal(2, fixture.Files.Rows.Count);
        Assert.Equal(101L, fixture.Records[0].SubmittedFileId);
        Assert.DoesNotContain("remove", fixture.Files.Operations);
    }

    [Fact]
    public void BothScreens_UseTheSharedLauncher_AndSubmittedFilesHaveAnOpenLink()
    {
        var director = Source(Director + "BdDirectorWindow.xaml.cs");
        var crm = Source("Kor.Operations.App/Crm/CrmView.xaml.cs");
        Assert.Contains("Get<FeeProposalLauncher>().Launch(row.Bid.OpportunityId, row.Bid.EngagementId", director, StringComparison.Ordinal);
        Assert.Contains("row.Bid.RfpNumber, row.Bid.Project, _actor", director, StringComparison.Ordinal);
        Assert.Contains("GetRequiredService<FeeProposalLauncher>().Launch(row.OpportunityId, row.Id", crm, StringComparison.Ordinal);
        Assert.Contains("row.OpportunityKey, row.ProjectName, ResolveActor()", crm, StringComparison.Ordinal);
        var xaml = Source(Director + "BdDirectorWindow.xaml");
        Assert.Contains("Content=\"Build fee proposal…\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Click=\"SubmittedFile_Click\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding HasSubmittedFile}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Get<IPursuitFileOpener>().OpenAsync(file, ct)", director, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rendered_LinkedProposalAttachesEachRender_AndLatestBecomesSubmittedFile()
    {
        var fixture = new DirectorPendingBidsFixture();
        var links = new Mock<IOpportunityFeeProposalLinkStore>();
        links.Setup(s => s.IsLinkedAsync(101, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var builder = new FakeProposalEvents();
        var messages = new List<string>();
        var launcher = new FeeProposalLauncher(Mock.Of<IServiceProvider>(), links.Object, fixture.Engagements.Object,
            Mock.Of<ICrmActivityStore>(), fixture.Files);
        using var session = launcher.Connect(builder, 101, 1, "actor", () => Task.CompletedTask, messages.Add);
        using var source = new AttachmentFixtureFile();
        var proposalId = Guid.NewGuid().ToString();
        await builder.RenderAsync(proposalId, source.Path);
        Assert.Equal(new[] { "add", "update" }, fixture.Files.Operations);
        var first = Assert.Single(fixture.Files.Rows);
        Assert.Equal(first.Id, fixture.Records[0].SubmittedFileId);
        Assert.Equal(Path.GetFileName(source.Path), fixture.Records[0].SubmittedFileName);
        Assert.Equal(File.ReadAllBytes(source.Path), fixture.Files.Bytes[first.Id]);
        Assert.Equal("application/pdf", first.ContentType);
        await builder.RenderAsync(proposalId, source.Path);
        Assert.Equal(new[] { "add", "update", "add", "update" }, fixture.Files.Operations);
        Assert.Equal(2, fixture.Files.Rows.Count);
        Assert.Equal(fixture.Files.Rows[1].Id, fixture.Records[0].SubmittedFileId);
        Assert.Contains(first, fixture.Files.Rows);
        Assert.Empty(messages);
        session.Dispose();
        await builder.RenderAsync(proposalId, source.Path);
        Assert.Equal(2, fixture.Files.Rows.Count);
    }

    [Theory]
    [InlineData(101L)]
    [InlineData(null)]
    public async Task Rendered_UnlinkedOrRelationshipPursuit_WritesNothing(long? opportunityId)
    {
        var fixture = new DirectorPendingBidsFixture();
        var links = new Mock<IOpportunityFeeProposalLinkStore>();
        var builder = new FakeProposalEvents();
        var launcher = new FeeProposalLauncher(Mock.Of<IServiceProvider>(), links.Object, fixture.Engagements.Object,
            Mock.Of<ICrmActivityStore>(), fixture.Files);
        using var session = launcher.Connect(builder, opportunityId, 1, "actor", () => Task.CompletedTask, _ => { });
        await builder.RenderAsync(Guid.NewGuid().ToString(), "not-opened.pdf");
        Assert.Empty(fixture.Files.Operations);
        fixture.Engagements.VerifyNoOtherCalls();
        if (opportunityId is null) links.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Rendered_WaitsForFirstSaveLink_LogsOnce_AndDoesNotLinkARepurposedBuilder()
    {
        var fixture = new DirectorPendingBidsFixture();
        var links = new Mock<IOpportunityFeeProposalLinkStore>();
        var linked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        links.Setup(s => s.LinkAsync(101, It.IsAny<Guid>(), "actor", It.IsAny<CancellationToken>())).Returns(linked.Task);
        var activities = new Mock<ICrmActivityStore>();
        var builder = new FakeProposalEvents();
        var launcher = new FeeProposalLauncher(Mock.Of<IServiceProvider>(), links.Object, fixture.Engagements.Object, activities.Object, fixture.Files);
        using var session = launcher.Connect(builder, 101, 1, "actor", () => Task.CompletedTask, _ => { });
        using var source = new AttachmentFixtureFile();
        var id = Guid.NewGuid().ToString();
        builder.Save(id);
        var render = builder.RenderAsync(id, source.Path);
        Assert.False(render.IsCompleted);
        Assert.Empty(fixture.Files.Operations);
        linked.SetResult();
        await render;
        Assert.Equal(new[] { "update", "add", "update" }, fixture.Files.Operations);
        Assert.Equal(CrmEngagementStage.Submitted, fixture.Records[0].Stage);
        var otherId = Guid.NewGuid().ToString();
        builder.Save(otherId);
        await builder.RenderAsync(otherId, source.Path);
        links.Verify(s => s.LinkAsync(101, It.IsAny<Guid>(), "actor", It.IsAny<CancellationToken>()), Times.Once);
        activities.Verify(s => s.AppendAsync(It.Is<CrmActivity>(a => a.EngagementId == 1 && a.ActivityType == CrmActivityType.Deliverable),
            "actor", It.IsAny<CancellationToken>()), Times.Once);
        Assert.Single(fixture.Files.Rows);
    }

    [Fact]
    public async Task Rendered_UpdateFailureLeavesAttachment_AndReportsIt()
    {
        var fixture = new DirectorPendingBidsFixture();
        fixture.Engagements.Setup(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("update failed"));
        var links = new Mock<IOpportunityFeeProposalLinkStore>();
        links.Setup(s => s.IsLinkedAsync(101, It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var builder = new FakeProposalEvents();
        var messages = new List<string>();
        var launcher = new FeeProposalLauncher(Mock.Of<IServiceProvider>(), links.Object, fixture.Engagements.Object,
            Mock.Of<ICrmActivityStore>(), fixture.Files);
        using var session = launcher.Connect(builder, 101, 1, "actor", () => Task.CompletedTask, messages.Add);
        using var source = new AttachmentFixtureFile();
        await builder.RenderAsync(Guid.NewGuid().ToString(), source.Path);
        Assert.Single(fixture.Files.Rows);
        Assert.Contains("attachment remains", Assert.Single(messages), StringComparison.Ordinal);
        Assert.DoesNotContain("remove", fixture.Files.Operations);
    }

    private sealed class FakeProposalEvents : IFeeProposalEvents
    {
        public event EventHandler<string>? ProposalSaved;
        public event Func<ProposalRenderedEvent, Task>? ProposalRendered;
        public void Save(string id) => ProposalSaved?.Invoke(this, id);
        public Task RenderAsync(string id, string path)
            => ProposalRendered?.Invoke(new ProposalRenderedEvent(id, path, "application/pdf")) ?? Task.CompletedTask;
    }

    [Fact]
    public async Task Assign_UsesExistingReassignPath_AndStoredOwner()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var vm = fixture.Create();
        await vm.LoadAsync(CancellationToken.None);
        var row = vm.PursuitForBid(vm.PendingBids.Single(b => b.Bid.EngagementId == 1));
        Assert.NotNull(row);
        await vm.ReassignAsync(row!, "new owner", "actor", "reason", CancellationToken.None);
        fixture.Pursuits.Verify(s => s.ReassignAsync(1, 101, "Islam", "new owner", "actor", "reason", It.IsAny<CancellationToken>()), Times.Once);
        var source = Source(Director + "BdDirectorWindow.xaml.cs");
        Assert.Contains("await ReassignPursuitAsync(row)", source, StringComparison.Ordinal);
        Assert.Contains("new ReassignDialog(", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpportunityEdit_UsesExistingStoreAndDialog_ThenReloads()
    {
        var fixture = new DirectorPendingBidsFixture();
        using var vm = fixture.Create();
        var row = new BdDirectorPendingBidRow(fixture.Bids[0], fixture.Now);
        var opportunity = await vm.GetBidOpportunityAsync(row, CancellationToken.None);
        Assert.NotNull(opportunity);
        var edited = opportunity! with { SubmissionDeadlineUtc = fixture.Now.AddDays(5) };
        await vm.SaveBidOpportunityAsync(edited, "actor", CancellationToken.None);
        fixture.Opportunities.Verify(s => s.GetByIdAsync(101, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Opportunities.Verify(s => s.UpdateAsync(edited, "actor", It.IsAny<CancellationToken>()), Times.Once);
        fixture.VerifyReads(Times.Once());
        Assert.Contains("new OpportunityEntryDialog(opportunity)", Source(Director + "BdDirectorWindow.xaml.cs"), StringComparison.Ordinal);
    }

    // Invoked by BdScreensRenderTests after it installs the theme on its STA thread.
    internal static void VerifySubmissionDialogs()
    {
        // In the full suite another test's STA thread may own Application.Current; touching it from here throws.
        // A window closed on this thread cannot shut down an Application another thread owns, so the guard is only
        // needed when this thread owns it.
        var app = Application.Current;
        var owned = app?.CheckAccess() == true;
        var shutdown = owned ? app!.ShutdownMode : ShutdownMode.OnExplicitShutdown;
        if (owned) app!.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            foreach (var accept in new[] { true, false })
            {
                var fixture = new DirectorPendingBidsFixture();
                using var vm = fixture.Create();
                var window = new BdDirectorWindow(vm, Mock.Of<IProposalStaffStore>());
                ((System.Windows.Controls.RadioButton)window.FindName("NavLibrary")).IsChecked = true; // v3: the tabbed screens live in Library
                var row = new BdDirectorPendingBidRow(fixture.Bids[0], fixture.Now);
                using var source = new AttachmentFixtureFile();
                window.SubmitBidAsync(row, dialog =>
                {
                    dialog.Loaded += (_, _) =>
                    {
                        ((TextBox)dialog.FindName("FileNameBox")).Text = "proposal.pdf";
                        ((TextBox)dialog.FindName("FolderLinkBox")).Text = "pasted folder";
                        if (accept)
                        {
                            ((Button)dialog.FindName("OkButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Assert.Null(dialog.Result); // OK without Browse must leave the dialog open
                            dialog.SelectFile(source.Path);
                            ((TextBox)dialog.FindName("FileNameBox")).Text = "proposal.pdf";
                        }
                        var button = (Button)dialog.FindName(accept ? "OkButton" : "CancelButton");
                        // Invoke OnClick so IsCancel's built-in dialog command runs too.
                        typeof(Button).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                            .Invoke(button, null);
                    };
                    return dialog.ShowDialog();
                }, "actor", CancellationToken.None).GetAwaiter().GetResult();
                fixture.Engagements.Verify(s => s.UpdateAsync(It.Is<CrmEngagement>(e => e.Stage == CrmEngagementStage.Submitted
                    && e.SubmittedFileName == "proposal.pdf" && e.FolderLink == "pasted folder"), "actor", It.IsAny<CancellationToken>()),
                    accept ? Times.Once() : Times.Never());
                fixture.VerifyReads(accept ? Times.Once() : Times.Never());
            }
        }
        finally { if (owned) app!.ShutdownMode = shutdown; }
    }

    private static string Source(string path) => File.ReadAllText(Path.Combine(Root(), path));
    private static string Root()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CLAUDE.md"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }
}

internal sealed class DirectorPendingBidsFixture
{
    internal DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
    internal Mock<IBdDirectorStore> Director { get; } = new();
    internal Mock<ICrmEngagementStore> Engagements { get; } = new();
    internal Mock<IPursuitOverwatchStore> Pursuits { get; } = new();
    internal Mock<IOpportunityStore> Opportunities { get; } = new();
    internal MemoryPursuitFileStore Files { get; } = new();
    internal List<CrmEngagement> Records { get; } = new();
    internal List<PendingBidRow> Bids { get; } = new();

    internal DirectorPendingBidsFixture()
    {
        Bids.Add(new(1, 101, "EXAMPLE-RFP-1", "Illustrative recreation centre", "Illustrative city", "Islam", "Islam Shabana", "ishabana@korstructural.com",
            Now.AddHours(6), CrmEngagementStage.Drafting, null, null, null, null));
        Bids.Add(new(2, 102, "EXAMPLE-RFP-2", "Illustrative school", "Illustrative district", "ishabana@korstructural.com", "Islam Shabana", "ishabana@korstructural.com",
            Now.AddDays(3), CrmEngagementStage.Drafting, Now.AddDays(-5), null, null, null));
        Bids.Add(new(3, 103, "EXAMPLE-RFP-3", "Illustrative library", "Illustrative city", "Omar", "Omar Alcazar Pastrana", "omara@korstructural.com",
            Now.AddHours(-1), CrmEngagementStage.Submitted, Now.AddHours(-2), "library-proposal.pdf", "https://example.invalid/bids", null));
        Records.AddRange(Bids.Select(b => new CrmEngagement { Id = b.EngagementId, OpportunityId = b.OpportunityId, Stage = b.Stage,
            OwnerStaffId = b.OwnerStaffId, SubmittedFileName = b.SubmittedFileName, FolderLink = b.FolderLink, RowVersion = new byte[] { 1 } }));
        Records.AddRange(Enumerable.Range(10, 3).Select(id => new CrmEngagement { Id = id, OpportunityId = null,
            OwnerStaffId = "Islam", Stage = id == 12 ? CrmEngagementStage.Submitted : CrmEngagementStage.Drafting, PotentialProjects = "Relationship negative control" }));
        SetupReads(Director);
        Director.Setup(s => s.GetPeopleAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new[]
        {
            new BdDirectorPerson("ishabana@korstructural.com", "Islam Shabana", new[] { "Islam", "ishabana@korstructural.com" }, 5, 0, 5, 0, 0, 0, 0, 0, 0),
            new BdDirectorPerson("omara@korstructural.com", "Omar Alcazar Pastrana", new[] { "Omar" }, 1, 0, 1, 0, 0, 0, 0, 0, 0),
        });
        Director.Setup(s => s.GetFunnelAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BdDirectorFunnel(new Dictionary<int, double>(), Array.Empty<BdDirectorUndecidedPursuit>(), 0, 0, 0, 0));
        Director.Setup(s => s.GetTargetActualsAsync(It.IsAny<IReadOnlyList<BdTargetKey>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new BdTargetActuals(new Dictionary<BdTargetKey, decimal>(), 6, 0));
        Director.Setup(s => s.GetAccountsAsync(1000, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDirectorAccount>());
        Director.Setup(s => s.GetRecentSignalsAsync(30, 25, It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDirectorSignal>());
        Engagements.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Records);
        Engagements.Setup(s => s.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((long id, CancellationToken ct) => Records.SingleOrDefault(e => e.Id == id));
        Engagements.Setup(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CrmEngagement record, string actor, CancellationToken ct) =>
            {
                Files.Operations.Add("update");
                Records[Records.FindIndex(e => e.Id == record.Id)] = record;
                var index = Bids.FindIndex(b => b.EngagementId == record.Id);
                Bids[index] = Bids[index] with { Stage = record.Stage, SubmittedAtUtc = Now, SubmittedFileName = record.SubmittedFileName, FolderLink = record.FolderLink, SubmittedFileId = record.SubmittedFileId };
                return record;
            });
        Pursuits.Setup(s => s.ListAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Records.Select(e => new PursuitOverwatchRow(e.Id, e.OpportunityId,
            e.OwnerStaffId!, (int)e.Stage, "Illustrative pursuit", "Illustrative buyer", null, null, Now, 0, null, Now)).ToArray());
        Pursuits.Setup(s => s.ReassignAsync(It.IsAny<long>(), It.IsAny<long?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ReassignOutcome.Reassigned);
        Opportunities.Setup(s => s.ListAsync(It.IsAny<CancellationToken>(), 5000, false, false)).ReturnsAsync(Array.Empty<Opportunity>());
        Opportunities.Setup(s => s.GetByIdAsync(It.IsAny<long>(), It.IsAny<CancellationToken>())).ReturnsAsync((long id, CancellationToken ct) => new Opportunity { Id = id, SubmissionDeadlineUtc = Now });
        Opportunities.Setup(s => s.UpdateAsync(It.IsAny<Opportunity>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((Opportunity o, string actor, CancellationToken ct) => o);
    }

    internal void SetupReads(Mock<IBdDirectorStore> store)
    {
        store.Setup(s => s.ListPendingBidsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Bids
            .Where(b => Records.Any(e => e.Id == b.EngagementId && e.OpportunityId is not null && e.Stage is CrmEngagementStage.Drafting or CrmEngagementStage.Submitted)).ToArray());
        store.Setup(s => s.CountRelationshipPursuitsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => Records.Count(e => e.OpportunityId is null
            && e.Stage is CrmEngagementStage.Drafting or CrmEngagementStage.Submitted));
    }

    internal void VerifyReads(Times times)
    {
        Director.Verify(s => s.ListPendingBidsAsync(It.IsAny<CancellationToken>()), times);
        Director.Verify(s => s.CountRelationshipPursuitsAsync(It.IsAny<CancellationToken>()), times);
    }

    internal BdDirectorViewModel Create()
    {
        var attribution = new Mock<IBdAttributionStore>();
        attribution.Setup(s => s.GetSummaryAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new BdAttributionSummary(0, 0, 0, 0, 0, Array.Empty<AttributionSourceRow>(), Array.Empty<AttributionOwnerRow>()));
        var ledger = new Mock<IBdDirectorLedgerStore>();
        ledger.Setup(s => s.ListTargetsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdTarget>());
        ledger.Setup(s => s.ListAsksAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdAsk>());
        ledger.Setup(s => s.ListDecisionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<BdDecision>());
        var claims = new Mock<IKnowledgeClaimStore>();
        DirectorMarketDefaults.SetupEmptyScoring(ledger, claims);
        claims.Setup(s => s.ListSourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<KnowledgeSourceSummary>());
        claims.Setup(s => s.ListConceptsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<KnowledgeConceptRow>());
        return new(Director.Object, Pursuits.Object, Engagements.Object, attribution.Object, ledger.Object, Mock.Of<IDeltekClientContextService>(), claims.Object,
            DirectorMarketDefaults.Dashboard(), Opportunities.Object, DirectorMarketDefaults.Brief(), Files, Mock.Of<ICrmActivityStore>(), DirectorMarketDefaults.Staff());
    }
}
