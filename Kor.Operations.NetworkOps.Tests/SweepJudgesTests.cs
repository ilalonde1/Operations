#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Service.Sweep;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// What the health sweep may clear. A finding it does not judge is left exactly as it was: the source that raises it was
// not read this time, or another job owns it. The failure this pins (audit 2026-10-16, 1.3): MeshCentral unread for 15
// minutes -> every mesh-* finding cleared, a "resolution" written for a fix that never happened, then all raised again as
// new on the next fresh read (new ids, acknowledgements lost, mailed again).
public sealed class SweepJudgesTests
{
    [Fact]
    public void An_unread_MeshCentral_leaves_the_remote_control_findings_alone()
    {
        Assert.False(HealthSweeper.Judges("mesh-silent", biosRead: true, meshRead: false));
        Assert.False(HealthSweeper.Judges("mesh-missing", biosRead: true, meshRead: false));
        Assert.True(HealthSweeper.Judges("mesh-silent", biosRead: true, meshRead: true));
        // Everything else is still judged when only MeshCentral was unread.
        Assert.True(HealthSweeper.Judges("low-disk:c", biosRead: true, meshRead: false));
    }

    [Fact]
    public void An_unread_Lenovo_catalog_leaves_the_BIOS_finding_alone()
    {
        Assert.False(HealthSweeper.Judges(Kor.Operations.NetworkOps.Core.Bios.BiosRules.Rule, biosRead: false, meshRead: true));
        Assert.True(HealthSweeper.Judges(Kor.Operations.NetworkOps.Core.Bios.BiosRules.Rule, biosRead: true, meshRead: true));
    }

    [Fact]
    public void Findings_another_job_owns_are_never_judged_here()
    {
        Assert.False(HealthSweeper.Judges("updates-due", biosRead: true, meshRead: true));
    }

    [Fact]
    public void Every_finding_MeshRules_raises_is_one_it_owns()
    {
        // So a new mesh rule cannot slip past the guard above.
        foreach (var presence in new[] { new MeshPresence(false, false), new MeshPresence(true, false), new MeshPresence(true, true) })
            foreach (var f in MeshRules.Evaluate(presence, answered: true))
                Assert.True(MeshRules.Owns(f.RuleKey), $"MeshRules raised '{f.RuleKey}' but does not own it");
    }
}
