#nullable enable
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The ESXi probe must EMIT every field EsxiRules reads -- a rule that reads a field the snapshot never sends is silently
// dead. esxi.vm-nic-legacy was exactly that for a week: vm_nics() was computed but "nics" was never put in the snapshot,
// so the rule's handcrafted-input test passed while the live rule could never fire (audit 2026-10-06, finding #11). And
// the snapshot must be PRINTED before the session Logout, or a Logout() that throws discards a fully-built read, leaving
// the host rack.unreachable (finding #9).
//
// WHAT IT COVERS: the Class-2 fields exist in the emitted snapshot; the per-VM nics list is wired to vm_nics(v); print
// precedes Logout. WHAT IT DOES NOT: that the probe runs on a real host, or that a field holds the right value -- only a
// live read proves that. A SAME-CLASS FAULT IT WOULD NOT CATCH: a field emitted but always empty because its collector
// silently failed; the differential harness the audit proposes (inject absence at each boundary) is what would.
public sealed class EsxiProbeContractTests
{
    private static readonly string Probe = EsxiRules.Script;

    [Fact]
    public void The_snapshot_emits_every_field_the_rules_read()
    {
        foreach (var field in new[] { "name", "version", "vms", "datastores", "sensors", "iscsiDrops", "iscsiPaths", "maintenanceMode", "overallStatus" })
            Assert.Contains($"\"{field}\"", Probe);
        Assert.Contains("\"nics\": vm_nics(v)", Probe);   // the per-VM NIC list esxi.vm-nic-legacy reads -- the field that was missing
    }

    [Fact]
    public void The_snapshot_is_printed_before_logout_so_a_logout_failure_cannot_lose_it()
    {
        var print = Probe.IndexOf("print(json.dumps(out))", System.StringComparison.Ordinal);
        var logout = Probe.IndexOf("sessionManager.Logout()", System.StringComparison.Ordinal);   // the call, not the word in a comment
        Assert.True(print >= 0 && logout >= 0, "the probe must both print the snapshot and log out");
        Assert.True(print < logout, "the snapshot must be printed BEFORE Logout(): a logout failure must not throw away a built read");
    }
}
