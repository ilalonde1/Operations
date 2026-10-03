#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.NetworkOps.Core.Network;

namespace Kor.Operations.App.NetworkOps;

/// <summary>
/// The Network window's content (Ian, 2026-10-02: "see which DEVICES by name (and user) ... see it clearly"), from the map
/// the service builds (Core/Network/NetworkMaps). No WPF here: what the left list holds, what a switch's ports read, and
/// what a search finds are decided once and tested on the real map of that evening.
/// </summary>
public sealed class NetworkOpsNetworkModel
{
    public NetworkOpsNetworkModel(NetworkMapResponse response)
    {
        Response = response;
        var map = response.Map;
        var all = map.Everything().ToList();
        Places =
        [
            .. map.Switches.Select(s => new NetworkPlace("switch", s.Mac, s.Name,
                $"{s.Model}{(s.Ip is { } ip ? " · " + ip : "")} · {s.Ports.Count(p => p.Kind == "device")} devices", s.Depth)),
            .. map.AccessPoints.Select(a => new NetworkPlace("ap", a.Mac, a.Name, $"{a.Model} · {a.Clients.Count} wireless", 1)),
            .. map.OtherWireless.Count > 0 ? [new NetworkPlace("other-wireless", "", "Other wireless", $"{map.OtherWireless.Count} on access points not listed", 1)] : Array.Empty<NetworkPlace>(),
            .. map.Unplaced.Count > 0 ? [new NetworkPlace("unplaced", "", "Unplaced", $"{map.Unplaced.Count} wired, port not recorded", 1)] : Array.Empty<NetworkPlace>(),
        ];
        Headline = $"{all.Count(p => p.Placement == "port")} devices on {map.Switches.Count} switches · {all.Count(p => p.Placement == "wireless")} wireless on {map.AccessPoints.Count} access points";
        var fleet = all.Where(p => p.Endpoint.Pc is not null).ToList();
        Subline = (response.BuiltUtc is { } b ? $"Read {b.ToLocalTime():yyyy-MM-dd HH:mm} from the UniFi controller, DC01's DHCP and NetworkOps' own checks" : "Not built yet")
                  + $" · {fleet.Count(p => p.Placement == "port")} PCs on a port, {fleet.Count(p => p.Endpoint.User is not null)} with their person"
                  + " · a date is when it last connected there, not that it is there now";
        Notes = response.Notes;
    }

    public NetworkMapResponse Response { get; }
    public IReadOnlyList<NetworkPlace> Places { get; }
    public string Headline { get; }
    public string Subline { get; }
    public IReadOnlyList<string> Notes { get; }

    /// <summary>A switch: every port, front-panel order -- empty ones too, so the page is the switch. An access point or a
    /// list: one row per device.</summary>
    public IReadOnlyList<NetworkRow> RowsOf(NetworkPlace place)
    {
        var map = Response.Map;
        switch (place.Kind)
        {
            case "switch":
                var s = map.Switches.First(x => x.Mac == place.Key);
                return s.Ports.Select(p => PortRow(p)).ToList();
            case "ap":
                return map.AccessPoints.First(a => a.Mac == place.Key).Clients.Select(c => EndpointRow(null, c, null)).ToList();
            case "other-wireless":
                return map.OtherWireless.Select(c => EndpointRow(null, c, c.Via)).ToList();
            default:
                return map.Unplaced.Select(c => EndpointRow(null, c, c.Via is { Length: > 0 } v ? $"last on {v}" : null)).ToList();
        }
    }

    /// <summary>Everything that matches -- a PC, a person, an IP, a MAC, a maker, a switch -- and where each is.</summary>
    public IReadOnlyList<NetworkRow> Search(string text)
    {
        var q = text.Trim();
        if (q.Length == 0) return [];
        return Response.Map.Everything()
            .Where(p => new[] { p.Endpoint.Name, p.Endpoint.User, p.Endpoint.Ip, p.Endpoint.Mac, p.Endpoint.Pc, p.Endpoint.Maker, p.Switch }
                .Any(x => x?.Contains(q, StringComparison.OrdinalIgnoreCase) == true))
            .OrderBy(p => p.Placement == "port" ? 0 : p.Placement == "wireless" ? 1 : p.Placement == "also-seen" ? 2 : 3)
            .ThenBy(p => p.Endpoint.Name, StringComparer.OrdinalIgnoreCase)
            .Select(p => EndpointRow(p.Port, p.Endpoint, p.Where))
            .ToList();
    }

    private static NetworkRow PortRow(NetPort p)
    {
        var also = p.AlsoSeen.Select(a => $"{a.Name}{(a.Ip is { } ip ? " (" + ip + ")" : "")}{(a.SeenUtc is { } t ? ", " + t.ToLocalTime().ToString("yyyy-MM-dd") : "")}").ToList();
        var alsoText = also.Count == 0 ? "" : "Also seen here: " + string.Join("; ", also.Take(8)) + (also.Count > 8 ? $"; +{also.Count - 8} more" : "");
        return p.Kind switch
        {
            "uplink" => new NetworkRow(p.Number, $"↑ {p.On?.Name ?? "the switch above"}", "", "", "", "link to the switch above", "", alsoText, "uplink"),
            "link" => new NetworkRow(p.Number, $"↓ {p.On?.Name}", "", p.On?.Ip ?? "", "", "link to the switch / access point below", p.On?.Mac ?? "", alsoText, "link"),
            "empty" when also.Count == 0 => new NetworkRow(p.Number, "—", "", "", "", "", "", "", "empty"),
            "empty" => new NetworkRow(p.Number, "nothing connected now", "", "", "", "", "", alsoText, "empty"),
            _ => EndpointRow(p.Number, p.On!, null) with { Also = alsoText },
        };
    }

    private static NetworkRow EndpointRow(int? port, NetEndpoint e, string? where)
        => new(port, e.Name,
            e.User is { } u ? Person(u) + (e.UserSource is "usual" or null ? "" : $" ({e.UserSource})") : "",
            e.Ip ?? "",
            // The column is "Last connected": a bare date is that; one the controller only recorded says "seen".
            e.SeenUtc is { } t ? $"{(e.SeenIsConnected ? "" : "seen ")}{t.ToLocalTime():yyyy-MM-dd}" : "",
            e.NameSource == "NetworkOps agent" ? "NetworkOps PC" : e.NameSource + (e.Maker is { } m && e.NameSource != "maker" ? $" · {m}" : ""),
            e.Mac, "", "device", where);

    /// <summary>"kor\markb" -> "markb": the domain is the same for everyone and only makes the column wider.</summary>
    public static string Person(string user) => user.Contains('\\') ? user[(user.IndexOf('\\') + 1)..] : user;
}

/// <param name="Kind">switch | ap | other-wireless | unplaced</param>
/// <param name="Depth">How far below the core: switches are indented under the switch they hang from.</param>
public sealed record NetworkPlace(string Kind, string Key, string Name, string Sub, int Depth)
{
    public System.Windows.Thickness Indent => new((Depth - 1) * 16, 0, 0, 0);
    public string Glyph => Kind switch { "switch" => "", "ap" => "", _ => "" };
}

/// <param name="Kind">device | link | uplink | empty -- the row's look.</param>
/// <param name="Where">In a search: the switch and port, or the access point.</param>
public sealed record NetworkRow(int? Port, string Device, string Person, string Ip, string When, string NamedBy, string Mac, string Also, string Kind, string? Where = null)
{
    public string PortText => Port is { } p ? p.ToString(System.Globalization.CultureInfo.InvariantCulture) : "";
    public bool HasAlso => Also.Length > 0;
    public bool IsDevice => Kind == "device";
}
