using System.IO;
using Kor.Operations.App.BusinessDevelopment.Director;
using Kor.Operations.App.Crm;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;
using Moq;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

// Pins what a regional lead can do from My Pursuits without the Director: set their own next step, and mark a pursuit
// submitted with the proposal stored. Compares what reaches the stores only; it does not open the dialogs or the checklist
// window (the checklist is the Director's own window, opened with the selected pursuit), and a store that ignores a field
// would not be caught here.
public sealed class LeadPursuitActionsTests
{
    private static (CrmViewModel Vm, Mock<ICrmEngagementStore> Store, Mock<IPursuitFileStorage> Files) Build()
    {
        var store = new Mock<ICrmEngagementStore>();
        store.Setup(s => s.UpdateAsync(It.IsAny<CrmEngagement>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CrmEngagement e, string _, CancellationToken _) => e);
        var files = new Mock<IPursuitFileStorage>();
        var vm = new CrmViewModel(store.Object, Mock.Of<ICrmActivityStore>(), Mock.Of<ICrmContactStore>(),
            Mock.Of<IOpportunityStore>(), Mock.Of<IDeltekClientContextService>(), Mock.Of<ICanonicalOrgStore>(),
            Mock.Of<IPursuitContextStore>(), Mock.Of<IOpportunityInterestedFirmStore>(), Mock.Of<IPursuitFileStore>(), files.Object);
        return (vm, store, files);
    }

    [Fact]
    public async Task A_lead_sets_their_own_next_step_due_at_five_pm_local()
    {
        var (vm, store, _) = Build();
        var row = new CrmEngagementRowView(new CrmEngagement { Id = 7, Stage = CrmEngagementStage.Drafting, OwnerStaffId = "lead" }, null);

        await vm.SetNextStepAsync(row, "  Call the architect  ", new DateTime(2026, 10, 30), "lead", CancellationToken.None);

        var expectedDue = new DateTimeOffset(DateTime.SpecifyKind(new DateTime(2026, 10, 30, 17, 0, 0), DateTimeKind.Local)).ToUniversalTime();
        store.Verify(s => s.UpdateAsync(It.Is<CrmEngagement>(e => e.Id == 7 && e.NextActionNote == "Call the architect"
            && e.NextActionDueUtc == expectedDue && e.Stage == CrmEngagementStage.Drafting), "lead", It.IsAny<CancellationToken>()), Times.Once);
        await Assert.ThrowsAsync<ArgumentException>(() => vm.SetNextStepAsync(row, " ", DateTime.Today, "lead", CancellationToken.None));
    }

    [Fact]
    public async Task Marking_submitted_stores_the_proposal_and_points_the_pursuit_at_it()
    {
        var (vm, store, files) = Build();
        var pdf = Path.Combine(Path.GetTempPath(), $"kor-lead-submit-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(pdf, new byte[] { 0x25, 0x50, 0x44, 0x46 });
        try
        {
            var stored = new PursuitFile(55, 9, 39716, Path.GetFileName(pdf), null, null, 4, "application/pdf", DateTimeOffset.UtcNow, "lead", true);
            files.Setup(f => f.StoreAsync(9, 39716, It.IsAny<string>(), pdf, "lead", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PursuitFileUpload(stored.FileName, "", Array.Empty<byte>(), 4, "application/pdf", stored));
            var row = new CrmEngagementRowView(new CrmEngagement { Id = 9, OpportunityId = 39716, Stage = CrmEngagementStage.Drafting, OwnerStaffId = "lead" }, null);

            await vm.MarkSubmittedAsync(row, new SubmittedDetails("Proposal.pdf", null, pdf), "lead", CancellationToken.None);

            store.Verify(s => s.UpdateAsync(It.Is<CrmEngagement>(e => e.Id == 9 && e.Stage == CrmEngagementStage.Submitted
                && e.SubmittedFileId == 55 && e.SubmittedFileName == "Proposal.pdf"), "lead", It.IsAny<CancellationToken>()), Times.Once);
            await Assert.ThrowsAsync<ArgumentException>(() =>
                vm.MarkSubmittedAsync(row, new SubmittedDetails("Proposal.pdf", null, null), "lead", CancellationToken.None));
        }
        finally
        {
            File.Delete(pdf);
        }
    }
}
