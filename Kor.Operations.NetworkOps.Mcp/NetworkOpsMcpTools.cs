using System.ComponentModel;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Kor.Operations.NetworkOps.Mcp;

/// <summary>Read tools over NetworkOps' live database -- the structured context a Claude session needs, pulled on demand
/// instead of a point-in-time prompt plus parsing <c>netops</c> CLI text. Reads only; audited action tools (acknowledge,
/// run-fix, re-check) come in a later phase, through the store's existing audited paths.</summary>
[McpServerToolType]
public sealed class NetworkOpsMcpTools
{
    private static object Shape(FleetFinding f, DateTime now) => new
    {
        f.FindingId,
        f.Device,
        f.RuleKey,
        severity = f.Severity.ToString(),
        f.Title,
        f.Evidence,
        firstSeenUtc = f.FirstSeenUtc,
        lastSeenUtc = f.LastSeenUtc,
        state = f.AcknowledgedUtc is not null ? "acknowledged" : f.SnoozedUntilUtc > now ? "snoozed" : "open",
        acknowledgedBy = f.AcknowledgedBy,
        ackNote = f.AckNote,
    };

    private static object Shape(DeviceRow d) => new
    {
        d.DeviceId,
        d.Name,
        d.Kind,
        presence = d.PresenceState,
        lastReachableUtc = d.LastReachableUtc,
        lastCheckedUtc = d.LastCheckedUtc,
        agentVersion = d.AgentVersion,
        agentConnected = d.AgentConnected,
        summary = d.Summary,
    };

    private static async Task<(FleetSnapshot Pcs, FleetSnapshot Rack)> BothAsync(NetworkOpsStore store, CancellationToken ct)
        => (await store.FleetSnapshotAsync(ct), await store.FleetSnapshotAsync(ct, rack: true));

    [McpServerTool(Name = "networkops_fleet"), Description(
        "Every machine NetworkOps monitors -- the PCs and the rack -- with each one's open findings (problems). " +
        "Use this first to see the whole picture.")]
    public static async Task<object> Fleet(IServiceProvider services, CancellationToken ct)
    {
        var (pcs, rack) = await BothAsync(services.GetRequiredService<NetworkOpsStore>(), ct);
        var now = DateTime.UtcNow;
        var devices = pcs.Devices.Concat(rack.Devices).ToList();
        var findings = pcs.OpenFindings.Concat(rack.OpenFindings).ToList();
        return new
        {
            counts = new
            {
                machines = devices.Count,
                liveFindings = findings.Count(f => !f.IsQuiet(now)),
                parked = findings.Count(f => f.IsQuiet(now)),
            },
            devices = devices.Select(Shape),
            findings = findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Device).Select(f => Shape(f, now)),
        };
    }

    [McpServerTool(Name = "networkops_findings"), Description(
        "Open findings across the fleet, worst first. Optionally filter to one host, or to a minimum severity " +
        "(Info, Warning, Critical). Parked (acknowledged or snoozed) findings are included and marked in 'state'.")]
    public static async Task<object> Findings(IServiceProvider services,
        [Description("Only this device, e.g. KOR-206-N; omit for the whole fleet.")] string? host = null,
        [Description("Lowest severity to include: Info | Warning | Critical; omit for all.")] string? minSeverity = null,
        CancellationToken ct = default)
    {
        var (pcs, rack) = await BothAsync(services.GetRequiredService<NetworkOpsStore>(), ct);
        var now = DateTime.UtcNow;
        var findings = pcs.OpenFindings.Concat(rack.OpenFindings).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(host))
            findings = findings.Where(f => f.Device.Equals(host, StringComparison.OrdinalIgnoreCase));
        if (Enum.TryParse<Severity>(minSeverity, ignoreCase: true, out var min))
            findings = findings.Where(f => f.Severity >= min);
        return new { findings = findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Device).Select(f => Shape(f, now)) };
    }

    [McpServerTool(Name = "networkops_device"), Description(
        "One machine in detail: its open findings, recent history (what was done and what cleared), and its last full " +
        "health check. Give the device name, e.g. KOR-206-N.")]
    public static async Task<object> Device(IServiceProvider services,
        [Description("The device name, e.g. KOR-206-N.")] string name, CancellationToken ct)
    {
        var store = services.GetRequiredService<NetworkOpsStore>();
        var (pcs, rack) = await BothAsync(store, ct);
        var now = DateTime.UtcNow;
        var dev = pcs.Devices.Concat(rack.Devices).FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (dev is null) return new { error = $"No device named '{name}' is monitored." };
        var open = pcs.OpenFindings.Concat(rack.OpenFindings)
            .Where(f => f.Device.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Severity).Select(f => Shape(f, now));
        var history = await store.DeviceHistoryAsync(dev.DeviceId, ct);
        var last = await store.LastCheckAsync(dev.DeviceId, ct);
        return new { device = Shape(dev), openFindings = open, history, lastCheck = last };
    }
}
