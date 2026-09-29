#nullable enable
using Kor.Operations.NetworkOps.Core.Health;
using Kor.Operations.NetworkOps.Core.Learning;
using Xunit;

namespace Kor.Operations.NetworkOps.Tests;

// What the Command Center tells Ian about one PC: the same problem elsewhere, the fleet pattern
// that explains it, how old the information is, and what has fixed it before.
//
// WHAT IT COVERS: "same problem" grouping (per program for crash loops, per family otherwise, never
// the PC itself); a pattern applies only to a PC that has the fact AND the problem; pattern members;
// the stale boundary; quiet (acknowledged/snoozed) findings; learned fixes filtered to one family.
// WHAT IT DOES NOT: the SQL that loads these, or the page drawing them (the app's own XAML tests and
// a look at the running window cover that). A SAME-CLASS FAULT IT WOULD NOT CATCH: the reader loading
// a SUPERSEDED fact as current would make PatternsFor wrong on real data while every case here passes.
public sealed class CommandCenterViewTests
{
    private static readonly DateTime Now = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);

    private static FleetFinding F(string device, string ruleKey, Severity sev = Severity.Warning, DateTime? ack = null, DateTime? snooze = null)
        => new(1, device, ruleKey, sev, ruleKey, "", Now.AddDays(-1), Now, ack, ack is null ? null : "ian", snooze, null);

    [Fact]
    public void Same_problem_elsewhere_is_per_program_for_crash_loops_and_never_the_pc_itself()
    {
        var fleet = new[]
        {
            F("KOR-216", "crash-loop:opushutil.exe"),
            F("KOR-305", "crash-loop:opushutil.exe"),
            F("kor-216", "crash-loop:opushutil.exe"),     // the same PC, differently cased: still itself
            F("KOR-101", "crash-loop:revit.exe"),         // a different program is a different problem
            F("KOR-202", "gpu-hangs"),
        };

        Assert.Equal(["KOR-305"], CommandCenterView.SameProblemElsewhere(fleet[0], fleet));
    }

    [Fact]
    public void Same_problem_elsewhere_groups_by_family_when_the_key_carries_a_subject()
    {
        // disk-filling:C and disk-filling:D are one problem: a disk is filling.
        var fleet = new[] { F("KOR-202", "disk-filling:C"), F("KOR-205", "disk-filling:D"), F("KOR-206-N", "disk-filling:C") };

        Assert.Equal(["KOR-205", "KOR-206-N"], CommandCenterView.SameProblemElsewhere(fleet[0], fleet));
    }

    private static readonly ActivePattern AccessEngine = new("crash-loop:opushutil.exe", "access.engine.2016", "16.0.5044.1000",
        11, 14, 0, 15, "11 of 14 PCs with Access Engine 2016 crash-loop opushutil.exe, against 0 of 15 without", Now.AddDays(-2));

    [Fact]
    public void A_pattern_explains_a_finding_only_on_a_pc_that_has_the_fact()
    {
        var with = new Dictionary<string, string> { ["access.engine.2016"] = "16.0.5044.1000" };
        var otherVersion = new Dictionary<string, string> { ["access.engine.2016"] = "16.0.4519.1000" };
        var without = new Dictionary<string, string> { ["hw.model"] = "Lenovo 30DH" };

        Assert.Single(CommandCenterView.PatternsFor("crash-loop:opushutil.exe", with, [AccessEngine]));
        Assert.Empty(CommandCenterView.PatternsFor("crash-loop:opushutil.exe", otherVersion, [AccessEngine]));
        Assert.Empty(CommandCenterView.PatternsFor("crash-loop:opushutil.exe", without, [AccessEngine]));
        Assert.Empty(CommandCenterView.PatternsFor("crash-loop:revit.exe", with, [AccessEngine]));   // a different problem
    }

    [Fact]
    public void Pattern_members_have_the_fact_and_the_problem_open_now()
    {
        var facts = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["KOR-216"] = new Dictionary<string, string> { ["access.engine.2016"] = "16.0.5044.1000" },
            ["KOR-305"] = new Dictionary<string, string> { ["access.engine.2016"] = "16.0.5044.1000" },   // has the fact, problem not open
            ["KOR-101"] = new Dictionary<string, string>(),                                                // problem open, no fact
        };
        var open = new[] { F("KOR-216", "crash-loop:opushutil.exe"), F("KOR-101", "crash-loop:opushutil.exe") };

        Assert.Equal(["KOR-216"], CommandCenterView.MembersOf(AccessEngine, facts, open));
    }

    [Fact]
    public void Freshness_goes_stale_after_three_days_and_never_checked_is_its_own_state()
    {
        Assert.Equal(Freshness.NeverChecked, CommandCenterView.FreshnessOf(null, Now));
        Assert.Equal(Freshness.Current, CommandCenterView.FreshnessOf(Now - CommandCenterView.StaleAfter, Now));
        Assert.Equal(Freshness.Stale, CommandCenterView.FreshnessOf(Now - CommandCenterView.StaleAfter - TimeSpan.FromMinutes(1), Now));
    }

    [Fact]
    public void Ago_reads_as_a_duration()
    {
        Assert.Equal("never", CommandCenterView.Ago(null, Now));
        Assert.Equal("just now", CommandCenterView.Ago(Now.AddSeconds(-20), Now));
        Assert.Equal("7 min ago", CommandCenterView.Ago(Now.AddMinutes(-7), Now));
        Assert.Equal("30 h ago", CommandCenterView.Ago(Now.AddHours(-30), Now));
        Assert.Equal("3 d ago", CommandCenterView.Ago(Now.AddDays(-3), Now));
    }

    [Fact]
    public void Quiet_means_acknowledged_or_snoozed_into_the_future()
    {
        Assert.False(F("A", "gpu-hangs").IsQuiet(Now));
        Assert.True(F("A", "gpu-hangs", ack: Now.AddHours(-1)).IsQuiet(Now));
        Assert.True(F("A", "gpu-hangs", snooze: Now.AddHours(1)).IsQuiet(Now));
        Assert.False(F("A", "gpu-hangs", snooze: Now.AddHours(-1)).IsQuiet(Now));   // the snooze ran out
    }

    [Fact]
    public void Changes_skip_the_baseline_and_show_changes_additions_and_removals_newest_first()
    {
        var baseline = Now.AddDays(-10);
        var history = new (string, string, DateTime, DateTime?)[]
        {
            ("gpu.driver", "32.0.15.8142", baseline, Now.AddDays(-2)),
            ("gpu.driver", "32.0.15.8180", Now.AddDays(-2), Now.AddDays(-1)),
            ("gpu.driver", "32.0.15.8200", Now.AddDays(-1), null),
            ("bios.version", "S03KT61A", baseline, null),                 // baseline only: no change
            ("app.newforma", "2023.1", baseline, Now.AddDays(-4)),         // uninstalled
            ("app.tekla", "2025.0", Now.AddDays(-3), null),                // installed after the baseline
        };

        var changes = CommandCenterView.ChangesFrom(history);

        Assert.Equal(
            [
                "gpu.driver 32.0.15.8180 → 32.0.15.8200",
                "gpu.driver 32.0.15.8142 → 32.0.15.8180",
                "app.tekla added (2025.0)",
                "app.newforma removed (was 2023.1)",
            ],
            changes.Select(c => c.Description));
        Assert.Equal(Now.AddDays(-4), changes[3].AtUtc);
    }

    [Fact]
    public void No_history_is_no_changes() => Assert.Empty(CommandCenterView.ChangesFrom([]));

    [Fact]
    public void Learned_fixes_come_only_from_the_findings_own_family()
    {
        var driver = new FactChange("gpu.driver", "32.0.15.8142", "32.0.15.8180");
        var resolutions = new[]
        {
            new Resolution("gpu-hangs", Now.AddDays(-5), false, [driver], []),
            new Resolution("gpu-hangs", Now.AddDays(-4), false, [driver], []),
            new Resolution("disk-filling:C", Now.AddDays(-3), false, [driver], []),
            new Resolution("disk-filling:C", Now.AddDays(-2), false, [driver], []),
        };

        var fix = Assert.Single(CommandCenterView.LearnedFixesFor("gpu-hangs", resolutions));
        Assert.Equal("gpu.driver changed", fix.Change);
        Assert.Equal(2, fix.Times);
        Assert.Equal(2, fix.OfResolutions);
    }
}
