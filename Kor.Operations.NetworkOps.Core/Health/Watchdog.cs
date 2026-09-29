#nullable enable
namespace Kor.Operations.NetworkOps.Core.Health;

/// <summary>What the watcher read: the newest service heartbeat, or why it could not read one.</summary>
public sealed record HeartbeatReading(string? ServiceHost, DateTime? LastBeatUtc, string? Version, string? ReadError);

/// <summary>
/// What the watcher remembers between runs, so one outage is one alert, a reminder on the cooldown,
/// and one all-clear. <see cref="Problem"/> is the KIND of problem ("silent", "unreadable"), never its text.
/// </summary>
public sealed record WatchdogState(bool Down, string? Problem, DateTime? DownSinceUtc, DateTime? LastAlertUtc)
{
    public static readonly WatchdogState Fresh = new(false, null, null, null);
}

public enum WatchdogAction { None, Alert, Recovered }

public sealed record WatchdogOutcome(WatchdogAction Action, string? Subject, string? Body, WatchdogState State, string LogLine);

// The dead-man switch: runs on ANOTHER machine (KOR-FS01) and raises the alarm when the NetworkOps
// service stops beating, or when its database cannot be read at all -- which is what APP01 being down
// looks like from outside. The thing that reports a dead service must not be the service (D10).
//
// Modelled on tools/Watch-OpportunitiesHeartbeat.ps1 (threshold, cooldown, one recovery mail), with
// two differences: it never tries to restart anything (it is on another machine, with no rights
// there), and "cannot read the heartbeat" is itself an alarm, not an error in the watcher's own log.
public static class Watchdog
{
    public static WatchdogOutcome Decide(HeartbeatReading r, WatchdogState state, DateTime nowUtc, TimeSpan silentAfter, TimeSpan remindEvery, string watcher)
    {
        var (kind, problem) = ProblemOf(r, nowUtc, silentAfter);
        var seen = r.LastBeatUtc is { } b
            ? $"last heartbeat {b.ToLocalTime():ddd d MMM HH:mm} from {r.ServiceHost} ({r.Version ?? "version unknown"})"
            : "no heartbeat recorded";

        if (kind is null)
        {
            if (!state.Down)
                return new(WatchdogAction.None, null, null, state, $"ok: {seen}");
            var since = state.DownSinceUtc is { } d ? $" since {d.ToLocalTime():ddd d MMM HH:mm}" : "";
            return new(WatchdogAction.Recovered,
                "[RECOVERED] NetworkOps service is running again",
                $"The NetworkOps service is beating again: {seen}.\r\nIt had been reported down{since}.\r\n\r\nChecked from {watcher}.",
                WatchdogState.Fresh, $"recovered: {seen}");
        }

        // A new KIND of problem (the service was silent; now its database cannot even be reached) is
        // news and alerts at once; the same problem growing older only reminds on the cooldown.
        // Comparing the text instead would alert every run: "silent for 16 minutes" is not "... 17".
        var isNews = !state.Down || state.Problem != kind;
        var reminderDue = state.LastAlertUtc is not { } last || nowUtc - last >= remindEvery;
        if (!isNews && !reminderDue)
            return new(WatchdogAction.None, null, null, state, $"still down, reminder not due: {problem}");

        return new(WatchdogAction.Alert,
            state.Down ? "[STILL DOWN] NetworkOps service" : "[ALERT] NetworkOps service is down",
            $"{problem}\r\n\r\nWhile it is down nothing watches the PCs: no health sweeps, no census, no \"check this PC now\".\r\n"
            + "Look at: the Kor.Operations.NetworkOps service on KOR-APP01 (services.msc), its log in %ProgramData%\\KorOperations\\NetworkOps\\logs, "
            + $"and whether KOR-APP01 itself is up.\r\n\r\nChecked from {watcher}. If it stays down you will be reminded every {remindEvery.TotalMinutes:0} minutes, "
            + "and you will get one message when it recovers.",
            new WatchdogState(true, kind, state.Down ? state.DownSinceUtc ?? nowUtc : nowUtc, nowUtc),
            $"ALERT: {problem}");
    }

    /// <summary>(null, null) when healthy; otherwise the kind of problem and one sentence saying what is wrong.</summary>
    internal static (string? Kind, string? Text) ProblemOf(HeartbeatReading r, DateTime nowUtc, TimeSpan silentAfter)
    {
        if (r.ReadError is { } err)
            return ("unreadable", $"The NetworkOps database on KOR-APP01 cannot be read from here, so the service cannot be seen at all: {err}");
        if (r.LastBeatUtc is not { } beat)
            return ("never-beat", "The NetworkOps service has never written a heartbeat.");
        var silent = nowUtc - beat;
        return silent > silentAfter
            ? ("silent", $"The NetworkOps service on {r.ServiceHost} has been silent for {Minutes(silent)} (the alarm is at {silentAfter.TotalMinutes:0} minutes).")
            : (null, null);
    }

    private static string Minutes(TimeSpan t) => t.TotalHours >= 2 ? $"{t.TotalHours:0} hours" : $"{t.TotalMinutes:0} minutes";
}
