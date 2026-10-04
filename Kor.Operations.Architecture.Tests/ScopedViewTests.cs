using Kor.Operations.Architecture;
using Xunit;

namespace Kor.Operations.Architecture.Tests;

public sealed class ScopedViewTests
{
    [Fact]
    public void StandardsEstateIsTheEightNodePilotScene()
    {
        var model = new ArchModel(
            1,
            new[]
            {
                new ArchProject("Kor.Operations.App", "Kor.Operations.App", "desktop app", "net8.0-windows", Array.Empty<string>(), Array.Empty<string>(), 0, 0),
            },
            new[]
            {
                new ArchType("Kor.Operations.App:Kor.Operations.StandardDetails.MasterPublisher", "MasterPublisher", "class", "Kor.Operations.StandardDetails", "Kor.Operations.App", "Kor.Operations.App/StandardDetails/MasterPublisher.cs", "write"),
                new ArchType("Kor.Operations.App:Kor.Operations.StandardDetails.DrafterBridgeClient", "DrafterBridgeClient", "class", "Kor.Operations.StandardDetails", "Kor.Operations.App", "Kor.Operations.App/StandardDetails/DrafterBridgeClient.cs", "service"),
                new ArchType("Kor.Operations.App:Kor.Operations.StandardDetails.KorStandardsReadRepository", "KorStandardsReadRepository", "class", "Kor.Operations.StandardDetails", "Kor.Operations.App", "Kor.Operations.App/StandardDetails/KorStandardsReadRepository.cs", "service"),
                new ArchType("Kor.Operations.App:Kor.Operations.StandardDetails.StandardDetailsWindow", "StandardDetailsWindow", "class", "Kor.Operations.StandardDetails", "Kor.Operations.App", "Kor.Operations.App/StandardDetails/StandardDetailsWindow.xaml.cs", "ui"),
            },
            Array.Empty<ArchEdge>(),
            Array.Empty<ArchFormat>(),
            Array.Empty<ArchExternal>(),
            Array.Empty<ArchVerb>(),
            Array.Empty<ArchDuplicate>(),
            Array.Empty<ArchOrphan>(),
            Array.Empty<ArchCycle>(),
            Array.Empty<ArchGraph>(),
            Array.Empty<ArchScript>(),
            new ArchStats(0, 0, 0));

        var view = Assert.Single(ScopedViews.All, v => v.Name == "standards-estate");
        var graph = GraphBuilder.BuildScoped(model, view);
        var boxes = view.DerivedNodes
            .Select(n => (n.Id, n.W, n.H))
            .Concat(view.AuthoredNodes.Select(n => (n.Id, n.W, n.H)))
            .ToList();

        Assert.Equal(12.2, view.PageWidth);
        Assert.Equal(8.6, view.PageHeight);
        Assert.Equal(8, boxes.Count);
        Assert.All(boxes, b =>
        {
            Assert.True(b.W > 0, $"{b.Id} width must be authored and non-zero");
            Assert.True(b.H > 0, $"{b.Id} height must be authored and non-zero");
        });
        Assert.Equal(8, graph.Nodes.Count);
        Assert.Equal(10, graph.Edges.Count);
        Assert.Single(graph.Edges, e => e.Kind.StartsWith("built:", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(graph.Nodes, n =>
            n.Id == "operations-app"
            && n.Detail.Contains("4/4 derived type(s)", StringComparison.Ordinal));
    }
}
