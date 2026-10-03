#nullable enable
using System.Globalization;
using System.Text.RegularExpressions;

namespace Kor.Operations.NetworkOps.Core.Learning;

/// <summary>How a person (or a Claude session) names machines and moments to `netops`: the pure rules, so they are tested.</summary>
public static class HostNames
{
    /// <summary>
    /// The ONE way --hosts names machines, for every verb: a PC or rack device by name (a rack name matches up to its
    /// bracket: "KOR-FS01" is "KOR-FS01 (file server)"), "all" = every PC, "rack" = every rack device. `netops fix`
    /// resolved PCs only until 2026-10-02, so a server's updates had to be installed by posting to the API by hand.
    /// </summary>
    public static IReadOnlyList<DeviceRow> Resolve(IReadOnlyList<DeviceRow> pcs, IReadOnlyList<DeviceRow> rack, string hostsArg, out IReadOnlyList<string> unknown)
    {
        var missing = new List<string>();
        var found = new List<DeviceRow>();
        var everything = pcs.Concat(rack).ToList();
        foreach (var h in hostsArg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (h.Equals("all", StringComparison.OrdinalIgnoreCase)) { found.AddRange(pcs); continue; }
            if (h.Equals("rack", StringComparison.OrdinalIgnoreCase)) { found.AddRange(rack); continue; }
            var dev = everything.FirstOrDefault(d => d.Name.Equals(h, StringComparison.OrdinalIgnoreCase))
                      ?? everything.FirstOrDefault(d => d.Name.StartsWith(h + " (", StringComparison.OrdinalIgnoreCase));
            if (dev is null) missing.Add(h); else found.Add(dev);
        }
        unknown = missing;
        return found.DistinctBy(d => d.DeviceId).OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>"12h" / "3d" back from now, or a date/time read as local; null = unreadable. Nothing given: 24 hours back.</summary>
    public static DateTime? ParseSince(string? since, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(since)) return nowUtc.AddHours(-24);
        var m = Regex.Match(since.Trim(), "^(\\d+)\\s*([hd])$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var n = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            return m.Groups[2].Value.Equals("h", StringComparison.OrdinalIgnoreCase) ? nowUtc.AddHours(-n) : nowUtc.AddDays(-n);
        }
        return DateTime.TryParse(since, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var local) ? local.ToUniversalTime() : null;
    }
}
