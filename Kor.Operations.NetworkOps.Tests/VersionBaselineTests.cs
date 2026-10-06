#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// CLASS 1: every component records its version; nothing compared it to a secure baseline or an end-of-support date. The
// fixtures are the REAL 2026-10-05 readings (ESXi 7.0.2 build-17867351 on both hosts; Veeam B&R 12.3.2.4854 = the build
// with CVE-2025-64393) plus the baseline as it would be seeded in db/KorNetworkOps/013.
//
// WHAT IT COVERS: ESXi past end-of-support fires; a Veeam build below the secure one fires Critical and carries the CVE;
// the current build does NOT fire; the Veeam UPGRADE (to 12.3.2.4934) clears it; a recorded version with no baseline row
// is flagged for review; the baseline guards its OWN freshness (empty table, or a row not reviewed in 45 days).
// WHAT IT DOES NOT: DSM / the switch / the printers (no probe yet) -- stated in VersionBaselineRules' own summary.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a vulnerable DSM/printer firmware (no probe), so not even baseline.stale fires.
public sealed class VersionBaselineTests
{
    private static readonly DateTime Now = new(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);

    private static readonly IReadOnlyList<VersionBaselineRow> Baseline =
    [
        new("VMware ESXi 7.0", null, new DateTime(2025, 10, 2, 0, 0, 0, DateTimeKind.Utc), Now.AddDays(-1), "vSphere 7 end of general support"),
        new("Veeam Backup & Replication", "12.3.2.4934", null, Now.AddDays(-1), "CVE-2025-64393 (CVSS 9.4): RCE by a backup operator"),
        new("Windows Server 2025", "10.0.26100", null, Now.AddDays(-1)),
    ];

    private static Dictionary<string, string> Esxi() => new() { ["esxi.version"] = "VMware ESXi 7.0.2 build-17867351" };
    private static Dictionary<string, string> Veeam(string v) => new() { ["veeam.version"] = v };
    private static Dictionary<string, string> WinCurrent() => new() { ["os.build"] = "10.0.26100", ["os.caption"] = "Microsoft Windows Server 2025 Standard" };

    [Fact]
    public void Both_missed_faults_fire_the_current_build_does_not()
    {
        var esxi = VersionBaselineRules.Evaluate(Esxi(), Baseline, Now);
        Assert.Single(esxi, f => f.RuleKey == "version.end-of-support" && f.Severity == Severity.Warning);
        Assert.DoesNotContain(esxi, f => f.RuleKey == "version.insecure");         // an EoS product has no "secure build" set

        var veeam = VersionBaselineRules.Evaluate(Veeam("12.3.2.4854"), Baseline, Now);
        var ins = Assert.Single(veeam, f => f.RuleKey == "version.insecure");
        Assert.Equal(Severity.Critical, ins.Severity);
        Assert.Contains("CVE-2025-64393", ins.Evidence);
        Assert.Contains("12.3.2.4934", ins.Evidence);

        Assert.Empty(VersionBaselineRules.Evaluate(WinCurrent(), Baseline, Now));   // on the current secure build -> nothing
    }

    [Fact]
    public void The_veeam_upgrade_clears_the_insecure_finding()
    {
        Assert.Contains(VersionBaselineRules.Evaluate(Veeam("12.3.2.4854"), Baseline, Now), f => f.RuleKey == "version.insecure");
        Assert.DoesNotContain(VersionBaselineRules.Evaluate(Veeam("12.3.2.4934"), Baseline, Now), f => f.RuleKey == "version.insecure");  // exactly the secure build
        Assert.DoesNotContain(VersionBaselineRules.Evaluate(Veeam("12.3.2.5000"), Baseline, Now), f => f.RuleKey == "version.insecure");  // newer
    }

    [Fact]
    public void A_recorded_version_with_no_baseline_row_is_flagged_for_review()
    {
        var f = VersionBaselineRules.Evaluate(new Dictionary<string, string> { ["fw.model"] = "pfSense Plus 24.03-RELEASE" }, Baseline, Now);
        Assert.Single(f, x => x.RuleKey == "version.no-baseline" && x.Severity == Severity.Info);
    }

    [Fact]
    public void The_baseline_guards_its_own_freshness()
    {
        Assert.Null(VersionBaselineRules.BaselineHealth(Baseline, Now));            // every row reviewed yesterday
        var stale = Baseline.Append(new VersionBaselineRow("Synology DSM", null, null, Now.AddDays(-60))).ToList();
        var s = VersionBaselineRules.BaselineHealth(stale, Now);
        Assert.Equal("baseline.stale", s!.RuleKey);
        Assert.Contains("Synology DSM", s.Evidence);
        Assert.Equal(Severity.Warning, VersionBaselineRules.BaselineHealth([], Now)!.Severity);   // nothing loaded -> run 013
    }
}
