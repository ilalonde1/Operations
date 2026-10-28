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

    // A name lookup, not a fleet read: the exact live (not retired) device of that name.
    private static async Task<int?> DeviceIdAsync(NetworkOpsStore store, string host, CancellationToken ct)
        => await store.DeviceByNameAsync(host, ct) is { } d && d.Name.Equals(host, StringComparison.OrdinalIgnoreCase) ? d.DeviceId : null;

    [McpServerTool(Name = "networkops_acknowledge"), Description(
        "Acknowledge (park) a finding by its id: it stops counting against green and drops off the to-clear list. Use it " +
        "when the state is known or expected. Reversible (reopen from the machine's page). Give the findingId from " +
        "networkops_findings or networkops_device.")]
    public static async Task<object> Acknowledge(IServiceProvider services,
        [Description("The finding's id (from networkops_findings / networkops_device).")] long findingId,
        [Description("Why, in a few words -- recorded on the finding.")] string? note = null,
        CancellationToken ct = default)
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
        "as an action. A DISRUPTIVE fix (one that restarts the machine, server or PC, idle or in use) is REFUSED unless you " +
        "pass confirmed=true. The fix id is a FixCatalog id (e.g. repair-wmi, free-disk-space, restart-pc); a finding's rule " +
        "often implies its fix. A reply with an actionId means QUEUED, NOT RUN: read the result with networkops_runs before " +
        "reporting it.")]
    public static async Task<object> RunFix(IServiceProvider services,
        [Description("The machine name, e.g. KOR-206-N.")] string host,
        [Description("The fix id (a FixCatalog id, e.g. repair-wmi, free-disk-space, restart-pc).")] string fixId,
        [Description("The fix's input, if it needs one (most do not).")] string? param = null,
        // false = refused for any disruptive fix (FixCatalog.Unconfirmed): leaving it out can never restart a machine.
        [Description("Pass true to go ahead with a disruptive fix (any restart), whoever is on the machine.")] bool confirmed = false,
        CancellationToken ct = default)
    {
        var store = services.GetRequiredService<NetworkOpsStore>();
        var options = services.GetRequiredService<IOptions<NetworkOpsOptions>>().Value;
        var fix = FixCatalog.Get(fixId);
        if (fix is null) return new { error = $"'{fixId}' is not a fix NetworkOps knows." };
        if (await DeviceIdAsync(store, host, ct) is not { } deviceId || await store.DeviceForActionAsync(deviceId, ct) is not { } dev)
            return new { error = $"No machine named '{host}' is monitored." };
        var by = By(services);
        var request = System.Text.Json.JsonSerializer.Serialize(new { param, confirmed, presence = dev.Presence, via = "mcp" });
        // The same refusals as POST /api/devices/{id}/fixes, in its words: unknown/invalid param, wrong target, and ANY
        // unconfirmed disruptive fix -- refused here, in this reply, never queued to fail later in ActionRunner (27 Oct: FS01
        // queued unconfirmed, failed at once, and "Queued" was reported as started).
        var unconfirmed = FixCatalog.Unconfirmed(fix, dev.Name, dev.PresenceState, dev.Presence, confirmed);
        string? refuse = FixCatalog.Invalid(fix, param)
            ?? (dev.Source == "Rack" && !Sweep.ActionRunner.CanRun(options, dev.Name, fix)
                ? (fix.Target == FixCatalog.Esxi ? $"{dev.Name} is not an ESXi host" : $"{dev.Name} is not a Windows server APP01 can run fixes on") : null)
            ?? (dev.Source != "Rack" && fix.Target == FixCatalog.Esxi ? $"{fix.Title} is for an ESXi host, not a PC" : null)
            ?? (unconfirmed is null ? null : unconfirmed + " (pass confirmed=true)");
        if (refuse is not null)
        {
            await store.RecordRefusedActionAsync(deviceId, fix.Id, by, request, refuse, ct);
            Serilog.Log.ForContext("Area", "Mcp").Information("mcp run-fix {Fix} on {Host} by {By}: REFUSED ({Why})", fix.Id, host, by, refuse);
            return new { host, fixId = fix.Id, refused = refuse, needsConfirmation = unconfirmed is not null };
        }
        if (await store.QueueFixOnceAsync(deviceId, fix.Id, by, request, ct) is not { } actionId)
        {
            await store.RecordRefusedActionAsync(deviceId, fix.Id, by, request, NetworkOpsStore.AlreadyInFlight, ct);
            return new { host, fixId = fix.Id, refused = NetworkOpsStore.AlreadyInFlight, needsConfirmation = false };
        }
        Serilog.Log.ForContext("Area", "Mcp").Information("mcp run-fix {Fix} on {Host} by {By}: queued action {ActionId}", fix.Id, host, by, actionId);
        return new { host, fixId = fix.Id, actionId, state = "queued, NOT run",
            message = $"Queued, NOT run: '{fix.Title}' on {host}, audited as action {actionId}. It has not started; read its result with networkops_runs before reporting it." };
    }
}
