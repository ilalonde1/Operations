#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Learning;
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
/// toolbar off and says which; the sign-in page is never covered; a ready Connecting/Setting-up page looks wedged only
/// after 20 seconds; the restart prompt follows page state and reconnect needs the payload's running-service evidence.
/// WHAT IT DOES NOT: that MeshCentral's page does what its functions are named for -- the 9 functions and 6 elements were
/// read in KOR-MESH01's MeshCentral 1.2.5 source on 2026-10-02 (9 of 9, 6 of 6), and the bridge re-checks the functions
/// every half second in the live page. Nothing here signs in or draws a remote screen, exercises dispatcher timing or
/// the live request/poll flow, or proves that a running agent can negotiate its next desktop session.
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
    public void Every_page_function_the_script_calls_is_declared()
    {
        // Every call in the script is to one of: its own functions, the browser's, or a declared required/optional page function.
        var own = Regex.Matches(Script, @"function ([A-Za-z]+)\(").Select(m => m.Groups[1].Value);
        var browser = new[] { "postMessage", "filter", "push", "parseInt", "isNaN", "querySelectorAll", "querySelector", "getElementById", "createElement",
            "appendChild", "contains", "substring", "stringify", "String", "setInterval", "function", "if", "for", "return", "catch" };
        var allowed = own.Concat(browser).Concat(KorRemoteBridge.RequiredFunctions).Concat(KorRemoteBridge.OptionalFunctions).ToHashSet();
        var calls = Regex.Matches(Script.Split('\n').Where(l => !l.TrimStart().StartsWith("//")).Aggregate("", (a, l) => a + l + "\n"), @"(?<![.\w])([A-Za-z_]\w*)\s*\(")
            .Select(m => m.Groups[1].Value).Distinct().Where(c => !allowed.Contains(c)).ToList();
        Assert.True(calls.Count == 0, "page functions called without being declared required or optional: " + string.Join(", ", calls));
    }

    [Fact]
    public void Optional_aspect_fix_calls_are_typeof_guarded()
    {
        Assert.Contains("typeof putstore === 'function'", Script);
        Assert.Contains("typeof toggleAspectRatio === 'function'", Script);
        Assert.Contains("typeof deskAspectRatio === 'undefined'", Script);
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
    public void Only_a_ready_connecting_or_setting_up_page_past_the_threshold_looks_wedged()
    {
        Assert.Equal(TimeSpan.FromSeconds(20), KorRemoteViewerModel.StuckAfter);
        foreach (var state in new[] { 1, 2 })
        {
            var vm = new KorRemoteViewerModel("KOR-206", null);
            vm.Apply(new BridgeState("desktop", [], "KOR-206", state));
            Assert.False(vm.LooksWedged(TimeSpan.Zero));
            Assert.False(vm.LooksWedged(KorRemoteViewerModel.StuckAfter - TimeSpan.FromTicks(1)));
            Assert.True(vm.LooksWedged(KorRemoteViewerModel.StuckAfter));
            Assert.True(vm.LooksWedged(TimeSpan.FromMinutes(1)));
        }
    }

    [Fact]
    public void Connected_login_disconnected_and_not_ready_pages_never_look_wedged()
    {
        var vm = new KorRemoteViewerModel("KOR-206", null);
        var elapsed = TimeSpan.FromMinutes(1);
        Assert.False(vm.LooksWedged(elapsed));
        foreach (var page in new[]
        {
            Connected(),
            new BridgeState("login", [], "KOR-206", 1),
            new BridgeState("login", [], "KOR-206", 2),
            new BridgeState("desktop", [], "KOR-206", 0),
            new BridgeState("desktop", [], null, 1),
            new BridgeState("desktop", [], "", 2),
            new BridgeState("desktop", ["deskSetDisplay"], "KOR-206", 1),
            new BridgeState("desktop", ["deskSetDisplay"], "KOR-206", 2),
        })
        {
            vm.Apply(page);
            Assert.False(vm.LooksWedged(elapsed));
            vm.SetRestartPrompt(true);
            Assert.False(vm.ShowsRestartPrompt);
        }
    }

    [Fact]
    public void The_restart_prompt_uses_the_overlay_and_clears_when_the_attempt_ends()
    {
        foreach (var next in new[] { Connected(), new BridgeState("login"), new BridgeState("desktop", [], "KOR-206", 0) })
        {
            var vm = new KorRemoteViewerModel("KOR-206", null);
            vm.Apply(new BridgeState("desktop", [], "KOR-206", 1));
            Assert.False(vm.ShowsRestartPrompt);
            Assert.False(vm.ShowsOverlay);
            var changed = new List<string?>();
            vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
            vm.SetRestartPrompt(true);
            Assert.True(vm.ShowsRestartPrompt);
            Assert.True(vm.ShowsOverlay);
            Assert.Equal(vm.WedgedHint, vm.Overlay);
            Assert.Contains(nameof(vm.Overlay), changed);
            Assert.Contains(nameof(vm.ShowsOverlay), changed);
            Assert.Contains(nameof(vm.ShowsRestartPrompt), changed);
            vm.Apply(new BridgeState("desktop", [], "KOR-206", 2));
            Assert.True(vm.ShowsRestartPrompt); // repeated bridge messages preserve the same prompt
            vm.Apply(next);
            Assert.False(vm.ShowsRestartPrompt);
            Assert.False(vm.ShowsOverlay);
            vm.Apply(new BridgeState("desktop", [], "KOR-206", 1));
            Assert.False(vm.ShowsRestartPrompt); // the next attempt must reach its own threshold
            vm.SetRestartPrompt(true);
            vm.SetRestartPrompt(false);
            Assert.False(vm.ShowsOverlay);
        }
    }

    [Theory]
    [InlineData("Done", """[{"Installed":true,"Running":true,"Est443":1}]""", true)]
    [InlineData("Done", """{"Installed":true,"Running":true,"Est443":0}""", true)]
    [InlineData("Done", """[{"Installed":false}]""", false)]
    [InlineData("Done", """[{"Installed":true,"Running":false}]""", false)]
    [InlineData("Done", """[{"Result":"ran"}]""", false)]
    [InlineData("Failed", """[{"Installed":true,"Running":true}]""", false)]
    [InlineData("Refused", """[{"Installed":true,"Running":true}]""", false)]
    [InlineData("Running", """[{"Installed":true,"Running":true}]""", false)]
    [InlineData("Done", "[]", false)]
    [InlineData("Done", "null", false)]
    [InlineData("Done", "not json", false)]
    [InlineData("Done", null, false)]
    public void A_reconnect_needs_a_completed_restart_with_running_service_evidence(string status, string? output, bool succeeded)
    {
        var action = new ActionRow(788, FixCatalog.RestartMeshAgent, "operator", DateTime.UtcNow, null, status, null, output);
        Assert.Equal(succeeded, KorRemoteViewerModel.RestartSucceeded(action));
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
