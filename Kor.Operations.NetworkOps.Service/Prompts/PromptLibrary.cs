#nullable enable
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Kor.Operations.NetworkOps.Core.Learning;
using Kor.Operations.NetworkOps.Core.Prompts;
using Kor.Operations.NetworkOps.Service.Store;
using Microsoft.Extensions.Options;

namespace Kor.Operations.NetworkOps.Service.Prompts;

// The Prompt Library: what the Command Center hands a person to start a Claude session with. Nothing here is a stored
// document. A device or finding prompt is assembled from the database the moment it is opened (Core/Prompts/
// PromptComposer); a tool prompt is the tool's brief, embedded from Prompts/Tools/<id>.md beside the tool's code -- so it
// changes in the same commit as the tool -- plus what the service sees live. Every prompt opened is recorded as a run
// with a one-time token, and the session reports its outcome back to that run (ApiHost: /api/prompt-runs/{id}/outcome).
internal sealed class PromptLibrary(NetworkOpsStore store, Agents.AgentHub agents, Mesh.MeshState mesh, Power.PowerState power,
    IOptions<NetworkOpsOptions> options, TimeProvider clock)
{
    public sealed record ToolBrief(string Id, string Title, string Summary);

    public static readonly IReadOnlyList<ToolBrief> Tools =
    [
        new("networkops", "NetworkOps: the service, the Command Center and the store", "How NetworkOps is built and run, where each piece lives, and how to change it safely."),
        new("agent", "The endpoint agent", "The per-PC agent: how it calls in, how jobs reach it, install/upgrade/remove and the rollout."),
        new("remote-control", "Remote control (MeshCentral)", "KOR-MESH01, the Mesh agents on every PC and server, and the Connect button."),
        new("rack-power", "The rack and the UPS shutdown chain", "The 5-minute rack watch, its collectors, and the power chain (dry run, not armed)."),
    ];

    public static string? Brief(string id)
    {
        if (!Tools.Any(t => t.Id == id)) return null;
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Kor.Operations.NetworkOps.Service.Prompts.Tools.{id}.md");
        if (s is null) return null;
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    public async Task<PromptCatalog> CatalogAsync(CancellationToken ct)
    {
        var (fleet, rack) = (await store.FleetSnapshotAsync(ct), await store.FleetSnapshotAsync(ct, rack: true));
        var devices = fleet.Devices.Concat(rack.Devices)
            .Select(d => new PromptSubject(d.DeviceId, d.Name, d.Kind,
                fleet.OpenOn(d.Name).Concat(rack.OpenOn(d.Name))
                    .OrderByDescending(f => f.Severity).ThenBy(f => f.Title, StringComparer.Ordinal)
                    .Select(f => new PromptFinding(f.FindingId, f.RuleKey, f.Title, f.Severity)).ToList()))
            .OrderByDescending(s => s.Findings.Count > 0).ThenBy(s => s.Device, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PromptCatalog(Tools.Select(t => new PromptTool(t.Id, t.Title, t.Summary)).ToList(), devices, await store.PromptRunsAvailableAsync(ct));
    }

    public enum Refusal { None, BadRequest, NotFound }

    /// <summary>Writes the prompt asked for, recording the run first when reporting is available.</summary>
    public async Task<(Refusal Refusal, string? Error, RenderedPrompt? Prompt)> RenderAsync(PromptRequest req, string by, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        switch (req.Kind)
        {
            case "tool":
            {
                var tool = Tools.FirstOrDefault(t => t.Id == req.ToolId);
                if (tool is null || Brief(tool.Id) is not { } brief) return (Refusal.NotFound, $"no tool '{req.ToolId}'", null);
                var report = await StartRunAsync("tool", tool.Id, null, null, null, by, ct);
                var md = PromptComposer.Tool(tool.Title, brief, await LiveAsync(tool.Id, ct), report, now);
                return (Refusal.None, null, await FinishAsync(report, $"Claude: {tool.Title}", $"claude-{tool.Id}", md, ct));
            }
            case "device" or "finding":
            {
                var (fleet, rack) = (await store.FleetSnapshotAsync(ct), await store.FleetSnapshotAsync(ct, rack: true));
                fleet = Shown(fleet, await store.AgentRecordsAsync(ct), agents, mesh, await store.MeshRecordsAsync(ct));
                rack = Api.ApiHost.WithMesh(rack, mesh, await store.MeshRecordsAsync(ct));
                FleetFinding? focus = null;
                if (req.Kind == "finding")
                {
                    focus = fleet.OpenFindings.Concat(rack.OpenFindings).FirstOrDefault(f => f.FindingId == req.FindingId);
                    if (focus is null) return (Refusal.NotFound, "that finding is not open any more", null);
                }
                var isRack = focus is not null ? rack.Devices.Any(d => d.Name.Equals(focus.Device, StringComparison.OrdinalIgnoreCase)) : rack.Devices.Any(d => d.DeviceId == req.DeviceId);
                var snap = isRack ? rack : fleet;
                var dev = focus is not null
                    ? snap.Devices.FirstOrDefault(d => d.Name.Equals(focus.Device, StringComparison.OrdinalIgnoreCase))
                    : snap.Devices.FirstOrDefault(d => d.DeviceId == req.DeviceId);
                if (dev is null) return (Refusal.NotFound, "no such device", null);

                var history = await store.DeviceHistoryAsync(dev.DeviceId, ct);
                var resolutions = (await store.ResolutionRowsAsync(ct)).Select(r => r.ToResolution()).ToList();
                var facts = snap.FactsOf(dev.Name);
                var input = new DevicePromptInput(
                    dev.Name, dev.Kind, facts,
                    snap.OpenOn(dev.Name).ToList(),
                    focus,
                    history.Cleared,
                    CommandCenterView.ChangesFrom(history.Facts.Select(f => (f.Fact, f.Value, f.FirstSeenUtc, f.SupersededUtc)).ToList()),
                    history.Actions ?? [],
                    history.Notes,
                    focus is null ? [] : CommandCenterView.SameProblemElsewhere(focus, fleet.OpenFindings.Concat(rack.OpenFindings)),
                    focus is null ? [] : CommandCenterView.PatternsFor(focus.RuleKey, facts, snap.Patterns),
                    focus is null ? [] : CommandCenterView.LearnedFixesFor(focus.RuleKey, resolutions),
                    focus is null ? null : Knowledge.For(focus.RuleKey),
                    focus is null ? [] : await store.AcceptedLearningsAsync(focus.RuleKey, ct),
                    new PromptAccess(isRack, dev.Presence, dev.AgentConnected, dev.AgentVersion, dev.MeshConnected, ConnectUrl(dev.MeshNodeId), dev.Summary),
                    dev.LastCheckedUtc, now,
                    await store.LastCheckAsync(dev.DeviceId, ct));

                var subject = focus is null ? dev.Name : $"{dev.Name}: {focus.RuleKey}";
                var report = await StartRunAsync(req.Kind, subject, dev.DeviceId, focus?.FindingId, focus?.RuleKey, by, ct);
                var md = PromptComposer.Device(input, report);
                var file = focus is null ? $"claude-{dev.Name}" : $"claude-{dev.Name}-{FixLearning.FamilyOf(focus.RuleKey)}";
                return (Refusal.None, null, await FinishAsync(report, focus is null ? $"Claude: {dev.Name}" : $"Claude: {focus.Title} on {dev.Name}", file, md, ct));
            }
            default:
                return (Refusal.BadRequest, "kind must be tool, device or finding", null);
        }
    }

    public static readonly IReadOnlySet<string> Outcomes = new HashSet<string>(StringComparer.Ordinal) { "solved", "partly", "not-solved", "no-action" };

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private async Task<PromptReport?> StartRunAsync(string kind, string subject, int? deviceId, long? findingId, string? ruleKey, string by, CancellationToken ct)
    {
        if (!await store.PromptRunsAvailableAsync(ct)) return null;
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var id = await store.CreatePromptRunAsync(kind, subject, deviceId, findingId, ruleKey, by, HashToken(token), ct);
        return new PromptReport(id, token, options.Value.ApiPublicUrl);
    }

    private async Task<RenderedPrompt> FinishAsync(PromptReport? report, string title, string file, string markdown, CancellationToken ct)
    {
        if (report is not null) await store.SetPromptHashAsync(report.RunId, SHA256.HashData(Encoding.UTF8.GetBytes(markdown)), ct);
        var safe = new string(file.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());
        return new RenderedPrompt(report?.RunId, title, $"{safe}{(report is null ? "" : $"-run{report.RunId}")}.md", markdown);
    }

    private string? ConnectUrl(string? nodeId)
        => nodeId is null || options.Value.MeshUrl.Length == 0 ? null
         : $"{options.Value.MeshUrl.TrimEnd('/')}/?gotonode={Uri.EscapeDataString(Transport.MeshCentralClient.LinkId(nodeId))}&viewmode=11";

    /// <summary>What the service sees right now, for one tool's prompt.</summary>
    private async Task<IReadOnlyList<(string, string)>> LiveAsync(string tool, CancellationToken ct)
    {
        var o = options.Value;
        var fleet = Shown(await store.FleetSnapshotAsync(ct), await store.AgentRecordsAsync(ct), agents, mesh, await store.MeshRecordsAsync(ct));
        var rack = Api.ApiHost.WithMesh(await store.FleetSnapshotAsync(ct, rack: true), mesh, await store.MeshRecordsAsync(ct));
        var now = clock.GetUtcNow().UtcDateTime;
        var live = new List<(string, string)> { ("Service", $"NetworkOps {Jobs.JobDispatcher.Version} on {Environment.MachineName}") };
        switch (tool)
        {
            case "networkops":
                live.Add(("PCs", $"{fleet.Devices.Count}, {fleet.Devices.Count(d => d.LastCheckedUtc is { } c && now - c < CommandCenterView.StaleAfter)} checked in the last 3 days"));
                live.Add(("Rack", $"{rack.Devices.Count} devices"));
                live.Add(("Open findings", $"{fleet.OpenFindings.Count} on PCs ({fleet.OpenFindings.Count(f => f.Severity == Core.Health.Severity.Critical)} critical), {rack.OpenFindings.Count} on the rack"));
                live.Add(("Fleet patterns", fleet.Patterns.Count == 0 ? "none" : string.Join("; ", fleet.Patterns.Select(p => p.Summary).Take(5))));
                break;
            case "agent":
                live.Add(("Agents", $"{fleet.Devices.Count(d => d.AgentVersion is not null)} of {fleet.Devices.Count} PCs have it; {fleet.Devices.Count(d => d.AgentConnected)} connected now"));
                live.Add(("Versions", string.Join(", ", fleet.Devices.Where(d => d.AgentVersion is not null).GroupBy(d => d.AgentVersion).OrderByDescending(g => g.Count()).Select(g => $"{g.Key} ×{g.Count()}"))));
                live.Add(("Without it", string.Join(", ", fleet.Devices.Where(d => d.AgentVersion is null).Select(d => d.Name).Order(StringComparer.OrdinalIgnoreCase)) is { Length: > 0 } none ? none : "none"));
                live.Add(("Agents switched on", o.AgentsEnabled ? "yes" : "NO (AgentsEnabled=false: every check uses the network route)"));
                break;
            case "remote-control":
                live.Add(("MeshCentral", o.MeshEnabled ? o.MeshUrl : "not configured on this service"));
                live.Add(("Last read", mesh.LastReadUtc == default ? "never" : $"{CommandCenterView.Ago(mesh.LastReadUtc, now)}{(mesh.Fresh ? "" : " (STALE)")}{(mesh.LastError is { } e ? $"; last error: {e}" : "")}"));
                var all = fleet.Devices.Concat(rack.Devices).ToList();
                live.Add(("Linked", $"{all.Count(d => d.MeshNodeId is not null)} of {all.Count} devices have a Mesh agent; {all.Count(d => d.MeshConnected)} connected"));
                live.Add(("PCs without it", string.Join(", ", fleet.Devices.Where(d => d.MeshNodeId is null).Select(d => d.Name).Order(StringComparer.OrdinalIgnoreCase)) is { Length: > 0 } pcs ? pcs : "none"));
                break;
            case "rack-power":
                var p = power.Snapshot(o.PowerChainArmed, []);
                live.Add(("Power", $"{p.Level} -- {p.Reason}; chain {(p.Armed ? "ARMED" : "NOT armed (dry run only)")}"));
                foreach (var u in p.Ups) live.Add((u.Name, u.Reachable ? $"{u.Source}, {u.ChargePercent}% charge, {u.MinutesRemaining} min, load {u.LoadPercent}%" : $"unreachable: {u.Error}"));
                foreach (var d in rack.Devices.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
                    live.Add((d.Name, $"{d.Summary ?? "no read yet"} (read {CommandCenterView.Ago(d.LastCheckedUtc, now)})"));
                foreach (var f in rack.OpenFindings.OrderByDescending(f => f.Severity)) live.Add(($"Open on {f.Device}", $"{f.Title} -- {f.Evidence}"));
                break;
        }
        return live;
    }

    /// <summary>The fleet as the Command Center shows it: SQL, plus the agents' and Mesh agents' live state.</summary>
    private static FleetSnapshot Shown(FleetSnapshot fleet, IReadOnlyDictionary<string, NetworkOpsStore.AgentRecord> installed, Agents.AgentHub hub,
        Mesh.MeshState mesh, IReadOnlyDictionary<int, NetworkOpsStore.MeshRecord> stored)
        => Api.ApiHost.WithMesh(Api.ApiHost.WithAgents(fleet, installed, hub), mesh, stored);
}
