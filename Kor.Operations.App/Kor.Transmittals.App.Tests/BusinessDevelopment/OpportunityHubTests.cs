#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.BusinessDevelopment.Workspace;
using Kor.Operations.App.Opportunities;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Sources;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>Checks classification, reachability, ownership and ordering, not rendering or SQL paging.</summary>
public sealed class OpportunityHubTests
{
    [Theory]
    [InlineData(OpportunitySourceType.ArcGisFeatureService, OpportunityKind.Application)]
    [InlineData(OpportunitySourceType.TempestProspero, OpportunityKind.Application)]
    [InlineData(OpportunitySourceType.EngagementHq, OpportunityKind.Application)]
    [InlineData(OpportunitySourceType.BcBid, OpportunityKind.Bid)]
    [InlineData(OpportunitySourceType.GenericJsonAward, OpportunityKind.Award)]
    [InlineData(OpportunitySourceType.Manual, OpportunityKind.Other)]
    [InlineData(OpportunitySourceType.GraphEmail, OpportunityKind.Other)]
    [InlineData((OpportunitySourceType)12345, OpportunityKind.Other)]
    public void SourceTypesClassifyWithoutNames(OpportunitySourceType source, OpportunityKind expected)
        => Assert.Equal(expected, OpportunityKinds.FromSource(source));

    [Fact]
    public void MixedTransportsRequireExplicitMetadataAndUnknownTypesRemainOther()
    {
        Assert.Equal(OpportunityKind.Other, OpportunityKinds.FromSource(OpportunitySourceType.GenericJson));
        Assert.Equal(OpportunityKind.Application, OpportunityKinds.FromSource(OpportunitySourceType.GenericJson, "application"));
        Assert.Equal(OpportunityKind.Bid, OpportunityKinds.FromSource(OpportunitySourceType.GenericJson, "\"bid\""));
        Assert.Equal(OpportunityKind.Award, OpportunityKinds.FromSource(OpportunitySourceType.GenericJson, "award"));
        Assert.Equal(OpportunityKind.Other, OpportunityKinds.FromSource((OpportunitySourceType)12345, "bid"));
        Assert.Equal(OpportunityKind.Other, OpportunityKinds.FromSource(OpportunitySourceType.Rss, "typo"));
    }

    [Fact]
    public void ConflictingObservationsAreNotMisrepresentedAndCrossPostsCountOnce()
    {
        Assert.Equal(OpportunityKind.Bid, OpportunityKinds.Combine([OpportunityKind.Bid, OpportunityKind.Bid, OpportunityKind.Other]));
        Assert.Equal(OpportunityKind.Other, OpportunityKinds.Combine([OpportunityKind.Application, OpportunityKind.Bid]));
        Assert.Equal(OpportunityKind.Other, OpportunityKinds.Combine([]));
    }

    [Fact]
    public void SignalsAndBidsPartitionEveryKindIncludingUnknownAndMineIsAnOverlap()
    {
        var rows = Enum.GetValues<OpportunitySourceType>().Append((OpportunitySourceType)12345)
            .Select((type, id) => Row(id, OpportunityKinds.FromSource(type))).ToArray();
        foreach (var row in rows)
        {
            Assert.NotEqual(OpportunityHubSelection.Matches(row, OpportunityHubSegment.Signals, "me"),
                OpportunityHubSelection.Matches(row, OpportunityHubSegment.Bids, "me"));
        }
        var mine = new OpportunityRowView(new Opportunity { OwnerStaffId = " ME@KOR.COM " }) { Kind = OpportunityKind.Bid };
        Assert.True(OpportunityHubSelection.Matches(mine, OpportunityHubSegment.Mine, "me@kor.com"));
        Assert.True(OpportunityHubSelection.Matches(mine, OpportunityHubSegment.Bids, "me@kor.com"));
        Assert.False(OpportunityHubSelection.Matches(mine, OpportunityHubSegment.Mine, "someoneelse"));
        Assert.False(OpportunityHubSelection.Matches(Row(99, OpportunityKind.Other), OpportunityHubSegment.Mine, ""));
    }

    [Fact]
    public void SignalsUseFiledDatesNotRecentIngestionAndLeaveUnknownDatesLast()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new[] { Row(1, OpportunityKind.Application, filed: now.AddYears(-9)),
            Row(2, OpportunityKind.Application, filed: now.AddDays(-2)), Row(3, OpportunityKind.Other) };
        Assert.Equal([2L, 1L, 3L], OpportunityHubSelection.Sort(rows, OpportunityHubSegment.Signals, now).Select(r => r.Id));
    }

    [Fact]
    public void BidsSortLiveDeadlinesBeforeExpiredUndatedAndAwards()
    {
        var now = DateTimeOffset.UtcNow;
        var rows = new[] { Row(1, OpportunityKind.Bid, deadline: now.AddDays(-2)),
            Row(2, OpportunityKind.Bid, deadline: now.AddDays(3)), Row(3, OpportunityKind.Bid),
            Row(4, OpportunityKind.Bid, deadline: now.AddDays(1)), Row(5, OpportunityKind.Award, deadline: now.AddDays(1)) };
        Assert.Equal([4L, 2L, 1L, 3L, 5L], OpportunityHubSelection.Sort(rows, OpportunityHubSegment.Bids, now).Select(r => r.Id));
    }

    [Fact]
    public async Task BazaarKindSwitchDoesNotRestoreAGrabbedRow()
    {
        var hubStore = new Mock<IOpportunityHubStore>();
        hubStore.Setup(s => s.ListHubAsync(It.IsAny<CancellationToken>())).ReturnsAsync([
            new(new Opportunity { Id = 1, Name = "Application" }, OpportunityKind.Application, DateTimeOffset.UtcNow),
            new(new Opportunity { Id = 2, Name = "Bid" }, OpportunityKind.Bid, DateTimeOffset.UtcNow)]);
        var grab = new Mock<IPursuitGrabStore>();
        grab.Setup(s => s.GrabAsync(1, "me", It.IsAny<CancellationToken>())).ReturnsAsync(new GrabResult(GrabOutcome.Grabbed, 1));
        var vm = new BazaarViewModel(Mock.Of<IOpportunityStore>(), grab.Object, Mock.Of<IPursuitLifecycleStore>(), hubStore.Object);
        vm.ConfigureHub(OpportunityHubSegment.Signals, null);
        await vm.LoadAsync(CancellationToken.None);
        var row = Assert.Single(vm.Grabbable);
        Assert.Equal(1, row.Id);
        await vm.GrabAsync(row, "me", CancellationToken.None);
        vm.ConfigureHub(OpportunityHubSegment.Bids, null);
        Assert.Equal(2, Assert.Single(vm.Grabbable).Id);
        vm.ConfigureHub(OpportunityHubSegment.Signals, null);
        Assert.Empty(vm.Grabbable);
        Assert.Contains("Loaded 0", vm.StatusMessage);
    }

    [Fact]
    public void DeepLinkRevealsItsKindAndClearsConflictingFilters()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var vm = new OpportunitiesViewModel(Mock.Of<IOpportunityStore>(), Mock.Of<IHeartbeatStore>(),
                    Mock.Of<IOpportunityScoringService>(), Mock.Of<IIngestionRunStore>(), Mock.Of<IIngestionTriggerStore>(),
                    Mock.Of<IOpportunitySourceStore>(), Mock.Of<Kor.Operations.App.Crm.IDeltekClientContextService>(),
                    Mock.Of<ICrmEngagementStore>(), Mock.Of<ICrmActivityStore>(), Mock.Of<ICrmContactStore>(),
                    Mock.Of<IOpportunityObservationStore>(), Mock.Of<IOpportunityInterestedFirmStore>(), Mock.Of<IPursuitGrabStore>());
                vm.Opportunities.Add(Row(1, OpportunityKind.Application));
                vm.Opportunities.Add(Row(2, OpportunityKind.Bid));
                vm.ConfigureHub(OpportunityHubSegment.Mine, "me");
                vm.FilterText = "not a matching name";
                Assert.Empty(vm.FilteredOpportunitiesView.Cast<OpportunityRowView>());
                Assert.True(vm.SelectById(2));
                Assert.Equal(OpportunityHubSegment.Bids, vm.HubSegment);
                Assert.Equal(2, Assert.Single(vm.FilteredOpportunitiesView.Cast<OpportunityRowView>()).Id);
                Assert.Equal(2, vm.Selected!.Id);
                vm.ConfigureHub(OpportunityHubSegment.Signals, "me", OpportunityKind.Other);
                Assert.Null(vm.Selected);
                Assert.True(vm.SelectById(1));
                Assert.Null(vm.HubKind);
                Assert.Equal(1, Assert.Single(vm.FilteredOpportunitiesView.Cast<OpportunityRowView>()).Id);
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The isolated WPF view-model check timed out.");
        Assert.Null(failure);
    }

    private static OpportunityRowView Row(long id, OpportunityKind kind, DateTimeOffset? filed = null, DateTimeOffset? deadline = null)
        => new(new Opportunity { Id = id, SubmissionDeadlineUtc = deadline }) { Kind = kind, FiledAtUtc = filed };
}
