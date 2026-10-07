#nullable enable
using System.Linq;
using Kor.Operations.NetworkOps.Core.Probes;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The LIGHT Windows-health probe run over a MeshCentral agent (BK01, remote-only servers). The contract that keeps it
// usable: MeshCentral runs ONE command per agent at a time, and the FULL server.ps1 (Get-WinEvent / vssadmin / Get-HotFix)
// ran long enough to leave BK01's agent "already busy" so no sweep could read it. So the light probe must (a) emit the
// basic fields ServerRules reads, (b) NEVER add one of the slow/hang-prone cmdlets back, and (c) omit the AV/NIC fields so
// a false av-none / nic-public cannot fire on a box it was never meant to judge for those.
//
// WHAT IT DOES NOT COVER: that it runs fast on a real agent (proven live), or that a field holds the right value.
public sealed class ServerBasicsProbeContractTests
{
    // The CODE only -- comment lines (which name the slow cmdlets and the omitted fields on purpose) are stripped, so the
    // check is on what the probe runs, not on what it documents (a comment matched the grep before: same class as Logout()).
    private static readonly string Code = string.Join("\n", ProbeLibrary.Get("server-basics").Split('\n').Where(l => !l.TrimStart().StartsWith("#")));

    [Fact]
    public void It_emits_the_basics_ServerRules_reads()
    {
        foreach (var field in new[] { "Disks", "PendingReboot", "StoppedAutoServices", "OsBuild", "OsCaption", "UptimeHours" })
            Assert.Contains(field, Code);
    }

    [Fact]
    public void It_stays_light_no_event_log_vss_or_hotfix()
    {
        foreach (var slow in new[] { "Get-WinEvent", "vssadmin", "Get-HotFix" })
            Assert.DoesNotContain(slow, Code);   // the heavy probe's hang culprits must never come back to the Mesh path
    }

    [Fact]
    public void It_omits_the_AV_and_NIC_fields_so_those_rules_stay_silent()
    {
        Assert.DoesNotContain("WebrootStatus", Code);   // ServerRules gates the AV check on this; omit it, no av-none here
        Assert.DoesNotContain("NicCategories", Code);   // and the NIC check on this
    }
}
