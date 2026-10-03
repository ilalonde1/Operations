#nullable enable
using System.Text;

namespace Kor.Operations.NetworkOps.Core.Network;

/// <summary>What GET /api/network returns: the map, when it was built, and what the build could not read.</summary>
public sealed record NetworkMapResponse(DateTime? BuiltUtc, IReadOnlyList<string> Notes, NetworkMap Map);

/// <summary>
/// The port map as text (`netops network`): every switch, parent first and indented under it, each port that has something
/// on it -- the device, its person, its IP, where its name came from -- and the access points with their wireless devices.
/// With a search, only the lines that match (a PC, a person, an IP, a MAC, a switch) and where each is.
/// </summary>
public static class NetworkMapText
{
    public static string Render(NetworkMapResponse r, string? search = null)
    {
        var map = r.Map;
        var sb = new StringBuilder();
        var built = r.BuiltUtc is { } b ? $"built {b.ToLocalTime():yyyy-MM-dd HH:mm}" : "not built";
        var all = map.Everything().ToList();
        sb.AppendLine($"Port map, {built}: {map.Switches.Count} switches, {map.AccessPoints.Count} access points; " +
                      $"{all.Count(p => p.Placement == "port")} on a port, {all.Count(p => p.Placement == "wireless")} wireless, " +
                      $"{all.Count(p => p.Placement == "also-seen")} also seen, {all.Count(p => p.Placement == "unplaced")} unplaced");
        foreach (var n in r.Notes) sb.AppendLine("  note: " + n);

        if (search is { Length: > 0 } q)
        {
            var hits = all.Where(p => Matches(p.Endpoint, q) || (p.Switch?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false)).ToList();
            sb.AppendLine($"\"{q}\": {hits.Count} of {all.Count}");
            foreach (var h in hits) sb.AppendLine($"  {h.Where,-42} {Line(h.Endpoint)}");
            return sb.ToString();
        }

        foreach (var s in map.Switches)
        {
            var pad = new string(' ', (s.Depth - 1) * 2);
            sb.AppendLine();
            sb.AppendLine($"{pad}{s.Name}  ({s.Model}{(s.Ip is { } ip ? ", " + ip : "")})" +
                          (s.ParentName is { } pn ? $"  <- {pn}{(s.ParentPort is { } pp ? $" port {pp}" : "")}" : ""));
            foreach (var p in s.Ports.Where(p => p.On is not null || p.AlsoSeen.Count > 0))
            {
                var what = p.Kind switch
                {
                    "uplink" => $"up to {p.On?.Name ?? "the parent"}",
                    "link" => $"down to {p.On?.Name}",
                    _ => p.On is { } on ? Line(on) : "(nothing now)",
                };
                sb.AppendLine($"{pad}  {p.Number,3}  {what}");
                foreach (var a in p.AlsoSeen.Take(6)) sb.AppendLine($"{pad}         also seen: {Line(a)}");
                if (p.AlsoSeen.Count > 6) sb.AppendLine($"{pad}         also seen: +{p.AlsoSeen.Count - 6} more");
            }
        }
        foreach (var ap in map.AccessPoints)
        {
            sb.AppendLine();
            sb.AppendLine($"{ap.Name}  ({ap.Model}, wireless: {ap.Clients.Count})");
            foreach (var c in ap.Clients) sb.AppendLine($"       {Line(c)}");
        }
        if (map.Unplaced.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"Unplaced ({map.Unplaced.Count}): wired, but the controller did not record a switch port it manages");
            foreach (var u in map.Unplaced) sb.AppendLine($"       {Line(u)}{(u.Via is { Length: > 0 } v ? $"  (last on {v})" : "")}");
        }
        return sb.ToString();
    }

    /// <summary>"KOR-207 | kor\markb (usual) | 192.168.1.73 | seen 2026-09-29" -- and how it was named, when not by its agent.</summary>
    // " | ", not " · ": the middle dot printed as "?" in a Windows console (2026-10-02).
    public static string Line(NetEndpoint e)
        => string.Join(" | ", new[]
        {
            e.Name,
            e.User is { } u ? $"{u}{(e.UserSource is { } us ? $" ({us})" : "")}" : null,
            e.Ip,
            e.NameSource is "NetworkOps agent" ? null : $"named by {e.NameSource}{(e.Maker is { } m && e.NameSource != "maker" ? $", {m}" : "")}",
            e.SeenUtc is { } t ? $"{(e.SeenIsConnected ? "connected since" : "seen")} {t.ToLocalTime():yyyy-MM-dd}" : null,
            e.Mac,
        }.Where(x => !string.IsNullOrEmpty(x)));

    private static bool Matches(NetEndpoint e, string q)
        => new[] { e.Name, e.User, e.Ip, e.Mac, e.Pc, e.Maker }.Any(x => x?.Contains(q, StringComparison.OrdinalIgnoreCase) == true);
}
