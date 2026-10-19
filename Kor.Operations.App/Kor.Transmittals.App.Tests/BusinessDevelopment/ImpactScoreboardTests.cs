#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.MorningBrief;
using Kor.Operations.Financials;
using Kor.Opportunities.Data.Crm;
using Xunit;

namespace Kor.Operations.App.Tests.BusinessDevelopment;

/// <summary>
/// The impact scoreboard's arithmetic: month conversion and walk-back of AR from today, DSO's twelve-month rule, the open month
/// left out, the engine-reading formulas, and the baseline-to-now lines. Does NOT read Deltek or the store: a wrong account
/// code or transaction type in ImpactHistory.Sql, or a CRM stage that changes meaning, would pass every test here. The live
/// cross-check on 18 Oct 2026 was September invoiced $289,348 from LedgerAR = the engine's PRSummaryMain figure.
/// </summary>
public sealed class ImpactScoreboardTests
{
    private static IEnumerable<ImpactLedgerRow> Year(int fromPeriod, int months, double invoiced, double collected, double change)
    {
        var d = new DateTime(fromPeriod / 100, fromPeriod % 100, 1);
        for (var i = 0; i < months; i++, d = d.AddMonths(1))
            yield return new ImpactLedgerRow(d.Year * 100 + d.Month, "CAD", invoiced, collected, change);
    }

    [Fact]
    public void Ar_walks_back_from_today_through_each_later_months_change_and_the_open_month_is_left_out()
    {
        var rows = Year(202510, 13, 100_000, 90_000, 10_000).ToList(); // 202510..202610, AR rising 10k a month

        var months = ImpactHistory.Build(rows, 1.36, arNowCadEquiv: 1_000_000, lastClosedPeriod: 202609);

        Assert.Equal(12, months.Count);
        Assert.Equal(202609, months[^1].Period);
        Assert.Equal(990_000, months[^1].ArEnd, 6);   // today's AR less October's change
        Assert.Equal(880_000, months[0].ArEnd, 6);    // eleven more months back
    }

    [Fact]
    public void Usd_converts_and_dso_waits_for_a_year_of_invoicing()
    {
        var rows = Year(202510, 12, 100_000, 0, 0).Concat(new[] { new ImpactLedgerRow(202609, "USA", 10_000, 5_000, 0) }).ToList();

        var months = ImpactHistory.Build(rows, 1.36, 1_200_000, 202609);

        Assert.Equal(113_600, months[^1].Invoiced, 6);
        Assert.Equal(6_800, months[^1].Collected, 6);
        Assert.All(months.Take(11), m => Assert.Equal(0, m.Dso));
        Assert.Equal(1_200_000 / 1_213_600.0 * 365.0, months[^1].Dso, 6);
    }

    [Fact]
    public void Last_closed_period_and_period_arithmetic()
    {
        Assert.Equal(202609, ImpactHistory.LastClosedPeriod(new DateTime(2026, 10, 18)));
        Assert.Equal(202512, ImpactHistory.LastClosedPeriod(new DateTime(2026, 1, 5)));
        Assert.Equal(202410, ImpactHistory.PeriodMonthsBefore(202609, 23));
    }

    [Fact]
    public void Baseline_to_now_lines_say_which_way_is_better()
    {
        var readings = new[]
        {
            new ImpactReading(new DateOnly(2026, 10, 18), "ArOver60", 1_562_177),
            new ImpactReading(new DateOnly(2026, 10, 18), "CrmWonCount", 305),
            new ImpactReading(new DateOnly(2026, 11, 18), "ArOver60", 1_200_000),
            new ImpactReading(new DateOnly(2026, 11, 18), "CrmWonCount", 309),
        };

        var fact = MorningImpact.Fact(Array.Empty<ImpactMonth>(), readings);

        Assert.Contains("AR over 60 days: baseline $1,562,177, now $1,200,000 (−$362,177, better).", fact.Lines);
        Assert.Contains("Pursuits won (CRM running total): baseline 305, now 309 (+4, better).", fact.Lines);
        Assert.StartsWith("Readings from 2026-10-18 (baseline) to 2026-11-18, 2 days recorded.", fact.Lines[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_full_year_prices_thirty_days_of_dso()
    {
        var months = Year(202510, 12, 0, 0, 0).Select(r => new ImpactMonth(r.Period, 365_000, 300_000, 2_000_000, 166)).ToList();

        var fact = MorningImpact.Fact(months, Array.Empty<ImpactReading>());

        Assert.Contains(fact.Lines, l => l.StartsWith("Each 30 days off DSO frees about $360,000 of cash", StringComparison.Ordinal));
        Assert.Contains(fact.Lines, l => l.Contains("GST included", StringComparison.Ordinal));
    }
}
