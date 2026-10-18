#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.Financials;
using Kor.Operations.Financials.Loaders;
using Xunit;

namespace Kor.Operations.Tests.Financials;

/// <summary>
/// The AR trend plots the running balance, not PRSummaryMain's per-period change. Covers the accumulation and that
/// period "0" (the opening balance) counts but never prints. Does not cover FX between buckets or the live scope.
/// </summary>
public sealed class ArRunningBalanceTests
{
    [Fact]
    public void Accumulates_InPeriodOrder_IncludingTheOpeningPeriod()
    {
        var balance = RevenueLoader.RunningArBalance(new Dictionary<string, double>
        {
            ["202509"] = -300, ["0"] = 1_000, ["202510"] = 50, ["202508"] = 200,
        });

        Assert.Equal(1_000, balance["0"]);
        Assert.Equal(1_200, balance["202508"]);
        Assert.Equal(900, balance["202509"]);
        Assert.Equal(950, balance["202510"]);
    }

    [Fact]
    public void Series_CarriesBalances_AndNeverPrintsPeriodZero()
    {
        var pr = new Dictionary<string, PrAgg>(StringComparer.OrdinalIgnoreCase)
        {
            ["0"] = new("0", 0, 0, 1_000, 0, 0, 0),
            ["202508"] = new("202508", 10, 10, 200, 0, 0, 0),
            ["202509"] = new("202509", 10, 10, -300, 0, 0, 0),
            ["202510"] = new("202510", 10, 10, 50, 0, 0, 0),
        };

        var series = RevenueLoader.BuildSeries(pr, new Dictionary<string, CalRow>(), points: 2);

        Assert.Equal(new[] { "202509", "202510" }, series.Periods.Select(p => p.Period));
        Assert.Equal(new[] { 900d, 950d }, series.Periods.Select(p => p.Ar));
    }
}
