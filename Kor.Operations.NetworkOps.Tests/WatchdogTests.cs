#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The dead-man switch's decisions over a sequence of runs, as the scheduled task would make them.
//
// WHAT IT COVERS: healthy stays quiet; silence past the threshold alerts once; the same outage only
// reminds on the cooldown however its wording changes; a new kind of problem alerts at once; recovery
// sends one all-clear naming when the outage began, then quiet; an unreadable database is an alarm.
// WHAT IT DOES NOT: the SQL read, the SMTP send, or the state file (the `netops watchdog` verb, proven
// by a dry run), nor whether the mail lands in an inbox rather than junk (SPF: see the design doc).
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a state file that fails to save would make every run look
// like the first -- an alert every 10 minutes -- while every decision here is correct.
public sealed class WatchdogTests
{
    private static readonly DateTime T0 = new(2026, 9, 29, 6, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Silent = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan Remind = TimeSpan.FromMinutes(60);

    private static HeartbeatReading Beat(DateTime at) => new("KOR-APP01", at, "0.2.0", null);

    private static WatchdogOutcome Run(HeartbeatReading r, WatchdogState s, DateTime now) => Watchdog.Decide(r, s, now, Silent, Remind, "KOR-FS01");

    [Fact]
    public void A_fresh_heartbeat_is_quiet()
    {
        var o = Run(Beat(T0.AddMinutes(-1)), WatchdogState.Fresh, T0);
        Assert.Equal(WatchdogAction.None, o.Action);
        Assert.Same(WatchdogState.Fresh, o.State);
    }

    [Fact]
    public void Silence_alerts_once_then_reminds_on_the_cooldown_then_recovers_once()
    {
        var lastBeat = T0;
        var s = WatchdogState.Fresh;
        var actions = new List<(int Minute, WatchdogAction Action)>();
        // The task runs every 10 minutes for three hours; the service dies at T0 and comes back at 150.
        for (var m = 10; m <= 180; m += 10)
        {
            var now = T0.AddMinutes(m);
            if (m >= 150) lastBeat = now.AddSeconds(-30);
            var o = Run(Beat(lastBeat), s, now);
            s = o.State;
            if (o.Action != WatchdogAction.None) actions.Add((m, o.Action));
        }

        Assert.Equal(
            [(20, WatchdogAction.Alert), (80, WatchdogAction.Alert), (140, WatchdogAction.Alert), (150, WatchdogAction.Recovered)],
            actions);
        Assert.Same(WatchdogState.Fresh, s);
    }

    [Fact]
    public void The_first_alert_says_down_and_a_reminder_says_still_down()
    {
        var first = Run(Beat(T0), WatchdogState.Fresh, T0.AddMinutes(20));
        var reminder = Run(Beat(T0), first.State, T0.AddMinutes(80));
        Assert.StartsWith("[ALERT]", first.Subject);
        Assert.StartsWith("[STILL DOWN]", reminder.Subject);
        Assert.Contains("silent for 80 minutes", reminder.Body);
        Assert.Equal(T0.AddMinutes(20), reminder.State.DownSinceUtc);   // the outage began at the first alert, not the reminder
    }

    [Fact]
    public void A_new_kind_of_problem_alerts_at_once_inside_the_cooldown()
    {
        var silent = Run(Beat(T0), WatchdogState.Fresh, T0.AddMinutes(20));
        var unreadable = Run(new HeartbeatReading(null, null, null, "A network-related error occurred (KOR-APP01)"), silent.State, T0.AddMinutes(30));

        Assert.Equal(WatchdogAction.Alert, unreadable.Action);
        Assert.Contains("cannot be read from here", unreadable.Body);
        Assert.Equal("unreadable", unreadable.State.Problem);
    }

    [Fact]
    public void Recovery_names_when_the_outage_began()
    {
        var down = Run(Beat(T0), WatchdogState.Fresh, T0.AddMinutes(20));
        var back = Run(Beat(T0.AddMinutes(39)), down.State, T0.AddMinutes(40));
        Assert.Equal(WatchdogAction.Recovered, back.Action);
        Assert.Contains($"since {T0.AddMinutes(20).ToLocalTime():ddd d MMM HH:mm}", back.Body);
    }

    [Fact]
    public void An_unreadable_database_and_a_service_that_never_beat_are_both_alarms()
    {
        Assert.Equal(WatchdogAction.Alert, Run(new HeartbeatReading(null, null, null, "login failed"), WatchdogState.Fresh, T0).Action);
        Assert.Equal(WatchdogAction.Alert, Run(new HeartbeatReading(null, null, null, null), WatchdogState.Fresh, T0).Action);
    }

    [Fact]
    public void Exactly_at_the_threshold_is_not_yet_silent()
    {
        Assert.Equal(WatchdogAction.None, Run(Beat(T0), WatchdogState.Fresh, T0 + Silent).Action);
        Assert.Equal(WatchdogAction.Alert, Run(Beat(T0), WatchdogState.Fresh, T0 + Silent + TimeSpan.FromSeconds(1)).Action);
    }
}
