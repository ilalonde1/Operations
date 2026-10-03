#nullable enable
using System.Globalization;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Power;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// What clicking a tile shows (Ian, 2026-10-02: "why don't the tiles within the device click anywhere? ... what does the
// clicking on it allow?"). A tile with a finding picks the finding; a healthy one shows "About this part" -- its Info rows.
//
// WHAT IT COVERS: EVERY tile built from EVERY fixture -- the 11 PC checks (probe v6 to v10), the rack's real readings of
// 2026-10-02, and each collector's rule run on its own fixture (ESXi x2, Synology x3, EdgeSwitch, UniFi, Windows server,
// Veeam, internet, UPS) so the facts are the real ones -- has rows, no row is blank, and no row is labelled with a raw
// metric key (a metric added without a label). Then the rows that answer the question on the parts that matter: the
// failing drive's model, errors and hours; a switch port's attached devices; a UniFi device's address and firmware; a
// host's model and version.
// WHAT IT DOES NOT: whether a row is the most useful thing to show, or how the panel looks (the render test draws it).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a new FACT key with no label shows under its raw key ("ntp.servers") -- facts
// are allowed through unlabelled on purpose, so they are never dropped; only metrics are held to a label.
public sealed class PartInfoTests
{
    private static string Fx(string dir, string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", dir, name));

    public static IEnumerable<object[]> PcChecks()
        => Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health"), "*.json").Select(f => new object[] { Path.GetFileName(f) });

    private static IReadOnlyList<PcComponent> Of(RackResult r)
        => RackComponents.Of(r.Facts, r.Metrics.Select(m => new DeviceReading(m.Metric, m.Subject, m.Value, DateTime.UtcNow)).ToList(),
            r.Findings.Select(f => (f.RuleKey, f.Severity)).ToList());

    private static readonly DateTime At = DateTime.Parse("2026-10-02T21:00:00Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);

    /// <summary>Every collector's real read, through its rule as the rack sweep runs it.</summary>
    private static readonly Dictionary<string, Func<RackResult>> Reads = BuildReads();

    public static IEnumerable<object[]> RackReads() => BuildReads().Keys.Select(k => new object[] { k });

    private static RackResult Read(string name) => Reads[name]();

    private static Dictionary<string, Func<RackResult>> BuildReads()
    {
        string[] production = ["Kor-DC01", "vcenter", "Kor-FS01", "Kor-APP01", "Kor-RDS01", "Kor-BK01", "KOR-UNIFI01"];
        var pings = new Dictionary<string, (int, int, double)> { ["1.1.1.1"] = (4, 4, 9.5), ["8.8.8.8"] = (4, 3, 14) };
        return new()
        {
            ["esxi-10"] = () => EsxiRules.Evaluate(Fx("rack", "esxi-10.json"), production, At),
            ["esxi-16"] = () => EsxiRules.Evaluate(Fx("rack", "esxi-16.json"), production, At),
            ["synology-12"] = () => SynologyRules.Evaluate(RackRulesTests.Walk(Fx("rack", "synology-12.txt")), 10),
            ["synology-15"] = () => SynologyRules.Evaluate(RackRulesTests.Walk(Fx("rack", "synology-15.txt")), 10),
            ["synology-105"] = () => SynologyRules.Evaluate(RackRulesTests.Walk(Fx("rack", "synology-105.txt")), 10),
            ["edgeswitch"] = () => EdgeSwitchRules.Evaluate(RackRulesTests.Walk(Fx("rack", "edgeswitch.txt")), new Dictionary<string, string>(), _ => "KOR-FS01 (192.168.1.20)"),
            ["unifi"] = () => UniFiRules.Evaluate(Fx("rack", "unifi.json")),
            ["server-app01"] = () => ServerRules.Evaluate(Fx("rack", "server-app01.json")),
            ["veeam"] = () => VeeamRules.Evaluate(Fx("rack", "veeam-jobs.json"), Fx("rack", "veeam-repos.json"), At),
            ["internet"] = () => InternetRules.Evaluate(new InternetCheck("184.71.160.54", "184.71.160.54", true, pings)),
            ["ups"] = () => UpsRules.Evaluate(new UpsReading("E", At, true, PowerSource.Mains, 0, 48, 100, 25, false, false, null)),
        };
    }

    private static void EveryTileHasRows(IReadOnlyList<PcComponent> tiles, string source)
    {
        foreach (var t in tiles)
        {
            Assert.True(t.Info is { Count: > 0 }, $"{source}: the {t.Kind} tile '{t.Title} / {t.Line1}' clicks to nothing");
            Assert.All(t.Info!, r =>
            {
                Assert.False(string.IsNullOrWhiteSpace(r.Label), $"{source}: a {t.Kind} row with no label");
                Assert.False(string.IsNullOrWhiteSpace(r.Value), $"{source}: the {t.Kind} row '{r.Label}' is blank");
                // A metric key shown as a label ("cpu.pct") is a metric added with no label in RackComponents.MetricLabels.
                Assert.False(System.Text.RegularExpressions.Regex.IsMatch(r.Label, @"^[a-z\-]+(\.[a-z\-]+)+$"), $"{source}: '{r.Label}' is a raw metric key -- give it a label");
            });
        }
    }

    [Theory]
    [MemberData(nameof(PcChecks))]
    public void Every_PC_tile_has_something_to_show(string file)
        => EveryTileHasRows(PcComponents.Of(HealthSnapshot.Parse(Fx("health", file)), []), file);   // KOR-213 (no inventory) has no tiles: its text line stays

    [Fact]
    public void Every_rack_tile_from_the_real_readings_has_something_to_show()
    {
        var all = JsonSerializer.Deserialize<Dictionary<string, List<DeviceReading>>>(Fx("rack", "readings-2026-10-02.json"), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        foreach (var (device, readings) in all)
            if (readings.Count > 0) EveryTileHasRows(RackComponents.Of(new Dictionary<string, string>(), readings, []), device);
    }

    [Theory]
    [MemberData(nameof(RackReads))]
    public void Every_tile_from_each_collectors_real_read_has_something_to_show(string read)
    {
        var tiles = Of(Read(read));
        Assert.NotEmpty(tiles);
        EveryTileHasRows(tiles, read);
    }

    [Fact]
    public void The_failing_drive_says_what_it_is_and_what_is_wrong()
    {
        var d = PcComponents.Of(HealthSnapshot.Parse(Fx("health", "KOR-208-N-v10-2026-10-02.json")), [])
            .Single(t => t.Kind == "drive" && t.Line2.StartsWith("ST2000DM006", StringComparison.Ordinal));
        var rows = d.Info!.ToDictionary(r => r.Label, r => r.Value);
        Assert.Equal("ST2000DM006-2DM164", rows["Model"]);
        Assert.Contains("hours", rows["Powered on"]);
        Assert.True(rows.ContainsKey("Read errors"), string.Join(" | ", d.Info!.Select(r => $"{r.Label}={r.Value}")));
        // Windows calls it Healthy; shown bare beside the errors that would read as "fine".
        Assert.Contains("say otherwise", rows["Windows says"]);
    }

    [Fact]
    public void A_switch_port_lists_each_device_on_it_and_a_UniFi_device_its_address_and_firmware()
    {
        // The real walk (edgeswitch.txt) predates the MAC table; SwitchPortsTests' walk has one: three devices on port 2.
        var port = Of(EdgeSwitchRules.Evaluate(SwitchPortsTests.Walk(), new Dictionary<string, string>(), m => m == "74:83:c2:0a:78:24" ? "UniFi USF5P 192.168.1.60" : null))
            .Single(t => t.Title == "Port 2");
        Assert.Equal(3, port.Info!.Count(r => r.Label == "Attached"));                                   // one row each, not one long line
        Assert.Contains(port.Info!, r => r.Label == "Attached" && r.Value == "UniFi USF5P 192.168.1.60");
        Assert.Contains(port.Info!, r => r.Label == "Interface" && r.Value.StartsWith("Slot: 0 Port: 2", StringComparison.Ordinal));

        var ap = Of(Read("unifi")).First(t => t.Kind == "unifi-device");
        Assert.Contains(ap.Info!, r => r.Label == "Address" && r.Value.StartsWith("192.168.", StringComparison.Ordinal));
        Assert.Contains(ap.Info!, r => r.Label == "Firmware");

        var host = Of(Read("esxi-10")).Single(t => t.Kind == "system");
        Assert.Contains(host.Info!, r => r.Label == "Model");
        Assert.Contains(host.Info!, r => r.Label == "ESXi");
    }
}
