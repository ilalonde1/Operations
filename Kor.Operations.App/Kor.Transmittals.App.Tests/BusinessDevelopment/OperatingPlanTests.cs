#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Kor.MorningBrief;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Plan;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// The live operating plan (migration 372): every {Metric} the seeded text names is one the plan can read, readings fill the
/// prose and never print a stale or invented number, today's detail lands on the right stage, and the one page carries every
/// stage, goal and view with the links the app routes. Writes director-plan.html for the render harness.
/// Does NOT cover the SQL that loads the plan or the radar counts, the composer's new readings against a live engine, the
/// WebView2 hosting or PrintToPdf itself; a wrong column in SqlOperatingPlanStore, or a Utilization KPI whose text stops
/// parsing as a number, would pass every test here.
/// </summary>
public sealed class OperatingPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 19, 15, 0, 0, TimeSpan.Zero);

    private static PlanStageRow Stage(int no, string pillar, string today) =>
        new(no, pillar, $"Stage {no}", $"Owner {no}", $"What {no}", today, $"System {no}", $"Weak {no}", no % 2 == 0 ? "Broken" : "Watch");

    private static OperatingPlanModel Model() => new(
        Enumerable.Range(1, 9).Select(n => Stage(n, n <= 5 ? "BD" : n == 9 ? "IT" : "Ops",
            n == 2 ? "{PursuitsLive} live; {PursuitsWithFee} carry a fee." : n == 8 ? "{ArOver90} over 90 days; {Dso} days." : "Today {0}")).ToList(),
        new[]
        {
            new PlanGoalRow(1, "BD", 2, "Every live pursuit has a fee estimate", "PursuitsWithFee", 0, null, "every live pursuit ({PursuitsLive})",
                new DateOnly(2027, 3, 31), "Recommended", null, null),
            new PlanGoalRow(7, "Ops", 8, "Owed over 90 days under $700k", "ArOver90", null, 700_000, "under $700k", new DateOnly(2027, 3, 31), "Decided",
                Now, "ilalonde"),
        },
        new[] { new PlanPointRow(1, "BD", "WhereToPlay", "Grow repeat developers.", "{PursuitsLive} live."), new PlanPointRow(2, "All", "Decide", "The goals.", "Yours.") },
        new[] { new PlanRhythmRow(1, "Daily hour", "You, 6:05.", "Moves.", "All of them.", 1, 9) },
        new[] { new PlanPillarRow("IT", "IT", "Monitoring.", "NetworkOps.", "{ItUpdatesDue} due.", "Patch.") });

    private static PursuitState Pursuit(long id, decimal? fee, int touchedDaysAgo, int stage = 1) =>
        new(id, "cmurtagh@korstructural.com", "Conor Murtagh", stage, $"Project {id}", $"Buyer {id}", 7, null, fee, Now.AddDays(-90), Now.AddDays(-touchedDaysAgo));

    private static readonly ImpactReading[] Scoreboard =
    {
        new(new DateOnly(2026, 10, 18), "ArOver90", 1_417_771), new(new DateOnly(2026, 10, 19), "ArOver90", 1_414_621),
    };

    [Fact]
    public void Every_metric_the_seeded_plan_names_is_one_the_plan_can_read()
    {
        var sql = File.ReadAllText(Path.Combine(BdDirectorBuildersRegressionTests.RepoRoot(), "Kor.Opportunities.Data", "Schema", "372_OperatingPlan.sql"));
        sql = Regex.Replace(sql, @"(?s)/\*.*?\*/", ""); // the header explains the {Metric} form; only the seed counts
        var named = PlanReadings.TokensIn(sql).Distinct().ToList();
        Assert.NotEmpty(named);
        Assert.DoesNotContain(named, n => PlanMetrics.Find(n) is null);
        // Goal metrics are written as quoted keys in the goal seed, not tokens; they must be readable too.
        var goalMetrics = Regex.Matches(sql, @"N'(PursuitsWithFee|[A-Z][A-Za-z0-9]+)', [0-9.NUL]+, [0-9.NUL]+, N'").Select(m => m.Groups[1].Value).ToList();
        Assert.Contains("ArOver90", goalMetrics);
        Assert.DoesNotContain(goalMetrics, m => PlanMetrics.Find(m) is null);
    }

    [Fact]
    public void Readings_fill_the_prose_and_a_metric_with_no_reading_prints_a_dash_not_a_number()
    {
        var readings = PlanReadings.From(Scoreboard, new[] { Pursuit(1, 80_000m, 3), Pursuit(2, null, 30), Pursuit(3, null, 20, stage: 7) }, null, 4, Now);
        Assert.Equal("2 live; 1 carry a fee.", readings.Fill("{PursuitsLive} live; {PursuitsWithFee} carry a fee."));
        Assert.Equal("$1.41M over 90 days; — days.", readings.Fill("{ArOver90} over 90 days; {Dso} days."));
        Assert.Equal("— signals", readings.Fill("{Signals30d} signals"));
        Assert.Equal("1", readings.Show("PursuitsUntouched14"));
        Assert.Equal(("−$3k", true), readings.Change("ArOver90"));
        Assert.Equal("—", readings.Fill("{NoSuchMetric}"));
    }

    [Fact]
    public void Money_days_and_percent_print_the_way_the_plan_reads_them()
    {
        Assert.Equal("$2.26M", PlanMetrics.Format(PlanUnit.Money, 2_262_438));
        Assert.Equal("$367k", PlanMetrics.Format(PlanUnit.Money, 366_838));
        Assert.Equal("$950", PlanMetrics.Format(PlanUnit.Money, 950));
        Assert.Equal("70.8%", PlanMetrics.Format(PlanUnit.Percent, 70.8));
        Assert.Equal("174", PlanMetrics.Format(PlanUnit.Days, 174.4));
    }

    [Fact]
    public void Roll_up_asks_land_on_the_stage_their_anchor_serves_and_the_hours_moves_on_operations_and_it()
    {
        PursuitTouchstoneRow Rule(string applies, string anchor) => new(1, applies, anchor, 0, 0, "Rule", "Carries", Array.Empty<string>());
        Assert.Equal(2, PlanItems.StageOf(Rule("Relationship", "Opened")));
        Assert.Equal(2, PlanItems.StageOf(Rule("Relationship", "LastTouch")));
        Assert.Equal(2, PlanItems.StageOf(Rule("Any", "NextAction")));
        Assert.Equal(3, PlanItems.StageOf(Rule("Tender", "Opened")));
        Assert.Equal(3, PlanItems.StageOf(Rule("Tender", "Deadline")));
        Assert.Equal(4, PlanItems.StageOf(Rule("Any", "Submitted")));
        Assert.Equal(5, PlanItems.StageOf(Rule("Any", "Won")));

        var due = new TouchstoneDue(Pursuit(9, null, 40) with { Title = "" }, Rule("Any", "NextAction"), new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30));
        var hour = new DirectorHourContent("2026-10-19", "Morning", "t", null, Array.Empty<HourPlanLine>(), null, Array.Empty<HourMove>(),
            Array.Empty<string>(), Array.Empty<string>(), null, null)
        {
            Sections = new[]
            {
                new HourSection("COO", "c", Array.Empty<HourPlanLine>(), new[] { Move("Chase the 90-day invoices") }, Array.Empty<string>()),
                new HourSection("IT", "i", Array.Empty<HourPlanLine>(), new[] { Move("Patch the 35") }, Array.Empty<string>()),
            },
        };
        var items = PlanItems.From(new[] { due }, hour);
        Assert.Equal(new[] { 2, 6, 9 }, items.Select(i => i.StageNo).ToArray());
        // A pursuit with no title reads as its buyer, not "(Buyer)".
        Assert.StartsWith("Buyer 9 · Conor", items[0].Title);
        Assert.Equal("kor://pursuit/9", items[0].Link);
    }

    private static HourMove Move(string title) => new(1, title, 10, new[] { "why" }, null, Array.Empty<HourDraft>(), Array.Empty<string>(), Array.Empty<HourLink>());

    [Fact]
    public void The_one_page_carries_every_stage_goal_and_view_with_the_links_the_app_routes()
    {
        var html = OperatingPlanPage.Draw(Model(), Scoreboard, new[] { Pursuit(1, 80_000m, 3) }, new PlanRadar(1731, 128, 140),
            Array.Empty<TouchstoneDue>(), null, Now);
        foreach (var n in Enumerable.Range(1, 9)) Assert.Contains($"data-stage=\"{n}\"", html);
        foreach (var view in new[] { "v-map", "v-strategy", "v-rhythm", "v-platform" }) Assert.Contains($"id=\"{view}\"", html);
        Assert.Contains("Every live pursuit has a fee estimate", html);
        Assert.Contains("every live pursuit (1)", html);
        Assert.Contains(OperatingPlanHtml.PrintLink, html);
        Assert.Contains(OperatingPlanHtml.DecidePrefix + "1\"", html); // recommended goal: Decide link
        Assert.DoesNotContain(OperatingPlanHtml.DecidePrefix + "7\"", html); // decided goal: none
        Assert.DoesNotContain("{", Regex.Replace(html, "(?s)<style>.*?</style>|<script>.*?</script>", "")); // no unfilled token
        Assert.Contains(".screen{display:none!important}", html); // print hides the toolbar and screen-only parts

        Assert.True(OperatingPlanHtml.TryParseDecide("kor://plan/decide/12", out var id) && id == 12);
        Assert.False(OperatingPlanHtml.TryParseDecide("kor://plan/decide/x", out _));
        Assert.Equal("KOR-Operating-Plan-2026-10-19.pdf", OperatingPlanHtml.PdfFileName(Now));

        var dir = Path.Combine(Path.GetTempPath(), "kor-bd-screens");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "director-plan.html"), html);
    }

    [Fact]
    public void The_composer_records_the_plans_coo_readings_and_leaves_out_what_the_engine_did_not_supply()
    {
        var months = Enumerable.Range(1, 12).Select(i => new Kor.Operations.Financials.ImpactMonth(202500 + i, i <= 6 ? 490_000 : 367_000, 0, 0, i == 12 ? 174 : 180)).ToList();
        static Kor.Operations.Financials.ExecutiveSummaryResult Result(params Kor.Operations.Financials.ExecutiveKpi[] kpis) =>
            new(Now, kpis.ToList(), new List<Kor.Operations.Financials.ExecutiveTrend>(), new List<Kor.Operations.Financials.ExecutiveAlert>());
        var readings = MorningImpact.PlanReadings(Result(), 814, months, 94);
        Assert.Equal(814, readings["ActiveProjects"]);
        Assert.Equal(94, readings["BillingLagProjects"]);
        Assert.Equal(174, readings["Dso"]);
        Assert.Equal(367_000, readings["InvoicedMonthAvg6"]);
        Assert.Equal(490_000, readings["InvoicedMonthAvgPrior6"]);
        Assert.False(readings.ContainsKey("Utilization")); // no Utilization KPI in the result: not recorded, never zero
        Assert.Equal(70.8, MorningImpact.PlanReadings(Result(new Kor.Operations.Financials.ExecutiveKpi("Utilization", "70.8%", "", "")), 814, months, 94)["Utilization"]);
        Assert.False(MorningImpact.PlanReadings(Result(), 814, months.Take(5).ToList(), null).ContainsKey("InvoicedMonthAvg6"));
        Assert.False(readings.ContainsKey("OverBudgetHours")); // no over-budget KPI: not recorded

        // Hours past budget: the KPI's own rows, positive overruns only, no rate applied.
        static Kor.Operations.Financials.KpiProjectDrilldownRow Over(double hours) => new("30001-01", "P", "PM", hours, 1.5, 0.9, 0.9, false);
        var withOver = MorningImpact.PlanReadings(Result(new Kor.Operations.Financials.ExecutiveKpi("Projects Over Budget", "3", "", "",
            ProjectDrilldownRows: new[] { Over(1466.35), Over(1420.77), Over(-5) })), 814, months, 94);
        Assert.Equal(2887.12, withOver["OverBudgetHours"], 2);
        Assert.Equal("2,887 hours", PlanMetrics.Format(PlanUnit.Hours, withOver["OverBudgetHours"]));
    }
}
