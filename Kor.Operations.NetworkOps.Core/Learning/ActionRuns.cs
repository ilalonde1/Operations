#nullable enable
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Learning;

/// <summary>
/// One run as a person started it: the same fix (or deploy, or command) asked for on several machines at once, read back as
/// one line with its progress -- "Install updates (no restart) · 7 of 29 finished · 5 done · 2 failed · 22 running · 7 min".
/// The Updates and Deploy views follow only what THEY queued, while they are open; a run started from To clear, the MCP or
/// another window was visible nowhere (2026-10-22: 29 installs ran with the screen saying "Loading...", and the button was
/// pressed twice). This is read from NetworkOps.Actions, so it shows every run, wherever it was started.
/// </summary>
public sealed record ActionRun(string Kind, string Title, string RequestedBy, DateTime StartedUtc, IReadOnlyList<ChangeAction> Machines)
{
    public int Total => Machines.Count;
    public int Done => Machines.Count(m => m.Status == "Done");
    public int Running => Machines.Count(m => m.Status == "Running");
    public int Queued => Machines.Count(m => m.Status == "Requested");
    public int Refused => Machines.Count(m => m.Status == "Refused");
    /// <summary>Ended without succeeding (Failed, Abandoned, anything that is not Done, in flight or Refused).</summary>
    public int Failed => Total - Done - Running - Queued - Refused;
    public bool InFlight => Running + Queued > 0;
    public DateTime? FinishedUtc => InFlight ? null : Machines.Max(m => m.CompletedUtc ?? m.RequestedUtc);

    /// <summary>The run in one line, counts first, then how long it has been going (or took).</summary>
    public string Line(DateTime nowUtc)
    {
        var parts = new List<string> { Title };
        if (InFlight) parts.Add($"{Total - Running - Queued} of {Total} finished");
        else parts.Add($"finished {Dates.Clock(FinishedUtc!.Value.ToLocalTime())}");
        if (Done > 0) parts.Add($"{Done} done");
        if (Failed > 0) parts.Add($"{Failed} failed");
        if (Refused > 0) parts.Add($"{Refused} not run");
        if (Running > 0) parts.Add($"{Running} running");
        if (Queued > 0) parts.Add($"{Queued} queued");
        parts.Add(InFlight ? Minutes(nowUtc - StartedUtc) : "took " + Minutes(FinishedUtc!.Value - StartedUtc));
        return string.Join(" · ", parts);
    }

    /// <summary>One machine's state in words: what it printed when it finished, or how long it has been going.</summary>
    public static string MachineLine(ChangeAction a, DateTime nowUtc) => a.Status switch
    {
        "Requested" => "queued",
        "Running" => $"running {Minutes(nowUtc - a.RequestedUtc)}",
        "Done" => "done" + (a.Detail is { Length: > 0 } d ? ": " + d : ""),
        "Refused" => "not run" + (a.Detail is { Length: > 0 } d ? ": " + d : ""),
        _ => a.Status.ToLowerInvariant() + (a.Detail is { Length: > 0 } d ? ": " + d : ""),
    };

    private static string Minutes(TimeSpan t) => t.TotalMinutes < 1 ? "under a minute" : $"{(int)t.TotalMinutes} min";
}

public static class ActionRuns
{
    /// <summary>Requests of the same kind by the same person within this long of the run's first are one run (a fan-out
    /// inserts its rows within seconds; a second click a minute later is a second run, and shows as one).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(30);

    /// <summary>Finished runs stay on screen this long, so the result is read, not missed.</summary>
    public static readonly TimeSpan KeepFinished = TimeSpan.FromHours(1);

    /// <summary>The actions grouped into runs, newest first: every run with a machine still in flight, and finished runs from
    /// the last <see cref="KeepFinished"/>.</summary>
    public static IReadOnlyList<ActionRun> Group(IEnumerable<ChangeAction> actions, DateTime nowUtc)
    {
        var runs = new List<ActionRun>();
        foreach (var g in actions.GroupBy(a => (a.Kind, a.RequestedBy)))
        {
            List<ChangeAction>? current = null;
            foreach (var a in g.OrderBy(a => a.RequestedUtc).ThenBy(a => a.ActionId))
            {
                if (current is null || a.RequestedUtc - current[0].RequestedUtc > Window)
                {
                    if (current is not null) runs.Add(Make(current));
                    current = [];
                }
                current.Add(a);
            }
            if (current is not null) runs.Add(Make(current));
        }
        return runs.Where(r => r.InFlight || nowUtc - r.FinishedUtc!.Value <= KeepFinished)
            .OrderByDescending(r => r.InFlight).ThenByDescending(r => r.StartedUtc).ToList();

        static ActionRun Make(List<ChangeAction> m) =>
            new(m[0].Kind, TitleOf(m[0].Kind), m[0].RequestedBy, m[0].RequestedUtc, m.OrderBy(a => a.Device, StringComparer.OrdinalIgnoreCase).ToList());
    }

    /// <summary>The fix's own title from the catalog; otherwise the kind, as the audit names it.</summary>
    public static string TitleOf(string kind) => FixCatalog.Get(kind)?.Title ?? kind switch
    {
        "run-command" => "Command",
        _ => kind,
    };
}
