#nullable enable
using Kor.Operations.NetworkOps.Core.Actions;
using Kor.Operations.NetworkOps.Service;
using Kor.Operations.NetworkOps.Service.Sweep;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The first fix that runs on an ESXi host: "Remove the leftover Veeam datastore" (Ian, 2026-10-02: "Can we actually remove
// the stale data store?" -- it came back four times that week and was removed by hand each time).
//
// WHAT IT COVERS: the fix is offered on esxi.stale-mount and takes the datastore from the finding; a name that could be
// read by a shell as more than a name is refused before anything runs; the script checks every condition (Veeam name, NFS,
// inaccessible, no VMs) BEFORE it removes, and reads the host again after; it can run only on an ESXi host, and a Windows
// fix never on one (one rule, ActionRunner.CanRun, for the API's refusal and the runner).
// WHAT IT DOES NOT: pyVmomi itself -- the removal was run live on ESXi host .16 on 2026-10-02 and read back.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: a Veeam datastore that is inaccessible only for a moment (a restore starting) with
// no VM registered yet would be removed; Veeam re-mounts it when the restore needs it.
public sealed class EsxiFixTests
{
    private static FixAction Fix => FixCatalog.Get(FixCatalog.RemoveStaleDatastore)!;

    [Fact]
    public void It_is_offered_on_the_leftover_finding_and_takes_the_datastore_from_it()
    {
        Assert.Contains(Fix, FixCatalog.For("esxi.stale-mount:VeeamBackup_KOR-APP01.int.korstructural.c_"));
        Assert.Equal("VeeamBackup_KOR-APP01.int.korstructural.c_", FixCatalog.ParamFromFinding(Fix, "esxi.stale-mount:VeeamBackup_KOR-APP01.int.korstructural.c_"));
        Assert.Equal(FixCatalog.Esxi, Fix.Target);
        Assert.False(Fix.Disruptive);
    }

    [Theory]
    [InlineData("VeeamBackup_KOR-APP01.int.korstructural.c_", true)]
    [InlineData("x'; rm -rf /vmfs; '", false)]
    [InlineData("a b", false)]
    [InlineData("$(reboot)", false)]
    [InlineData(null, false)]
    public void Only_a_plain_datastore_name_reaches_the_host(string? name, bool ok)
        => Assert.Equal(ok, FixCatalog.Invalid(Fix, name) is null);

    [Fact]
    public void Every_check_comes_before_the_removal_and_the_host_is_read_again_after()
    {
        var s = FixCatalog.Script(Fix, null);
        int At(string text) { var i = s.IndexOf(text, StringComparison.Ordinal); Assert.True(i >= 0, $"the script no longer contains: {text}"); return i; }
        var remove = At("RemoveDatastore(d)");
        Assert.All(new[] { At("startswith(\"VeeamBackup_\")"), At("not in (\"NFS\", \"NFS41\")"), At("if s.accessible:"), At("if vms:"), At("if dry:") },
            check => Assert.True(check < remove, "a check comes after the removal"));
        Assert.True(At("gone = not [x for x in h.datastore") > remove, "the host is not read again after removing");
    }

    [Fact]
    public void It_runs_only_on_an_ESXi_host_and_Windows_fixes_never_do()
    {
        var o = new NetworkOpsOptions
        {
            Rack =
            [
                new RackDevice { Name = "ESXi host .16 (standby)", Kind = "Host", Collector = "Esxi", Address = "192.168.1.16" },
                new RackDevice { Name = "KOR-FS01 (file server)", Kind = "Server", Collector = "WindowsServer", Address = "KOR-FS01" },
            ],
        };
        Assert.True(ActionRunner.CanRun(o, "ESXi host .16 (standby)", Fix));
        Assert.False(ActionRunner.CanRun(o, "KOR-FS01 (file server)", Fix));
        Assert.False(ActionRunner.CanRun(o, "ESXi host .16 (standby)", FixCatalog.Get("restart-pc")!));
        Assert.True(ActionRunner.CanRun(o, "KOR-FS01 (file server)", FixCatalog.Get("restart-pc")!));
    }
}
