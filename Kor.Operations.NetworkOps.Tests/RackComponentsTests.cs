#nullable enable
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The rack's tiles (Ian, 2026-10-02: "the whole point of this is that it collates and manages EVERYTHING on a network"),
// from every rack device's REAL latest readings that evening (Fixtures/rack/readings-2026-10-02.json, `netops readings
// --hosts rack`) and the findings open on them then.
//
// WHAT IT COVERS: each kind of device gets the tiles its readings support (hosts, NAS, switch, UniFi, internet, UPS, Veeam,
// Windows servers); the words on them; a device with no readings gets none (it keeps its text line); each rack finding
// family colours the part it is about and nothing else -- a Veeam job, a datastore, a NAS disk, a server drive.
// WHAT IT DOES NOT: what a collector does not read (the Netgate itself, each UniFi device, a NAS disk's SMART detail) --
// those are collection, not drawing; or how the tiles look (the render test).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a new rack finding family not mapped here colours no tile (it stays in the list).
public sealed class RackComponentsTests
{
    private static readonly Dictionary<string, List<DeviceReading>> Readings = Load();

    private static Dictionary<string, List<DeviceReading>> Load()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rack", "readings-2026-10-02.json"));
        return JsonSerializer.Deserialize<Dictionary<string, List<DeviceReading>>>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
    }

    private static IReadOnlyList<PcComponent> Tiles(string device, Dictionary<string, string>? facts = null, params (string, Severity)[] open)
        => RackComponents.Of(facts ?? [], Readings[device], open);

    [Fact]
    public void An_ESXi_host_shows_its_load_its_VMs_and_each_datastore()
    {
        var t = Tiles("ESXi host .10 (production)", new() { ["hw.model"] = "Lenovo ThinkSystem SR650", ["esxi.version"] = "VMware ESXi 7.0.2" });
        Assert.Equal(["system", "uptime", "cpu", "memory", "vms", "datastore", "datastore"], t.Select(x => x.Kind));
        Assert.All(t.Where(x => x.Kind is "cpu" or "memory" or "datastore"), x => Assert.InRange(x.FillPct!.Value, 0, 100));
        Assert.EndsWith("running", t.Single(x => x.Kind == "vms").Line1);
    }

    [Fact]
    public void A_NAS_shows_its_volume_and_its_disks_and_a_bad_disk_colours_the_disks()
    {
        var t = Tiles("UC3200 SAN", null, ("syno.disk:Disk 7", Severity.Critical));
        Assert.Equal("12 disks", t.Single(x => x.Kind == "disks").Line1);
        Assert.StartsWith("hottest ", t.Single(x => x.Kind == "disks").Line2);
        Assert.Equal((Severity?)Severity.Critical, t.Single(x => x.Kind == "disks").Worst);
        Assert.Null(t.Single(x => x.Kind == "volume").Worst);
    }

    [Fact]
    public void The_switch_shows_its_ports_and_a_port_going_down_colours_them()
    {
        var t = Tiles("Core switch (EdgeSwitch 10G)", new() { ["fw.version"] = "1.8.1" }, ("switch.port-down:0/5", Severity.Warning));
        var ports = t.Single(x => x.Kind == "ports");
        // 16 front-panel ports, 9 up. This read (before 2026-10-02's anchor) still holds the CPU interface: it is no port, so
        // not "10 of 17", no 17th light, and no "Port 0" tile (the render showed all three).
        Assert.Equal("9 of 16 up", ports.Line1);
        Assert.Equal(16, ports.Lights!.Count);
        Assert.Equal(16, ports.Info!.Count);
        Assert.Equal(9, t.Count(x => x.Kind == "port"));
        Assert.DoesNotContain(t, x => x.Title == "Port 0");
        Assert.Equal((Severity?)Severity.Warning, ports.Worst);
    }

    [Fact]
    public void UniFi_shows_devices_online_and_an_offline_device_colours_them()
    {
        var t = Tiles("UniFi network", null, ("unifi.offline:f4:92:bf:ae:1f:23", Severity.Warning));
        var devices = t.Single(x => x.Kind == "devices");
        Assert.Matches(@"^\d+ of \d+ online$", devices.Line1);
        Assert.Equal((Severity?)Severity.Warning, devices.Worst);
        Assert.Contains(t, x => x.Kind == "alarms");
    }

    [Fact]
    public void Every_UniFi_device_is_a_tile_offline_first_coloured_by_its_finding()
    {
        // The controller's real read (Fixtures/rack/unifi.json), through the rule exactly as the rack sweep runs it.
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "rack", "unifi.json"));
        var read = UniFiRules.Evaluate(json);
        var readings = read.Metrics.Select(m => new DeviceReading(m.Metric, m.Subject, m.Value, DateTime.UtcNow)).ToList();
        var open = read.Findings.Select(f => (f.RuleKey, f.Severity)).ToList();
        var tiles = RackComponents.Of(read.Facts, readings, open).Where(t => t.Kind == "unifi-device").ToList();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(doc.RootElement.GetProperty("devices").GetArrayLength(), tiles.Count);              // every device, none lost
        var offline = read.Findings.Count(f => f.RuleKey.StartsWith("unifi.offline:", StringComparison.Ordinal));
        Assert.All(tiles.Take(offline), t => Assert.NotNull(t.Worst));                                    // offline ones first, coloured
        Assert.All(tiles.Skip(offline), t => Assert.StartsWith("online", t.Line2));
        Assert.Contains(tiles, t => t.Title == "Access point" && t.Line1 == "BMZ-AP01 [Outside Boardroom]");
        // Each opens the ONE place its ports / wireless are shown -- the Network window at its MAC -- not a copy here
        // (Ian, 2026-10-02: "duplicate ways to get into the same data. No data duplication").
        Assert.All(tiles, t => Assert.Matches("^network:([0-9a-f]{2}:){5}[0-9a-f]{2}$", t.Opens));
        Assert.Equal("network:", RackComponents.Of(read.Facts, readings, open).Single(t => t.Kind == "devices").Opens);
    }

    [Fact]
    public void A_UniFi_device_fact_always_fits_its_column_and_parses()
    {
        var name = new string('x', 300);
        var json = $$"""{"site":"S","now":1000,"openAlarms":[],"devices":[{"name":"{{name}}","model":"U7PG2","type":"uap","ip":"192.168.1.9","mac":"aa:bb","version":"6.8.2","adopted":true,"upgradable":true,"upgradeTo":"6.9","lastSeen":990,"ports":2}]}""";
        var fact = UniFiRules.Evaluate(json).Facts["unifi.device:aa:bb"];
        Assert.True(fact.Length <= 400, $"{fact.Length} characters: the store would cut it, and cut JSON is unreadable");
        Assert.Equal(80, UniFiDevice.Parse(fact)!.Name.Length);
        Assert.Null(UniFiDevice.Parse("{\"Name\":\"cut"));
    }

    [Fact]
    public void The_internet_shows_the_worst_target()
    {
        var readings = Readings["Internet (Netgate + Shaw)"];
        var t = Tiles("Internet (Netgate + Shaw)", new() { ["wan.public-ip"] = "184.71.160.54" });
        var tile = t.Single(x => x.Kind == "internet");
        Assert.Equal($"{readings.Where(r => r.Metric == "ping.ms").Max(r => r.Value):0} ms", tile.Line1);
        Assert.EndsWith("· 184.71.160.54", tile.Line2);
    }

    [Fact]
    public void Veeam_shows_each_job_with_its_result_and_a_warning_colours_only_that_job()
    {
        var jobs = Readings["Veeam backups (BK01)"].Where(r => r.Metric == "job.age.hours").Select(r => r.Subject).ToList();
        var t = Tiles("Veeam backups (BK01)", jobs.ToDictionary(j => $"veeam.result:{j}", _ => "Warning"), ($"veeam.warning:{jobs[0]}", Severity.Warning));
        var jobTiles = t.Where(x => x.Kind == "job").ToList();
        Assert.Equal(jobs.Count, jobTiles.Count);
        Assert.StartsWith("Warning · ", jobTiles[0].Line2);
        Assert.Single(jobTiles, x => x.Worst is not null);
        Assert.Equal(2, t.Count(x => x.Kind == "repo"));
    }

    [Fact]
    public void A_Windows_server_shows_each_drive_and_its_updates()
    {
        var t = Tiles("KOR-FS01 (file server)", new() { ["os.caption"] = "Microsoft Windows Server 2025 Standard" }, ("server.unpatched", Severity.Warning));
        Assert.Equal(2, t.Count(x => x.Kind == "drive"));
        Assert.Equal((Severity?)Severity.Warning, t.Single(x => x.Kind == "updates").Worst);
    }

    [Fact]
    public void A_UPS_shows_charge_runtime_and_load()
        => Assert.Equal(["charge", "runtime", "load"], Tiles("APC SRT1500 UPS").Select(x => x.Kind));

    [Fact]
    public void A_device_with_no_readings_gets_no_tiles()
        => Assert.Empty(Tiles("KOR-MESH01 (remote control, MeshCentral)"));

    [Fact]
    public void Every_rack_device_read_that_evening_draws()
    {
        foreach (var (name, _) in Readings)
        {
            var t = Tiles(name);
            Assert.All(t, x => Assert.False(string.IsNullOrWhiteSpace(x.Line1), $"{name}: a {x.Kind} tile says nothing"));
        }
        Assert.Equal(15, Readings.Count(kv => Tiles(kv.Key).Count > 0));   // 16 devices, MESH01 has no readings yet
    }
}
