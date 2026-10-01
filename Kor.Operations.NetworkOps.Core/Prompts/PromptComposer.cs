#nullable enable
using System.Text;
using Kor.Operations.NetworkOps.Core.Learning;

namespace Kor.Operations.NetworkOps.Core.Prompts;

/// <summary>How the session can reach the machine, as NetworkOps knows it right now.</summary>
public sealed record PromptAccess(bool IsRack, string? Presence, bool AgentConnected, string? AgentVersion, bool MeshConnected, string? ConnectUrl, string? RackSummary);

/// <summary>Where and how the session reports its outcome. Null when reporting is not available (migration 007 not run).</summary>
public sealed record PromptReport(long RunId, string Token, string ApiBaseUrl);

/// <summary>What an earlier Claude session learned about this kind of problem, accepted by Ian.</summary>
public sealed record SessionLearning(DateTime AtUtc, string Device, string Text);

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
    DateTime NowUtc);

// A Claude prompt is never stored as a finished document: it is composed here, from what NetworkOps knows at the moment
// someone opens it, so it cannot go stale. Pure -- the service gathers the input, this only writes it -- so a test can
// prove what a prompt says. Every prompt carries the same working rules and ends with the same instruction: report the
// outcome back, so what the session learned comes into the system instead of staying in a terminal.
public static class PromptComposer
{
    public const string RepoPath = @"C:\VIsual Studio Projects\Operations";

    /// <summary>A prompt about one device -- or, with a Focus, about one finding on it.</summary>
    public static string Device(DevicePromptInput i, PromptReport? report)
    {
        var sb = new StringBuilder();
        var subject = i.Focus is { } f ? $"Solve \"{f.Title}\" on {i.DeviceName}" : $"Look at {i.DeviceName}";
        sb.AppendLine($"# {subject}");
        sb.AppendLine();
        sb.AppendLine($"Written by NetworkOps at {i.NowUtc:yyyy-MM-dd HH:mm} UTC from its live database: everything below is what it knows about {i.DeviceName} right now.");
        if (i.Focus is { } focus)
            sb.AppendLine($"Your job: find out why this is happening on {i.DeviceName}, fix it if the fix is safe and allowed (rules below), and report back.");
        else
            sb.AppendLine($"Your job: review {i.DeviceName}'s open problems, decide what matters and why, fix what is safe and allowed, and report back.");
        sb.AppendLine();
        Rules(sb);

        sb.AppendLine("## The machine");
        sb.AppendLine($"- **{i.DeviceName}** ({i.Kind}), last checked {CommandCenterView.Ago(i.LastCheckedUtc, i.NowUtc)}.");
        foreach (var (k, v) in i.Facts.Where(kv => IsIdentity(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Take(40))
            sb.AppendLine($"- {k}: {v}");
        var apps = i.Facts.Where(kv => !IsIdentity(kv.Key)).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key[4..]} {kv.Value}").ToList();
        if (apps.Count > 0) sb.AppendLine($"- Apps ({apps.Count}): {string.Join("; ", apps.Take(60))}{(apps.Count > 60 ? "; …" : "")}");
        sb.AppendLine();

        sb.AppendLine("## Reaching it");
        var a = i.Access;
        if (!a.IsRack)
        {
            sb.AppendLine($"- Who is on it at the last check: {a.Presence ?? "not known"}.");
            sb.AppendLine(a.AgentConnected
                ? $"- NetworkOps agent {a.AgentVersion} is connected: checks and allow-listed fixes reach it in seconds (Command Center: Check this PC now / Fix…)."
                : a.AgentVersion is not null ? $"- NetworkOps agent {a.AgentVersion} is installed but NOT connected: checks fall back to the network route." : "- No NetworkOps agent: checks use the network route (a one-shot service over SMB, as SYSTEM).");
        }
        else if (a.RackSummary is { } rs) sb.AppendLine($"- Last read: {rs}.");
        sb.AppendLine(a.ConnectUrl is { } url
            ? $"- Remote screen: {url} ({(a.MeshConnected ? "Mesh agent connected" : "Mesh agent NOT connected right now")}). If nobody is at it or its monitors are off, use RDP from the same page."
            : "- Remote control: no Mesh agent on it.");
        sb.AppendLine($"- Read anything on it with the repo's CLI, which runs ON the machine (one call, one result; never chatty reads over the VPN): `netops run --script x.ps1 --hosts {i.DeviceName}` from `{RepoPath}`. Build it first if needed: `dotnet build Kor.Operations.NetworkOps.Cli`.");
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

        Closing(sb, report, i.DeviceName);
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
        sb.AppendLine("- Never echo, store or commit a password or key. Never content-search OneDrive. Never kill processes by name machine-wide.");
        sb.AppendLine("- KOR-1001 is the machine you are running on; APP01 cannot reach it by name.");
        sb.AppendLine("- Say what you checked and what you found; state as fact only what is in live output.");
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
        sb.AppendLine("} | ConvertTo-Json");
        sb.AppendLine($"Invoke-RestMethod -Method Post -Uri '{report.ApiBaseUrl.TrimEnd('/')}/api/prompt-runs/{report.RunId}/outcome' -Headers @{{ 'X-Prompt-Token' = '{report.Token}' }} -ContentType 'application/json' -Body $body -SkipCertificateCheck");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine($"The token works once, for this run ({report.RunId}) only. The outcome lands on {(device is null ? "the run" : device)} as a note and in the Prompt Library.");
    }

    /// <summary>Identity and state facts worth a session's attention; app version facts (dozens) are left out.</summary>
    private static bool IsIdentity(string fact) => !fact.StartsWith("app.", StringComparison.Ordinal);

    private static string Sev(Health.Severity s) => s switch { Health.Severity.Critical => "CRITICAL", Health.Severity.Warning => "Warning", _ => "Info" };

    private static string Trim(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
