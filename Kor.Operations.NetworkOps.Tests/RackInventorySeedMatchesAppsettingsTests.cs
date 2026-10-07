#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Moving the rack inventory out of appsettings.json and into SQL (014_RackInventory.sql) must change NOTHING about
// what NetworkOps monitors. The service loads the list from SQL and falls back to the appsettings "Rack" block when
// 014 has not run, so while BOTH exist they MUST name the same devices with the same reach-config -- otherwise the
// fallback (or the seed) silently monitors a different fleet than the other. This is that differential, on the two
// source-of-truth files, so seed drift fails the build instead of being found by someone noticing a device went dark.
//
// WHAT IT COVERS: the set of device Names; and per device its Collector, Address, MeshName, UpsName, CertSha256,
// VolumeFreeWarnPct (default 10) and each SSH HostKey -- every field RackInventoryAsync reconstructs.
// WHAT IT DOES NOT: the Ups[] SNMP cards and the PowerChain plan (still in appsettings by design, not moved); and it
// compares TEXT, not a live DB read, so it cannot catch a column that loads wrong at runtime (the loader's mapping
// is the second gate). When appsettings.Rack is eventually emptied because SQL is the only source, this test retires.
public sealed class RackInventorySeedMatchesAppsettingsTests
{
    private sealed record Dev(string Name, string Kind, string Collector, string Address, string MeshName, string UpsName, string CertSha256, int VolumeFreeWarnPct, string[] HostKeys);

    private static string RepoRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Kor.Operations.NetworkOps.Service"))) root = root.Parent;
        Assert.NotNull(root);
        return root!.FullName;
    }

    private static List<Dev> Appsettings(string root)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "Kor.Operations.NetworkOps.Service", "appsettings.json")));
        string S(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        return doc.RootElement.GetProperty("Rack").EnumerateArray().Select(e => new Dev(
            S(e, "Name"), S(e, "Kind"), S(e, "Collector"), S(e, "Address"), S(e, "MeshName"), S(e, "UpsName"), S(e, "CertSha256"),
            e.TryGetProperty("VolumeFreeWarnPct", out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 10,
            e.TryGetProperty("HostKeys", out var h) && h.ValueKind == JsonValueKind.Array ? h.EnumerateArray().Select(x => x.GetString() ?? "").ToArray() : [])
        ).ToList();
    }

    [Fact]
    public void The_014_seed_and_the_appsettings_Rack_block_name_the_same_devices_with_the_same_reach_config()
    {
        var root = RepoRoot();
        var sql = File.ReadAllText(Path.Combine(root, "db", "KorNetworkOps", "014_RackInventory.sql"));
        var app = Appsettings(root);
        Assert.NotEmpty(app);

        // Every device-value row of the main INSERT begins "(N'<Name>'," -- the host-key rows begin "(@unifi,", so this
        // is exactly the seeded devices. Names here carry no apostrophes, so [^'] is safe.
        var seededNames = Regex.Matches(sql, @"\(N'([^']+)',\s*'").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(seededNames.Count, seededNames.Distinct().Count());                 // no name seeded twice
        Assert.True(seededNames.ToHashSet().SetEquals(app.Select(d => d.Name)),
            "014 seeds a different set of device names than appsettings Rack:\n  only in SQL: " +
            string.Join(", ", seededNames.Except(app.Select(d => d.Name))) +
            "\n  only in appsettings: " + string.Join(", ", app.Select(d => d.Name).Except(seededNames)));

        foreach (var d in app)
        {
            // The device's own seed row: the line that opens with its name.
            var row = sql.Split('\n').FirstOrDefault(l => l.TrimStart().StartsWith($"(N'{d.Name}',"));
            Assert.True(row is not null, $"no seed row for '{d.Name}'");
            Assert.Contains($"'{d.Collector}'", row!);
            if (d.Address.Length > 0) Assert.Contains($"N'{d.Address}'", row!);
            if (d.MeshName.Length > 0) Assert.Contains($"N'{d.MeshName}'", row!);
            if (d.UpsName.Length > 0) Assert.Contains($"N'{d.UpsName}'", row!);
            if (d.CertSha256.Length > 0) Assert.Contains($"'{d.CertSha256}'", row!);

            // The row ends "..., <VolumeFreeWarnPct>, <SortOrder>)," (every row but the last) or ");" (the last) --
            // the first of the two trailing numbers is VolumeFreeWarnPct.
            var tail = Regex.Match(row!, @",\s*(-?\d+),\s*(-?\d+)\)\s*[;,]?\s*$");
            Assert.True(tail.Success, $"could not read the trailing numbers of '{d.Name}' row");
            Assert.Equal(d.VolumeFreeWarnPct, int.Parse(tail.Groups[1].Value));

            foreach (var k in d.HostKeys) Assert.Contains($"'{k}'", sql);
        }
    }
}
