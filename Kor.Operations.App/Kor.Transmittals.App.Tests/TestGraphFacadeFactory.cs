#nullable enable
using Kor.Operations.Graph;
using Microsoft.Graph;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Microsoft.Kiota.Serialization.Json;
using Moq;

namespace Kor.Operations.Tests;

internal static class TestGraphFacadeFactory
{
    public static (GraphFacade Facade, Mock<IRequestAdapter> Adapter, Func<RequestInformation?> LastRequest) Create()
    {
        var adapter = new Mock<IRequestAdapter>(MockBehavior.Loose);
        RequestInformation? lastRequest = null;

        adapter.SetupProperty(a => a.BaseUrl, "https://graph.microsoft.com/v1.0");
        adapter.SetupGet(a => a.SerializationWriterFactory).Returns(new JsonSerializationWriterFactory());
        adapter.Setup(a => a.SendNoContentAsync(
                It.IsAny<RequestInformation>(),
                It.IsAny<Dictionary<string, ParsableFactory<IParsable>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<RequestInformation, Dictionary<string, ParsableFactory<IParsable>>, CancellationToken>((request, _, _) => lastRequest = request)
            .Returns(Task.CompletedTask);

        var graphClient = new GraphServiceClient(adapter.Object, "https://graph.microsoft.com/v1.0");
        var facade = new GraphFacade(graphClient, "drive-id");
        return (facade, adapter, () => lastRequest);
    }
}
