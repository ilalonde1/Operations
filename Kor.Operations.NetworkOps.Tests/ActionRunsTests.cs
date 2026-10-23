#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// ActionRuns.Group: the Command Center's runs strip. NetworkOps.Actions rows grouped into the runs a person started, each
// read as one line of progress.
//
// WHAT THIS COVERS: a fan-out (same kind, same person, rows seconds apart) is ONE run; a second click 43 s later is a
// SECOND run (the 2026-10-22 double press), not merged into the first; different kinds or people are separate runs; the
// counts (done / failed / not run / running / queued) and the line built from them; in-flight runs kept however old,
// finished ones only within the keep window; in-flight first. WHAT IT DOES NOT: the SQL that reads the rows, or the strip
// that draws them. A fault it would NOT catch: two different people's runs started the same second under the same account
// (e.g. two Claude sessions both signed in as the service user) read as one run.
public class ActionRunsTests
{
    private static readonly DateTime T0 = new(2026, 10, 23, 6, 12, 3, DateTimeKind.Utc);
    private static long _id = 1000;

    private static ChangeAction A(string device, string status, DateTime requested, string kind = "install-updates", string by = "ilalonde", string? detail = null, DateTime? completed = null) =>
        new(device, _id++, kind, by, requested, status, detail, completed);

    [Fact]
    public void A_fan_out_is_one_run_and_a_second_press_43s_later_is_a_second_run()
    {
        var rows = new List<ChangeAction>();
        for (var i = 0; i < 29; i++) rows.Add(A($"PC{i:00}", "Running", T0.AddMilliseconds(i * 30)));
        for (var i = 0; i < 29; i++) rows.Add(A($"PC{i:00}", "Refused", T0.AddSeconds(43), detail: "this fix is already queued or running on it", completed: T0.AddSeconds(43)));

        var runs = ActionRuns.Group(rows, T0.AddMinutes(7));

        Assert.Equal(2, runs.Count);
        Assert.Equal(29, runs[0].Total);
        Assert.True(runs[0].InFlight);
        Assert.Equal(29, runs[1].Refused);
        Assert.False(runs[1].InFlight);
    }

    [Fact]
    public void Different_kinds_or_people_are_separate_runs()
    {
        var runs = ActionRuns.Group([A("PC1", "Running", T0), A("PC2", "Running", T0, kind: "restart-pc"), A("PC3", "Running", T0, by: "someone")], T0);
        Assert.Equal(3, runs.Count);
    }

    [Fact]
    public void The_line_counts_each_state_and_the_time_so_far()
    {
        var rows = new List<ChangeAction>
        {
            A("KOR-101", "Done", T0, detail: "Installed 1 of 1; no restart needed.", completed: T0.AddMinutes(1)),
            A("KOR-204", "Done", T0, completed: T0.AddMinutes(7)),
            A("BOARDROOM", "Failed", T0, detail: "Offline", completed: T0.AddSeconds(6)),
            A("KOR-207", "Running", T0),
            A("KOR-208-N", "Requested", T0),
        };
        var run = Assert.Single(ActionRuns.Group(rows, T0.AddMinutes(7)));

        Assert.Equal("Install updates (no restart) · 3 of 5 finished · 2 done · 1 failed · 1 running · 1 queued · 7 min", run.Line(T0.AddMinutes(7)));
        Assert.Equal("done: Installed 1 of 1; no restart needed.", ActionRun.MachineLine(rows[0], T0.AddMinutes(7)));
        Assert.Equal("running 7 min", ActionRun.MachineLine(rows[3], T0.AddMinutes(7)));
    }

    [Fact]
    public void A_finished_run_says_when_and_how_long_then_drops_off_after_the_keep_window()
    {
        var rows = new List<ChangeAction> { A("PC1", "Done", T0, completed: T0.AddMinutes(20)), A("PC2", "Failed", T0, completed: T0.AddMinutes(28)) };

        var run = Assert.Single(ActionRuns.Group(rows, T0.AddMinutes(30)));
        Assert.False(run.InFlight);
        Assert.Contains("1 done · 1 failed · took 28 min", run.Line(T0.AddMinutes(30)));

        Assert.Empty(ActionRuns.Group(rows, T0.AddMinutes(28) + ActionRuns.KeepFinished + TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void An_in_flight_run_is_kept_however_old_and_comes_first()
    {
        var old = A("PC1", "Running", T0.AddHours(-9));
        var recent = A("PC2", "Done", T0.AddMinutes(-5), kind: "restart-pc", completed: T0.AddMinutes(-4));
        var runs = ActionRuns.Group([recent, old], T0);
        Assert.Equal(2, runs.Count);
        Assert.Same(old, runs[0].Machines[0]);
    }
}
