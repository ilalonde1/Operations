using System.ComponentModel;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Server;

namespace Kor.Operations.NetworkOps.Service.Mcp;

/// <summary>The NetworkOps MCP tools: live, structured NetworkOps context for a Claude session over MCP, instead of a
/// point-in-time prompt plus parsing <c>netops</c> CLI text. Served by the Command Center service itself (over HTTP,
/// behind the same Entra auth), so there is one authenticated, audited endpoint on APP01 and the DB credential never
/// leaves the server. Reads only; audited action tools (acknowledge, run-fix, re-check) come in a later phase through
/// the store's existing audited paths.
/// <para>NetworkOpsStore is internal, so a public tool method cannot take it as a parameter (CS0051): each tool takes
/// <see cref="IServiceProvider"/> and resolves the store from DI.</para></summary>
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
        presenceSummary = d.Presence,
        lastReachableUtc = d.LastReachableUtc,
        lastCheckedUtc = d.LastCheckedUtc,
        agentVersion = d.AgentVersion,
        agentConnected = d.AgentConnected,
        agentLastContactUtc = d.AgentLastContactUtc,
        remoteControlConnected = d.MeshConnected,
        summary = d.Summary,
    };

    // The same assembly the app and the CLI read (Api/FleetView): live agents, remote control and UPS included. These tools
    // used to read the raw SQL, so every PC said agentConnected=false and remote control was missing (audit 2026-10-16).
    private static Task<(FleetSnapshot Pcs, FleetSnapshot Rack)> BothAsync(IServiceProvider services, CancellationToken ct)
        => services.GetRequiredService<Api.FleetView>().BothAsync(ct);

    private static object ReadAt(FleetSnapshot pcs, FleetSnapshot rack) => new { readUtc = pcs.ReadUtc, remoteControlReadUtc = pcs.MeshReadUtc ?? rack.MeshReadUtc };

    [McpServerTool(Name = "networkops_fleet"), Description(
        "Every machine NetworkOps monitors -- the PCs and the rack -- with each one's open findings (problems). " +
        "Use this first to see the whole picture.")]
    public static async Task<object> Fleet(IServiceProvider services, CancellationToken ct)
    {
        var (pcs, rack) = await BothAsync(services, ct);
        var now = DateTime.UtcNow;
        var devices = pcs.Devices.Concat(rack.Devices).ToList();
        var findings = pcs.OpenFindings.Concat(rack.OpenFindings).ToList();
        return new
        {
            read = ReadAt(pcs, rack),
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
        var (pcs, rack) = await BothAsync(services, ct);
        var now = DateTime.UtcNow;
        var findings = pcs.OpenFindings.Concat(rack.OpenFindings).AsEnumerable();
        if (!string.IsNullOrWhiteSpace(host))
            findings = findings.Where(f => f.Device.Equals(host, StringComparison.OrdinalIgnoreCase));
        if (Enum.TryParse<Severity>(minSeverity, ignoreCase: true, out var min))
            findings = findings.Where(f => f.Severity >= min);
        return new { read = ReadAt(pcs, rack), findings = findings.OrderByDescending(f => f.Severity).ThenBy(f => f.Device).Select(f => Shape(f, now)) };
    }

    [McpServerTool(Name = "networkops_device"), Description(
        "One machine in detail: its open findings, recent history (what was done and what cleared), and its last full " +
        "health check. Give the device name, e.g. KOR-206-N.")]
    public static async Task<object> Device(IServiceProvider services,
        [Description("The device name, e.g. KOR-206-N.")] string name, CancellationToken ct)
    {
        var store = services.GetRequiredService<NetworkOpsStore>();
        var (pcs, rack) = await BothAsync(services, ct);
        var now = DateTime.UtcNow;
        var dev = pcs.Devices.Concat(rack.Devices).FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (dev is null) return new { error = $"No device named '{name}' is monitored." };
        var open = pcs.OpenFindings.Concat(rack.OpenFindings)
            .Where(f => f.Device.Equals(name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.Severity).Select(f => Shape(f, now));
        var history = await store.DeviceHistoryAsync(dev.DeviceId, ct);
        var last = await store.LastCheckAsync(dev.DeviceId, ct);
        return new { read = ReadAt(pcs, rack), device = Shape(dev), openFindings = open, history, lastCheck = last };
    }

    [McpServerTool(Name = "networkops_jobs"), Description(
        "The scheduled background jobs behind every reading (the health sweep, the rack sweep, remote control, the update " +
        "scan, backups, the UPS rehearsal) and whether each is keeping up: Ok, Running, Failing, Stuck (hung), Late (missed " +
        "its schedule) or NeverRan. Check this when readings look old or a finding should have cleared and has not.")]
    public static async Task<object> Jobs(IServiceProvider services, CancellationToken ct)
    {
        var rows = await services.GetRequiredService<Kor.Operations.NetworkOps.Service.Jobs.JobHealth>().StatusAsync(ct);
        return new { needsAttention = rows.Count(r => r.NeedsAttention), jobs = rows };
    }

    [McpServerTool(Name = "networkops_runs"), Description(
        "Every fix, install or deploy RUN in flight, and those finished in the last hour, wherever it was started (the " +
        "Command Center, this MCP, the CLI): one progress line each (\"Install updates (no restart) · 12 of 29 finished · " +
        "10 done · 2 failed · 17 running · 25 min\") and each machine's state. The same list as the Command Center's runs " +
        "strip. Use it to answer \"is it done?\" -- not the per-machine device pages.")]
    public static async Task<object> Runs(IServiceProvider services, CancellationToken ct)
    {
        var runs = await services.GetRequiredService<NetworkOpsStore>().ActionRunsAsync(ct);
        var now = DateTime.UtcNow;
        return new
        {
            inFlight = runs.Count(r => r.InFlight),
            runs = runs.Select(r => new
            {
                line = r.Line(now), r.Kind, r.RequestedBy, r.StartedUtc, r.InFlight,
                machines = r.Machines.Select(m => new { m.Device, m.ActionId, state = ActionRun.MachineLine(m, now) }),
            }),
        };
    }
}
