#nullable enable
using System;
using System.Collections.Generic;
using Kor.Operations.Financials;
using Xunit;

namespace Kor.Operations.Tests.Financials;

public sealed class InCollectionsPhaseBalanceTests
{
    private static readonly DateTime AsOf = new(2026, 10, 17);

    [Fact]
    public void InCaseInvoice_SumsAllPhases_AndAgesEachPhase()
    {
        var rows = new (string Wbs1, string Invoice, double Balance, string Bucket, DateTime? InvoiceDate, DateTime? DueDate)[]
        {
            ("31103-01", "00038658", 84_000.0, "CAD", AsOf.AddDays(-120), null),
            ("31103-01", "00038658", 77_962.50, "CAD", AsOf.AddDays(-120), AsOf.AddDays(-30)),
            ("31103-01", "not-in-case", 500_000.0, "CAD", AsOf.AddDays(-180), null),
            ("other-project", "00038658", 900_000.0, "CAD", AsOf.AddDays(-180), null),
        };
        var cases = new HashSet<(string Wbs1, string Invoice)> { ("31103-01", "00038658") };

        var result = FinancialsService.AccumulateInCollectionsByWbs1(rows, AsOf, 1.36, cases);

        var project = Assert.Single(result);
        Assert.Equal("31103-01", project.Key);
        Assert.Equal(161_962.50, project.Value.Total);
        Assert.Equal(84_000.0, project.Value.Aged90Plus);
    }

    [Fact]
    public void UsaBucket_ConvertsEveryPhaseAndAgedContribution()
    {
        var rows = new (string Wbs1, string Invoice, double Balance, string Bucket, DateTime? InvoiceDate, DateTime? DueDate)[]
        {
            ("USA-project", "invoice", 84_000.0, "usa", AsOf.AddDays(-91), null),
            ("USA-project", "invoice", 77_962.50, "USA", AsOf.AddDays(-90), null),
        };
        var cases = new HashSet<(string Wbs1, string Invoice)> { ("USA-project", "invoice") };

        var result = FinancialsService.AccumulateInCollectionsByWbs1(rows, AsOf, 1.36, cases);

        var project = Assert.Single(result).Value;
        Assert.Equal(220_269.0, project.Total, precision: 6);
        Assert.Equal(114_240.0, project.Aged90Plus, precision: 6);
    }

    [Fact]
    public void PhaseCredits_ReduceTotalAndAgedBalance_AndUndatedRowsAreNotAged()
    {
        var rows = new (string Wbs1, string Invoice, double Balance, string Bucket, DateTime? InvoiceDate, DateTime? DueDate)[]
        {
            ("project", "invoice", 1_000.0, "CAD", AsOf.AddDays(-120), null),
            ("project", "invoice", -250.0, "CAD", AsOf.AddDays(-120), null),
            ("project", "invoice", 50.0, "CAD", null, null),
        };
        var cases = new HashSet<(string Wbs1, string Invoice)> { ("project", "invoice") };

        var result = FinancialsService.AccumulateInCollectionsByWbs1(rows, AsOf, 1.36, cases);

        var project = Assert.Single(result).Value;
        Assert.Equal(800.0, project.Total);
        Assert.Equal(750.0, project.Aged90Plus);
        Assert.Empty(FinancialsService.AccumulateInCollectionsByWbs1(
            rows, AsOf, 1.36, new HashSet<(string Wbs1, string Invoice)>()));
    }
}
