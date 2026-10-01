#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.App.NetworkOps;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.App.Tests.NetworkOps;

// The NetworkOps Command Center's two view models, driven from a fixture fleet (no database).
//
// WHAT IT COVERS: the fleet grid's order (worst first), its one-line headline per state, that an
// acknowledged finding stops a PC showing red, the filter and problems-only toggle, the KPI tiles and
// a silent service, pattern members; on one PC, the explanation of the selected finding (knowledge,
// other PCs, the fleet pattern it belongs to, the empty-state texts) and the history tabs.
// WHAT IT DOES NOT: the API it talks to (NetworkOpsClient; the service side is ApiAccessTests plus a
// live signed-in call), nor how the windows look (NetworkOpsWindowsRenderTests). A SAME-CLASS FAULT IT
// WOULD NOT CATCH: a DateTime that crosses the API as Local instead of UTC would shift every "ago" by the
// UTC offset; the fixture is built in UTC, so only a live call against the real service shows that.
public sealed class NetworkOpsViewModelTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    private static FleetFinding F(long id, string device, string ruleKey, Severity sev, string title, DateTime? ack = null)
        => new(id, device, ruleKey, sev, title, "evidence", Now.AddDays(-2), Now.AddMinutes(-5), ack, ack is null ? null : "ian", null, null);

    internal static FleetSnapshot Fleet(DateTime? lastBeat = null)
    {
        var devices = new List<DeviceRow>
        {
            new(1, "KOR-101", Now.AddMinutes(-3), Now.AddMinutes(-30), Presence: "nobody signed in", PresenceState: "Nobody"),
            new(2, "KOR-216", Now.AddMinutes(-3), Now.AddMinutes(-30), Presence: "rchan · locked since 12:10", PresenceState: "Locked",
                AgentVersion: "1.0.0", AgentConnected: false, AgentLastContactUtc: Now.AddHours(-3)),
            new(3, "KOR-305", Now.AddMinutes(-3), Now.AddMinutes(-30), Presence: "nobody at the console · jli on a remote session, idle 12 min", PresenceState: "RemoteOnly"),
            new(4, "KOR-208-N", Now.AddMinutes(-3), Now.AddMinutes(-30), Presence: "kwurmlinger · active, idle 12 min", PresenceState: "Active",
                AgentVersion: "1.0.0", AgentConnected: true, AgentLastContactUtc: Now.AddSeconds(-10),
                MeshNodeId: "node//bJ@yhUBjIF4c8rS8MThrje0BENzyV5SCkrj4lZqrwPu4cOg3pq3Qj5YQwtqot2NX", MeshConnected: true),
            new(5, "SPARE8", null, null),
        };
        var engine = new Dictionary<string, string> { [Facts.Model] = "Lenovo 30DH", ["access.engine.2016"] = "16.0.5044.1000" };
        var facts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["KOR-101"] = new Dictionary<string, string> { [Facts.Model] = "Dell Precision 3660" },
            ["KOR-216"] = engine,
            ["KOR-305"] = engine,
            ["KOR-208-N"] = new Dictionary<string, string> { [Facts.Model] = "Lenovo 30DH" },
        };
        var open = new List<FleetFinding>
        {
            F(10, "KOR-216", "crash-loop:opushutil.exe", Severity.Warning, "opushutil.exe keeps crashing"),
            F(11, "KOR-305", "crash-loop:opushutil.exe", Severity.Warning, "opushutil.exe keeps crashing"),
            F(12, "KOR-208-N", "disk-errors:Disk 1", Severity.Critical, "Disk 1 is reporting uncorrected read errors"),
            F(13, "KOR-208-N", "not-restarted", Severity.Info, "Not restarted in 21 days"),
            F(14, "KOR-101", "gpu-hangs", Severity.Warning, "Graphics driver keeps resetting", ack: Now.AddHours(-1)),
        };
        var patterns = new List<ActivePattern>
        {
            new("crash-loop:opushutil.exe", "access.engine.2016", "16.0.5044.1000", 2, 2, 0, 3,
                "2 of 2 PCs with access.engine.2016 = 16.0.5044.1000 have crash-loop:opushutil.exe, against 0 of 3 without", Now.AddDays(-1)),
        };
        return new FleetSnapshot(devices, facts, open, patterns,
            new ServiceBeat("KOR-APP01", Now.AddDays(-1), lastBeat ?? Now.AddSeconds(-20), "0.2.0"));
    }

    private static NetworkOpsCommandCenterViewModel Center(FleetSnapshot? s = null)
    {
        var vm = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured("test: no service"));
        vm.Apply(s ?? Fleet(), Now);
        return vm;
    }

    [Fact]
    public void Fleet_is_worst_first_and_an_acknowledged_finding_does_not_keep_a_pc_red()
    {
        var vm = Center();

        Assert.Equal(["KOR-208-N", "KOR-216", "KOR-305", "KOR-101", "SPARE8"], vm.Fleet.Select(r => r.Name));
        Assert.Equal(HealthState.Critical, vm.Fleet[0].State);
        Assert.Equal(HealthState.Healthy, vm.Fleet.Single(r => r.Name == "KOR-101").State);   // its only finding is acknowledged
        Assert.Equal(HealthState.Unknown, vm.Fleet.Single(r => r.Name == "SPARE8").State);
    }

    [Fact]
    public void Each_row_says_what_matters_in_one_line()
    {
        var rows = Center().Fleet.ToDictionary(r => r.Name);

        Assert.Equal("Disk 1 is reporting uncorrected read errors  (+1 more)", rows["KOR-208-N"].Headline);
        Assert.Equal("opushutil.exe keeps crashing", rows["KOR-216"].Headline);
        Assert.Equal("Nothing new (1 acknowledged or snoozed)", rows["KOR-101"].Headline);
        Assert.Equal("Never reached", rows["SPARE8"].Headline);
        Assert.Equal("Lenovo 30DH", rows["KOR-216"].Model);
    }

    [Fact]
    public void Filter_matches_name_model_or_problem_and_problems_only_hides_quiet_pcs()
    {
        var vm = Center();

        vm.FilterText = "opushutil";
        Assert.Equal(["KOR-216", "KOR-305"], vm.Fleet.Select(r => r.Name));

        vm.FilterText = "30DH";
        Assert.Equal(["KOR-208-N", "KOR-216", "KOR-305"], vm.Fleet.Select(r => r.Name));

        vm.FilterText = "";
        vm.ProblemsOnly = true;
        Assert.Equal(["KOR-208-N", "KOR-216", "KOR-305"], vm.Fleet.Select(r => r.Name));
    }

    [Fact]
    public void Tiles_count_the_fleet_and_a_silent_service_shows_red()
    {
        var vm = Center();
        Assert.Equal("1", vm.CriticalHeadline);
        Assert.Equal("2", vm.AttentionHeadline);
        Assert.Equal("1 / 5", vm.HealthyHeadline);
        Assert.Equal("1", vm.StaleHeadline);
        Assert.Equal("Running", vm.ServiceHeadline);

        var silent = Center(Fleet(lastBeat: Now.AddMinutes(-10)));
        Assert.Equal("Silent", silent.ServiceHeadline);
    }

    [Fact]
    public void Pattern_lists_the_pcs_it_is_about()
    {
        var p = Assert.Single(Center().Patterns);
        Assert.Equal("KOR-216, KOR-305", p.MembersText);
    }

    private static NetworkOpsDeviceViewModel Device(string name)
    {
        var s = Fleet();
        return new NetworkOpsDeviceViewModel(NetworkOpsClient.Unconfigured("test: no service"), s, s.Devices.Single(d => d.Name == name));
    }

    [Fact]
    public void A_pc_explains_its_first_finding_with_the_other_pcs_and_the_pattern()
    {
        var vm = Device("KOR-216");

        Assert.Equal("opushutil.exe keeps crashing", vm.SelectedFinding?.Title);
        Assert.Equal("Also open on 1 other PC(s): KOR-305", vm.ElsewhereText);
        Assert.Equal([Fleet().Patterns[0].Summary], vm.PatternTexts);
        Assert.False(string.IsNullOrWhiteSpace(vm.Meaning));
        Assert.NotEmpty(vm.KnownFixes);
        Assert.Contains("Nothing learned yet", Assert.Single(vm.LearnedFixes));
    }

    [Fact]
    public void A_pc_lists_critical_first_and_says_when_nothing_explains_it()
    {
        var vm = Device("KOR-208-N");

        Assert.Equal(["Disk 1 is reporting uncorrected read errors", "Not restarted in 21 days"], vm.OpenFindings.Select(f => f.Title));
        Assert.Equal("Only this PC has it right now.", vm.ElsewhereText);
        Assert.StartsWith("None:", Assert.Single(vm.PatternTexts));
    }

    [Fact]
    public void An_acknowledged_finding_says_who_and_sorts_after_live_ones()
    {
        var vm = Device("KOR-101");
        var row = Assert.Single(vm.OpenFindings);
        Assert.Equal("Acknowledged by ian", row.QuietText);
        Assert.True(vm.SelectedIsQuiet);
    }

    [Fact]
    public void Who_is_on_the_pc_shows_on_the_row_and_the_page_and_only_active_counts_as_in_use()
    {
        var vm = Center();
        Assert.Equal("kwurmlinger · active, idle 12 min", vm.Fleet.Single(r => r.Name == "KOR-208-N").Presence);
        Assert.Equal("", vm.Fleet.Single(r => r.Name == "SPARE8").Presence);

        Assert.Equal("On it at the last check: kwurmlinger · active, idle 12 min", Device("KOR-208-N").PresenceLine);
        Assert.True(Device("KOR-208-N").SomeoneActive);
        Assert.False(Device("KOR-216").SomeoneActive);   // locked: a restart needs no confirmation
        Assert.False(Device("KOR-305").SomeoneActive);   // remote-only: nobody at the keyboard
        Assert.Equal("", Device("SPARE8").PresenceLine);
    }

    [Fact]
    public void The_agent_line_says_which_route_checks_take()
    {
        Assert.StartsWith("Agent 1.0.0 · connected", Device("KOR-208-N").AgentLine);
        Assert.Contains("not connected, last heard 3 h", Device("KOR-216").AgentLine);
        Assert.StartsWith("No agent", Device("KOR-101").AgentLine);
        Assert.Equal("Reinstall agent", Device("KOR-208-N").AgentButtonText);
        Assert.Equal("Install agent", Device("KOR-101").AgentButtonText);
        Assert.False(Device("KOR-101").HasAgent);
    }

    [Fact]
    public void Connect_opens_the_devices_desktop_in_KOR_Remote_and_is_offered_only_with_a_mesh_agent()
    {
        var pc = Device("KOR-208-N");
        Assert.True(pc.CanConnect);
        // MeshCentral's own deep link: the node id without "node//", URL-encoded, straight onto the desktop tab.
        Assert.Equal("https://kor-mesh01.int.korstructural.com/?gotonode=bJ%40yhUBjIF4c8rS8MThrje0BENzyV5SCkrj4lZqrwPu4cOg3pq3Qj5YQwtqot2NX&viewmode=11", pc.ConnectUrl);
        Assert.StartsWith("Remote control: connected", pc.RemoteLine);
        Assert.False(pc.CanInstallRemote);

        var none = Device("KOR-101");
        Assert.False(none.CanConnect);
        Assert.True(none.CanInstallRemote);
        Assert.Equal("Remote control: not installed", none.RemoteLine);
    }

    [Fact]
    public void History_tabs_show_past_problems_changes_and_notes()
    {
        var vm = Device("KOR-216");
        vm.ApplyHistory(new DeviceHistory(
            [new ClearedFinding("gpu-hangs", Severity.Warning, "Graphics driver keeps resetting", "", Now.AddDays(-9), Now.AddDays(-7), "cleared after gpu.driver 1 → 2")],
            [
                new FactHistoryRow("gpu.driver", "1", Now.AddDays(-10), Now.AddDays(-7)),
                new FactHistoryRow("gpu.driver", "2", Now.AddDays(-7), null),
            ],
            [new NoteRow("ian@korstructural.com", Now.AddDays(-1), "Reseated the card")]));

        Assert.Equal("cleared after gpu.driver 1 → 2", Assert.Single(vm.Cleared).How);
        Assert.Equal("gpu.driver 1 → 2", Assert.Single(vm.Changes).Description);
        Assert.Equal("Reseated the card", Assert.Single(vm.Notes).Body);
    }

    [Fact]
    public async System.Threading.Tasks.Task Without_the_api_settings_the_page_says_what_is_missing()
    {
        var vm = new NetworkOpsCommandCenterViewModel(NetworkOpsClient.Unconfigured($"App.config is missing {NetworkOpsClient.BaseUrlKey}"));
        await vm.RefreshAsync(default);

        Assert.True(vm.IsConnectionLost);
        Assert.Contains(NetworkOpsClient.BaseUrlKey, vm.ConnectionLostMessage);
    }
}
