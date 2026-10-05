#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.App.NetworkOps;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

/// <summary>
/// The KOR Remote viewer (Ian, 2026-10-02: a ScreenConnect-style window "with controls"; "will need to be able to switch to
/// individual monitors or see all").
///
/// WHAT IT COVERS: the bridge script and the window agree on the page functions the toolbar depends on (the script's NEED
/// list IS KorRemoteBridge.RequiredFunctions); the script calls no page function outside that list; every toolbar command the
/// window sends exists in the script; what the bridge reports becomes the right light, words, monitor buttons ("1", "2",
/// "All", the one on screen filled) and key list (Ctrl+Alt+Del has its own item); a renamed page function turns the
/// toolbar off and says which; the sign-in page is never covered.
/// WHAT IT DOES NOT: that MeshCentral's page does what its functions are named for -- the 9 functions and 6 elements were
/// read in KOR-MESH01's MeshCentral 1.2.5 source on 2026-10-02 (9 of 9, 6 of 6), and the bridge re-checks the functions
/// every half second in the live page. Nothing here signs in or draws a remote screen.
/// A SAME-CLASS FAULT IT WOULD NOT CATCH: a MeshCentral update that keeps a function's name but changes what it does or the
/// arguments it takes (deskToggleFull reading a new event field) -- named, present, and wrong.
/// </summary>
public sealed class KorRemoteViewerTests
{
    private static string Script => KorRemoteBridge.Script;

    private static string WindowCode => File.ReadAllText(Path.Combine(XamlStaticResourceOrderTests.GetRepoRoot(), "Kor.Operations.App", "NetworkOps", "KorRemoteViewerWindow.xaml.cs"));

    [Fact]
    public void The_script_and_the_window_depend_on_the_same_page_functions()
    {
        var need = Regex.Match(Script, @"var NEED = \[([^\]]*)\]").Groups[1].Value;
        var listed = Regex.Matches(need, "'([A-Za-z]+)'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(KorRemoteBridge.RequiredFunctions, listed);
    }

    [Fact]
    public void The_script_calls_no_page_function_it_does_not_check_for()
    {
        // Every call in the script is to one of: its own functions, the browser's, or a checked page function.
        var own = Regex.Matches(Script, @"function ([A-Za-z]+)\(").Select(m => m.Groups[1].Value);
        var browser = new[] { "postMessage", "filter", "push", "parseInt", "isNaN", "querySelectorAll", "querySelector", "getElementById", "createElement",
            "appendChild", "contains", "substring", "stringify", "String", "setInterval", "function", "if", "for", "return", "catch" };
        var allowed = own.Concat(browser).Concat(KorRemoteBridge.RequiredFunctions).ToHashSet();
        var calls = Regex.Matches(Script.Split('\n').Where(l => !l.TrimStart().StartsWith("//")).Aggregate("", (a, l) => a + l + "\n"), @"(?<![.\w])([A-Za-z_]\w*)\s*\(")
            .Select(m => m.Groups[1].Value).Distinct().Where(c => !allowed.Contains(c)).ToList();
        Assert.True(calls.Count == 0, "page functions called without being in NEED: " + string.Join(", ", calls));
    }

    [Fact]
    public void Every_command_the_window_sends_is_one_the_script_carries_out()
    {
        var sent = Regex.Matches(WindowCode, @"KorRemoteBridge\.Command\(""([a-z]+)""").Select(m => m.Groups[1].Value).Distinct().ToList();
        Assert.True(sent.Count >= 9, $"only {sent.Count} commands found -- the pattern no longer matches the window code");
        var api = Regex.Match(Script, @"window\.__korRemote = \{(.*?)\n    \};", RegexOptions.Singleline).Groups[1].Value;
        Assert.All(sent, c => Assert.Matches(@"\b" + c + @": function", api));
    }

    private static BridgeState Connected(params BridgeDisplay[] displays) => new("desktop", [], "KOR-208-N", 3,
        [new("0", ""), new("655406", "Ctrl-Alt-Del"), new("1048652", "Win-L"), new("1048653", "Win-M")], displays);

    [Fact]
    public void Each_monitor_and_all_of_them_are_buttons_with_the_one_on_screen_filled()
    {
        var vm = new KorRemoteViewerModel("KOR-208-N", null);
        vm.Apply(Connected(new(0, "All Displays", false), new(1, "Display 1", false), new(2, "Display 2", true)));
        Assert.True(vm.ShowsDisplays);
        Assert.Equal(["1", "2", "All"], vm.Monitors.Select(m => m.Label));
        Assert.Equal([1, 2, 0], vm.Monitors.Select(m => m.Number));                       // what deskSetDisplay is given
        Assert.True(vm.Monitors.Single(m => m.Label == "2").Selected);
        Assert.Equal("Every monitor at once", vm.Monitors.Single(m => m.Label == "All").Tip);

        vm.Apply(Connected(new BridgeDisplay(1, "Display 1", true)));                                   // one screen: no choice to offer
        Assert.False(vm.ShowsDisplays);
    }

    [Fact]
    public void The_light_and_words_follow_the_connection()
    {
        var vm = new KorRemoteViewerModel("KOR-208-N", "kwurmlinger · active");
        Assert.Equal("Opening…", vm.StatusText);
        Assert.True(vm.ShowsOverlay);
        vm.Apply(new BridgeState("login"));
        Assert.Equal("Sign in to KOR Remote", vm.StatusText);
        Assert.False(vm.ShowsOverlay);                                                      // the sign-in page must be visible
        Assert.False(vm.IsReady);
        vm.Apply(new BridgeState("desktop", [], "KOR-208-N", 1));
        Assert.Equal("Connecting…", vm.StatusText);
        vm.Apply(Connected());
        Assert.Equal("Connected", vm.StatusText);
        Assert.True(vm.IsConnected);
        Assert.Same(NetworkOpsBrushes.Healthy, vm.Light);
        Assert.Equal(["Win-L", "Win-M"], vm.Keys.Select(k => k.Text));                     // Ctrl+Alt+Del is its own item
        Assert.Contains("kwurmlinger", vm.PresenceText);
    }

    [Fact]
    public void A_renamed_page_function_turns_the_toolbar_off_and_says_which()
    {
        var vm = new KorRemoteViewerModel("KOR-208-N", null);
        vm.Apply(new BridgeState("desktop", ["deskSetDisplay"], "KOR-208-N", 3));
        Assert.False(vm.IsReady);
        Assert.False(vm.IsConnected);
        Assert.Contains("deskSetDisplay", vm.Problem);
        Assert.True(vm.ShowsOverlay);
        Assert.Same(NetworkOpsBrushes.Critical, vm.Light);
    }

    [Fact]
    public void A_message_that_is_not_the_bridges_changes_nothing()
    {
        var vm = new KorRemoteViewerModel("KOR-208-N", null);
        vm.Apply(KorRemoteBridge.Parse("not json"));
        Assert.Equal("Opening…", vm.StatusText);
        var parsed = KorRemoteBridge.Parse("""{"page":"desktop","missing":[],"node":"KOR-208-N","state":3,"keys":[],"displays":[{"number":1,"name":"Display 1","selected":true}],"files":false}""");
        Assert.Equal(3, parsed!.State);
        Assert.Equal("Display 1", parsed.Displays![0].Name);
    }
}
