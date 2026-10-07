#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The health rules against snapshots captured from real KOR workstations on 2026-09-28, whose
// faults were confirmed by hand the same day. Each test pins what MUST fire and what must NOT
// on that machine -- a rule that fires everywhere is as useless as one that never fires.
//
// WHAT IT COVERS: every rule in HealthRules against six real machines (a failing-disk box, a
// loud/unbacked one, a GPU-hang one, broken WMI, an unplugged USB drive, a non-Lenovo board),
// plus synthetic edges for the date and percentage thresholds. WHAT IT DOES NOT: whether the
// probe reads the right thing on the machine (live runs prove that), and thresholds' fitness
// across the whole fleet -- that is Phase 2's comparison month. A SAME-CLASS FAULT IT WOULD
// NOT CATCH: a new fault class with no rule yet (e.g. token-broker storms on 306) -- the rules
// only know what has been named.
public sealed class HealthRulesTests
{
    private static HealthSnapshot Fixture(string host)
        => HealthSnapshot.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health", host + ".json")));

    private static IReadOnlyDictionary<string, Finding> Findings(string host)
        => HealthRules.Evaluate(Fixture(host)).ToDictionary(f => f.RuleKey);

    // Probe v3 counts distinct GPU resets. Real counts measured 2026-09-29 on the same machines:
    // 206-N 2 (was 236 log entries), 104N 52 (was 6,339), 308 10 (was 1,060).
    private static HealthSnapshot AsV3(string host, int resets)
    {
        var s = Fixture(host);
        return s with { ProbeVersion = 3, Events14d = s.Events14d! with { GpuHang = new EventSummary(resets, DateTime.UtcNow) } };
    }

    [Fact]
    public void V3_two_resets_in_two_weeks_is_not_a_finding()
        => Assert.DoesNotContain(HealthRules.Evaluate(AsV3("KOR-206-N", 2)), x => x.RuleKey == "gpu-hangs");

    [Fact]
    public void V3_ten_resets_is_a_warning_and_the_evidence_says_resets()
    {
        var g = HealthRules.Evaluate(AsV3("KOR-305", 10)).Single(x => x.RuleKey == "gpu-hangs");
        Assert.Equal(Severity.Warning, g.Severity);
        Assert.StartsWith("10 GPU resets in 14 d", g.Evidence);
    }

    [Fact]
    public void V3_fifty_two_resets_with_no_discrete_card_is_critical_and_says_check_the_card()
    {
        var g = HealthRules.Evaluate(AsV3("KOR-104N", 52)).Single(x => x.RuleKey == "gpu-hangs");
        Assert.Equal(Severity.Critical, g.Severity);
        Assert.Contains("no discrete card visible", g.Evidence);
    }

    [Fact]
    public void Kor206N_the_failing_data_drive_and_the_gpu_hangs_are_all_raised()
    {
        var f = Findings("KOR-206-N");
        Assert.Equal(Severity.Critical, f["disk-missing"].Severity);
        Assert.Contains("Samsung SSD 870 EVO", f["disk-missing"].Evidence);
        Assert.Contains("letters with no volume: D", f["disk-missing"].Evidence);
        Assert.Contains("20 controller resets", f["disk-failing"].Evidence);
        Assert.Equal(Severity.Warning, f["gpu-hangs"].Severity);            // 244: bad, not the 500+ tier
        Assert.Contains("hardware-errors", f.Keys);                         // 5 WHEA
        Assert.Contains("crash-loop:opushutil.exe", f.Keys);
        Assert.Contains("outlook-index:kevinw@korstructural.com.ost", f.Keys);
        Assert.DoesNotContain("outlook-index:kevinw@korstructural.com(2).nst", f.Keys);   // 1 failure is not a pattern
        Assert.DoesNotContain("unexpected-shutdowns", f.Keys);             // 1 in 14 d
        Assert.DoesNotContain("fan-profile-loud", f.Keys);                 // set to Balance on 09-28
    }

    [Fact]
    public void Kor216_loud_fans_unbacked_data_and_the_stale_screenconnect_are_raised()
    {
        var f = Findings("KOR-216");
        Assert.Contains("Performance mode", f["fan-profile-loud"].Evidence);
        Assert.Contains("D: 119.1 GB", f["unbacked-data"].Evidence);
        Assert.Contains("2 ScreenConnect", f["remote-tools-stale"].Evidence);
        Assert.Contains("crash-loop:opushutil.exe", f.Keys);
        Assert.DoesNotContain("gpu-hangs", f.Keys);
        Assert.DoesNotContain("disk-missing", f.Keys);
        Assert.DoesNotContain("disk-failing", f.Keys);
    }

    [Fact]
    public void Kor305_only_its_gpu_hangs()
    {
        var f = Findings("KOR-305");
        Assert.Equal(new[] { "gpu-hangs" }, f.Keys.ToArray());
        Assert.Contains("RTX A2000", f["gpu-hangs"].Evidence);
    }

    [Fact]
    public void Kor213_broken_wmi_is_one_plain_finding_and_its_null_lists_do_not_crash_the_rules()
    {
        var s = Fixture("KOR-213");
        Assert.False(s.WmiHealthy);
        var f = HealthRules.Evaluate(s).ToDictionary(x => x.RuleKey);
        Assert.Equal(Severity.Critical, f["wmi-broken"].Severity);
        Assert.DoesNotContain("disk-missing", f.Keys);      // its disk list was unreadable, not empty-of-disks
        Assert.DoesNotContain("probe-incomplete", f.Keys);  // said once, as wmi-broken
    }

    [Fact]
    public void Kor104N_the_worst_gpu_in_the_fleet_is_critical_and_an_unplugged_usb_drive_is_not_a_failure()
    {
        var f = Findings("KOR-104N");
        Assert.Equal(Severity.Critical, f["gpu-hangs"].Severity);          // 6,418 in 14 d
        Assert.Contains("6418", f["gpu-hangs"].Evidence);
        Assert.Contains("Best Performance", f["fan-profile-loud"].Evidence);
        Assert.DoesNotContain("disk-missing", f.Keys);                     // WD My Passport, USBSTOR
    }

    [Fact]
    public void Kor202_non_lenovo_board_gets_no_fan_finding_and_each_crashing_process_is_its_own_finding()
    {
        var f = Findings("KOR-202");
        Assert.DoesNotContain("fan-profile-loud", f.Keys);                 // ASUS: cooling 'n/a'
        Assert.Contains("crash-loop:opushutil.exe", f.Keys);
        Assert.Contains("crash-loop:kamosvc.exe", f.Keys);
        Assert.Contains("crash-loop:prevhost.exe", f.Keys);
        Assert.Contains("D: 53.1 GB", f["unbacked-data"].Evidence);
    }

    // PowerShell unrolls a one-item list to the bare item. On 2026-09-28 every C:-only machine
    // sent Volumes as an object and the whole fleet report died on the first one.
    [Fact]
    public void A_list_that_arrives_as_a_single_object_is_read_as_a_one_item_list()
    {
        const string json = """
            { "Computer": "SYNTH", "CollectedAt": "2026-09-28T12:00:00", "WmiHealthy": true,
              "Volumes": { "Letter": "C", "Label": "", "SizeGB": 100, "FreeGB": 3 },
              "AppCrashes14d": { "Process": "Revit.exe", "Count": 7, "Last": "2026-09-28T10:00:00" },
              "ProbeErrors": [] }
            """;
        var s = HealthSnapshot.Parse(json);
        Assert.Single(s.Volumes);
        var keys = HealthRules.Evaluate(s).Select(f => f.RuleKey).ToList();
        Assert.Contains("low-disk:c", keys);
        Assert.Contains("crash-loop:revit.exe", keys);
    }

    [Theory]
    [InlineData("[]")]        // empty array -> a[0] used to throw ArgumentOutOfRange, escaping HealthSweeper's JsonException catch
    [InlineData("42")]        // a scalar -> AsObject() used to throw InvalidOperation
    [InlineData("[\"x\"]")]
    public void A_probe_that_returns_junk_but_valid_JSON_fails_as_a_JsonException(string json)
        => Assert.Throws<System.Text.Json.JsonException>(() => HealthSnapshot.Parse(json));

    // ---- threshold edges, synthetic ----

    private static HealthSnapshot Clean() => new() { Computer = "SYNTH", CollectedAt = new DateTime(2026, 9, 28), WmiHealthy = true };

    [Fact]
    public void A_clean_machine_raises_nothing()
        => Assert.Empty(HealthRules.Evaluate(Clean()));

    [Fact]
    public void Not_patched_fires_after_35_days_and_not_before()
    {
        var at34 = Clean() with { Update = new UpdateInfo(true, 1, new DateTime(2026, 8, 25), "KB1", "wusa") };
        var at36 = Clean() with { Update = new UpdateInfo(true, 1, new DateTime(2026, 8, 23), "KB1", "wusa") };
        Assert.DoesNotContain(HealthRules.Evaluate(at34), x => x.RuleKey == "not-patched");
        Assert.Contains(HealthRules.Evaluate(at36), x => x.RuleKey == "not-patched");
    }

    [Fact]
    public void A_pending_reboot_is_only_overdue_after_three_days_up()
    {
        var pending = new PendingRebootInfo(true, false, false);
        Assert.DoesNotContain(HealthRules.Evaluate(Clean() with { PendingReboot = pending, Os = new OsInfo("W", "25H2", 26200, 1, null, 70) }), x => x.RuleKey == "reboot-overdue");
        Assert.Contains(HealthRules.Evaluate(Clean() with { PendingReboot = pending, Os = new OsInfo("W", "25H2", 26200, 1, null, 80) }), x => x.RuleKey == "reboot-overdue");
    }

    [Fact]
    public void Low_disk_is_warning_under_10_percent_and_critical_under_5()
    {
        var eight = HealthRules.Evaluate(Clean() with { Volumes = [new VolumeInfo("C", null, 100, 8)] }).Single();
        var three = HealthRules.Evaluate(Clean() with { Volumes = [new VolumeInfo("C", null, 100, 3)] }).Single();
        Assert.Equal(("low-disk:c", Severity.Warning), (eight.RuleKey, eight.Severity));
        Assert.Equal(Severity.Critical, three.Severity);
    }

    [Fact]
    public void A_small_system_drive_flags_on_the_absolute_floor_even_above_10_percent_but_a_large_one_does_not()
    {
        // BK01's C: 20.5 GB free of 99.4 (20.6%) -- Ninja caught it, the percentage-only rule did not.
        var tight = HealthRules.Evaluate(Clean() with { Volumes = [new VolumeInfo("C", null, 99.4, 20.5)] }).Single(x => x.RuleKey == "low-disk:c");
        Assert.Equal(Severity.Warning, tight.Severity);
        // A large drive merely 20% free keeps tens of GB of headroom and must NOT flag (no fleet-wide noise).
        Assert.DoesNotContain(HealthRules.Evaluate(Clean() with { Volumes = [new VolumeInfo("C", null, 500, 100)] }), x => x.RuleKey == "low-disk:c");
    }

    [Fact]
    public void A_vmware_phantom_virtual_sata_node_is_not_a_missing_drive_but_a_real_loss_still_is()
    {
        // A VMware VM's SATA controller lists its unpopulated ports as ghost "VMware Virtual SATA Hard Drive" nodes
        // (BK01, 7 of them, raised Critical in error 2026-10-07). They are not lost drives.
        var ghost = new MissingDiskInfo("VMware Virtual SATA Hard Drive", @"SCSI\DISK&VEN_VMWARE&PROD_VIRTUAL_SATA_HAR\5&354AE4D7&0&010000");
        Assert.DoesNotContain(HealthRules.Evaluate(Clean() with { MissingDisks = [ghost] }), x => x.RuleKey == "disk-missing");
        // A genuinely missing data drive (not a USB stick, not a VMware ghost) still raises it.
        var real = new MissingDiskInfo("Samsung SSD 870 EVO", @"SCSI\DISK&VEN_SAMSUNG&PROD_SAMSUNG_SSD_870\4&ABC123&0&000000");
        Assert.Contains(HealthRules.Evaluate(Clean() with { MissingDisks = [real] }), x => x.RuleKey == "disk-missing");
    }

    [Fact]
    public void An_iscsi_san_volume_is_not_counted_as_unbacked_data_but_a_local_data_drive_is()
    {
        // BK01's E:/F: are iSCSI Synology volumes -- the Veeam repository itself, not a loose local drive (Ian, 2026-10-07).
        var repo = Clean() with
        {
            DataOutsideSystemDrive = [new DataVolumeInfo("E", 1247.7)],
            PhysicalDisks = [new PhysicalDiskInfo("SYNOLOGY Storage", "Unspecified", "iSCSI", 26000, "Healthy", "OK", "E", false, 0)],
        };
        Assert.DoesNotContain(HealthRules.Evaluate(repo), x => x.RuleKey == "unbacked-data");
        // A genuine local data drive with real data on it still raises it.
        var local = Clean() with
        {
            DataOutsideSystemDrive = [new DataVolumeInfo("D", 53.1)],
            PhysicalDisks = [new PhysicalDiskInfo("Samsung SSD 870 EVO", "SSD", "SATA", 500, "Healthy", "OK", "D", false, 0)],
        };
        Assert.Contains(HealthRules.Evaluate(local), x => x.RuleKey == "unbacked-data");
    }

    [Fact]
    public void Only_the_2020_access_engine_dlls_are_stale_not_the_patched_ones()
    {
        var old = Clean() with { Office = new OfficeInfo("16.0.20326.20072", "16.0.5023.1000", true) };
        var patched = Clean() with { Office = new OfficeInfo("16.0.20326.20072", "16.0.5495.1002", true) };
        Assert.Contains(HealthRules.Evaluate(old), x => x.RuleKey == "office-access-engine-stale");
        Assert.DoesNotContain(HealthRules.Evaluate(patched), x => x.RuleKey == "office-access-engine-stale");
    }

    [Fact]
    public void Three_disk_resets_is_failing_two_is_not()
    {
        EventCounts Resets(int n) => new(null, new EventSummary(n, null), null, null, null, null);
        Assert.DoesNotContain(HealthRules.Evaluate(Clean() with { Events14d = Resets(2) }), x => x.RuleKey == "disk-failing");
        Assert.Contains(HealthRules.Evaluate(Clean() with { Events14d = Resets(3) }), x => x.RuleKey == "disk-failing");
    }
}
