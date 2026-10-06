#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Rack;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// CLASS 2 (ESXi side): "up is not the same as configured right." The host was reachable and its VMs on, but a VM ran a
// legacy E1000e NIC (Kor-BK01, capping backups near 1G) and a storage path flapped in vmkernel.log. Fixtures mirror the
// real 2026-10-05 readings (esxi-nic-audit: igc 2.5G NICs, Kor-BK01 on E1000e, Kor-Lab01_proxy powered OFF).
//
// WHAT IT COVERS: a powered-ON VM on a non-VMXNET3 adapter -> esxi.vm-nic-legacy (not a powered-off one); repeated
// iscsivmk_StopConnection in the current vmkernel.log -> esxi.iscsi-flapping (above the threshold, not a couple; not when
// the log could not be read, drops = -1).
// WHAT IT DOES NOT: which portal/vmk the active SAN path uses (fault (e), a separate rule), a legacy SCSI controller, or
// flapping that rotated out of the current log.
public sealed class EsxiClass2Tests
{
    private static readonly DateTime At = new(2026, 10, 6, 21, 0, 0, DateTimeKind.Utc);

    private static RackResult Eval(string vms, int iscsiDrops) => EsxiRules.Evaluate(
        $$"""{"version":"VMware ESXi 7.0.2 build-17867351","maintenanceMode":false,"sensors":[],"datastores":[],"cpuMhzTotal":10000,"cpuMhzUsed":500,"memMbUsed":1000,"memMbTotal":10000,"iscsiDrops":{{iscsiDrops}},"vms":{{vms}}}""",
        [], At);

    private static RackResult EvalPaths(string iscsiPaths) => EsxiRules.Evaluate(
        $$"""{"version":"VMware ESXi 7.0.2 build-17867351","maintenanceMode":false,"sensors":[],"datastores":[],"cpuMhzTotal":10000,"cpuMhzUsed":500,"memMbUsed":1000,"memMbTotal":10000,"iscsiDrops":0,"iscsiPaths":{{iscsiPaths}},"vms":[]}""",
        [], At);

    [Fact]
    public void An_active_san_path_on_the_management_subnet_is_wrong_the_storage_subnet_is_fine()
    {
        // The broken .16 state (pre 6-Oct): the active path's working connection is on 192.168.1.x (management).
        var bad = EvalPaths("""[{"runtime":"vmhba64:C2:T0:L1","state":"active","local":"192.168.1.16","remote":"192.168.1.12"},{"runtime":"vmhba64:C1:T0:L1","state":"active","local":"192.168.200.16","remote":"192.168.200.13"}]""");
        Assert.Single(bad.Findings, f => f.RuleKey == "esxi.iscsi-wrong-path:vmhba64:C2:T0:L1" && f.Severity == Severity.Warning);
        Assert.DoesNotContain(bad.Findings, f => f.RuleKey == "esxi.iscsi-wrong-path:vmhba64:C1:T0:L1");   // the storage-subnet path is fine

        // The fixed state (real .16 now): the management path is State=off, the active ones are on storage -> nothing.
        var good = EvalPaths("""[{"runtime":"vmhba64:C2:T0:L1","state":"off","local":"192.168.1.16","remote":"192.168.1.12"},{"runtime":"vmhba64:C1:T0:L1","state":"active","local":"192.168.200.16","remote":"192.168.200.13"},{"runtime":"vmhba64:C3:T0:L1","state":"active","local":"192.168.200.16","remote":"192.168.200.12"}]""");
        Assert.DoesNotContain(good.Findings, f => f.RuleKey.StartsWith("esxi.iscsi-wrong-path"));
    }

    [Fact]
    public void A_legacy_nic_on_a_powered_on_vm_is_flagged_a_powered_off_one_is_not()
    {
        var r = Eval("""[{"name":"Kor-BK01","power":"poweredOn","nics":["VirtualE1000e"]},{"name":"KOR-MESH01","power":"poweredOn","nics":["VirtualVmxnet3"]},{"name":"Kor-Lab01_proxy","power":"poweredOff","nics":["VirtualE1000e","VirtualE1000e"]}]""", 0);
        var f = Assert.Single(r.Findings, x => x.RuleKey == "esxi.vm-nic-legacy:Kor-BK01");
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("E1000e", f.Evidence);
        Assert.DoesNotContain(r.Findings, x => x.RuleKey.StartsWith("esxi.vm-nic-legacy:KOR-MESH01"));   // VMXNET3 is fine
        Assert.DoesNotContain(r.Findings, x => x.RuleKey.StartsWith("esxi.vm-nic-legacy:Kor-Lab01_proxy")); // powered off
    }

    [Fact]
    public void All_vmxnet3_raises_no_nic_finding()
        => Assert.DoesNotContain(Eval("""[{"name":"Kor-BK01","power":"poweredOn","nics":["VirtualVmxnet3"]}]""", 0).Findings,
            x => x.RuleKey.StartsWith("esxi.vm-nic-legacy"));

    [Fact]
    public void Repeated_iscsi_drops_flap_a_couple_do_not()
    {
        Assert.Contains(Eval("[]", 40).Findings, f => f.RuleKey == "esxi.iscsi-flapping" && f.Severity == Severity.Warning);
        Assert.Equal(40, Eval("[]", 40).Metrics.Single(m => m.Metric == "iscsi.drops").Value);
        Assert.DoesNotContain(Eval("[]", 2).Findings, f => f.RuleKey == "esxi.iscsi-flapping");

        var unread = Eval("[]", -1);   // the log could not be read
        Assert.DoesNotContain(unread.Findings, f => f.RuleKey == "esxi.iscsi-flapping");
        Assert.DoesNotContain(unread.Metrics, m => m.Metric == "iscsi.drops");
    }
}
