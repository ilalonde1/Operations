#nullable enable
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

// What the Command Center API returns and the page reads: one definition, shared by the service that
// fills it and the page that draws it, so the two can never drift. Plain records, JSON both ways
// (web defaults: camelCase, case-insensitive).

public sealed record DeviceRow(int DeviceId, string Name, DateTime? LastReachableUtc, DateTime? LastCheckedUtc);

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

public sealed record DeviceHistory(IReadOnlyList<ClearedFinding> Cleared, IReadOnlyList<FactHistoryRow> Facts, IReadOnlyList<NoteRow> Notes);

public sealed record TriggerState(string Status, string? Result, DateTime RequestedUtc, DateTime? ClaimedUtc, DateTime? CompletedUtc);

/// <summary>Request bodies.</summary>
public sealed record AnnotateRequest(string? Note, DateTime? UntilUtc);

public sealed record NoteRequest(string Body);

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
