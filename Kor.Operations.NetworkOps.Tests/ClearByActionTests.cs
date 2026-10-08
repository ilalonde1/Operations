#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// ClearByAction: the worklist regrouped by the ACTION that clears issues, deduping the machines. The point Ian made --
// "13 reboots for this cause, 9 for that, 6 for the other, but how many overlap?" -- so one restart clears every reboot
// reason on a machine, and the saving (machines carried by 2+ reasons) is shown, not hidden behind three separate counts.
public sealed class ClearByActionTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    private static FleetFinding F(long id, string device, string rule, Severity sev)
        => new(id, device, rule, sev, rule + " title", "evidence", Now.AddDays(-1), Now, null, null, null, null);

    private static ToClearView Build(params FleetFinding[] findings)
    {
        var devices = findings.Select(f => f.Device).Distinct(StringComparer.OrdinalIgnoreCase)
            .Select((n, i) => new DeviceRow(i + 1, n, null, null)).ToList();
        var snap = new FleetSnapshot(devices, new Dictionary<string, IReadOnlyDictionary<string, string>>(), findings.ToList(), [], null);
        return ToClear.Build([snap], new HashSet<string>(), Now);
    }

    [Fact]
    public void Three_reboot_reasons_collapse_into_one_restart_over_the_union_of_machines()
    {
        // not-restarted, reboot-overdue and crash-loop are ALL cleared by restart-pc (FixCatalog), overlapping on machines.
        var v = Build(
            F(1, "KOR-1", "not-restarted", Severity.Warning),
            F(2, "KOR-2", "not-restarted", Severity.Warning),
            F(3, "KOR-3", "not-restarted", Severity.Warning),
            F(4, "KOR-2", "reboot-overdue", Severity.Warning),
            F(5, "KOR-3", "reboot-overdue", Severity.Warning),
            F(6, "KOR-4", "reboot-overdue", Severity.Warning),
            F(7, "KOR-3", "crash-loop:x", Severity.Warning),
            F(8, "KOR-5", "crash-loop:x", Severity.Warning));

        var restart = ClearByAction.Of(v.Open).Single(a => a.Fix.Id == "restart-pc");
        Assert.Equal(5, restart.Machines);          // union of KOR-1..KOR-5, not 3+3+2 = 8
        Assert.Equal(8, restart.Findings);          // the 8 placements it would clear
        Assert.Equal(2, restart.OverlapMachines);   // KOR-2 (2 reasons) and KOR-3 (3 reasons) clear several in one restart
        Assert.Equal(3, restart.Reasons.Count);     // not-restarted, reboot-overdue, crash-loop
        Assert.True(restart.Consolidates);
    }

    [Fact]
    public void A_single_reason_action_does_not_claim_to_consolidate()
    {
        var v = Build(F(1, "KOR-1", "low-disk:C", Severity.Warning), F(2, "KOR-2", "low-disk:C", Severity.Warning));
        var free = ClearByAction.Of(v.Open).Single();
        Assert.Equal("free-disk-space", free.Fix.Id);
        Assert.Equal(2, free.Machines);
        Assert.Equal(0, free.OverlapMachines);
        Assert.False(free.Consolidates);
    }

    [Fact]
    public void An_issue_with_no_catalog_fix_is_not_an_action()
        => Assert.Empty(ClearByAction.Of(Build(F(1, "KOR-1", "mailbox-near-limit:x", Severity.Info)).Open));
}
