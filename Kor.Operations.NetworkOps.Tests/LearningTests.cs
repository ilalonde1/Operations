#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// The learning layer: facts, change tracking, metrics, predictions, fleet correlation, fix
// learning, knowledge coverage and device status. Real v2 snapshots (Fixtures/health-v2, captured
// 2026-09-28) wherever a real machine shows the case; synthetic series only for time behaviour no
// single snapshot can show.
//
// WHAT IT COVERS: fact extraction incl. placeholder boards, app de-noising and broken WMI; the fact
// diff; metric extraction; every prediction rule's firing edge; the Theil-Sen slope's resistance to
// an outlier; Fisher's exact test against known values; the GPU pattern from 28 Sep reproduced by the
// correlation engine and its false-pattern guards; fix ranking; that every rule family has knowledge.
// WHAT IT DOES NOT: the store, the service's orchestration of these steps, or whether thresholds suit
// the fleet (the comparison month). A SAME-CLASS FAULT IT WOULD NOT CATCH: a fact whose value is
// reported inconsistently by the probe between sweeps (e.g. a driver string with and without a
// prefix) would read as a change every sweep -- only a live multi-sweep run shows that.
public sealed class LearningTests
{
    private static HealthSnapshot V2(string host)
        => HealthSnapshot.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "health-v2", host + ".json")));

    // ------------------------------------------------------------------ facts

    [Fact]
    public void Facts_of_a_lenovo_workstation_name_its_model_gpu_and_key_apps()
    {
        var f = Facts.Extract(V2("KOR-206-N"))!;
        Assert.Equal("Lenovo ThinkStation P3 Tower", f[Facts.Model]);
        Assert.Equal("NVIDIA T400 4GB", f[Facts.GpuName]);          // the discrete card, not the Intel one listed first
        Assert.Equal("32.0.15.9651", f[Facts.GpuDriver]);
        Assert.Equal("26200.9550", f[Facts.OsBuild]);
        Assert.Equal("96", f[Facts.RamGb]);
        Assert.Contains("app.revit.2025", f.Keys);
        Assert.Contains("app.etabs.23", f.Keys);
        Assert.Equal("16.0.5044.1000", f["access.engine.2016"]);
    }

    [Fact]
    public void A_self_built_pc_is_identified_by_its_motherboard_not_the_placeholder_strings()
    {
        var f = Facts.Extract(V2("KOR-202"))!;
        Assert.Equal("ASUSTeK COMPUTER INC. PRIME Z390-A", f[Facts.Model]);
        Assert.DoesNotContain("System manufacturer", f[Facts.Model]);
        Assert.Equal("NVIDIA GeForce GTX 1660 Ti", f[Facts.GpuName]);
    }

    [Fact]
    public void Revit_update_and_content_entries_collapse_to_one_fact_per_year_at_the_highest_version()
    {
        var f = Facts.Extract(V2("KOR-305"))!;
        Assert.Equal("21.1.90.15", f["app.revit.2021"]);            // "Revit 2021" + "Revit 2021.1.9" → one
        Assert.Equal("27.2.0.39", f["app.revit.2027"]);
        Assert.DoesNotContain(f.Keys, k => k.Contains("mep", StringComparison.OrdinalIgnoreCase) || k.Contains("content", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Broken_wmi_yields_no_facts_so_nothing_known_is_superseded()
    {
        Assert.Null(Facts.Extract(V2("KOR-213")));
        Assert.Empty(FactDiff.Compute(new Dictionary<string, string> { [Facts.GpuName] = "NVIDIA Quadro P1000" }, null));
    }

    [Fact]
    public void The_fact_diff_names_changes_additions_and_removals()
    {
        var current = new Dictionary<string, string> { [Facts.GpuDriver] = "32.0.15.8142", ["app.bluebeam"] = "20.3.30", [Facts.RamGb] = "32" };
        var observed = new Dictionary<string, string> { [Facts.GpuDriver] = "32.0.15.9651", [Facts.RamGb] = "32", ["app.revit.2026"] = "26.5.0.55" };
        var d = FactDiff.Compute(current, observed).ToDictionary(c => c.Fact);
        Assert.Equal(("32.0.15.8142", "32.0.15.9651"), (d[Facts.GpuDriver].OldValue, d[Facts.GpuDriver].NewValue));
        Assert.Null(d["app.bluebeam"].NewValue);                    // uninstalled
        Assert.Null(d["app.revit.2026"].OldValue);                  // installed
        Assert.False(d.ContainsKey(Facts.RamGb));                   // unchanged is not a change
    }

    // ------------------------------------------------------------------ metrics + predictions on real machines

    [Fact]
    public void Kor202_a_45_gb_mailbox_and_a_seven_year_old_hard_disk_are_raised_before_either_breaks()
    {
        var s = V2("KOR-202");
        var h = new MetricHistory();
        foreach (var p in Metrics.Extract(s)) h.Add(p.Metric, p.Subject, s.CollectedAt, p.Value);
        var f = Predictions.Evaluate(s, h).ToDictionary(x => x.RuleKey);

        var big = f.Values.Single(x => x.RuleKey.StartsWith("mailbox-near-limit:", StringComparison.Ordinal));
        Assert.Contains("45.8 GB of 50 GB", big.Evidence);
        Assert.Equal(Severity.Warning, big.Severity);               // 45.8 < 47
        Assert.DoesNotContain(f.Keys, k => k.Contains("jmarkulin@korstructural.com.ost", StringComparison.Ordinal)
                                           && !k.Contains(" - jm", StringComparison.Ordinal));   // the 39.5 GB one is fine

        var hdd = f["disk-aging:st4000dm004-2cv104"];
        Assert.Contains("7.4 years", hdd.Evidence);                 // 65,298 hours
    }

    [Fact]
    public void A_healthy_new_laptop_with_a_fresh_battery_raises_no_prediction()
    {
        var s = V2("KOR-1001");
        Assert.Equal(102, s.Battery!.HealthPct);
        var h = new MetricHistory();
        foreach (var p in Metrics.Extract(s)) h.Add(p.Metric, p.Subject, s.CollectedAt, p.Value);
        Assert.Empty(Predictions.Evaluate(s, h));
    }

    // ------------------------------------------------------------------ predictions over time (synthetic)

    private static readonly DateTime T0 = new(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc);

    private static HealthSnapshot At(DateTime t) => new() { Computer = "SYNTH", CollectedAt = t, WmiHealthy = true };

    [Fact]
    public void A_drive_losing_2_gb_a_day_with_40_left_is_projected_full_in_about_20_days()
    {
        var h = new MetricHistory();
        for (var d = 0; d <= 10; d++) h.Add(Metrics.DiskFreeGb, "C", T0.AddDays(d), 60 - 2 * d);   // 60 → 40
        var f = Predictions.Evaluate(At(T0.AddDays(10)), h).Single(x => x.RuleKey == "disk-filling:c");
        Assert.Equal(Severity.Warning, f.Severity);
        Assert.Contains("about 20 days", f.Title);
    }

    [Fact]
    public void A_cleanup_restarts_the_projection_instead_of_being_averaged_across()
    {
        // Losing 2 GB/day, someone frees 30 GB on day 4, and it keeps losing 2 GB/day. Across the
        // jump every slope would read "growing"; from the jump the drive is full in 64/2 = 32 days.
        var h = new MetricHistory();
        double[] free = [50, 48, 46, 44, 74, 72, 70, 68, 66, 64];
        for (var d = 0; d < free.Length; d++) h.Add(Metrics.DiskFreeGb, "D", T0.AddDays(d), free[d]);
        Assert.True(Predictions.TheilSenPerDay(h.Get(Metrics.DiskFreeGb, "D")) > 0, "premise: across the jump the slope reads as growing");
        var f = Predictions.Evaluate(At(T0.AddDays(9)), h).Single(x => x.RuleKey == "disk-filling:d");
        Assert.Contains("about 32 days", f.Title);
    }

    [Fact]
    public void Theil_sen_shrugs_off_a_single_bad_reading()
    {
        var h = new MetricHistory();
        double[] free = [50, 48, 46, 5, 42, 40, 38, 36];            // one garbage reading on day 3
        for (var d = 0; d < free.Length; d++) h.Add(Metrics.DiskFreeGb, "C", T0.AddDays(d), free[d]);
        Assert.Equal(-2.0, Predictions.TheilSenPerDay(h.Get(Metrics.DiskFreeGb, "C")), 1);
    }

    [Fact]
    public void A_drive_that_is_not_shrinking_raises_nothing()
    {
        var h = new MetricHistory();
        for (var d = 0; d <= 10; d++) h.Add(Metrics.DiskFreeGb, "C", T0.AddDays(d), 12);
        Assert.DoesNotContain(Predictions.Evaluate(At(T0.AddDays(10)), h), x => x.RuleKey.StartsWith("disk-filling", StringComparison.Ordinal));
    }

    [Fact]
    public void Gpu_hangs_doubling_in_a_week_is_raised_as_getting_worse()
    {
        var h = new MetricHistory();
        h.Add(Metrics.GpuHangs14d, "", T0, 20);
        h.Add(Metrics.GpuHangs14d, "", T0.AddDays(7), 55);
        var f = Predictions.Evaluate(At(T0.AddDays(7)), h).Single(x => x.RuleKey == "gpu-hangs-rising");
        Assert.Contains("20 → 55", f.Evidence);
    }

    [Fact]
    public void A_drive_logging_new_read_errors_is_raised_the_week_it_starts()
    {
        var s = At(T0.AddDays(5)) with { DiskReliability = [new DiskReliabilityInfo("WDC HDD", "S1", null, 30, null, 12, 0, 0, 20000)] };
        var h = new MetricHistory();
        h.Add(Metrics.DiskReadErrors, "WDC HDD", T0, 4);
        h.Add(Metrics.DiskReadErrors, "WDC HDD", T0.AddDays(5), 12);
        var f = Predictions.Evaluate(s, h).Single(x => x.RuleKey == "disk-errors:wdc hdd");
        Assert.Contains("8 new", f.Evidence);
    }

    [Fact]
    public void Uncorrected_read_errors_are_critical_immediately()
    {
        var s = At(T0) with { DiskReliability = [new DiskReliabilityInfo("WDC HDD", "S1", null, 30, null, 12, 3, 0, 20000)] };
        Assert.Equal(Severity.Critical, Predictions.Evaluate(s, new MetricHistory()).Single(x => x.RuleKey == "disk-errors:wdc hdd").Severity);
    }

    [Fact]
    public void Windows_10_is_flagged_as_unsupported_and_windows_11_is_not()
    {
        var w10 = At(T0) with { Os = new OsInfo("Windows 10 Pro", "22H2", 19045, 6456, null, 5) };
        var w11 = At(T0) with { Os = new OsInfo("Windows 11 Pro", "25H2", 26200, 9550, null, 5) };
        Assert.Contains(Predictions.Evaluate(w10, new MetricHistory()), x => x.RuleKey == "os-unsupported");
        Assert.DoesNotContain(Predictions.Evaluate(w11, new MetricHistory()), x => x.RuleKey == "os-unsupported");
    }

    [Fact]
    public void A_mailbox_growing_toward_the_limit_is_raised_before_it_reaches_40_gb()
    {
        var s = At(T0.AddDays(30)) with { MailStores = [new MailStoreInfo("jdoe", "jdoe@korstructural.com.ost", 38, null)] };
        var h = new MetricHistory();
        for (var d = 0; d <= 30; d += 5) h.Add(Metrics.MailStoreGb, @"jdoe\jdoe@korstructural.com.ost", T0.AddDays(d), 29 + 0.3 * d);   // 0.3 GB/day
        var f = Predictions.Evaluate(s, h).Single(x => x.RuleKey.StartsWith("mailbox-near-limit", StringComparison.Ordinal));
        Assert.Contains("limit in about 40 days", f.Evidence);      // (50-38)/0.3
    }

    // ------------------------------------------------------------------ fleet correlation

    [Fact]
    public void Fisher_exact_matches_known_values()
    {
        Assert.Equal(1.0 / 70, FleetCorrelation.FisherOneSided(4, 0, 0, 4), 6);   // [[4,0],[0,4]]: 1 / C(8,4)
        Assert.Equal(1.0, FleetCorrelation.FisherOneSided(0, 4, 4, 0), 6);         // the opposite direction is never enrichment
    }

    [Fact]
    public void The_gpu_pattern_of_28_september_is_found_automatically()
    {
        // 4 PCs on Quadro driver 8142, all hang; 26 others on other drivers, 11 hang (the real counts that night).
        var fleet = new List<FleetMember>();
        for (var i = 0; i < 4; i++) fleet.Add(Member($"Q{i}", "32.0.15.8142", hangs: true));
        for (var i = 0; i < 26; i++) fleet.Add(Member($"O{i}", i % 2 == 0 ? "32.0.15.9651" : "32.0.16.1088", hangs: i < 11));
        var ins = FleetCorrelation.Find(fleet).Single(x => x.Family == "gpu-hangs" && x.Fact == Facts.GpuDriver);
        Assert.Equal("32.0.15.8142", ins.Value);
        Assert.Equal((4, 4, 11, 26), (ins.AffectedWith, ins.TotalWith, ins.AffectedWithout, ins.TotalWithout));
        Assert.True(ins.PValue < 0.05, $"p = {ins.PValue}");
        Assert.StartsWith("gpu-hangs: 4 of 4 PCs with gpu.driver = 32.0.15.8142 have it, against 11 of 26 without", ins.Summary);
    }

    [Fact]
    public void Two_pcs_sharing_a_value_by_chance_is_not_a_pattern()
    {
        // 2 of 3 on a driver hang, 5 of 15 others hang: rate 0.67 but p is nowhere near 0.05.
        var fleet = new List<FleetMember>();
        for (var i = 0; i < 3; i++) fleet.Add(Member($"A{i}", "X", hangs: i < 2));
        for (var i = 0; i < 15; i++) fleet.Add(Member($"B{i}", "Y", hangs: i < 5));
        Assert.Empty(FleetCorrelation.Find(fleet));
    }

    [Fact]
    public void Serial_numbers_and_dates_never_explain_a_pattern()
    {
        var fleet = Enumerable.Range(0, 10).Select(i => new FleetMember($"P{i}",
            new Dictionary<string, string> { [Facts.BiosDate] = i < 4 ? "2019-01-03" : "2025-02-13", [Facts.OsBuild] = i < 4 ? "26100.1" : "26200.9550" },
            i < 4 ? new HashSet<string> { "gpu-hangs" } : new HashSet<string>())).ToList();
        Assert.Empty(FleetCorrelation.Find(fleet));
    }

    private static FleetMember Member(string name, string driver, bool hangs)
        => new(name, new Dictionary<string, string> { [Facts.GpuDriver] = driver }, hangs ? new HashSet<string> { "gpu-hangs" } : new HashSet<string>());

    // ------------------------------------------------------------------ fix learning

    [Fact]
    public void A_change_that_cleared_the_same_problem_twice_is_offered_as_a_fix_and_a_one_off_is_not()
    {
        var driver = new FactChange(Facts.GpuDriver, "32.0.15.8142", "32.0.15.9651");
        var rs = new[]
        {
            new Resolution("gpu-hangs", T0, Rebooted: true, [driver], []),
            new Resolution("gpu-hangs", T0.AddDays(2), Rebooted: true, [driver], []),
            new Resolution("gpu-hangs", T0.AddDays(3), Rebooted: false, [new FactChange("app.bluebeam", "21.6.1", null)], []),
        };
        var fixes = FixLearning.Rank(rs);
        Assert.Equal("gpu.driver changed", fixes[0].Change);                  // specific change ranks above "restart" on a tie
        Assert.Equal((2, 3), (fixes[0].Times, fixes[0].OfResolutions));
        Assert.Contains(fixes, x => x.Change == "restart");
        Assert.DoesNotContain(fixes, x => x.Change == "app.bluebeam removed");  // once is an anecdote
        Assert.Equal("cleared after gpu.driver 32.0.15.8142 → 32.0.15.9651, a restart", rs[0].Summary);
    }

    [Fact]
    public void Families_group_per_process_and_per_drive_findings()
        => Assert.Equal(("crash-loop", "disk-filling", "wmi-broken"),
            (FixLearning.FamilyOf("crash-loop:revit.exe"), FixLearning.FamilyOf("disk-filling:c"), FixLearning.FamilyOf("wmi-broken")));

    // ------------------------------------------------------------------ knowledge coverage

    [Fact]
    public void Every_family_a_rule_can_raise_has_a_knowledge_entry()
    {
        // Source-scan: every finding key literal the rules and jobs can emit. A new rule without an
        // explanation fails here, so the Command Center never shows a finding it cannot explain.
        var root = RepoRoot();
        var files = new[]
        {
            Path.Combine(root, "Kor.Operations.NetworkOps.Core", "Health", "HealthRules.cs"),
            Path.Combine(root, "Kor.Operations.NetworkOps.Core", "Learning", "Predictions.cs"),
            Path.Combine(root, "Kor.Operations.NetworkOps.Service", "Jobs", "Jobs.cs"),
        };
        var families = new SortedSet<string>(StringComparer.Ordinal);
        var created = new Regex(@"new\(\$?""([a-z]+(?:-[a-z]+)+)[:""]");                        // new("disk-missing", … / new($"low-disk:{…}", …
        var worsening = new Regex(@"Worsening\([^;]*?,\s*\$?""([a-z]+(?:-[a-z]+)+)[:""]");      // Worsening(…, "gpu-hangs-rising", … / $"crashes-rising:{…}"
        var constKey = new Regex(@"SilentRule\s*=\s*""([a-z-]+)""");
        foreach (var f in files)
        {
            var src = File.ReadAllText(f);
            foreach (var rx in new[] { created, worsening, constKey })
                foreach (Match m in rx.Matches(src)) families.Add(m.Groups[1].Value);
        }
        Assert.True(families.Count >= 30, $"scan found only {families.Count} families -- the scan is broken: {string.Join(", ", families)}");
        var missing = families.Where(fam => !Knowledge.Families.Contains(fam)).ToList();
        Assert.True(missing.Count == 0, "no knowledge entry for: " + string.Join(", ", missing));
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "Directory.Build.props"))) d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }

    // ------------------------------------------------------------------ device status

    [Fact]
    public void An_acknowledged_critical_stops_colouring_the_pc_but_an_expired_snooze_does_not()
    {
        var now = T0;
        OpenFindingView F(Severity s, DateTime? ack = null, DateTime? snooze = null) => new("k", s, ack, snooze);
        Assert.Equal(HealthState.Unknown, DeviceStatus.Of(false, [], now));
        Assert.Equal(HealthState.Healthy, DeviceStatus.Of(true, [], now));
        Assert.Equal(HealthState.Critical, DeviceStatus.Of(true, [F(Severity.Critical), F(Severity.Info)], now));
        Assert.Equal(HealthState.Watch, DeviceStatus.Of(true, [F(Severity.Critical, ack: now), F(Severity.Info)], now));
        Assert.Equal(HealthState.Healthy, DeviceStatus.Of(true, [F(Severity.Warning, snooze: now.AddDays(1))], now));
        Assert.Equal(HealthState.Attention, DeviceStatus.Of(true, [F(Severity.Warning, snooze: now.AddDays(-1))], now));
    }
}
