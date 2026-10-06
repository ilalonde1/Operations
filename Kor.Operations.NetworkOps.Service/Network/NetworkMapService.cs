#nullable enable
using System.Net;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Network;
using Kor.Operations.NetworkOps.Service.Rack;
using Kor.Operations.NetworkOps.Service.Store;
using Kor.Operations.NetworkOps.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Network;

/// <summary>
/// Builds the port map (Core/Network/NetworkMaps: the rules) after every rack sweep that read the UniFi controller, from:
///   the controller's read          kept by RackCollector as it reads it (no second SSH session)
///   the fleet's network cards      each PC's last health check, and its usual person from 30 days of checks
///   DC01's DHCP leases             Probes/dhcp-leases.ps1 on the DHCP server, through the same channel as the server probe
///   names APP01 already knows      MacDirectory: ARP + rack addresses + reverse DNS (the core switch, servers, the firewall)
/// The fleet and the leases change slowly and cost a query and a remote run: read at most every 30 minutes. The map is
/// served live (GET /api/network) and written to NetworkPlacements / NetworkMoves (010) for history and documentation.
/// </summary>
internal sealed class NetworkMapService(NetworkOpsStore store, MacDirectory macs, IOptions<NetworkOpsOptions> options, ILogger<NetworkMapService> log)
{
    private static readonly TimeSpan SlowEvery = TimeSpan.FromMinutes(30);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _unifiJson;
    private IReadOnlyList<DhcpLease> _leases = [];
    private DateTime _leasesUtc;
    private string? _leasesProblem;
    private IReadOnlyList<FleetPc> _fleet = [];
    private DateTime _fleetUtc;

    // The published map, its build time and its notes are ONE immutable value, swapped in a single volatile write, so a
    // reader (GET /api/network) never sees a fresh map stamped with a stale time or notes that describe a different build.
    private sealed record Snapshot(NetworkMap? Map, DateTime? BuiltUtc, IReadOnlyList<string> Notes);
    private volatile Snapshot _published = new(null, null, []);
    public NetworkMap? Current => _published.Map;
    public DateTime? BuiltUtc => _published.BuiltUtc;
    /// <summary>What the last build could not read (leases, history), said with the map rather than hidden.</summary>
    public IReadOnlyList<string> Notes => _published.Notes;

    /// <summary>The map, its build time and its notes as ONE atomic read. A caller MUST use this rather than reading
    /// Current/BuiltUtc/Notes separately, which could tear across a publish (2026-10-03 re-audit). Null until first built.</summary>
    public NetworkMapResponse? Response()
    {
        var s = _published;
        if (s.Map is not { } map) return null;
        // The firewall rides alongside the map (it is not part of the port graph). Its own read, kept fresh like the
        // core's: a read older than 15 minutes is not presented as "now" -- the panel is left out rather than stale.
        var fw = _firewall;
        var firewall = fw is not null && DateTime.UtcNow - fw.ReadUtc < TimeSpan.FromMinutes(15) ? fw : null;
        return new NetworkMapResponse(s.BuiltUtc, s.Notes, map, firewall);
    }

    /// <summary>The controller's read, as RackCollector got it.</summary>
    public void SetUniFi(string json) => _unifiJson = json;

    // The live read is one immutable value (json, why-not, when): set and read together, never torn, and stamped so a
    // stale read is not presented as "now".
    private sealed record LiveState(string? Json, string? Problem, DateTime Utc);
    private volatile LiveState _live = new(null, null, default);

    private volatile CoreSwitchRead? _core;

    /// <summary>The core switch's own read (Rack/RackCollector, SNMP): its panel in the map.</summary>
    public void SetCore(CoreSwitchRead core) => _core = core;

    private volatile FirewallRead? _firewall;

    /// <summary>The firewall's own read (Rack/RackCollector, SNMP v2c): its panel alongside the map.</summary>
    public void SetFirewall(FirewallRead firewall) => _firewall = firewall;

    /// <summary>The controller's live API read (Rack/UniFiApi), or null and why not: the map then has no "now" in it.</summary>
    public void SetLive(string? json, string? problem) => _live = new(json, problem, DateTime.UtcNow);

    public async Task<string> RefreshAsync(CancellationToken ct)
    {
        if (_unifiJson is not { } json) return "port map: no UniFi read yet";
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;
            var notes = new List<string>();
            if (now - _fleetUtc > SlowEvery)
            {
                try { _fleet = await store.FleetForMapAsync(30, now, ct).ConfigureAwait(false); _fleetUtc = now; }
                catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Port map: the fleet could not be read"); notes.Add("the fleet's network cards could not be read: " + ex.Message); }
            }
            if (now - _leasesUtc > SlowEvery) await ReadLeasesAsync(now, ct).ConfigureAwait(false);
            if (_leasesProblem is { } lp) notes.Add(lp);

            var site = UniFiSite.Parse(json);
            var label = await macs.LabellerAsync(ct).ConfigureAwait(false);
            var byMac = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mac in site.Clients.Select(c => c.Mac).Concat(site.Devices.SelectMany(d => d.PortTable.Select(p => p.LastMac).Concat([d.Uplink?.Mac]))).OfType<string>().Distinct())
                if (label(mac) is { } l && !l.StartsWith("UniFi ", StringComparison.Ordinal) && !IPAddress.TryParse(l, out _))
                    byMac[mac] = System.Text.RegularExpressions.Regex.Replace(l, @"\s*\(\d{1,3}(\.\d{1,3}){3}\)$", "");   // the IP is its own column
            var byIp = options.Value.Rack.Where(r => IPAddress.TryParse(r.Address, out _)).GroupBy(r => r.Address).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

            UniFiLive? live = null;
            var liveNow = _live;   // the immutable triple, read once
            if (liveNow.Json is { } lj)
            {
                // Mirror the core's 15-minute rule: a live read older than that is not presented as "connected now".
                if (now - liveNow.Utc >= TimeSpan.FromMinutes(15))
                    notes.Add($"the UniFi live read is {(now - liveNow.Utc).TotalMinutes:0} min old -- no \"connected now\" in this map");
                else
                {
                    // Any schema failure (bad JSON, or a numeric field the shape did not expect) degrades the live source to a
                    // note -- it must never throw out of the whole refresh and freeze the map (2026-10-03 re-audit, finding 6).
                    try { live = UniFiLive.Parse(lj); }
                    catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException or OverflowException or InvalidOperationException)
                    { notes.Add("the UniFi live read could not be parsed: " + ex.Message); }
                }
            }
            else if (liveNow.Problem is { } why) notes.Add(why + " -- no \"connected now\" in this map");
            // The core's read is used only while fresh: a switch not read for 15 minutes is not drawn as if it were live.
            var core = _core is { } c && now - c.ReadUtc < TimeSpan.FromMinutes(15) ? c : null;
            if (core is null) notes.Add("the core switch has not been read in the last 15 minutes -- its panel is left out");
            var map = NetworkMaps.Build(site, _fleet, _leases, new KnownNames(byMac, byIp), live, core);
            _published = new Snapshot(map, now, notes);
            var (saved, moves) = await store.SaveNetworkMapAsync(map, now, ct).ConfigureAwait(false);
            var faults = await ApplyLinkFindingsAsync(map, now, ct).ConfigureAwait(false);
            var placed = map.Everything().ToList();
            return $"port map: {placed.Count(p => p.Placement == "port")} on a port, {placed.Count(p => p.Placement == "wireless")} wireless, " +
                   $"{_fleet.Count(f => map.PortOf(f.Name) is not null)} of {_fleet.Count(f => f.Macs.Count > 0)} fleet PCs placed" +
                   (saved ? (moves > 0 ? $", {moves} moved" : "") : " (not stored: run 010_NetworkMap.sql)") +
                   (faults > 0 ? $", {faults} link fault(s)" : "") +
                   (notes.Count > 0 ? "; " + string.Join("; ", notes) : "");
        }
        finally { _gate.Release(); }
    }

    /// <summary>Raise or clear the "link-fault" finding on each device from its port's synthesised health (NetworkFindings).
    /// Owned here -- the sweeps never touch that rule. Bounded to the devices faulty now plus those the STORE holds open:
    /// it once cleared from an in-memory "last raised" set, which a restart empties, so 23 findings raised by 0.32's
    /// dropped-packet rule were never revisited after 0.33.1 retired it (2026-10-05).</summary>
    private async Task<int> ApplyLinkFindingsAsync(NetworkMap map, DateTime now, CancellationToken ct)
    {
        var raised = new List<(int DeviceId, Core.Health.Finding Finding)>();
        foreach (var (name, f) in Core.Network.NetworkFindings.LinkFaults(map))
        {
            try
            {
                if (await store.DeviceByNameAsync(name, ct).ConfigureAwait(false) is { } dev) raised.Add((dev.DeviceId, f));
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Port map: {Device} could not be looked up for its link finding", name); }
        }
        var openInStore = await store.DevicesWithOpenFindingAsync(Core.Network.NetworkFindings.LinkRule, ct).ConfigureAwait(false);
        foreach (var (deviceId, finding) in LinkFindingTargets(raised, openInStore))
        {
            try
            {
                var open = (await store.OpenFindingsAsync(deviceId, null, ct).ConfigureAwait(false)).Where(f => f.RuleKey == Core.Network.NetworkFindings.LinkRule).ToList();
                var changes = Core.Health.FindingDiff.Compute(open, finding is null ? [] : [finding]);
                if (changes.Count > 0) await store.ApplyChangesAsync(deviceId, changes, now, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { log.LogWarning(ex, "Port map: the link finding for device {DeviceId} could not be applied", deviceId); }
        }
        return raised.Select(r => r.DeviceId).Distinct().Count();
    }

    /// <summary>Which devices to diff, and what each raises now (null = nothing, so an open one clears). Keyed by DeviceId,
    /// not name: a rack device is "NAS01" on the map and "NAS01 (Veeam repository)" in the store, and diffing it once per
    /// name would clear under one name what the other just raised.</summary>
    internal static IReadOnlyList<(int DeviceId, Core.Health.Finding? Finding)> LinkFindingTargets(
        IEnumerable<(int DeviceId, Core.Health.Finding Finding)> raisedNow, IEnumerable<int> openInStore)
    {
        var targets = new Dictionary<int, Core.Health.Finding?>();
        foreach (var (id, f) in raisedNow) targets.TryAdd(id, f);
        foreach (var id in openInStore) targets.TryAdd(id, null);
        return targets.Select(t => (t.Key, t.Value)).ToList();
    }

    private async Task ReadLeasesAsync(DateTime now, CancellationToken ct)
    {
        var server = options.Value.DhcpServer;
        if (string.IsNullOrWhiteSpace(server)) { _leasesProblem = "no DHCP server configured (NetworkOps:DhcpServer)"; return; }
        var run = await new OnTargetChannel(TimeSpan.FromSeconds(90)).RunAsync(server, Core.Probes.ProbeLibrary.Get("dhcp-leases"), ct).ConfigureAwait(false);
        if (run.Status != OnTargetStatus.Ok || run.OutputJson is not { } json)
        {
            _leasesProblem = $"DHCP leases from {server} not read ({run.Status}: {run.Error}); names from the last read{(_leasesUtc == default ? " -- none yet" : $" at {_leasesUtc:HH:mm} UTC")}";
            return;
        }
        try
        {
            _leases = ParseLeases(json);
            _leasesUtc = now;
            _leasesProblem = null;
        }
        catch (JsonException ex)
        {
            // A clean Status but junk JSON (a prepended warning line, or output truncated at the channel's size cap) must
            // not throw out of the whole refresh and freeze the map forever (it did: _leasesUtc stayed unset, so every
            // sweep re-threw). Degrade to the last-good leases with a note, like the fleet read above.
            _leasesProblem = $"DHCP leases from {server} could not be parsed ({ex.Message}); names from the last read{(_leasesUtc == default ? " -- none yet" : $" at {_leasesUtc:HH:mm} UTC")}";
        }
    }

    /// <summary>The probe's output: an array of leases -- or, as the channel can wrap it, an array holding that array.</summary>
    internal static IReadOnlyList<DhcpLease> ParseLeases(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var items = root.ValueKind == JsonValueKind.Array && root.GetArrayLength() > 0 && root[0].ValueKind == JsonValueKind.Array ? root[0] : root;
        var list = new List<DhcpLease>();
        foreach (var e in items.ValueKind == JsonValueKind.Array ? items.EnumerateArray() : Enumerable.Repeat(items, 1))
        {
            if (e.ValueKind != JsonValueKind.Object) continue;
            string? S(string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            if (S("Mac") is not { Length: > 0 } mac || S("Ip") is not { Length: > 0 } ip) continue;
            list.Add(new DhcpLease(ip, mac, S("HostName"), S("State") ?? "", DateTime.TryParse(S("Expires"), null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var x) ? x : null));
        }
        return list;
    }
}
