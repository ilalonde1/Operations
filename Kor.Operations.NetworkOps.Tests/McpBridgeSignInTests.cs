using Kor.Operations.NetworkOps.Cli;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Board row 72: with the sign-in lapsed, every MCP tool said only "An error occurred invoking '<tool>'" while the CLI,
// on the same lapse, exited 4 "SIGN-IN NEEDED". These drive the bridge's guard with a token source that throws the
// CLI's own SignInNeededException, bare and wrapped the way an HTTP transport wraps it.
// COVERS: tools/call and tools/list through McpBridge.SignIn, bare + wrapped + aggregate, and that other faults pass
// through untouched. DOES NOT COVER: the bridge's START (McpClient.CreateAsync at launch) -- a bridge started while the
// sign-in has lapsed still fails to start and Claude shows the server as failed; the SDK's own wire behaviour (that an
// McpException's text reaches the session is the SDK's contract, read live once after a lapse, not tested here).
public class McpBridgeSignInTests
{
    private static SignInNeededException Lapse() => new("the NetworkOps sign-in has expired and needs MFA, and nobody can answer a prompt here. Ian: in a terminal on this PC run `netops token`");

    public static TheoryData<Func<Exception>> Shapes => new()
    {
        () => Lapse(),
        () => new HttpRequestException("send failed", Lapse()),
        () => new InvalidOperationException("transport", new HttpRequestException("send failed", Lapse())),
        () => new AggregateException(new TimeoutException(), Lapse()),
    };

    [Theory, MemberData(nameof(Shapes))]
    public async Task A_lapsed_sign_in_on_a_tool_call_says_so_and_how_to_renew(Func<Exception> fault)
    {
        var r = await McpBridge.SignIn.OrLapsedResult(() => throw fault());
        Assert.True(r.IsError);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(r.Content)).Text;
        Assert.StartsWith("NetworkOps SIGN-IN EXPIRED", text);
        Assert.Contains("netops token", text);
        Assert.DoesNotContain("An error occurred", text);
    }

    [Theory, MemberData(nameof(Shapes))]
    public async Task A_lapsed_sign_in_on_the_tool_list_says_so_and_how_to_renew(Func<Exception> fault)
    {
        var e = await Assert.ThrowsAsync<McpException>(() => McpBridge.SignIn.OrSayItLapsed<int>(() => throw fault()));
        Assert.StartsWith("NetworkOps SIGN-IN EXPIRED", e.Message);
        Assert.Contains("netops token", e.Message);
    }

    [Fact]
    public async Task Any_other_fault_passes_through_untouched()
    {
        var boom = new HttpRequestException("APP01 unreachable");
        Assert.Same(boom, await Assert.ThrowsAsync<HttpRequestException>(() => McpBridge.SignIn.OrLapsedResult(() => throw boom)));
        Assert.Same(boom, await Assert.ThrowsAsync<HttpRequestException>(() => McpBridge.SignIn.OrSayItLapsed<int>(() => throw boom)));
    }

    // The guard is only worth anything if the bridge's two handlers go through it. A source check (it would NOT catch a
    // third upstream call added outside these two handlers).
    [Fact]
    public void Both_bridge_handlers_go_through_the_sign_in_guard()
    {
        var src = File.ReadAllText(Path.Combine(Repo.Root, "Kor.Operations.NetworkOps.Cli", "McpBridge.cs"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(src, @"upstream\.CallToolAsync\("));
        Assert.Contains("SignIn.OrLapsedResult(() => upstream.CallToolAsync(", src);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(src, @"upstream\.ListToolsAsync\("));
        Assert.Contains("SignIn.OrSayItLapsed(() => upstream.ListToolsAsync(", src);
    }

    [Fact]
    public async Task A_good_call_is_returned_as_is()
    {
        var ok = new CallToolResult { Content = [new TextContentBlock { Text = "fine" }] };
        Assert.Same(ok, await McpBridge.SignIn.OrLapsedResult(() => Task.FromResult(ok)));
        Assert.Equal(7, await McpBridge.SignIn.OrSayItLapsed(() => Task.FromResult(7)));
    }

    private static class Repo
    {
        public static string Root
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Agent"))) dir = dir.Parent;
                return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
            }
        }
    }
}
