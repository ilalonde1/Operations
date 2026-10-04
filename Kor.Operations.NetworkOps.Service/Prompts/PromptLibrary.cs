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
                var report = await StartRunAsync("tool", tool.Id, null, null, null, null, by, ct);
                var md = PromptComposer.Tool(tool.Title, brief, await LiveAsync(tool.Id, ct), report, now);
                return (Refusal.None, null, await FinishAsync(report, $"Claude: {tool.Title}", $"claude-{tool.Id}", md, ct));
            }
            case "device" or "finding":
            {
                var (fleet, rack) = await SnapshotsAsync(ct);
                FleetFinding? focus = null;
                if (req.Kind == "finding")
                {
                    focus = fleet.OpenFindings.Concat(rack.OpenFindings).FirstOrDefault(f => f.FindingId == req.FindingId);
                    if (focus is null) return (Refusal.NotFound, "that finding is not open any more", null);
                }
                var input = await DeviceInputAsync(fleet, rack, focus, req.DeviceId, now, ct);
                if (input is null) return (Refusal.NotFound, "no such device", null);
                var (deviceId, i) = input.Value;

                var subject = focus is null ? i.DeviceName : $"{i.DeviceName}: {focus.RuleKey}";
                var report = await StartRunAsync(req.Kind, subject, deviceId, focus?.FindingId, focus?.RuleKey, null, by, ct);
                var md = PromptComposer.Device(i, report);
                var file = focus is null ? $"claude-{i.DeviceName}" : $"claude-{i.DeviceName}-{FixLearning.FamilyOf(focus.RuleKey)}";
                return (Refusal.None, null, await FinishAsync(report, focus is null ? $"Claude: {i.DeviceName}" : $"Claude: {focus.Title} on {i.DeviceName}", file, md, ct));
            }
            case "ask":
            {
                var question = req.Question?.Trim();
                if (string.IsNullOrEmpty(question)) return (Refusal.BadRequest, "an ask needs a question", null);
                if (question.Length > MaxQuestionChars) return (Refusal.BadRequest, $"keep the question under {MaxQuestionChars:N0} characters: the session can ask for more", null);
                var (fleet, rack) = await SnapshotsAsync(ct);
                DevicePromptInput? device = null;
                int? deviceId = null;
                if (req.DeviceId is not null)
                {
                    if (await DeviceInputAsync(fleet, rack, null, req.DeviceId, now, ct) is not { } found) return (Refusal.NotFound, "no such device", null);
                    (deviceId, device) = found;
                }
                var accepted = await store.KnowledgeCardsAsync(acceptedOnly: true, ct);
                var applies = device is null ? accepted : device.Cards ?? [];
                var library = accepted.Except(applies).ToList();
                var input = new AskPromptInput(question, by, device, NetworkBrief(), NetworkNow(fleet, rack, now), applies, library, now);

                var subject = $"{device?.DeviceName ?? "network"}: {question}";
                var report = await StartRunAsync("ask", subject, deviceId, null, null, question, by, ct);
                var md = PromptComposer.Ask(input, report);
                return (Refusal.None, null, await FinishAsync(report, $"Claude: {Short(question, 60)}{(device is null ? "" : $" ({device.DeviceName})")}",
                    $"claude-ask-{device?.DeviceName ?? "network"}", md, ct));
            }
            default:
                return (Refusal.BadRequest, "kind must be tool, device, finding or ask", null);
        }
    }

    public const int MaxQuestionChars = 2000;

    /// <summary>Why a proposed card is refused, or null.</summary>
    public static string? InvalidCard(CardProposal c)
        => string.IsNullOrWhiteSpace(c.Title) ? "card.title: what someone would search for"
         : string.IsNullOrWhiteSpace(c.Plain) ? "card.plain: one plain-English line -- what this card is and what approving it will do (no jargon)"
         : string.IsNullOrWhiteSpace(c.Symptom) ? "card.symptom: what the person sees"
         : KnowledgeCards.Invalid(c.AppliesTo) is { } why ? $"card.{why}"
         : c.AmendsCardId is <= 0 ? "card.amends: the id of a card to supersede, or leave it out"
         : null;

    private async Task<(FleetSnapshot Fleet, FleetSnapshot Rack)> SnapshotsAsync(CancellationToken ct)
    {
        var meshRecords = await store.MeshRecordsAsync(ct);
        var fleet = Shown(await store.FleetSnapshotAsync(ct), await store.AgentRecordsAsync(ct), agents, mesh, meshRecords);
        var rack = Api.ApiHost.WithMesh(await store.FleetSnapshotAsync(ct, rack: true), mesh, meshRecords);
        return (fleet, rack);
    }

    /// <summary>Everything NetworkOps knows about one device, with the accepted knowledge cards that apply to it.</summary>
    private async Task<(int DeviceId, DevicePromptInput Input)?> DeviceInputAsync(FleetSnapshot fleet, FleetSnapshot rack, FleetFinding? focus, int? deviceId, DateTime now, CancellationToken ct)
    {
        var isRack = focus is not null ? rack.Devices.Any(d => d.Name.Equals(focus.Device, StringComparison.OrdinalIgnoreCase)) : rack.Devices.Any(d => d.DeviceId == deviceId);
        var snap = isRack ? rack : fleet;
        var dev = focus is not null
            ? snap.Devices.FirstOrDefault(d => d.Name.Equals(focus.Device, StringComparison.OrdinalIgnoreCase))
            : snap.Devices.FirstOrDefault(d => d.DeviceId == deviceId);
        if (dev is null) return null;

        var history = await store.DeviceHistoryAsync(dev.DeviceId, ct);
        var resolutions = (await store.ResolutionRowsAsync(ct)).Select(r => r.ToResolution()).ToList();
        var facts = snap.FactsOf(dev.Name);
        var open = snap.OpenOn(dev.Name).ToList();
        var cards = (await store.KnowledgeCardsAsync(acceptedOnly: true, ct))
            .Where(c => KnowledgeCards.AppliesTo(c.AppliesTo, dev.Name, dev.Kind, facts, open.Select(o => o.RuleKey))).ToList();
        return (dev.DeviceId, new DevicePromptInput(
            dev.Name, dev.Kind, facts,
            open,
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
            await store.LastCheckAsync(dev.DeviceId, ct),
            cards));
    }

    /// <summary>KOR's network as built (Prompts/network.md, embedded beside the code that describes it).</summary>
    public static string NetworkBrief()
    {
        using var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("Kor.Operations.NetworkOps.Service.Prompts.network.md")
            ?? throw new InvalidOperationException("the network brief is not embedded");
        using var r = new StreamReader(s, Encoding.UTF8);
        return r.ReadToEnd();
    }

    /// <summary>What NetworkOps sees across the network now: counts, the worst open problems, the rack's state.</summary>
    internal static IReadOnlyList<string> NetworkNow(FleetSnapshot fleet, FleetSnapshot rack, DateTime now)
    {
        var lines = new List<string>
        {
            $"PCs: {fleet.Devices.Count}, {fleet.Devices.Count(d => d.LastCheckedUtc is { } c && now - c < CommandCenterView.StaleAfter)} checked in the last 3 days; agent connected on {fleet.Devices.Count(d => d.AgentConnected)}; remote control on {fleet.Devices.Count(d => d.MeshConnected)}.",
            $"Open problems: {fleet.OpenFindings.Count} on PCs ({fleet.OpenFindings.Count(f => f.Severity == Core.Health.Severity.Critical)} critical), {rack.OpenFindings.Count} on the rack.",
        };
        foreach (var g in fleet.OpenFindings.GroupBy(f => FixLearning.FamilyOf(f.RuleKey)).OrderByDescending(g => g.Max(f => f.Severity)).ThenByDescending(g => g.Count()).Take(12))
            lines.Add($"{g.First().Title} (`{g.Key}`): {g.Count()} PC(s) -- {string.Join(", ", g.Select(f => f.Device).Distinct().Take(8))}{(g.Count() > 8 ? ", …" : "")}");
        foreach (var d in rack.Devices.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
            lines.Add($"Rack {d.Name}: {d.Summary ?? "no read yet"} (read {CommandCenterView.Ago(d.LastCheckedUtc, now)})");
        foreach (var f in rack.OpenFindings.OrderByDescending(f => f.Severity)) lines.Add($"Rack problem on {f.Device}: {f.Title} -- {f.Evidence}");
        foreach (var p in fleet.Patterns.Take(5)) lines.Add($"Fleet pattern: {p.Summary}");
        return lines;
    }

    private static string Short(string s, int max)
    {
        var one = s.ReplaceLineEndings(" ");
        return one.Length <= max ? one : one[..(max - 1)] + "…";
    }

    public static readonly IReadOnlySet<string> Outcomes = new HashSet<string>(StringComparer.Ordinal) { "solved", "partly", "not-solved", "no-action" };

    public static byte[] HashToken(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private async Task<PromptReport?> StartRunAsync(string kind, string subject, int? deviceId, long? findingId, string? ruleKey, string? question, string by, CancellationToken ct)
    {
        if (!await store.PromptRunsAvailableAsync(ct)) return null;
        var knowledge = await store.KnowledgeAvailableAsync(ct);
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var id = await store.CreatePromptRunAsync(kind, subject, deviceId, findingId, ruleKey, by, HashToken(token), question, knowledge, ct);
        return new PromptReport(id, token, options.Value.ApiPublicUrl, Cards: knowledge);
    }

    private async Task<RenderedPrompt> FinishAsync(PromptReport? report, string title, string file, string markdown, CancellationToken ct)
    {
        if (report is not null) await store.SetPromptHashAsync(report.RunId, SHA256.HashData(Encoding.UTF8.GetBytes(markdown)), ct);
        var safe = new string(file.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray());
        return new RenderedPrompt(report?.RunId, title, $"{safe}{(report is null ? "" : $"-run{report.RunId}")}.md", markdown);
    }

    private string? ConnectUrl(string? nodeId) => Core.Learning.MeshLinks.DeviceUrl(options.Value.MeshUrl, nodeId, viewMode: 11);

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
