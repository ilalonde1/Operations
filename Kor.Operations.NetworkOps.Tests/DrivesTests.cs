#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// A drive finding says WHICH drive (Ian, 2026-10-02, on KOR-208-N's "ST2000DM006-2DM164: 3904 uncorrected read errors":
// "which disk is this? His system disk? His secondary disk?"). Probe v10 records each physical drive's letters and whether
// Windows boots from it; read live that day on 208-N, 206-N, 1001 and PERFORM2 (the drives below are 208-N's, as read).
//
// WHAT IT COVERS: the drive in words (system / data / no letter, its letters, its marketed size and kind); every drive
// finding's title and evidence using them (unrecoverable errors, new errors, wearing, aging, nearly full, unhealthy); and
// that a snapshot from before v10 keeps the old wording rather than guessing.
// WHAT IT DOES NOT: a drive with no physical-disk entry (a Storage Spaces virtual disk): it is named by model, as before.
// A SAME-CLASS FAULT IT WOULD NOT CATCH: two physical drives with the same model name (two identical SSDs) -- a finding is
// keyed by model, so both resolve to the first one's letters; no KOR PC has that today (PERFORM2's two differ by "Plus").
public sealed class DrivesTests
{
    private static readonly PhysicalDiskInfo Ssd = new("SAMSUNG MZVL2512HDJD-00BLL", "SSD", "NVMe", 477, "Healthy", "OK", "C", true);
    private static readonly PhysicalDiskInfo Hdd = new("ST2000DM006-2DM164", "HDD", "SATA", 1863, "Healthy", "OK", "D", false);

    private static HealthSnapshot Kor208N(params PhysicalDiskInfo[] disks) => new()
    {
        ProbeVersion = 10, Computer = "KOR-208-N", CollectedAt = new DateTime(2026, 10, 2, 13, 0, 0, DateTimeKind.Utc),
        PhysicalDisks = disks.Length > 0 ? disks : [Ssd, Hdd],
        Volumes = [new("C", "OS", 475, 120), new("D", "Data", 1863, 60)],
        DiskReliability = [new("ST2000DM006-2DM164", "Z4Z0", null, 34, 40, 3904, 3904, 0, 30_000)],
    };

    [Theory]
    [InlineData(1863, "2 TB")]
    [InlineData(477, "512 GB")]
    [InlineData(932, "1 TB")]
    [InlineData(954, "1 TB")]
    [InlineData(238, "256 GB")]
    [InlineData(3726, "4 TB")]
    [InlineData(5589, "6 TB")]
    public void A_size_reads_as_the_label_on_the_drive(double gib, string expected) => Assert.Equal(expected, Drives.Size(gib));

    [Fact]
    public void A_drive_is_named_by_what_it_is_for()
    {
        Assert.Equal("the system drive C:", Drives.Role(Ssd));
        Assert.Equal("the data drive D:", Drives.Role(Hdd));
        Assert.Equal("the data drive D:, E:", Drives.Role(Hdd with { Letters = "D,E" }));
        Assert.Equal("a drive with no letter", Drives.Role(Hdd with { Letters = "" }));
        Assert.Null(Drives.Role(Hdd with { Letters = null, System = null }));     // probe before v10: claim nothing
        Assert.Equal("2 TB hard drive", Drives.Kind(Hdd));
        Assert.Equal("512 GB NVMe SSD", Drives.Kind(Ssd));
    }

    [Fact]
    public void Kor208Ns_failing_drive_is_the_data_drive_D_and_the_finding_says_so()
    {
        var f = Predictions.Evaluate(Kor208N(), new MetricHistory()).Single(x => x.RuleKey == "disk-errors:st2000dm006-2dm164");
        Assert.Equal("The data drive D: has unrecoverable read errors", f.Title);
        Assert.Equal("ST2000DM006-2DM164 (2 TB hard drive): 3904 uncorrected read errors -- copy its data off now", f.Evidence);
        Assert.Equal(Severity.Critical, f.Severity);
    }

    [Fact]
    public void The_same_fault_on_the_system_drive_says_system()
    {
        var s = Kor208N(Ssd, Hdd with { Letters = "C", System = true }) with { };
        var f = Predictions.Evaluate(s, new MetricHistory()).Single(x => x.RuleKey == "disk-errors:st2000dm006-2dm164");
        Assert.Equal("The system drive C: has unrecoverable read errors", f.Title);
    }

    [Fact]
    public void A_full_drive_says_whether_it_is_the_system_drive()
    {
        var titles = HealthRules.Evaluate(Kor208N()).Where(x => x.RuleKey.StartsWith("low-disk:", StringComparison.Ordinal)).ToDictionary(x => x.RuleKey, x => x.Title);
        Assert.Equal("The data drive D: is nearly full", titles["low-disk:d"]);
        Assert.False(titles.ContainsKey("low-disk:c"));   // 120 of 475 GB free

        var sys = HealthRules.Evaluate(Kor208N() with { Volumes = [new("C", "OS", 475, 10)] }).Single(x => x.RuleKey == "low-disk:c");
        Assert.Equal("The system drive C: is nearly full", sys.Title);
    }

    [Fact]
    public void An_unhealthy_drive_is_named_in_the_failing_finding()
    {
        var f = HealthRules.Evaluate(Kor208N(Ssd, Hdd with { Health = "Warning" })).Single(x => x.RuleKey == "disk-failing");
        Assert.Contains("unhealthy: the data drive D: ST2000DM006-2DM164 Warning", f.Evidence);
    }

    [Fact]
    public void Before_probe_v10_the_wording_is_unchanged()
    {
        var old = Kor208N(Ssd with { Letters = null, System = null }, Hdd with { Letters = null, System = null });
        var f = Predictions.Evaluate(old, new MetricHistory()).Single(x => x.RuleKey == "disk-errors:st2000dm006-2dm164");
        Assert.Equal("A drive has unrecoverable read errors", f.Title);
        Assert.Equal("Drive D: is nearly full", HealthRules.Evaluate(old).Single(x => x.RuleKey == "low-disk:d").Title);
    }
}
