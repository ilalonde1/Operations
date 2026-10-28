using System.ComponentModel;
using Kor.Operations.NetworkOps.Core.Copy;
using Kor.Operations.NetworkOps.Service.Api;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Kor.Operations.NetworkOps.Service.Mcp;

/// <summary>
/// The NetworkOps MCP RUN tools -- a script on one machine, and a file copy run ON a machine -- so a session reaches a
/// machine through APP01's audited door instead of shelling out to the CLI or touching the machine itself. Both go
/// through SessionApi.RunAsync, the same path as `netops run`: a run-command action row before it starts, closed with
/// its output. What these add over the CLI is refusal, not reach:
///  - ONE named machine per call (no "all", no list): a fleet-wide script is the Command Center's, with its own preview;
///  - a purpose is required, and it is the audit line;
///  - a machine in SessionProtectedHosts (Ian's PC, the licensed Revit box) is refused unless the call names who
///    approved that one action -- recorded in the purpose. Every refusal is itself an audited action row.
/// </summary>
[McpServerToolType]
public sealed class NetworkOpsMcpRun
{
    public const int MinPurposeChars = 10;

    private static string By(IServiceProvider s)
        => s.GetService<IHttpContextAccessor>()?.HttpContext?.User is { Identity.IsAuthenticated: true } u ? ApiAccess.UserOf(u) : "mcp";

    /// <summary>Why a session may not run on this host with this purpose, or null. Pure, so the rules are tested as-is.</summary>
    internal static string? Refusal(NetworkOpsOptions o, string? host, string? purpose, string? approvedBy)
    {
        if (string.IsNullOrWhiteSpace(host)) return "name one machine";
        if (host.Equals("all", StringComparison.OrdinalIgnoreCase) || host.IndexOfAny([',', ';', ' ', '*']) >= 0)
            return "one named machine per call -- a fleet-wide run is the Command Center's, not a session's";
        if (string.IsNullOrWhiteSpace(purpose) || purpose.Trim().Length < MinPurposeChars)
            return $"say why, in a purpose of at least {MinPurposeChars} characters: it is the audit line";
        // The same rule the server applies to every run (SessionApi.RunAsync); checked here too so a refusal costs no lookup.
        return Core.Actions.ProtectedHosts.Refusal(o.SessionProtectedHosts, host, approvedBy);
    }

    private static async Task<object?> RefuseAsync(IServiceProvider services, string host, string via, string why, string by, CancellationToken ct)
    {
        var store = services.GetRequiredService<NetworkOpsStore>();
        if (await store.DeviceByNameAsync(host, ct) is { } d && d.Name.Equals(host, StringComparison.OrdinalIgnoreCase))
            await store.RecordRefusedActionAsync(d.DeviceId, Core.Actions.FixCatalog.RunCommand, by, System.Text.Json.JsonSerializer.Serialize(new { via }), why, ct);
        Serilog.Log.ForContext("Area", "Mcp").Information("mcp {Via} on {Host} by {By}: REFUSED ({Why})", via, host, by, why);
        return new { host, refused = why };
    }

    private static async Task<object> RunAsync(IServiceProvider services, string host, string script, int? timeoutSeconds, string via, string purpose,
        string? approvedBy, CancellationToken ct)
    {
        var (run, error, _) = await SessionApi.RunAsync(services.GetRequiredService<NetworkOpsStore>(), services.GetRequiredService<Agents.MachineRunner>(),
            services.GetRequiredService<IOptions<NetworkOpsOptions>>().Value, services.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("NetworkOps.Mcp.Run"),
            host, script, timeoutSeconds, By(services), via, purpose.Trim(), null, approvedBy, ct);
        return run is null
            ? new { host, error }
            : new { host = run.Device, actionId = run.ActionId, ok = run.Ok, route = run.Route, ms = run.Ms, output = Parse(run.OutputJson), error = run.Error };
    }

    // The script's output as JSON (so the session reads values, not an escaped string), or the raw text if it is not JSON.
    private static object? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return System.Text.Json.JsonDocument.Parse(json).RootElement.Clone(); } catch (System.Text.Json.JsonException) { return json; }
    }

    [McpServerTool(Name = "networkops_run"), Description(
        "Run a PowerShell script on ONE named machine as SYSTEM, through its NetworkOps agent (or APP01's network route), " +
        "audited as a run-command action -- the same path as `netops run`. Use it instead of any remote access of your own. " +
        "One machine per call (never 'all'); a purpose is required and is the audit line. KOR-1001 and KOR-302N are " +
        "protected: refused unless approvedBy names who approved this one action. Returns the action id, route, time and " +
        "the script's output; read the action again with `netops action --id`.")]
    public static async Task<object> Run(IServiceProvider services,
        [Description("The machine name, e.g. KOR-206-N or KOR-APP01. One machine.")] string host,
        [Description("The PowerShell to run on it (up to 20,000 characters). Return plain values or a [pscustomobject].")] string script,
        [Description("Why, in a sentence -- recorded as the action's purpose.")] string purpose,
        [Description("Seconds to wait, 10-300 (default 90).")] int? timeoutSeconds = null,
        [Description("For a protected machine only: who approved this one action, as they said it.")] string? approvedBy = null,
        CancellationToken ct = default)
    {
        var options = services.GetRequiredService<IOptions<NetworkOpsOptions>>().Value;
        var by = By(services);
        if (Refusal(options, host, purpose, approvedBy) is { } why) return (await RefuseAsync(services, host, "mcp run", why, by, ct))!;
        return await RunAsync(services, host, script, timeoutSeconds, "mcp networkops_run", purpose, approvedBy, ct);
    }

    [McpServerTool(Name = "networkops_copy"), Description(
        "Copy a folder's files with robocopy run ON one named machine (the one beside the files, e.g. a PC on the office " +
        "LAN copying to \\\\KOR-FS01\\...), never across the VPN. Audited as a run-command action. Never deletes: no " +
        "mirror, purge or move; without overwrite=true a file already at the destination is kept. The copy runs as a " +
        "one-off SYSTEM task, so it outlives this call: it waits up to waitSeconds (max 240) and returns the result -- " +
        "robocopy's exit, file count and bytes on both sides, and a SHA-256 sample of up to 10 files -- or State=running " +
        "with a copyId to read later with networkops_copy_status. Note: the machine's own computer account does the " +
        "copy, so the share must let that machine write.")]
    public static async Task<object> Copy(IServiceProvider services,
        [Description("The machine that runs the copy, e.g. KOR-104N. One machine.")] string host,
        [Description("The source folder, as that machine sees it: C:\\folder or \\\\server\\share\\folder.")] string source,
        [Description("The destination folder, as that machine sees it.")] string destination,
        [Description("Why, in a sentence -- recorded as the action's purpose.")] string purpose,
        [Description("Which files: one name or wildcard (default *.*, every file, with subfolders).")] string? files = null,
        [Description("true to replace files that already exist at the destination (default false: keep them).")] bool? overwrite = null,
        [Description("Seconds to wait for the copy to finish before returning State=running, 0-240 (default 120).")] int? waitSeconds = null,
        [Description("For a protected machine only: who approved this one action, as they said it.")] string? approvedBy = null,
        CancellationToken ct = default)
    {
        var options = services.GetRequiredService<IOptions<NetworkOpsOptions>>().Value;
        var by = By(services);
        if ((Refusal(options, host, purpose, approvedBy) ?? CopyScript.Invalid(source, destination, files)) is { } why)
            return (await RefuseAsync(services, host, "mcp copy", why, by, ct))!;
        var id = CopyScript.NewId();
        var wait = Math.Clamp(waitSeconds ?? CopyScript.DefaultWaitSeconds, 0, CopyScript.MaxWaitSeconds);
        var audit = $"copy {id}: {source} -> {destination}{(files is null ? "" : $" ({files})")}{(overwrite == true ? " OVERWRITE" : "")} -- {purpose.Trim()}";
        return await RunAsync(services, host, CopyScript.Start(id, source, destination, files, overwrite == true, wait), wait + 45, "mcp networkops_copy", audit, approvedBy, ct);
    }

    [McpServerTool(Name = "networkops_copy_status"), Description(
        "Read back a copy started by networkops_copy (or `netops copy`) on that machine: the finished result with its proof, " +
        "or that it is still running. Audited as a run-command action (read-only).")]
    public static async Task<object> CopyStatus(IServiceProvider services,
        [Description("The machine the copy runs on.")] string host,
        [Description("The copyId networkops_copy returned, e.g. 20261027-061500-a1b2c3.")] string copyId,
        [Description("For a protected machine only: who approved reading it.")] string? approvedBy = null,
        CancellationToken ct = default)
    {
        var options = services.GetRequiredService<IOptions<NetworkOpsOptions>>().Value;
        var by = By(services);
        var purpose = $"copy status {copyId}";
        if ((Refusal(options, host, purpose, approvedBy) ?? (CopyScript.IsId(copyId) ? null : "that is not a copy id")) is { } why)
            return (await RefuseAsync(services, host, "mcp copy-status", why, by, ct))!;
        return await RunAsync(services, host, CopyScript.Status(copyId), 60, "mcp networkops_copy_status", purpose, approvedBy, ct);
    }
}
