#nullable enable
using System.Text;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Core.Prompts;

/// <summary>How the session can reach the machine, as NetworkOps knows it right now.</summary>
public sealed record PromptAccess(bool IsRack, string? Presence, bool AgentConnected, string? AgentVersion, bool MeshConnected, string? ConnectUrl, string? RackSummary);

/// <summary>Where and how the session reports its outcome. Null when reporting is not available (migration 007).</summary>
/// <param name="Cards">Whether the session can bank a knowledge card with its report (migration 008).</param>
public sealed record PromptReport(long RunId, string Token, string ApiBaseUrl, bool Cards = false);

/// <summary>What an earlier Claude session learned about this kind of problem, accepted by Ian.</summary>
public sealed record SessionLearning(DateTime AtUtc, string Device, string Text);

/// <summary>The device's last good check exactly as the machine returned it (probe JSON).</summary>
public sealed record LastCheck(string Probe, DateTime AtUtc, string Json);

/// <summary>Everything a device or finding prompt is made of -- read from the database at the moment it is opened.</summary>
public sealed record DevicePromptInput(
    string DeviceName,
    string Kind,
    IReadOnlyDictionary<string, string> Facts,
    IReadOnlyList<FleetFinding> Open,
    FleetFinding? Focus,
    IReadOnlyList<ClearedFinding> Cleared,
    IReadOnlyList<FactChangeAt> Changes,
    IReadOnlyList<ActionRow> Actions,
    IReadOnlyList<NoteRow> Notes,
    IReadOnlyList<string> SameElsewhere,
    IReadOnlyList<ActivePattern> Patterns,
    IReadOnlyList<LearnedFix> Learned,
    KnowledgeEntry? Knowledge,
    IReadOnlyList<SessionLearning> FromSessions,
    PromptAccess Access,
    DateTime? LastCheckedUtc,
    DateTime NowUtc,
    LastCheck? Raw = null,
    IReadOnlyList<KnowledgeCard>? Cards = null);

/// <summary>A question in the person's own words: about one machine (Device set) or about the whole network.</summary>
/// <param name="NetworkBrief">How KOR's network is laid out (Service/Prompts/network.md, kept beside the code).</param>
/// <param name="NetworkNow">What NetworkOps sees across the network right now, one line each.</param>
/// <param name="Cards">Accepted cards that apply to the machine (or, network-wide, every accepted card).</param>
/// <param name="Library">Every other accepted card, by title: the session pulls one in full with `netops knowledge`.</param>
public sealed record AskPromptInput(
    string Question,
    string AskedBy,
    DevicePromptInput? Device,
    string NetworkBrief,
    IReadOnlyList<string> NetworkNow,
    IReadOnlyList<KnowledgeCard> Cards,
    IReadOnlyList<KnowledgeCard> Library,
    DateTime NowUtc);

// A Claude prompt is never stored as a finished document: it is composed here, from what NetworkOps knows at the moment
// someone opens it, so it cannot go stale. Pure -- the service gathers the input, this only writes it -- so a test can
// prove what a prompt says. Every prompt carries the same working rules and ends with the same instruction: report the
// outcome back, so what the session learned comes into the system instead of staying in a terminal.
//
// The session runs in a terminal on the person's own PC, often over the VPN. It never reaches a machine from there:
// every read goes to NetworkOps on APP01, which runs it on the machine (Reach, below).
public static class PromptComposer
{
    public const string RepoPath = @"C:\VIsual Studio Projects\Operations";

    /// <summary>A health check is ~10 KB; anything far past that would crowd out the rest of the prompt.</summary>
    public const int MaxRawChars = 60_000;

    /// <summary>A prompt about one device -- or, with a Focus, about one finding on it.</summary>
    public static string Device(DevicePromptInput i, PromptReport? report)
    {
        var sb = new StringBuilder();
        var subject = i.Focus is { } f ? $"Solve \"{f.Title}\" on {i.DeviceName}" : $"Look at {i.DeviceName}";
        sb.AppendLine($"# {subject}");
        sb.AppendLine();
        sb.AppendLine($"Written by NetworkOps at {i.NowUtc:yyyy-MM-dd HH:mm} UTC from its live database: everything below is what it knows about {i.DeviceName} right now.");
        sb.AppendLine(i.Focus is not null
            ? $"Your job: find out why this is happening on {i.DeviceName}, fix it if the fix is safe and allowed (rules below), and report back."
            : $"Your job: review {i.DeviceName}'s open problems, decide what matters and why, fix what is safe and allowed, and report back.");
        sb.AppendLine();
        Rules(sb);
        Reach(sb, i.DeviceName);
        Machine(sb, i);
        Cards(sb, i.Cards ?? [], []);
        Raw(sb, i);
        Closing(sb, report, i.DeviceName);
        return sb.ToString();
    }

    /// <summary>The person's own question, about one machine or the whole network, with everything NetworkOps knows around it.</summary>
    public static string Ask(AskPromptInput a, PromptReport? report)
    {
        var sb = new StringBuilder();
        var where = a.Device?.DeviceName;
        sb.AppendLine(where is null ? "# A question about KOR's network" : $"# A question about {where}");
        sb.AppendLine();
        sb.AppendLine($"Written by NetworkOps at {a.NowUtc:yyyy-MM-dd HH:mm} UTC from its live database, for {a.AskedBy}.");
        sb.AppendLine();
        sb.AppendLine("## The question");
        foreach (var line in a.Question.Trim().Split('\n')) sb.AppendLine($"> {line.TrimEnd()}");
        sb.AppendLine();
        sb.AppendLine("Your job: investigate it with what is below and what you read through NetworkOps, answer it, fix it if the fix is safe and allowed (rules below), and report back -- with a knowledge card if what you found would help on another machine or next time.");
        sb.AppendLine();
        Rules(sb);
        Reach(sb, where ?? "<machine>");
        sb.AppendLine("## The network");
        sb.AppendLine(a.NetworkBrief.TrimEnd());
        sb.AppendLine();
        if (a.NetworkNow.Count > 0)
        {
            sb.AppendLine("## Across the network right now");
            foreach (var l in a.NetworkNow) sb.AppendLine($"- {l}");
            sb.AppendLine();
        }
        if (a.Device is { } d) Machine(sb, d);
        Cards(sb, a.Cards, a.Library);
        if (a.Device is { } raw) Raw(sb, raw);
        Closing(sb, report, where);
        return sb.ToString();
    }

    /// <summary>A prompt about one of KOR's tools: its fixed brief (kept beside its code) plus what NetworkOps sees live.</summary>
    public static string Tool(string title, string brief, IReadOnlyList<(string Label, string Value)> live, PromptReport? report, DateTime nowUtc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {title}");
        sb.AppendLine();
        sb.AppendLine($"Written by NetworkOps at {nowUtc:yyyy-MM-dd HH:mm} UTC. The brief below is kept beside the tool's code (it changes in the same commit as the tool); the live state is read from the database and the running service now.");
        sb.AppendLine();
        Rules(sb);
        sb.AppendLine(brief.TrimEnd());
        sb.AppendLine();
        sb.AppendLine("## Live state");
        foreach (var (label, value) in live) sb.AppendLine($"- {label}: {value}");
        sb.AppendLine();
        Closing(sb, report, null);
        return sb.ToString();
    }

    private static void Rules(StringBuilder sb)
    {
        sb.AppendLine("## Working rules (binding)");
        sb.AppendLine($"- Work from the repo `{RepoPath}` and follow its `CLAUDE.md`. Search before you build; verify by reading back.");
        sb.AppendLine("- Read first. Changes to a machine go through NetworkOps (Command Center → Fix…, which is allow-listed, run as SYSTEM, audited and re-checked). Anything else that changes a PC, a server, GPO, the firewall or DNS needs Ian's OK first.");
        sb.AppendLine("- This repo and the live service are shared by other sessions. Solving a finding does NOT include changing the repo's code or DEPLOYING (service, app or agent): if the real fix is a code change, say so and hand it to Ian rather than shipping it from here. A deploy restarts the live service and carries every session's uncommitted changes. Report the finding either way.");
        sb.AppendLine("- Never echo, store or commit a password or key. Never content-search OneDrive. Never kill processes by name machine-wide.");
        sb.AppendLine("- KOR-1001 is the machine this terminal runs on (Ian's PC, often on the VPN).");
        sb.AppendLine("- Say what you checked and what you found; state as fact only what is in live output.");
        sb.AppendLine();
    }

    // How a session reads a machine. It runs on the person's PC, usually over the VPN, so it never touches the machine
    // itself: APP01 does, on the same network as every PC and server. And it reads without wasting time -- learned
    // 2026-10-01, when a session spent 10 of its 14 minutes on one event-log query that rendered every entry's text.
    private static void Reach(StringBuilder sb, string machine)
    {
        sb.AppendLine("## Reading a machine: always through APP01");
        sb.AppendLine($"Everything you read on a machine goes through NetworkOps on APP01 -- never from this PC to that one, which is usually on the far side of the VPN. Run from `{RepoPath}` (build once if needed: `dotnet build Kor.Operations.NetworkOps.Cli`):");
        sb.AppendLine($"- `netops run --script x.ps1 --hosts {machine} --timeout 90` -- APP01 runs your script ON the machine as SYSTEM (Windows PowerShell 5.1), through its agent in about a second, else APP01's own network route; the output comes back as JSON. Audited under your sign-in. Works for PCs and the Windows servers (KOR-APP01, KOR-DC01, ...).");
        sb.AppendLine($"- `netops last-check --hosts {machine}` -- the last full health check NetworkOps stored, as the machine returned it.");
        sb.AppendLine($"- `netops check --hosts {machine}` -- a fresh full health check now (stored, so the Command Center and its learning see it too).");
        sb.AppendLine("- `netops knowledge --search <words>` -- the knowledge banked from earlier sessions.");
        sb.AppendLine($"- `netops history --hosts {machine}` -- its past problems, what changed on it, notes, and every fix or run on it (with ids).");
        sb.AppendLine("- `netops action --id N` / `netops trigger --id N` -- one fix or run (status, full output) / one queued check or job.");
        sb.AppendLine("- `netops findings --hosts A,B` and `netops changes --since 24h` -- what is open now (PCs and rack), and what opened, cleared and was done since.");
        sb.AppendLine("- `netops fix --hosts A,B --fix <catalog id>` -- a catalog fix, exactly as the Command Center's Fix… runs it. --hosts takes PCs and rack devices alike (KOR-FS01), or all / rack.");
        sb.AppendLine("- The first call asks you to sign in: the same Entra sign-in and MFA as the app.");
        sb.AppendLine("How to read quickly:");
        sb.AppendLine("- Put every read into ONE script; return one object (it is converted to JSON for you).");
        sb.AppendLine("- Event logs: `Get-WinEvent -FilterHashtable @{ LogName=...; ProviderName=...; Id=...; StartTime=... } -MaxEvents N` and read `.Properties[n].Value`. NEVER render `.Message` across a whole log or thousands of events: that alone took over 10 minutes on KOR-217.");
        sb.AppendLine("- If a call needs more than 90 s, the script is the problem, not the network: narrow it and run it again; do not wait on it in the background.");
        sb.AppendLine();
    }

    private static void Machine(StringBuilder sb, DevicePromptInput i)
    {
        sb.AppendLine("## The machine");
        sb.AppendLine($"- **{i.DeviceName}** ({i.Kind}), last checked {CommandCenterView.Ago(i.LastCheckedUtc, i.NowUtc)}.");
        foreach (var (k, v) in i.Facts.Where(kv => IsIdentity(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Take(40))
            sb.AppendLine($"- {k}: {v}");
        var apps = i.Facts.Where(kv => !IsIdentity(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key[4..]} {kv.Value}").ToList();
        if (apps.Count > 0) sb.AppendLine($"- Apps ({apps.Count}): {string.Join("; ", apps.Take(60))}{(apps.Count > 60 ? "; …" : "")}");
        var a = i.Access;
        if (!a.IsRack)
        {
            sb.AppendLine($"- Who is on it at the last check: {a.Presence ?? "not known"}.");
            sb.AppendLine(a.AgentConnected
                ? $"- NetworkOps agent {a.AgentVersion} is connected: reads and fixes reach it in about a second."
                : a.AgentVersion is not null ? $"- NetworkOps agent {a.AgentVersion} is installed but NOT connected: APP01 falls back to its network route (20-70 s per call)." : "- No NetworkOps agent: APP01 uses its network route (a one-shot service over SMB, 20-70 s per call).");
        }
        else if (a.RackSummary is { } rs) sb.AppendLine($"- Last read: {rs}.");
        sb.AppendLine(a.ConnectUrl is { } url
            ? $"- Remote screen: {url} ({(a.MeshConnected ? "Mesh agent connected" : "Mesh agent NOT connected right now")}). If nobody is at it or its monitors are off, use RDP from the same page."
            : "- Remote control: no Mesh agent on it.");
        sb.AppendLine(i.Raw is { } raw
            ? $"- **Read first:** its last full {raw.Probe} check ({CommandCenterView.Ago(raw.AtUtc, i.NowUtc)}) is at the end of this prompt, exactly as the machine returned it. Most questions are answered there; go to the machine only for what it does not hold."
            : "- There is no stored check for it yet: read it on the machine.");
        sb.AppendLine();

        if (i.Focus is { } p)
        {
            sb.AppendLine($"## The problem: {p.Title}");
            sb.AppendLine($"- {Sev(p.Severity)} · rule `{p.RuleKey}` · open since {p.FirstSeenUtc:yyyy-MM-dd HH:mm} UTC · seen {CommandCenterView.Ago(p.LastSeenUtc, i.NowUtc)}");
            sb.AppendLine($"- Evidence: {p.Evidence}");
            if (p.AcknowledgedUtc is not null) sb.AppendLine($"- Acknowledged by {p.AcknowledgedBy}{(p.AckNote is null ? "" : $": {p.AckNote}")}");
            if (i.Knowledge is { } k)
            {
                sb.AppendLine($"- What it means: {k.Meaning}");
                if (k.Causes.Count > 0) sb.AppendLine($"- Likely causes (KOR's own diagnoses): {string.Join("; ", k.Causes)}");
                if (k.Fixes.Count > 0) sb.AppendLine($"- Known fixes, most likely first: {string.Join("; ", k.Fixes)}");
                sb.AppendLine($"- If nobody acts: {k.IfIgnored}");
            }
            sb.AppendLine(i.Learned.Count > 0
                ? $"- What has cleared it on THIS fleet: {string.Join("; ", i.Learned.Select(l => $"{l.Change} ({l.Times} of {l.OfResolutions} times)"))}"
                : "- What has cleared it on this fleet: nothing learned yet.");
            foreach (var s in i.FromSessions.OrderByDescending(s => s.AtUtc).Take(8))
                sb.AppendLine($"- Learned in an earlier session ({s.Device}, {s.AtUtc:yyyy-MM-dd}, accepted by Ian): {s.Text}");
            sb.AppendLine(i.SameElsewhere.Count > 0 ? $"- The same problem is open on: {string.Join(", ", i.SameElsewhere)}" : "- No other machine has it right now.");
            foreach (var pat in i.Patterns) sb.AppendLine($"- Fleet pattern: {pat.Summary}");
            sb.AppendLine();
        }

        var others = i.Open.Where(o => i.Focus is null || o.FindingId != i.Focus.FindingId).OrderByDescending(o => o.Severity).ToList();
        sb.AppendLine(i.Focus is null ? "## Open problems" : "## Everything else open on it");
        if (others.Count == 0) sb.AppendLine("- none");
        foreach (var o in others)
            sb.AppendLine($"- {Sev(o.Severity)} **{o.Title}** (`{o.RuleKey}`) -- {o.Evidence}{(o.IsQuiet(i.NowUtc) ? " [acknowledged/snoozed]" : "")}");
        sb.AppendLine();

        if (i.Changes.Count > 0)
        {
            sb.AppendLine("## What changed on it recently");
            foreach (var c in i.Changes.Take(12)) sb.AppendLine($"- {c.AtUtc:yyyy-MM-dd} {c.Description}");
            sb.AppendLine();
        }
        if (i.Cleared.Count > 0)
        {
            sb.AppendLine("## Past problems");
            foreach (var c in i.Cleared.OrderByDescending(c => c.ClearedUtc).Take(10))
                sb.AppendLine($"- {c.Title}: {c.FirstSeenUtc:yyyy-MM-dd} → {c.ClearedUtc:yyyy-MM-dd}{(c.Resolution is null ? "" : $" ({c.Resolution})")}");
            sb.AppendLine();
        }
        if (i.Actions.Count > 0)
        {
            sb.AppendLine("## Done to it through NetworkOps");
            foreach (var x in i.Actions.OrderByDescending(x => x.RequestedUtc).Take(10))
                sb.AppendLine($"- {x.RequestedUtc:yyyy-MM-dd HH:mm} {x.Kind} by {x.RequestedBy}: {x.Status}{(x.Detail is null ? "" : $" -- {Trim(x.Detail, 160)}")}");
            sb.AppendLine();
        }
        if (i.Notes.Count > 0)
        {
            sb.AppendLine("## Notes");
            foreach (var n in i.Notes.OrderByDescending(n => n.CreatedUtc).Take(6)) sb.AppendLine($"- {n.CreatedUtc:yyyy-MM-dd} {n.Author}: {Trim(n.Body, 300)}");
            sb.AppendLine();
        }
    }

    /// <summary>At most this many cards are written out in full; the rest are listed by title.</summary>
    public const int MaxCardsInFull = 15;

    private static void Cards(StringBuilder sb, IReadOnlyList<KnowledgeCard> cards, IReadOnlyList<KnowledgeCard> library)
    {
        if (cards.Count == 0 && library.Count == 0) return;
        sb.AppendLine("## What KOR has learned that applies here (accepted knowledge cards)");
        if (cards.Count == 0) sb.AppendLine("- none applies to this machine directly.");
        foreach (var c in cards.OrderByDescending(c => c.CreatedUtc).Take(MaxCardsInFull))
        {
            sb.AppendLine($"### {c.Title}  (card {c.CardId}; applies to: {c.AppliesTo}{(c.SourceDevice is null ? "" : $"; found on {c.SourceDevice}")}, {c.CreatedUtc:yyyy-MM-dd})");
            sb.AppendLine($"- Symptom: {c.Symptom}");
            if (c.Cause is { Length: > 0 }) sb.AppendLine($"- Cause: {c.Cause}");
            if (c.Check is { Length: > 0 }) sb.AppendLine($"- How to check: {c.Check}");
            if (c.Fix is { Length: > 0 }) sb.AppendLine($"- Fix: {c.Fix}");
        }
        var rest = cards.Skip(MaxCardsInFull).Concat(library).ToList();
        if (rest.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Also banked (read one in full with `netops knowledge --search <words>`):");
            foreach (var c in rest.OrderByDescending(c => c.CreatedUtc).Take(60)) sb.AppendLine($"- card {c.CardId}: {c.Title} (applies to: {c.AppliesTo})");
        }
        sb.AppendLine();
    }

    private static void Raw(StringBuilder sb, DevicePromptInput i)
    {
        if (i.Raw is not { } last) return;
        sb.AppendLine($"## Its last full check ({last.Probe}, {last.AtUtc:yyyy-MM-dd HH:mm} UTC, as returned)");
        if (last.Json.Length <= MaxRawChars)
        {
            sb.AppendLine("```json");
            sb.AppendLine(last.Json.Trim());
            sb.AppendLine("```");
        }
        else sb.AppendLine($"Too large to carry here ({last.Json.Length:N0} characters): `netops last-check --hosts {i.DeviceName}`.");
        sb.AppendLine();
    }

    private static void Closing(StringBuilder sb, PromptReport? report, string? device)
    {
        sb.AppendLine("## When you are done: report back (required)");
        if (report is null)
        {
            sb.AppendLine($"Reporting is not switched on yet (migration 007). Put the outcome in a note on {device ?? "the device"} in the Command Center: what was wrong, what you did, whether it worked, and anything worth adding to NetworkOps' knowledge.");
            return;
        }
        sb.AppendLine("Tell NetworkOps what happened, so the next session -- and the fleet's fix learning -- starts from it. Run this once, with your own words in it:");
        sb.AppendLine();
        sb.AppendLine("```powershell");
        sb.AppendLine("$body = @{");
        sb.AppendLine("    outcome = 'solved'          # solved | partly | not-solved | no-action");
        sb.AppendLine("    summary = 'What was wrong, what you did, and how you know it worked.'");
        sb.AppendLine("    learned = ''                # a cause or fix NetworkOps should know next time (optional; Ian approves it)");
        if (report.Cards)
        {
            sb.AppendLine("    # Bank it for other machines and next time (optional; Ian approves it). Leave card out if nothing generalises.");
            sb.AppendLine("    card = @{");
            sb.AppendLine("        plain     = 'In plain English, no jargon: what this card is and what approving it will DO. Ian reads this first, and decides from it.'");
            sb.AppendLine("        title     = 'ETABS crashes opening large models'   # what someone would search for");
            sb.AppendLine("        appliesTo = 'app:etabs'   # any | app:<name> | model:<text> | gpu:<text> | kind:<kind> | device:<name> | finding:<rule>; comma = or. Name EVERY model/machine it covers, from what you saw.");
            sb.AppendLine("        symptom   = 'What the person sees.'");
            sb.AppendLine("        cause     = 'What it actually was, and the evidence that proved it.'");
            sb.AppendLine("        check     = 'How to tell on another machine: what to read, and what it looks like when it is this.'");
            sb.AppendLine("        fix       = 'What cleared it, and how you know it did.'");
            sb.AppendLine("        tags      = 'etabs, crash'");
            sb.AppendLine("        # amends  = 123   # to CORRECT or WIDEN an existing card (use its id from the list above): Ian approves it as a new version, the old one retires. Do this -- do NOT write prose asking Ian to edit a card by hand.");
            sb.AppendLine("    }");
        }
        sb.AppendLine("} | ConvertTo-Json -Depth 4");
        sb.AppendLine($"Invoke-RestMethod -Method Post -Uri '{report.ApiBaseUrl.TrimEnd('/')}/api/prompt-runs/{report.RunId}/outcome' -Headers @{{ 'X-Prompt-Token' = '{report.Token}' }} -ContentType 'application/json' -Body $body -SkipCertificateCheck");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("If you report and then learn more (you fixed it after all, or the cause was something else), send it again with the same token: the new report REPLACES the earlier outcome, learning and card. That works until Ian decides the run.");
        sb.AppendLine($"The token is for this run ({report.RunId}) only. The outcome lands on {(device is null ? "the run" : device)} as a note and in the Prompt Library.");
    }

    /// <summary>Identity and state facts worth a session's attention; app version facts (dozens) are left out.</summary>
    private static bool IsIdentity(string fact) => !fact.StartsWith("app.", StringComparison.Ordinal);

    private static string Sev(Health.Severity s) => s switch { Health.Severity.Critical => "CRITICAL", Health.Severity.Warning => "Warning", _ => "Info" };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
