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

// ---- Windows updates (Core/Updates): what is waiting on each machine, and installing it on many at once, on demand.

/// <param name="Target">False when NetworkOps cannot run anything on it (Why says why): it is listed, never silently left out.</param>
/// <param name="Guard">Alone (only in a batch by itself: the domain controller) | NoRestart (never restarted from here: the machine running NetworkOps).</param>
/// <param name="ScanStatus">Ok | the reason the last search failed | null = never searched.</param>
/// <param name="Pending">What the last search found waiting; empty when nothing is.</param>
/// <param name="LastInstall">The last "Install updates" run on it: status and result line.</param>
public sealed record UpdateRow(int DeviceId, string Name, string Kind, bool IsServer, bool Target, string? Why, string? Guard,
    string? Presence, string? PresenceState, DateTime? ScannedUtc, string? ScanStatus, bool RebootPending,
    IReadOnlyList<Updates.PendingUpdate> Pending, Health.Severity? Due, string? DueTitle, string? LastInstall, DateTime? LastInstallUtc);

/// <param name="Restart">none | if-needed</param>
/// <param name="Confirmed">Set after being told someone is actively using one of the machines (a restart is refused otherwise).</param>
public sealed record UpdateInstallRequest(IReadOnlyList<int> DeviceIds, string Restart, bool Confirmed);

/// <summary>Per machine: queued (ActionId) or refused (why), and whether confirming would let it run.</summary>
/// <param name="Note">Queued, but not quite as asked (APP01: installed without the restart).</param>
public sealed record UpdateInstallOutcome(int DeviceId, string Name, long? ActionId, string? Refused, bool NeedsConfirmation, string? Note = null);

// ---- the Prompt Library: Claude prompts generated from the live database when opened (Core/Prompts/PromptComposer).

public sealed record PromptTool(string Id, string Title, string Summary);
public sealed record PromptFinding(long FindingId, string RuleKey, string Title, Health.Severity Severity);
public sealed record PromptSubject(int DeviceId, string Device, string Kind, IReadOnlyList<PromptFinding> Findings);
public sealed record PromptCatalog(IReadOnlyList<PromptTool> Tools, IReadOnlyList<PromptSubject> Devices, bool Reporting);

/// <param name="Kind">tool | device | finding | ask</param>
/// <param name="Question">An ask's own question, in the person's words. An ask with no DeviceId is about the whole network.</param>
public sealed record PromptRequest(string Kind, string? ToolId, int? DeviceId, long? FindingId, string? Question = null);

/// <param name="RunId">The recorded run the session reports back to; null when reporting is not available (migration 007).</param>
public sealed record RenderedPrompt(long? RunId, string Title, string FileName, string Markdown);

/// <param name="Outcome">solved | partly | not-solved | no-action</param>
/// <param name="Card">What the session found, as knowledge to bank for other machines (migration 008); Ian accepts it or not.</param>
public sealed record PromptOutcome(string Outcome, string Summary, string? Learned, CardProposal? Card = null);

public sealed record PromptRunRow(long RunId, string Kind, string Subject, string CreatedBy, DateTime CreatedUtc, DateTime? OutcomeUtc,
    string? Outcome, string? Summary, string? LearnedText, string? LearnedStatus, string? Question = null, string? CardTitle = null);

// ---- knowledge cards (db/KorNetworkOps/008): what a session learned, banked so every later prompt about a machine it
// applies to starts from it. A card is written by the session, accepted or rejected by Ian, never edited by anything else.

/// <param name="AppliesTo">Which machines it concerns: comma-separated terms, any one matching (Core/Prompts/KnowledgeCards).</param>
public sealed record CardProposal(string Title, string AppliesTo, string Symptom, string? Cause, string? Check, string? Fix, string? Tags);

public sealed record KnowledgeCard(long CardId, string Title, string AppliesTo, string Symptom, string? Cause, string? Check, string? Fix,
    string? Tags, string? SourceDevice, long? SourceRunId, string Status, DateTime CreatedUtc);

// ---- a Claude session reading a machine THROUGH APP01 (netops run): the service runs the script on the machine -- through
// its agent, else APP01's own network route -- so nothing goes from the person's PC to the machine.

/// <param name="Purpose">One line on why, for the audit.</param>
/// <param name="PromptRunId">The prompt run the session is working, when it has one.</param>
public sealed record RemoteRunRequest(string Script, int? TimeoutSeconds, string? Purpose, long? PromptRunId);

/// <param name="Route">agent | network</param>
public sealed record RemoteRunResult(long ActionId, string Device, bool Ok, string Route, int Ms, string? OutputJson, string? Error);

public sealed record LastCheckView(string Device, string Probe, DateTime AtUtc, string Json);

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
