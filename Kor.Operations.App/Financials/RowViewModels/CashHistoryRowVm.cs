#nullable enable
using System;
using System.Globalization;

namespace Kor.Operations.Financials;

public sealed class CashHistoryRowVm
{
    public string PeriodText { get; }
    public string TotalText { get; }
    public string CadText { get; }
    public string UsaText { get; }
    public string BccText { get; }

    public CashHistoryRowVm(string period, double total, double cad, double usa, double bcc)
    {
        PeriodText = FormatPeriod(period);
        TotalText = total.ToString("C0", CultureInfo.CurrentCulture);
        CadText = cad.ToString("C0", CultureInfo.CurrentCulture);
        UsaText = usa.ToString("C0", CultureInfo.CurrentCulture);
        BccText = bcc.ToString("C0", CultureInfo.CurrentCulture);
    }

    private static string FormatPeriod(string period)
    {
        if (string.IsNullOrWhiteSpace(period)) return string.Empty;
        var p = period.Trim();
        if (p.Length == 6 &&
            int.TryParse(p.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) &&
            int.TryParse(p.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
            m >= 1 && m <= 12)
        {
            return new DateTime(y, m, 1).ToString("MMM yyyy", CultureInfo.CurrentCulture);
        }
        return p;
    }
}

public sealed class CashAccountRowVm
{
    public string Company { get; }
    public string Account { get; }
    public string Org { get; }
    // Resolved currency bucket the headline tile counts this account as. Differs from Org
    // when Financials.Cash.UsdAccounts overrides a USD-denominated account inside a CAD entity.
    public string Currency { get; }
    public double Balance { get; }
    public string BalanceText { get; }

    public CashAccountRowVm(string company, string account, string org, string currency, double balance)
    {
        Company = company ?? string.Empty;
        Account = account ?? string.Empty;
        Org = org ?? string.Empty;
        Currency = currency ?? string.Empty;
        Balance = balance;
        BalanceText = balance.ToString("C0", CultureInfo.CurrentCulture);
    }
}
