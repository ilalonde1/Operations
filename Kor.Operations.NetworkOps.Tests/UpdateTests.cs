#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Updates;
using Kor.Operations.NetworkOps.Service.Updates;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// Windows updates on demand: when they are due (the finding), and the rules for installing on many machines at once.
//
// WHAT IT COVERS: a real scan from KOR-1001 parses; a one-update list arriving as a bare object still parses; the
// 3-day hold, the 14-day overdue, an out-of-band Critical, and non-security-only updates each give the right severity;
// Patch Tuesday is the second Tuesday; the batch rules (domain controller alone, APP01 never restarted, a restart on an
// active PC needs confirming, no second install, unreachable machines refused with their reason); the restart variant is
// the same script with its switch set; the install never takes drivers or feature upgrades; and every sweep that diffs
// findings leaves "updates-due" alone, so a health or rack sweep cannot clear what only the update scan raises.
// WHAT IT DOES NOT: the Windows Update calls themselves (proven by running the scan and an install on a real machine),
// the SQL reads, or the HTTP routes. A SAME-CLASS FAULT IT WOULD NOT CATCH: a NEW sweep, in a file not named in the
// source scan below, that diffs findings without the exclusion would clear the update finding unnoticed.
public sealed class UpdateTests
{
    private static readonly DateTime Today = new(2026, 10, 1, 9, 0, 0);

    private static PendingUpdate U(string kb, bool security, DateTime released, string? severity = "Important")
        => new(kb, $"Update KB{kb}", security ? "Security Updates" : "Updates", security, severity, released, 10, false, true);

    private static UpdateScan Scan(params PendingUpdate[] u) => new(1, Today, "PC", u, false, 1000);

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "updates", name));

    [Fact]
    public void A_real_scan_from_KOR_1001_parses_and_is_critical_at_23_days()
    {
        var s = UpdateScan.Parse(Fixture("KOR-1001-2026-10-01.json"));
        Assert.Equal(2, s.Updates.Count);
        Assert.All(s.Updates, u => Assert.True(u.Security));
        Assert.Null(s.Updates[1].Severity);   // Windows gave no rating: empty becomes unknown, not ""
        Assert.True(s.RebootPending);
        var f = Assert.Single(UpdateRules.Evaluate(s, Today));
        Assert.Equal(Severity.Critical, f.Severity);   // the .NET update has been out since 2026-09-08: 23 days
        Assert.Contains("oldest security released 2026-09-08 (23 d)", f.Evidence);
        Assert.Contains("a restart is already pending", f.Evidence);
    }

    [Fact]
    public void One_update_arriving_as_a_bare_object_still_parses()
    {
        var s = UpdateScan.Parse("""[{"ScanVersion":1,"ScannedAt":"2026-10-01T09:00:00","Computer":"X","Updates":{"Kb":"1","Title":"t","Security":true,"Released":"2026-09-30T00:00:00"},"RebootPending":false,"SearchMs":5}]""");
        Assert.Single(s.Updates);
    }

    [Fact]
    public void Security_updates_are_held_three_days_then_due_then_overdue_at_fourteen()
    {
        Assert.Equal(Severity.Info, UpdateRules.Evaluate(Scan(U("1", true, Today.AddDays(-2))), Today).Single().Severity);
        Assert.Equal(Severity.Warning, UpdateRules.Evaluate(Scan(U("1", true, Today.AddDays(-3))), Today).Single().Severity);
        Assert.Equal(Severity.Warning, UpdateRules.Evaluate(Scan(U("1", true, Today.AddDays(-13))), Today).Single().Severity);
        Assert.Equal(Severity.Critical, UpdateRules.Evaluate(Scan(U("1", true, Today.AddDays(-14))), Today).Single().Severity);
    }

    [Fact]
    public void Only_non_security_updates_are_listed_never_alarmed_on()
    {
        var f = UpdateRules.Evaluate(Scan(U("1", false, Today.AddDays(-60))), Today).Single();
        Assert.Equal(Severity.Info, f.Severity);
        Assert.Equal("Updates available", f.Title);
        Assert.Empty(UpdateRules.Evaluate(Scan(), Today));
    }

    [Fact]
    public void An_out_of_band_Critical_is_critical_at_once_but_Patch_Tuesdays_own_Critical_waits_its_turn()
    {
        var outOfBand = UpdateRules.Evaluate(Scan(U("9", true, new DateTime(2026, 9, 30), "Critical")), Today).Single();
        Assert.Equal(Severity.Critical, outOfBand.Severity);
        Assert.Contains("out-of-band Critical: KB9", outOfBand.Evidence);

        var patchTuesday = new DateTime(2026, 9, 8);
        Assert.Equal(Severity.Info, UpdateRules.Evaluate(Scan(U("8", true, patchTuesday, "Critical")), patchTuesday.AddDays(1)).Single().Severity);
    }

    [Fact]
    public void Patch_Tuesday_is_the_second_Tuesday()
    {
        Assert.Equal(new DateTime(2026, 9, 8), UpdateRules.PatchTuesday(2026, 9));     // Sep 1 2026 is a Tuesday
        Assert.Equal(new DateTime(2026, 10, 13), UpdateRules.PatchTuesday(2026, 10));
        Assert.Equal(new DateTime(2026, 11, 10), UpdateRules.PatchTuesday(2026, 11));
        Assert.True(UpdateRules.IsDayAfterPatchTuesday(new DateTime(2026, 10, 14, 8, 0, 0)));
        Assert.False(UpdateRules.IsDayAfterPatchTuesday(new DateTime(2026, 10, 13, 8, 0, 0)));
    }

    private static UpdateTarget T(int id, string name, string? guard = null, string? host = "h", bool server = false)
        => new(id, name, server ? "Server" : "Workstation", server, host, host is null ? "cannot reach it" : null, guard);

    [Fact]
    public void The_domain_controller_only_goes_in_a_batch_of_its_own()
    {
        var dc = T(1, "KOR-DC01", "Alone", server: true);
        var withOthers = UpdateBatch.Decide([new(dc, null, null, false), new(T(2, "KOR-101"), "Nobody", null, false)], false, false);
        Assert.Contains("batch of its own", withOthers[0].Refused);
        Assert.Equal(FixCatalog.InstallUpdates, withOthers[1].FixId);

        var alone = UpdateBatch.Decide([new(dc, null, null, false)], true, false).Single();
        Assert.Equal(FixCatalog.InstallUpdatesRestart, alone.FixId);
    }

    [Fact]
    public void APP01_installs_but_is_never_restarted_from_here_and_is_told_so()
    {
        var d = UpdateBatch.Decide([new(T(3, "KOR-APP01", "NoRestart", server: true), null, null, false)], true, false).Single();
        Assert.Equal(FixCatalog.InstallUpdates, d.FixId);
        Assert.Contains("never restarted from here", d.Note);
    }

    [Fact]
    public void A_restart_on_a_PC_someone_is_using_needs_confirming_an_install_alone_does_not()
    {
        var m = new BatchMember(T(4, "KOR-202"), "Active", "jmarkulin · active", false);
        var refused = UpdateBatch.Decide([m], true, false).Single();
        Assert.Null(refused.FixId);
        Assert.True(refused.NeedsConfirmation);
        Assert.Equal(FixCatalog.InstallUpdatesRestart, UpdateBatch.Decide([m], true, true).Single().FixId);
        Assert.Equal(FixCatalog.InstallUpdates, UpdateBatch.Decide([m], false, false).Single().FixId);
    }

    [Fact]
    public void An_unreachable_machine_or_one_already_installing_is_refused_with_why()
    {
        var bk = UpdateBatch.Decide([new(T(5, "BK01", host: null, server: true), null, null, false)], false, false).Single();
        Assert.Equal("cannot reach it", bk.Refused);
        var busy = UpdateBatch.Decide([new(T(6, "KOR-205"), null, null, true)], false, false).Single();
        Assert.Contains("already queued or running", busy.Refused);
    }

    [Fact]
    public void The_restart_variant_is_the_same_script_with_its_switch_set()
    {
        var plain = FixCatalog.Script(FixCatalog.Get(FixCatalog.InstallUpdates)!, null);
        var restart = FixCatalog.Script(FixCatalog.Get(FixCatalog.InstallUpdatesRestart)!, null);
        Assert.Equal("$RestartIfNeeded = $true\n" + plain, restart);
        Assert.False(FixCatalog.Get(FixCatalog.InstallUpdates)!.Disruptive);
        Assert.True(FixCatalog.Get(FixCatalog.InstallUpdatesRestart)!.Disruptive);
    }

    [Fact]
    public void The_install_never_takes_drivers_or_feature_upgrades()
    {
        var script = FixCatalog.Script(FixCatalog.Get(FixCatalog.InstallUpdates)!, null);
        Assert.Contains("Type='Software'", script);
        Assert.Contains("-contains 'Upgrades'", script);
        Assert.Contains("Type='Software'", Kor.Operations.NetworkOps.Core.Probes.ProbeLibrary.Get("updates"));
    }

    [Fact]
    public void Every_sweep_that_diffs_findings_leaves_the_update_finding_alone()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !Directory.Exists(Path.Combine(root.FullName, "Kor.Operations.NetworkOps.Service"))) root = root.Parent;
        var service = Path.Combine(root!.FullName, "Kor.Operations.NetworkOps.Service");
        var diffing = Directory.GetFiles(service, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Where(f => Regex.IsMatch(File.ReadAllText(f), @"OpenFindingsAsync\(") && File.ReadAllText(f).Contains("FindingDiff.Compute("))
            .ToList();
        Assert.True(diffing.Count >= 4, $"expected the health sweep, the rack sweep, the census and the update scan; found {diffing.Count}");
        // Safe either way: it leaves the update rule out (UpdateRules.Owns), or it only ever diffs its OWN rule (the census).
        var ownRuleOnly = new Regex(@"OpenFindingsAsync\([^;]*\)\)\.Where\(\w+ => \w+\.RuleKey == ");
        foreach (var f in diffing)
        {
            var src = File.ReadAllText(f);
            Assert.True(src.Contains("UpdateRules.Owns(") || ownRuleOnly.IsMatch(src),
                $"{Path.GetFileName(f)} diffs findings without leaving 'updates-due' to the update scan");
        }
    }
}
