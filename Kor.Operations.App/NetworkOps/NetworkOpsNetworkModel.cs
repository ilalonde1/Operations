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
            // "Connected": now / not now (the live read), else the date it last connected (or was only "seen" by the controller).
            e.ConnectedNow switch
            {
                true => "now" + (e.SeenUtc is { } ts && e.SeenIsConnected ? $" (since {ts.ToLocalTime():yyyy-MM-dd})" : ""),
                false => "not now" + (e.SeenUtc is { } tl ? $" · last {tl.ToLocalTime():yyyy-MM-dd}" : ""),
                null => e.SeenUtc is { } t ? $"{(e.SeenIsConnected ? "" : "seen ")}{t.ToLocalTime():yyyy-MM-dd}" : "",
            },
            e.NameSource == "NetworkOps agent" ? "NetworkOps PC" : e.NameSource + (e.Maker is { } m && e.NameSource != "maker" ? $" · {m}" : ""),
            e.Mac, "", "device", where);

    /// <summary>"kor\markb" -> "markb": the domain is the same for everyone and only makes the column wider.</summary>
    public static string Person(string user) => user.Contains('\\') ? user[(user.IndexOf('\\') + 1)..] : user;

    /// <summary>
    /// The switches as panels of port tiles (Ian, 2026-10-02: "shows me the switches in small cards as ports with important
    /// info displayed and it's clickable which brings you to the device page"): tree order, every port, front-panel order.
    /// </summary>
    public IReadOnlyList<SwitchPanel> Panels => Response.Map.Switches.Select(s => new SwitchPanel(s.Mac, s.Name,
        $"{s.Model}{(s.Ip is { } ip ? " · " + ip : "")}" +
        (s.ParentName is { } pn ? $" · hangs from {pn}{(s.ParentPort is { } pp ? $" port {pp}" : "")}" : "") +
        $" · {s.Ports.Count(p => p.Kind == "device")} devices" + (s.Ports.Any(p => p.Up is not null) ? $", {s.Ports.Count(p => p.Up == true)} of {s.Ports.Count} ports up" : ""),
        s.Depth, s.Ports.Select(p => Card(s, p)).ToList())).ToList();

    private static PortCard Card(NetSwitch s, NetPort p)
    {
        var row = PortRow(p);
        var speed = p.SpeedNow is { } sp ? sp >= 1000 ? $"{sp / 1000}G" : $"{sp}M" : "";
        var (state, title, sub) = p.Kind switch
        {
            "uplink" => ("link", $"↑ {p.On?.Name ?? "up"}", "to the switch above"),
            "link" => ("link", $"↓ {p.On?.Name}", "to the switch below"),
            "empty" => (p.Up == true ? "on" : "empty", p.AlsoSeen.Count > 0 ? "nothing now" : "", ""),
            _ => (p.On!.ConnectedNow switch { true => "on", false => "off", null => "known" },
                  p.On.Name, p.On.User is { } u ? Person(u) : p.On.Ip ?? ""),
        };
        var tip = string.Join("\n", new[]
        {
            $"{s.Name} port {p.Number}" + (speed.Length > 0 ? $" · {speed}" : "") + (p.Up is { } up ? (up ? " · link up" : " · link down") : ""),
            row.Device is { Length: > 0 } d && d != "—" ? d : null,
            row.Person is { Length: > 0 } pe ? "Person: " + pe : null,
            row.Ip is { Length: > 0 } i ? "IP: " + i : null,
            p.On?.ConnectedNow is { } now ? (now ? "Connected now" : "Not connected now") + (row.When is { Length: > 0 } w ? $" (last {w})" : "")
                : row.When is { Length: > 0 } w2 ? "Last connected " + w2 : null,
            p.Module is { } m ? "Module: " + m : null,
            p.PoeWatts is { } wt ? $"PoE: {wt:0.#} W" : null,
            row.Also is { Length: > 0 } a ? a : null,
        }.Where(x => x is not null));
        return new PortCard(p.Number, title, sub, speed, state, tip,
            p.Kind == "device" ? p.On!.Pc ?? p.On.Name : null,
            p.Kind is "link" ? p.On?.Mac : p.Kind == "uplink" ? s.ParentMac : null,
            row with { Where = $"{s.Name} port {p.Number}" });
    }
}

/// <summary>One switch's panel: its title line and every port as a tile.</summary>
public sealed record SwitchPanel(string Mac, string Name, string Sub, int Depth, IReadOnlyList<PortCard> Ports)
{
    public System.Windows.Thickness Indent => new((Depth - 1) * 24, 0, 0, 12);
}

/// <summary>One port tile.</summary>
/// <param name="State">on (connected now) | off (plugged in, not connected now) | known (no live read) | link | empty</param>
/// <param name="OpenName">The device whose NetworkOps page a click opens (a fleet PC, a rack device), when it has one.</param>
/// <param name="GoToSwitch">A link tile: the switch it leads to (its panel is scrolled to).</param>
public sealed record PortCard(int Number, string Title, string Sub, string Speed, string State, string Tip, string? OpenName, string? GoToSwitch, NetworkRow Row)
{
    private static readonly System.Windows.Media.Brush On = Frozen(0x22, 0x8B, 0x22), Off = Frozen(0xE5, 0xA8, 0x00),
        Known = Frozen(0x5B, 0x7A, 0x99), Link = Frozen(0x60, 0x9B, 0xD1), Empty = Frozen(0xD5, 0xDA, 0xDF);
    public System.Windows.Media.Brush Stripe => State switch { "on" => On, "off" => Off, "known" => Known, "link" => Link, _ => Empty };
    public bool IsEmpty => State == "empty" && Title.Length == 0;
    public double Fade => IsEmpty ? 0.55 : 1.0;
    private static System.Windows.Media.Brush Frozen(byte r, byte g, byte b) { var x = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b)); x.Freeze(); return x; }
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
