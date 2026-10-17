#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials;

public sealed class ArInvoicePhaseGroupingTests
{
    private static readonly DateTime AsOf = new(2026, 10, 17);

    [Fact]
    public void MultiPhaseInvoice_ReturnsOneSum_WithEarliestDatesAndFirstIdentity()
    {
        var first = Row("31103-01", "00038658", 84_000.0) with
        {
            InvoiceDate = AsOf.AddDays(-100),
            DueDate = AsOf.AddDays(-30),
        };
        var second = Row("31103-01", "00038658", 77_962.50) with
        {
            ProjectName = "later project label",
            Pm = "later PM",
            ClientId = "later client ID",
            ClientName = "later client label",
            InvoiceDate = AsOf.AddDays(-120),
            DueDate = AsOf.AddDays(-60),
        };

        var result = ArFinancialsService.AggregateInvoicePhaseRows(
            new[] { (first, "CAD"), (second, "CAD") }, AsOf, 1.36);

        Assert.Equal(first with
        {
            InvoiceDate = AsOf.AddDays(-120),
            DueDate = AsOf.AddDays(-60),
            DaysPastDue = 60,
            Balance = 161_962.50,
        }, Assert.Single(result));
    }

    [Fact]
    public void UsaPhase_IsConvertedBeforeSumming()
    {
        // Mixed buckets deliberately distinguish per-row FX from applying the
        // first row's rate to an already-grouped balance.
        var rows = new[]
        {
            (Row("project", "invoice", 100.0), "CAD"),
            (Row("project", "invoice", 200.0), "usa"),
        };

        var result = ArFinancialsService.AggregateInvoicePhaseRows(rows, AsOf, 1.36);

        Assert.Equal(372.0, Assert.Single(result).Balance, precision: 6);
    }

    [Fact]
    public void Grouping_PreservesTotalAndCollectionsSubset_AndUsesBothKeys()
    {
        var rows = new[]
        {
            Row("31103-01", "00038658", 84_000.0),
            Row("31103-01", "00038658", 77_962.50),
            Row("31103-01", "other-invoice", 1_000.0),
            Row("other-project", "00038658", -50.0),
        };
        var cases = new HashSet<(string Wbs1, string Invoice)> { ("31103-01", "00038658") };

        var result = ArFinancialsService.AggregateInvoicePhaseRows(
            rows.Select(row => (row, "CAD")), AsOf, 1.36);

        Assert.Equal(3, result.Count);
        Assert.Equal(rows.Sum(row => row.Balance), result.Sum(row => row.Balance));
        Assert.Equal(
            rows.Where(row => cases.Contains((row.Wbs1, row.Invoice))).Sum(row => row.Balance),
            result.Where(row => cases.Contains((row.Wbs1, row.Invoice))).Sum(row => row.Balance));
        Assert.Contains(result, row => row.Wbs1 == "31103-01" && row.Invoice == "other-invoice" && row.Balance == 1_000.0);
        Assert.Contains(result, row => row.Wbs1 == "other-project" && row.Invoice == "00038658" && row.Balance == -50.0);
    }

    [Fact]
    public void Dates_IgnoreNulls_FallBackToInvoiceDate_AndClampFutureAge()
    {
        var undated = Row("project", "invoice", 10.0);
        var dated = undated with { InvoiceDate = AsOf.AddDays(-120) };

        var invoiceOnly = ArFinancialsService.AggregateInvoicePhaseRows(
            new[] { (undated, "CAD"), (dated, "CAD") }, AsOf, 1.0);
        Assert.Equal(AsOf.AddDays(-120), Assert.Single(invoiceOnly).InvoiceDate);
        Assert.Null(invoiceOnly[0].DueDate);
        Assert.Equal(120, invoiceOnly[0].DaysPastDue);

        var futureDue = dated with { DueDate = AsOf.AddDays(10) };
        var dueDated = ArFinancialsService.AggregateInvoicePhaseRows(
            new[] { (undated, "CAD"), (futureDue, "CAD") }, AsOf, 1.0);
        Assert.Equal(AsOf.AddDays(10), Assert.Single(dueDated).DueDate);
        Assert.Equal(0, dueDated[0].DaysPastDue);

        var noDates = ArFinancialsService.AggregateInvoicePhaseRows(new[] { (undated, "CAD") }, AsOf, 1.0);
        Assert.Equal(0, Assert.Single(noDates).DaysPastDue);
        Assert.Empty(ArFinancialsService.AggregateInvoicePhaseRows(
            Array.Empty<(ArInvoiceOutstandingRow Row, string Bucket)>(), AsOf, 1.36));
    }

    private static ArInvoiceOutstandingRow Row(string wbs1, string invoice, double balance)
        => new(wbs1, "first project label", "first PM", invoice, "first client ID", "first client label",
            InvoiceDate: null, DueDate: null, DaysPastDue: 999, Balance: balance);
}
