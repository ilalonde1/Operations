#nullable enable
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// "Fans are set to a loud profile" had no one-click fix: the Fix dialog offered only the empty "Run a PowerShell
// command" box (Ian, KOR-310, 2026-10-01). And its evidence named "Balance mode", which a P340's BIOS does not have.
//
// WHAT IT COVERS: the finding offers the quiet-fans fix first (the general command last); it is not disruptive; the
// script picks the quiet value from the PC's own list, knowing both families' names (measured on 13 PCs, 2026-10-01:
// P340 'Best Experience', P350/P360 'Balance mode'), saves it, and reads it back; the evidence names both.
// WHAT IT DOES NOT: run the script -- the BIOS change is proven by running it on one PC and reading the setting back.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a third Lenovo family with a different quiet name: the script then changes
// nothing and says so ("no quiet profile among this BIOS's choices"), which is safe but not fixed.
public sealed class QuietFansTests
{
    [Fact]
    public void The_finding_offers_the_quiet_fans_fix_first_and_it_restarts_nothing()
    {
        var fixes = FixCatalog.For("fan-profile-loud");
        Assert.Equal("quiet-fans", fixes[0].Id);
        Assert.Equal(FixCatalog.RunCommand, fixes[^1].Id);
        Assert.False(fixes[0].Disruptive);
    }

    [Fact]
    public void The_script_picks_from_the_pcs_own_list_knowing_both_families()
    {
        var script = FixCatalog.Script(FixCatalog.Get("quiet-fans")!, null);
        Assert.Contains("'Balance mode', 'Best Experience'", script);
        Assert.Contains("[Optional:", script);              // the allowed values are read, not assumed
        Assert.Contains("SaveBiosSettings", script);
        Assert.Contains("Read it back", script);
    }

    [Fact]
    public void The_evidence_names_the_quiet_setting_for_both_families()
    {
        var f = Assert.Single(HealthRules.Evaluate(new HealthSnapshot { ProbeVersion = 8, CoolingMode = "Best Performance" }), x => x.RuleKey == "fan-profile-loud");
        Assert.Contains("'Best Experience' on a P340", f.Evidence);
        Assert.Contains("'Balance mode' on a P350/P360", f.Evidence);
        Assert.DoesNotContain(HealthRules.Evaluate(new HealthSnapshot { ProbeVersion = 8, CoolingMode = "Best Experience" }), x => x.RuleKey == "fan-profile-loud");
    }
}
