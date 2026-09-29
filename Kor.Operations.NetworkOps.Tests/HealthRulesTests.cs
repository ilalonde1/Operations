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
