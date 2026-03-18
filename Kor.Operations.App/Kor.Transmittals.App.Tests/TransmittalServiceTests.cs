#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Services;
using Moq;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class TransmittalServiceTests
{
    [Fact]
    public async Task SendAsync_HappyPath_PopulatesTransmittalAndLogsDelivery()
    {
        var (graphFacade, _, _) = TestGraphFacadeFactory.Create();

        var upload = new FakeUploadOrchestrator(
            new UploadOrchestrationResult(
                Folder: "/Projects/24001",
                CoverLocalPath: @"C:\Temp\cover.pdf",
                DriveId: "drive-123",
                ItemId: "item-456",
                CoverSharePointUrl: "https://sharepoint.example/cover.pdf",
                InternalLink: "https://sharepoint.example/internal",
                ExternalLink: "https://sharepoint.example/external"));

        var store = new Mock<ITransmittalsStore>(MockBehavior.Strict);
        store.Setup(s => s.LogTransmittalWithRecipientsAsync(
                It.IsAny<Guid>(),
                "24001",
                "Issue for review",
                "drive-123",
                "item-456",
                "https://sharepoint.example/cover.pdf",
                It.IsAny<DateTime>(),
                "sender@example.com",
                "1.2.3",
                It.Is<IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)>>(records =>
                    records.Single().Email == "recipient@example.com" &&
                    records.Single().Kind == "To" &&
                    records.Single().PersonalShareLink == "https://redirect.example/t/" + records.Single().LinkId),
                "Transmittal",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.MarkSentAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime>(),
                "sender@example.com",
                "1.2.3",
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new TransmittalService(graphFacade, upload, store.Object, null, "https://redirect.example", "1.2.3");
        var header = new Transmittal
        {
            ProjectNumber = "24001",
            ProjectName = "Library Upgrade",
            Subject = "Issue for review",
            Purpose = "For Review",
            CoverSheetFileName = "Transmittal-24001.pdf"
        };

        var result = await service.SendAsync(
            new TransmittalSendRequest
            {
                Header = header,
                Files =
                [
                    new TransmittalFile
                    {
                        FileName = "Drawing.pdf",
                        LocalPath = @"C:\Temp\Drawing.pdf",
                        SizeBytes = 1024
                    }
                ],
                ToRecipients = ["recipient@example.com"],
                CcRecipients = [],
                Folder = "/Projects/24001",
                SenderUpn = "sender@example.com",
                RemarksHtml = "<p>Please review.</p>",
                SignatureHtml = "<p>Regards</p>",
                NeedExternal = true,
                AttachIfSmall = false
            },
            CancellationToken.None);

        Assert.StartsWith("24001-", header.TransmittalNo);
        Assert.Equal("/Projects/24001", header.SharePointFolderPath);
        Assert.StartsWith("https://redirect.example/t/", header.InternalLink);
        Assert.Equal(header.InternalLink, header.ExternalLink);
        Assert.Contains("Click here to view the files", header.Remarks);
        Assert.Contains("recipient@example.com", result.AllRecipients);
        Assert.Equal(@"C:\Temp\cover.pdf", result.CoverLocalPath);

        store.VerifyAll();
    }

    [Fact]
    public async Task SendAsync_NullOrEmptyProjectNumber_ThrowsArgumentException()
    {
        var (graphFacade, _, _) = TestGraphFacadeFactory.Create();

        var service = new TransmittalService(
            graphFacade,
            new FakeUploadOrchestrator(new UploadOrchestrationResult("", "", "", "", "", "", null)),
            Mock.Of<ITransmittalsStore>(),
            null,
            "https://redirect.example",
            "1.2.3");

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(CreateRequest(projectNumber: null), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SendAsync(CreateRequest(projectNumber: " "), CancellationToken.None));
    }

    private static TransmittalSendRequest CreateRequest(string? projectNumber) =>
        new()
        {
            Header = new Transmittal
            {
                ProjectNumber = projectNumber ?? string.Empty,
                Subject = "Subject"
            },
            Files = [],
            ToRecipients = ["recipient@example.com"],
            CcRecipients = [],
            Folder = "/Projects/Test",
            SenderUpn = "sender@example.com"
        };

    private sealed class FakeUploadOrchestrator : IUploadOrchestrator
    {
        private readonly UploadOrchestrationResult _result;

        public FakeUploadOrchestrator(UploadOrchestrationResult result)
        {
            _result = result;
        }

        public Task RenderPreviewAsync(string outputPath, Transmittal header, IReadOnlyList<TransmittalFile> files, CancellationToken ct)
            => Task.CompletedTask;

        public Task<UploadOrchestrationResult> UploadAsync(
            Transmittal header,
            IReadOnlyList<TransmittalFile> files,
            string folder,
            bool needExternal,
            IProgress<(string file, long sent, long total)>? progress,
            IProgress<string>? status,
            CancellationToken ct)
            => Task.FromResult(_result);
    }
}
