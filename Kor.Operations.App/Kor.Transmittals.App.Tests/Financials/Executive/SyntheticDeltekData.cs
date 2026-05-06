#nullable enable
using AppFin = Kor.Operations.Financials;
using AppLoaders = Kor.Operations.Financials.Loaders;

namespace Kor.Operations.Tests.Financials.Executive;

internal static class SyntheticDeltekData
{
    public static AppFin.ExecutiveSummaryDeltekData Default(
        IReadOnlyList<AppFin.ArProjectOutstandingRow>? arProjectRows = null,
        IReadOnlyList<AppFin.ArInvoiceOutstandingRow>? arInvoiceRows = null,
        IReadOnlyList<AppLoaders.CashAccountBalanceRow>? cashAccountRows = null,
        double cashCad = 100_000,
        double cashUsa = 50_000,
        double cashBcc = 0,
        double cashFx = 1.36,
        double arFx = 1.36,
        double? arFirmwide = null,
        double? arOver60 = null,
        double arFirmwideCad = 0,
        double arFirmwideUsa = 0,
        double revenue30 = 10_000,
        double billed30 = 8_000,
        double revenue90 = 30_000,
        double billed90 = 24_000,
        double arScopedOutstanding = 0,
        bool wipDataLoaded = true,
        bool revenueGenerationDetected = true)
    {
        arProjectRows ??= Array.Empty<AppFin.ArProjectOutstandingRow>();
        arInvoiceRows ??= Array.Empty<AppFin.ArInvoiceOutstandingRow>();

        var calculatedArFirmwide = arFirmwide ?? arProjectRows.Sum(r => r.Total);
        var calculatedArOver60 = arOver60 ?? arProjectRows.Sum(r => r.Aged61To90 + r.Aged90Plus);
        var combinedCash = cashCad + (cashUsa * cashFx) + cashBcc;

        cashAccountRows ??= BuildCashRows(cashCad, cashUsa, cashBcc);

        return new AppFin.ExecutiveSummaryDeltekData(
            CashTotal: cashCad + cashUsa + cashBcc,
            CashCombinedCadEquivalent: combinedCash,
            CashCad: cashCad,
            CashUsa: cashUsa,
            CashBcc: cashBcc,
            CashUsdToCadRate: cashFx,
            CashPeriod: "202604",
            CashHistory: new[]
            {
                new AppFin.CashHistoryPoint("202604", cashCad, cashUsa, cashBcc)
            },
            CashPerAccount: cashAccountRows,
            UtilizationPct30: 0.75,
            UtilizationBillableHours30: 75,
            UtilizationTotalHours30: 100,
            UtilizationProjectRows: new[]
            {
                new AppFin.UtilizationProjectRow("P001", 30, 40),
                new AppFin.UtilizationProjectRow("P002", 45, 60)
            },
            ArOutstanding: calculatedArFirmwide,
            ArScopedOutstanding: arScopedOutstanding,
            ArOver60: calculatedArOver60,
            ArFirmwideOutstanding: calculatedArFirmwide,
            ArFirmwideOver60: calculatedArOver60,
            ArFirmwideOutstandingCad: arFirmwideCad,
            ArFirmwideOutstandingUsa: arFirmwideUsa,
            ArFirmwideUsdToCadRate: arFx,
            ArProjectRows: arProjectRows,
            ArInvoiceRows: arInvoiceRows,
            WipUnbilled: 1_000,
            WipOverbilled: 0,
            WipUnbilledNet: 1_000,
            WipUnbilledPeriod: "202604",
            WipProjectRows: Array.Empty<AppFin.WipProjectBreakdownRow>(),
            FirmWipUnbilled: 1_000,
            FirmWipOverbilled: 0,
            FirmWipNet: 1_000,
            Revenue30: revenue30,
            Revenue90: revenue90,
            Billed30: billed30,
            Billed90: billed90,
            RevenuePayerRows: new[] { new AppFin.TrendPayerAmountRow("P001", "Client A", revenue90) },
            BilledPayerRows: new[] { new AppFin.TrendPayerAmountRow("P001", "Client A", billed90) },
            ArPayerRows: new[] { new AppFin.TrendPayerAmountRow("P001", "Client A", arScopedOutstanding) },
            RevenueSeries: new[] { revenue30, revenue90 },
            BilledSeries: new[] { billed30, billed90 },
            ArSeries: new[] { calculatedArFirmwide },
            RevenueGenerationDetected: revenueGenerationDetected,
            WipDataLoaded: wipDataLoaded);
    }

    private static IReadOnlyList<AppLoaders.CashAccountBalanceRow> BuildCashRows(double cad, double usa, double bcc)
    {
        var rows = new List<AppLoaders.CashAccountBalanceRow>
        {
            new("CAD", "1110.00", "CAD", "CAD", cad),
            new("USA", "1170.00", "USA", "USA", usa)
        };

        if (Math.Abs(bcc) > 0.004)
            rows.Add(new AppLoaders.CashAccountBalanceRow("BCC", "1190.00", "BCC", "BCC", bcc));

        return rows;
    }
}
