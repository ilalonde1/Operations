#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// ToClear.Build: the "path to all-green" worklist. It groups LIVE findings by issue across the fleet, ranks them,
// attaches the fix that clears each, counts the distance to green, and parks acknowledged/snoozed ones so they don't
// count against green.
//
// WHAT THIS COVERS: grouping the same rule across machines into one issue; ranking (severity, then count, then age); the
// one-click flag (a fix that needs no input and interrupts no one); the fix attached from FixCatalog, or null when only
// the escape hatch applies; acknowledged + still-future-snoozed findings counted as PARKED, not open; the header counts.
// WHAT IT DOES NOT: that a fix actually clears the finding on a real PC (proven by running it), or the SQL that fills the
// snapshot. A fault it would NOT catch: a rule whose fix needs a per-machine param that differs within one ruleKey (the
// ruleKey carries the param, so a group shares it, but nothing here enforces that).
public sealed class ToClearTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);

    private static FleetFinding F(long id, string device, string rule, Severity sev, DateTime? acked = null, DateTime? snoozed = null)
        => new(id, device, rule, sev, rule + " title", "evidence", Now.AddDays(-1), Now, acked, acked is null ? null : "ian", snoozed, null);

    private static ToClearView Build(IReadOnlySet<string>? running = null)
    {
        var devices = new List<DeviceRow> { new(1, "KOR-1", null, null), new(2, "KOR-2", null, null), new(3, "KOR-3", null, null) };
        var findings = new List<FleetFinding>
        {
            F(10, "KOR-1", "low-disk:C", Severity.Warning),
            F(11, "KOR-2", "low-disk:C", Severity.Warning),
            F(12, "KOR-3", "low-disk:C", Severity.Warning),
            F(13, "KOR-1", "not-restarted", Severity.Critical),
            F(14, "KOR-2", "mailbox-near-limit:x", Severity.Info),
            F(15, "KOR-3", "gpu-hangs", Severity.Critical, acked: Now.AddHours(-1)),   // parked (acknowledged)
            F(16, "KOR-1", "wmi-broken", Severity.Warning, snoozed: Now.AddDays(1)),   // parked (snoozed into the future)
        };
        var snap = new FleetSnapshot(devices, new Dictionary<string, IReadOnlyDictionary<string, string>>(), findings, [], null);
        return ToClear.Build([snap], running ?? new HashSet<string>(), Now);
    }

    [Fact]
    public void It_groups_by_issue_and_counts_the_distance_to_green()
    {
        var v = Build();
        Assert.Equal(3, v.Issues);           // low-disk:C, not-restarted, mailbox-near-limit:x
        Assert.Equal(5, v.Machines);         // 3 + 1 + 1 live placements
        Assert.Equal(1, v.OneClickIssues);   // only free-disk-space is no-input AND non-disruptive
        Assert.Equal(2, v.Parked);           // the acknowledged gpu-hangs + the future-snoozed wmi-broken
    }

    [Fact]
    public void It_ranks_critical_first_then_by_count()
    {
        var v = Build();
        Assert.Equal("not-restarted", v.Open[0].RuleKey);         // Critical
        Assert.Equal("low-disk:C", v.Open[1].RuleKey);            // Warning, on 3 machines
        Assert.Equal("mailbox-near-limit:x", v.Open[2].RuleKey);  // Info
    }

    [Fact]
    public void The_same_issue_on_many_machines_is_one_row_with_its_machines()
    {
        var lowDisk = Build().Open.Single(i => i.RuleKey == "low-disk:C");
        Assert.Equal(3, lowDisk.Count);
        Assert.Equal(new[] { "KOR-1", "KOR-2", "KOR-3" }, lowDisk.Machines.Select(m => m.Name));
        Assert.Equal(new[] { 1, 2, 3 }, lowDisk.Machines.Select(m => m.DeviceId));
    }

    [Fact]
    public void Each_issue_carries_the_fix_that_clears_it_and_whether_it_is_one_click()
    {
        var v = Build();
        var lowDisk = v.Open.Single(i => i.RuleKey == "low-disk:C");
        Assert.Equal("free-disk-space", lowDisk.Fix!.Id);
        Assert.True(lowDisk.OneClick);

        var restart = v.Open.Single(i => i.RuleKey == "not-restarted");
        Assert.Equal("restart-pc", restart.Fix!.Id);
        Assert.True(restart.Fix.Disruptive);
        Assert.False(restart.OneClick);                 // disruptive -> needs confirmation, not one click

        var mailbox = v.Open.Single(i => i.RuleKey == "mailbox-near-limit:x");
        Assert.Null(mailbox.Fix);                        // only the escape hatch applies -> not "the fix"
        Assert.False(mailbox.OneClick);
    }

    [Fact]
    public void An_issue_with_its_fix_in_flight_on_any_machine_is_running()
    {
        // free-disk-space already queued on KOR-1 (deviceId 1) -> the whole low-disk issue reads as "fixing".
        var v = Build(new HashSet<string> { "1|free-disk-space" });
        Assert.True(v.Open.Single(i => i.RuleKey == "low-disk:C").Running);
        Assert.False(v.Open.Single(i => i.RuleKey == "not-restarted").Running);   // restart-pc not in flight
    }

    [Fact]
    public void An_unrelated_action_in_flight_does_not_mark_an_issue_running()
    {
        // An action of a DIFFERENT kind on an affected machine is not this issue's fix.
        var v = Build(new HashSet<string> { "1|restart-pc" });
        Assert.False(v.Open.Single(i => i.RuleKey == "low-disk:C").Running);
        Assert.True(v.Open.Single(i => i.RuleKey == "not-restarted").Running);    // restart-pc IS not-restarted's fix, on KOR-1
    }
}
