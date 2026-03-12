#nullable enable
using System.Text;
using Kor.Operations.Core;
using Moq;
using Xunit;

namespace Kor.Operations.Tests;

public sealed class GraphFacadeTests
{
    [Fact]
    public async Task SendMailAsync_SerializesExpectedSubjectAndRecipient()
    {
        var (facade, adapter, getLastRequest) = TestGraphFacadeFactory.Create();

        var header = new Transmittal
        {
            Subject = "Status update",
            ProjectNumber = "24001",
            ProjectName = "Test Project",
            Purpose = "For Review",
            Remarks = "<p>Hello</p>"
        };

        await facade.SendMailAsync(
            header,
            coverSheetServerUrl: string.Empty,
            coverSheetLocalPath: null,
            attachCover: false,
            ct: CancellationToken.None,
            senderUpn: "sender@example.com",
            toAndCcEmails: ["recipient@example.com"]);

        adapter.Verify(a => a.SendNoContentAsync(
            It.IsAny<Microsoft.Kiota.Abstractions.RequestInformation>(),
            It.IsAny<Dictionary<string, Microsoft.Kiota.Abstractions.Serialization.ParsableFactory<Microsoft.Kiota.Abstractions.Serialization.IParsable>>>(),
            It.IsAny<CancellationToken>()), Times.Once);

        var request = getLastRequest();
        Assert.NotNull(request);
        Assert.Contains("/users/{user%2Did}/sendMail", request!.UrlTemplate);
        Assert.Equal("sender@example.com", request.PathParameters["user%2Did"]?.ToString());

        Assert.NotNull(request.Content);
        request.Content.Position = 0;
        using var reader = new StreamReader(request.Content, Encoding.UTF8, leaveOpen: true);
        var json = await reader.ReadToEndAsync();

        Assert.Contains("\"subject\":\"Status update\"", json);
        Assert.Contains("\"address\":\"recipient@example.com\"", json);
    }
}
