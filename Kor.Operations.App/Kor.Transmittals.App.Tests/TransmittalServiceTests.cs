#nullable enable
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Graph;
using Kor.Operations.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        store.Setup(s => s.UpdateEmailStatusAsync(
                It.IsAny<Guid>(),
                It.IsAny<DateTime?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new TransmittalService(graphFacade, upload, store.Object, null, "https://redirect.example", "1.2.3", NullLogger<TransmittalService>.Instance);
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
        // Branded body (KorEmailTemplate): styled "View files" button replaces the
        // old bare "Click here" link, and the recipient list is rendered in-body.
        Assert.DoesNotContain("Click here to view the files", header.Remarks);
        Assert.Contains("View files", header.Remarks);
        Assert.Contains("recipient@example.com", header.Remarks);
        Assert.Contains("recipient@example.com", result.AllRecipients);
        Assert.Equal(@"C:\Temp\cover.pdf", result.CoverLocalPath);

        store.VerifyAll();
    }

    [Fact]
    public async Task SendAsync_OneOfTwoRecipientsFails_AttemptsAllAndThrowsAggregate()
    {
        var sentTo = new List<string>();
        var facade = new Mock<IGraphFacade>(MockBehavior.Loose);
        facade.Setup(f => f.ReserveTransmittalNumberAsync(It.IsAny<string>()))
            .ReturnsAsync("24001-001");
        facade.Setup(f => f.SendMailAsync(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.Is<IEnumerable<string>?>(r => r != null && r.First() == "ok@example.com")))
            .Callback<object, string, string?, bool, CancellationToken, string?, IEnumerable<string>?>(
                (_, _, _, _, _, _, recips) => sentTo.AddRange(recips!))
            .Returns(Task.CompletedTask);
        facade.Setup(f => f.SendMailAsync(
                It.IsAny<object>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>(),
                It.IsAny<string?>(),
                It.Is<IEnumerable<string>?>(r => r != null && r.First() == "fail@example.com")))
            .ThrowsAsync(new System.Net.Http.HttpRequestException("Graph API unavailable"));

        var upload = new FakeUploadOrchestrator(
            new UploadOrchestrationResult("/Projects/24001", string.Empty, "d", "i", string.Empty, string.Empty, null));

        string? capturedError = null;
        var store = new Mock<ITransmittalsStore>(MockBehavior.Loose);
        store.Setup(s => s.LogTransmittalWithRecipientsAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<IEnumerable<(string Email, string Kind, Guid LinkId, string? PersonalShareLink)>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        store.Setup(s => s.UpdateEmailStatusAsync(
                It.IsAny<Guid>(), It.IsAny<DateTime?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, DateTime?, string?, CancellationToken>((_, _, err, _) => capturedError = err)
            .Returns(Task.CompletedTask);

        var service = new TransmittalService(facade.Object, upload, store.Object, null, null, "1.0", NullLogger<TransmittalService>.Instance);

        var ex = await Assert.ThrowsAsync<AggregateException>(() => service.SendAsync(
            new TransmittalSendRequest
            {
                Header = new Transmittal { ProjectNumber = "24001", Subject = "Test", CoverSheetFileName = "cover.pdf" },
                Files = [],
                ToRecipients = ["ok@example.com", "fail@example.com"],
                CcRecipients = [],
                Folder = "/Projects/24001",
                SenderUpn = "sender@example.com"
            },
            CancellationToken.None));

        // Both recipients were attempted — not just the first
        Assert.Contains("ok@example.com", sentTo);

        // Exception message identifies the partial failure
        Assert.Contains("fail@example.com", ex.Message);
        Assert.Contains("1 of 2", ex.Message);

        // DB was updated with the error (not left silent)
        Assert.NotNull(capturedError);
        Assert.Contains("fail@example.com", capturedError);

        // Inner exception is the original Graph exception
        Assert.Single(ex.InnerExceptions);
        Assert.IsType<System.Net.Http.HttpRequestException>(ex.InnerExceptions[0]);
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
            "1.2.3",
            NullLogger<TransmittalService>.Instance);

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
