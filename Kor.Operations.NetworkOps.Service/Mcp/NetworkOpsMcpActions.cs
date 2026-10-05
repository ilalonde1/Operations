using System.ComponentModel;
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Service.Api;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Kor.Operations.NetworkOps.Service.Mcp;

/// <summary>The NetworkOps MCP ACTION tools -- acknowledge a finding, re-check a machine, run a catalog fix -- through the
/// SAME audited paths the Command Center uses: every run is a NetworkOps.Actions row, the disruptive-on-active refusal
/// still applies, and the acting person is the token's owner (the endpoint is Entra-gated). A session advises AND acts,
/// within the same policy as a human at the console.</summary>
[McpServerToolType]
public sealed class NetworkOpsMcpActions
{
    // Who is acting, for the audit: the authenticated caller (the MCP endpoint is behind the same auth as /api).
    private static string By(IServiceProvider s)
        => s.GetService<IHttpContextAccessor>()?.HttpContext?.User is { Identity.IsAuthenticated: true } u ? ApiAccess.UserOf(u) : "mcp";

    private static async Task<int?> DeviceIdAsync(NetworkOpsStore store, string host, CancellationToken ct)
    {
        foreach (var rack in new[] { false, true })
            if ((await store.FleetSnapshotAsync(ct, rack)).Devices.FirstOrDefault(d => d.Name.Equals(host, StringComparison.OrdinalIgnoreCase)) is { } d)
                return d.DeviceId;
        return null;
    }

    [McpServerTool(Name = "networkops_acknowledge"), Description(
        "Acknowledge (park) a finding by its id: it stops counting against green and drops off the to-clear list. Use it " +
        "when the state is known or expected. Reversible (reopen from the machine's page). Give the findingId from " +
        "networkops_findings or networkops_device.")]
    public static async Task<object> Acknowledge(IServiceProvider services,
        [Description("The finding's id (from networkops_findings / networkops_device).")] long findingId,
        [Description("Why, in a few words -- recorded on the finding.")] string? note,
        CancellationToken ct)
    {
        var store = services.GetRequiredService<NetworkOpsStore>();
        var by = By(services);
        var ok = await store.AnnotateAsync(findingId, NetworkOpsStore.Annotation.Acknowledge, by, note, null, ct);
        Serilog.Log.ForContext("Area", "Mcp").Information("mcp acknowledge finding {Id} by {By}: {Result}", findingId, by, ok ? "done" : "already cleared");
        return new { findingId, acknowledged = ok, message = ok ? "Acknowledged; it stops counting against green." : "That finding has already cleared -- nothing to acknowledge." };
    }

    [McpServerTool(Name = "networkops_recheck"), Description(
        "Run a fresh health check on a machine now (a PC's health probe, or a rack device's re-read) instead of waiting " +
        "for the scheduled sweep. Returns the trigger id; the result lands in a few seconds to a minute -- read it back " +
        "with networkops_device.")]
    public static async Task<object> Recheck(IServiceProvider services,
        [Description("The machine name, e.g. KOR-206-N (or a rack device).")] string host, CancellationToken ct)
    {
        var store = services.GetRequiredService<NetworkOpsStore>();
        var by = By(services);
        var trigger = await store.IsRackDeviceAsync(host, ct) ? await store.QueueRackCheckAsync(host, by, ct) : await store.QueueCheckAsync(host, by, ct);
        Serilog.Log.ForContext("Area", "Mcp").Information("mcp recheck {Host} by {By}: trigger {Trigger}", host, by, trigger);
        return trigger is { } t
            ? new { host, triggerId = t, message = "Check queued; read it back with networkops_device in a moment." }
            : new { host, error = $"No machine named '{host}' is monitored." };
    }

    [McpServerTool(Name = "networkops_run_fix"), Description(
        "Run a catalog fix on a machine through its agent as SYSTEM -- the SAME path as the Command Center's Fix, audited " +
        "as an action. A DISRUPTIVE fix (one that restarts the PC) is REFUSED while someone is actively using the machine " +
        "unless you pass confirmed=true. The fix id is a FixCatalog id (e.g. repair-wmi, free-disk-space, restart-pc); a " +
        "finding's rule often implies its fix.")]
    public static async Task<object> RunFix(IServiceProvider services,
        [Description("The machine name, e.g. KOR-206-N.")] string host,
        [Description("The fix id (a FixCatalog id, e.g. repair-wmi, free-disk-space, restart-pc).")] string fixId,
        [Description("The fix's input, if it needs one (most do not).")] string? param,
        [Description("Pass true to go ahead with a disruptive fix on a machine someone is actively using.")] bool confirmed,
        CancellationToken ct)
    {
        var store = services.GetRequiredService<NetworkOpsStore>();
        var options = services.GetRequiredService<IOptions<NetworkOpsOptions>>().Value;
        var fix = FixCatalog.Get(fixId);
        if (fix is null) return new { error = $"'{fixId}' is not a fix NetworkOps knows." };
        if (await DeviceIdAsync(store, host, ct) is not { } deviceId || await store.DeviceForActionAsync(deviceId, ct) is not { } dev)
            return new { error = $"No machine named '{host}' is monitored." };
        var by = By(services);
        var request = System.Text.Json.JsonSerializer.Serialize(new { param, confirmed, presence = dev.Presence, via = "mcp" });
        // The same refusals as POST /api/devices/{id}/fixes: unknown/invalid param, wrong target, and disruptive-on-active.
        string? refuse = FixCatalog.Invalid(fix, param)
            ?? (dev.Source == "Rack" && !Sweep.ActionRunner.CanRun(options, dev.Name, fix)
                ? (fix.Target == FixCatalog.Esxi ? $"{dev.Name} is not an ESXi host" : $"{dev.Name} is not a Windows server APP01 can run fixes on") : null)
            ?? (dev.Source != "Rack" && fix.Target == FixCatalog.Esxi ? $"{fix.Title} is for an ESXi host, not a PC" : null)
            ?? (fix.Disruptive && dev.PresenceState == "Active" && !confirmed
                ? $"someone is using {dev.Name} right now ({dev.Presence}): pass confirmed=true to go ahead" : null);
        if (refuse is not null)
        {
            await store.RecordRefusedActionAsync(deviceId, fix.Id, by, request, refuse, ct);
            Serilog.Log.ForContext("Area", "Mcp").Information("mcp run-fix {Fix} on {Host} by {By}: REFUSED ({Why})", fix.Id, host, by, refuse);
            return new { host, fixId = fix.Id, refused = refuse, needsConfirmation = fix.Disruptive && dev.PresenceState == "Active" && !confirmed };
        }
        var actionId = await store.QueueActionAsync(deviceId, fix.Id, by, request, ct);
        Serilog.Log.ForContext("Area", "Mcp").Information("mcp run-fix {Fix} on {Host} by {By}: queued action {ActionId}", fix.Id, host, by, actionId);
        return new { host, fixId = fix.Id, actionId, message = $"Queued '{fix.Title}' on {host}, audited as action {actionId}. Read it back with networkops_device, or it clears on the next check." };
    }
}
