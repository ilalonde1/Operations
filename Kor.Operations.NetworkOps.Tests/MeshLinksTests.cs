#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// A KOR Remote device link must name the device the way MeshCentral will read it. Ian, 2026-10-02: Connect from the app
// opened an empty Desktop for KOR-101 while the same device opened from KOR Remote worked. MeshCentral's parseUriArgs()
// (public/scripts/common-0.0.1.js, called with no decode flag) splits the URL on ? & | and keeps each value AS WRITTEN; the
// app and the prompts had escaped the id's '@' to '%40'. The class: A LINK BUILT FOR A READER THAT DOES NOT DECODE.
//
// WHAT IT COVERS: the link, read back by a copy of MeshCentral's own parser, gives exactly the node id; '@' and '$' (the
// characters MeshCentral's base64 ids use) survive; anything that is not such an id gives no link; nothing else in the
// code builds a gotonode link itself.
// WHAT IT DOES NOT: MeshCentral loading the device (that needs a signed-in browser: checked by Ian, 2026-10-02).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: another MeshCentral argument added by hand to a link (a key, a hide= flag) -- the
// copy of the parser checks only gotonode and viewmode.
public sealed class MeshLinksTests
{
    // MeshCentral 1.2.5's parseUriArgs(), without decodeUrl: split on ? & |, drop the first part, keep values as written.
    private static Dictionary<string, string> MeshCentralReads(string url)
        => Regex.Split(url, "[?&|]").Skip(1).Where(a => a.Contains('=')).ToDictionary(a => a[..a.IndexOf('=')], a => a[(a.IndexOf('=') + 1)..]);

    [Theory]
    [InlineData("node//6bM4@p1tjSNm6ZEW1LK3BGuWhckLeBspQj@x7t7ALpVrfF4anjtRH2s9NnEdPpDM")]   // KOR-101, the one that failed
    [InlineData("node//TFGGT45lasJ2ZA8jABM7xR6Q6vAWhEEbXjk714FUFcP@0S5uwenLRqnISJKEp11v")]   // KOR-208-N
    [InlineData("node//ab$cd@ef")]
    public void MeshCentral_reads_back_exactly_the_node_id(string nodeId)
    {
        var url = MeshLinks.DeviceUrl("https://kor-mesh01.int.korstructural.com/", nodeId)!;
        var args = MeshCentralReads(url);
        Assert.Equal(nodeId["node//".Length..], args["gotonode"]);
        Assert.Equal("11", args["viewmode"]);
        Assert.StartsWith("https://kor-mesh01.int.korstructural.com/?gotonode=", url);
    }

    [Fact]
    public void The_viewer_link_is_the_desktop_link_with_MeshCentrals_chrome_hidden()
    {
        var url = MeshLinks.ViewerUrl("https://kor-mesh01.int.korstructural.com/", "node//6bM4@p1tjSNm6ZEW1LK3BGuWhckLeBspQj@x7t7ALpVrfF4anjtRH2s9NnEdPpDM")!;
        var args = MeshCentralReads(url);
        Assert.Equal("6bM4@p1tjSNm6ZEW1LK3BGuWhckLeBspQj@x7t7ALpVrfF4anjtRH2s9NnEdPpDM", args["gotonode"]);
        Assert.Equal("11", args["viewmode"]);
        Assert.Equal("15", args["hide"]);                // header, top bar, footer, titles
        Assert.Null(MeshLinks.ViewerUrl("https://x/", "not an id!"));
    }

    [Fact]
    public void The_general_page_link_is_the_same_link_with_view_10()
        => Assert.Equal("10", MeshCentralReads(MeshLinks.DeviceUrl("https://x", "node//abc", viewMode: 10)!)["viewmode"]);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("node//has space")]
    [InlineData("node//a&b")]
    [InlineData("node//a%40b")]
    public void Anything_that_is_not_a_MeshCentral_id_gives_no_link(string? nodeId)
        => Assert.Null(MeshLinks.DeviceUrl("https://kor-mesh01", nodeId));

    [Fact]
    public void Nothing_else_builds_a_gotonode_link()
    {
        var root = RepoRoot();
        var builders = new[] { "Kor.Operations.App", "Kor.Operations.NetworkOps.Service", "Kor.Operations.NetworkOps.Core", "Kor.Operations.NetworkOps.Cli" }
            .SelectMany(p => Directory.EnumerateFiles(Path.Combine(root, p), "*.cs", SearchOption.AllDirectories))
            .Where(f => !f.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar) && !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)
                        && !f.Contains("Tests", StringComparison.Ordinal) && !f.EndsWith("MeshLinks.cs", StringComparison.Ordinal))
            .Where(f => File.ReadAllText(f).Contains("gotonode=", StringComparison.Ordinal))
            .Select(Path.GetFileName).ToList();
        Assert.True(builders.Count == 0, "a KOR Remote link built outside MeshLinks: " + string.Join(", ", builders));
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
