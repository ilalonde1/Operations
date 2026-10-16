#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Api;
using Kor.Operations.NetworkOps.Service.Mesh;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The fleet is assembled ONE way (Service/Api/FleetView), and what it serves says how old it is.
//
// WHAT IT COVERS: no service source outside FleetView calls the store's raw fleet read or the agent/remote-control
// overlays, apart from the named id lookups that never reach a screen (the class: the MCP tools served the raw SQL --
// agentConnected=false on every PC, no remote control -- while the app served the overlaid one; audit 2026-10-16,
// tier 2); remote control is stamped with the time of the read it came from, live or stored; a UPS's rack row
// carries the power watcher's reading when that is fresher, so one screen never shows two readings of one UPS.
// WHAT IT DOES NOT: prove the endpoints wire FleetView in (a route that called the store directly by another method
// name would pass) -- the deployed /api/fleet, /api/to-clear and networkops_fleet answers are compared live after a
// deploy. A SAME-CLASS FAULT IT WOULD NOT CATCH: the APP rebuilding the fleet itself from two reads (it reads /api/fleet
// and /api/rack and joins them) -- this test only reads the service.
public sealed class FleetAssemblyTests
{
    private static readonly DateTime Now = new(2026, 10, 16, 18, 0, 0, DateTimeKind.Utc);

    // Reads that look a device up by name or id and never show the fleet to anyone.
    private static readonly string[] IdLookups = ["UpdateScanner.cs", "MacDirectory.cs"];

    [Fact]
    public void Nothing_outside_FleetView_assembles_the_fleet()
    {
        var service = Path.Combine(RepoRoot(), "Kor.Operations.NetworkOps.Service");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(service, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var name = Path.GetFileName(file);
            if (name is "FleetView.cs" or "NetworkOpsStore.CommandCenter.cs") continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var raw = Regex.IsMatch(lines[i], @"\bFleetSnapshotAsync\(") && !IdLookups.Contains(name);
                var overlay = Regex.IsMatch(lines[i], @"\b(WithAgents|WithMesh|WithUps)\(");
                if (raw || overlay) offenders.Add($"{name}:{i + 1}: {lines[i].Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "the fleet is assembled outside FleetView:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void Remote_control_says_how_old_it_is_live_or_stored()
    {
        var fleet = new FleetSnapshot([new DeviceRow(1, "KOR-302N", Now, Now)], new Dictionary<string, IReadOnlyDictionary<string, string>>(), [], [], null);
        var clock = new FakeTimeProvider(new DateTimeOffset(Now));
        var live = new MeshState(clock);
        var stored = new Dictionary<int, NetworkOpsStore.MeshRecord> { [1] = new("node//old", false, Now.AddDays(-2), Now.AddHours(-3)) };

        // Just after a restart: nothing live, so the stored picture -- and its own time, three hours old.
        var cold = FleetView.WithMesh(fleet, live, stored);
        Assert.Equal(Now.AddHours(-3), cold.MeshReadUtc);
        Assert.False(cold.Devices[0].MeshConnected);

        // A live read: its time, and its answer.
        live.Update([new MeshLink(1, "KOR-302N", new MeshNode("node//x", "KOR-302N", "mesh//PCS", 1), "KOR PCs")]);
        var warm = FleetView.WithMesh(fleet, live, stored);
        Assert.Equal(Now, warm.MeshReadUtc);
        Assert.True(warm.Devices[0].MeshConnected);

        // Never read at all: no time, never a made-up one.
        Assert.Null(FleetView.WithMesh(fleet, new MeshState(clock), new Dictionary<int, NetworkOpsStore.MeshRecord>()).MeshReadUtc);
    }

    [Fact]
    public void A_ups_row_carries_the_fresher_reading_so_one_screen_shows_one()
    {
        var rack = new FleetSnapshot(
            [new DeviceRow(7, "Eaton 5PX UPS", Now.AddMinutes(-4), Now.AddMinutes(-4), "UPS", "Mains · 48 min · load 24%"),
             new DeviceRow(8, "APC SRT1500 UPS", Now.AddMinutes(-4), Now.AddMinutes(-4), "UPS", "Mains · 30 min · load 40%")],
            new Dictionary<string, IReadOnlyDictionary<string, string>>(), [], [], null);
        var config = new List<RackDevice>
        {
            new() { Name = "Eaton 5PX UPS", Kind = "UPS", Collector = "Ups", UpsName = "Eaton 5PX" },
            new() { Name = "APC SRT1500 UPS", Kind = "UPS", Collector = "Ups", UpsName = "APC SRT1500" },
        };
        var live = new List<UpsRow>
        {
            new("Eaton 5PX", "192.168.1.44", Now.AddSeconds(-20), true, "Mains", null, 52, 100, 23, false, false, null),
            new("APC SRT1500", "192.168.1.45", Now.AddSeconds(-20), false, "Unknown", null, null, null, null, false, false, "timeout"),
        };

        var rows = FleetView.WithUps(rack, live, config).Devices.ToDictionary(d => d.Name);

        Assert.Equal("Mains · 52 min · load 23%", rows["Eaton 5PX UPS"].Summary);        // the Power panel's numbers
        Assert.Equal(Now.AddSeconds(-20), rows["Eaton 5PX UPS"].LastCheckedUtc);          // and its time
        Assert.Equal("Mains · 30 min · load 40%", rows["APC SRT1500 UPS"].Summary);      // an unreachable live read changes nothing
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "Kor.Operations.NetworkOps.Agent"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("repo root not found above " + AppContext.BaseDirectory);
    }
}
