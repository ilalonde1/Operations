#nullable enable
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Network;

/// <summary>
/// Findings synthesised from the port map's live telemetry: a device whose switch port is in a bad or degraded state
/// (half-duplex, errors, low UniFi satisfaction, sub-gigabit on copper -- NetPort.Health). Owned by the map refresh
/// (Service/Network/NetworkMapService), so the health, rack and census sweeps never raise OR clear it.
/// </summary>
public static class NetworkFindings
{
    public const string LinkRule = "link-fault";

    /// <summary>The health/rack/census sweeps must leave this finding alone (they do not see the port telemetry).</summary>
    public static bool Owns(string ruleKey) => ruleKey == LinkRule;

    /// <summary>Per placed device, the link finding its port's synthesised health raises -- or nothing when the link is good,
    /// down (the device being offline is another finding's job) or has no live read.</summary>
    public static IEnumerable<(string Device, Finding Finding)> LinkFaults(NetworkMap map)
    {
        foreach (var pl in map.Everything().Where(p => p.Placement == "port" && p.Port is not null))
        {
            var sw = map.Switches.FirstOrDefault(s => s.Mac == pl.SwitchMac);
            var port = sw?.Ports.FirstOrDefault(x => x.Number == pl.Port);
            if (sw is null || port?.HealthReason is not { } reason) continue;
            var where = $"{sw.Name} port {port.Number}: {reason}";
            Finding? f = port.Health switch
            {
                "bad" => new Finding(LinkRule, Severity.Warning, "Network link fault", where),
                "suspect" => new Finding(LinkRule, Severity.Info, "Network link degraded", where),
                _ => null,   // "down" and "good" raise nothing here
            };
            if (f is not null) yield return (pl.Endpoint.Pc ?? pl.Endpoint.Name, f);
        }
    }
}
