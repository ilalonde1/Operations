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
    public void A_drive_filling_up_is_named_the_same_way()
    {
        var s = Kor208N();
        var h = new MetricHistory();
        // C: losing 4 GB a day for 3 days (KOR-208-N, 2026-10-02): 120 GB free -> about a month.
        for (var d = 3; d >= 0; d--) h.Add(Metrics.DiskFreeGb, "C", s.CollectedAt.AddDays(-d), 120 + 4 * d);
        var f = Predictions.Evaluate(s, h).Single(x => x.RuleKey == "disk-filling:c");
        Assert.StartsWith("The system drive C: will be full in about", f.Title);
    }

    [Fact]
    public void An_unhealthy_drive_is_named_in_the_failing_finding()
    {
        var f = HealthRules.Evaluate(Kor208N(Ssd, Hdd with { Health = "Warning" })).Single(x => x.RuleKey == "disk-failing");
        Assert.Contains("unhealthy: the data drive D: ST2000DM006-2DM164 Warning", f.Evidence);
    }

    // ---- probe v11: a drive whose data Windows no longer shows (KOR-208-N, 2026-10-02 15:05, as read at 17:30)

    private static HealthSnapshot Kor208NAfterTheFailure() => Kor208N(Ssd, Hdd with { Letters = "", UnmountedGB = 1863 }) with
    {
        Volumes = [new("C", "Windows", 474.7, 209.4)],
        OrphanDriveLetters = ["D", "E"],
    };

    [Fact]
    public void A_drive_whose_volume_dropped_out_is_critical_and_says_which_letters_it_left()
    {
        var f = HealthRules.Evaluate(Kor208NAfterTheFailure()).Single(x => x.RuleKey == "disk-unmounted:st2000dm006-2dm164");
        Assert.Equal(Severity.Critical, f.Severity);
        Assert.Equal("A data drive's volume is no longer mounted", f.Title);
        Assert.Equal("ST2000DM006-2DM164 (2 TB hard drive): 1,863 GB of data partition with no letter | letters left with nothing behind them: D, E | copy the data off with a recovery tool before anything writes to it", f.Evidence);
        // Its other findings name it the same way.
        Assert.Equal("The data drive that is no longer mounted has unrecoverable read errors",
            Predictions.Evaluate(Kor208NAfterTheFailure(), new MetricHistory()).Single(x => x.RuleKey == "disk-errors:st2000dm006-2dm164").Title);
    }

    [Fact]
    public void A_blank_new_drive_and_the_system_drive_never_raise_it()
    {
        Assert.DoesNotContain(HealthRules.Evaluate(Kor208N(Ssd, Hdd with { Letters = "", UnmountedGB = 0 })), x => x.RuleKey.StartsWith("disk-unmounted", StringComparison.Ordinal));
        Assert.DoesNotContain(HealthRules.Evaluate(Kor208N(Ssd with { UnmountedGB = 400 }, Hdd)), x => x.RuleKey.StartsWith("disk-unmounted", StringComparison.Ordinal));
        Assert.DoesNotContain(HealthRules.Evaluate(Kor208N()), x => x.RuleKey.StartsWith("disk-unmounted", StringComparison.Ordinal));   // before v11: null
        Assert.Equal("a drive with no letter", Drives.Role(Hdd with { Letters = "", UnmountedGB = 0 }));
    }

    [Fact]
    public void The_tile_says_not_mounted_and_is_red()
    {
        var s = Kor208NAfterTheFailure();
        var open = HealthRules.Evaluate(s).Concat(Predictions.Evaluate(s, new MetricHistory())).Select(x => (x.RuleKey, x.Severity)).ToList();
        var tile = PcComponents.Of(s, open).Single(t => t.Line2 == "ST2000DM006-2DM164");
        Assert.Equal("Not mounted", tile.Title);
        Assert.Equal((Severity?)Severity.Critical, tile.Worst);
        Assert.Contains("disk-unmounted:st2000dm006-2dm164", tile.RuleKeys);
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
