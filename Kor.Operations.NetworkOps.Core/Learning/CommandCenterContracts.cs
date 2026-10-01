#nullable enable
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

// What the Command Center API returns and the page reads: one definition, shared by the service that
// fills it and the page that draws it, so the two can never drift. Plain records, JSON both ways
// (web defaults: camelCase, case-insensitive).

/// <param name="Kind">Workstation for PCs; a Core.Rack.RackKinds value for the rack.</param>
/// <param name="Summary">Rack devices: the last read's one-line summary ("5 of 6 VMs running · 181 sensors ...").</param>
/// <param name="Presence">PCs: who was on it at the last check ("kevinw · active", "nobody signed in").</param>
/// <param name="PresenceState">Active | Locked | RemoteOnly | Nobody -- what a disruptive fix checks before it runs.</param>
/// <param name="AgentVersion">PCs with the endpoint agent installed: its version (null = no agent; checks use the network route).</param>
/// <param name="AgentConnected">The agent is calling in right now, so checks and fixes reach the PC through it.</param>
/// <param name="AgentLastContactUtc">When the agent was last heard from.</param>
public sealed record DeviceRow(int DeviceId, string Name, DateTime? LastReachableUtc, DateTime? LastCheckedUtc, string Kind = "Workstation", string? Summary = null,
    string? Presence = null, string? PresenceState = null, string? AgentVersion = null, bool AgentConnected = false, DateTime? AgentLastContactUtc = null,
    string? MeshNodeId = null, bool MeshConnected = false);
// MeshNodeId: remote control (MeshCentral) -- the device's node id ("node//..."), null when it has no Mesh agent;
// the Command Center's Connect button opens it. MeshConnected: its Mesh agent was connected at the last read.

/// <param name="Action">install (remote control: the Mesh agent)</param>
public sealed record MeshRequest(string Action);

// ---- the Prompt Library: Claude prompts generated from the live database when opened (Core/Prompts/PromptComposer).

public sealed record PromptTool(string Id, string Title, string Summary);
public sealed record PromptFinding(long FindingId, string RuleKey, string Title, Health.Severity Severity);
public sealed record PromptSubject(int DeviceId, string Device, string Kind, IReadOnlyList<PromptFinding> Findings);
public sealed record PromptCatalog(IReadOnlyList<PromptTool> Tools, IReadOnlyList<PromptSubject> Devices, bool Reporting);

/// <param name="Kind">tool | device | finding</param>
public sealed record PromptRequest(string Kind, string? ToolId, int? DeviceId, long? FindingId);

/// <param name="RunId">The recorded run the session reports back to; null when reporting is not available (migration 007).</param>
public sealed record RenderedPrompt(long? RunId, string Title, string FileName, string Markdown);

/// <param name="Outcome">solved | partly | not-solved | no-action</param>
public sealed record PromptOutcome(string Outcome, string Summary, string? Learned);

public sealed record PromptRunRow(long RunId, string Kind, string Subject, string CreatedBy, DateTime CreatedUtc, DateTime? OutcomeUtc,
    string? Outcome, string? Summary, string? LearnedText, string? LearnedStatus);

/// <param name="Decision">accept | reject</param>
public sealed record LearnedDecision(string Decision);

public sealed record ServiceBeat(string Host, DateTime StartedUtc, DateTime LastBeatUtc, string? Version);

public sealed record FleetSnapshot(
    IReadOnlyList<DeviceRow> Devices,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> FactsByDevice,
    IReadOnlyList<FleetFinding> OpenFindings,
    IReadOnlyList<ActivePattern> Patterns,
    ServiceBeat? Service)
{
    private static readonly IReadOnlyDictionary<string, string> NoFacts = new Dictionary<string, string>();

    // Case-insensitive whatever dictionary arrived: after a JSON round trip the comparer is ordinal.
    public IReadOnlyDictionary<string, string> FactsOf(string device)
        => FactsByDevice.TryGetValue(device, out var f) ? f
         : FactsByDevice.FirstOrDefault(kv => kv.Key.Equals(device, StringComparison.OrdinalIgnoreCase)).Value ?? NoFacts;

    public IEnumerable<FleetFinding> OpenOn(string device)
        => OpenFindings.Where(f => f.Device.Equals(device, StringComparison.OrdinalIgnoreCase));
}

public sealed record ClearedFinding(string RuleKey, Severity Severity, string Title, string Evidence, DateTime FirstSeenUtc, DateTime ClearedUtc, string? Resolution);

public sealed record FactHistoryRow(string Fact, string Value, DateTime FirstSeenUtc, DateTime? SupersededUtc);

public sealed record NoteRow(string Author, DateTime CreatedUtc, string Body);

public sealed record DeviceHistory(IReadOnlyList<ClearedFinding> Cleared, IReadOnlyList<FactHistoryRow> Facts, IReadOnlyList<NoteRow> Notes,
    IReadOnlyList<ActionRow>? Actions = null);

// ---- fixes: what the page may run on a machine (Core/Actions/FixCatalog), and what became of each run.

/// <summary>A fix as the page offers it.</summary>
public sealed record FixOption(string Id, string Title, string Explain, bool Disruptive, string? ParamLabel, string? PrefilledParam);

/// <param name="Confirmed">Set by the person after being told someone is actively using the machine (a disruptive fix is refused otherwise).</param>
public sealed record FixRequest(string ActionId, string? Param, string? FindingKey, string? Note, bool Confirmed);

/// <summary>One fix run: Requested -> Running -> Done | Failed | Refused.</summary>
public sealed record ActionRow(long ActionId, string Kind, string RequestedBy, DateTime RequestedUtc, DateTime? CompletedUtc, string Status, string? Detail, string? Output);

public sealed record TriggerState(string Status, string? Result, DateTime RequestedUtc, DateTime? ClaimedUtc, DateTime? CompletedUtc);

/// <summary>Request bodies.</summary>
public sealed record AnnotateRequest(string? Note, DateTime? UntilUtc);

public sealed record NoteRequest(string Body);

/// <param name="Action">install (also the upgrade) | remove</param>
public sealed record AgentRequest(string Action);

// ---- rack power: what GET /api/power returns.

public sealed record UpsRow(string Name, string Address, DateTime AtUtc, bool Reachable, string Source, int? SecondsOnBattery,
    int? MinutesRemaining, int? ChargePercent, int? LoadPercent, bool BatteryLow, bool ReplaceBattery, string? Error);

public sealed record PowerEventRow(DateTime AtUtc, string Kind, bool DryRun, bool Ok, string Text);

/// <param name="Level">Normal | Degraded | Trigger, or Off when the watcher is not configured.</param>
/// <param name="Armed">False: a real outage runs the chain as a dry run only.</param>
public sealed record PowerSnapshot(IReadOnlyList<UpsRow> Ups, string Level, string Reason, DateTime? LevelSinceUtc, bool Armed,
    IReadOnlyList<PowerEventRow> RecentEvents);

/// <summary>A resolution as the page needs it for fix learning (Resolution itself carries computed members).</summary>
public sealed record ResolutionRow(string RuleKey, DateTime ClearedUtc, bool Rebooted, IReadOnlyList<FactChange> ChangedFacts, IReadOnlyList<string> Actions)
{
    public Resolution ToResolution() => new(RuleKey, ClearedUtc, Rebooted, ChangedFacts, Actions);
}
