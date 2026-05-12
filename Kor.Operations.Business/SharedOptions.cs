#nullable enable
#pragma warning disable SA1649
namespace Kor.Operations.App.Options;

public sealed class DeltekOdbcOptions
{
    public string Dsn { get; init; } = "";
    public string OdbcDsn { get; init; } = "";
    public string User { get; init; } = "";
    public string Password { get; init; } = "";
    public string Catalog { get; init; } = "";
    public string PrLaborIdEng { get; init; } = "ENG";
    public string PrLaborIdDraft { get; init; } = "DRAFT";
    public double EngRate { get; set; } = 474;             // produces 58% eng share (calibrated Apr 2026)
    public double DraftRate { get; set; } = 655;           // produces 42% draft share (calibrated Apr 2026)
    public double TargetBillingRate { get; set; } = 185;   // portfolio median $/hr (calibrated Apr 2026)
    /// <summary>
    /// Imputed hourly cost rate applied to Partners (EmployeeId starting with 'P')
    /// in margin / profitability calculations.
    ///
    /// Why this exists: Partners are paid via distributions rather than hours, so
    /// Deltek EMCompany.ProvCostRate is $0 for them. Without imputation, any project with
    /// Partner hours would show artificially inflated margin (free labor).
    ///
    /// Why $250/hr: chosen as mid-range Canadian structural Partner opportunity
    /// cost  a sensible approximation of what a Partner hour displaces at senior-
    /// engineer external-billing equivalent. Not the Partner external rate (e.g.,
    /// Markulin's $1,015/hr), which would flag nearly every project as loss-leader.
    ///
    /// Adjustable firm-wide via this option; per-Partner overrides and a settings
    /// UI are planned (Prompt 17c).
    /// </summary>
    public double PartnerImputedCostRate { get; set; } = 250.0;
    public bool UseTargetRateBudget { get; set; }          // false = peer-based (default), true = target-rate formula
}

public sealed class FinancialsOptions
{
    public string PnLGlFlipSign { get; init; } = "";
    public string FiscalYearStartMonth { get; init; } = "";
    public string PnLEngRate { get; init; } = "";
    public string PnLDraftRate { get; init; } = "";
    public string PnLOtherDirectRate { get; init; } = "";
    public string PnLOverheadRate { get; init; } = "";
    public string PnLIncomeGroupTypes { get; init; } = "";
    public string PnLExpenseGroupTypes { get; init; } = "";
    public string PnLGlTableNameLike { get; init; } = "";
    public string BilledRevenueAccounts { get; init; } = "";
    public string BilledExpenseAccountRanges { get; init; } = "";
    public string BilledOtherIncomeAccountRanges { get; init; } = "";

    /// <summary>
    /// Comma-separated 4-char account prefixes inside the expense ranges that
    /// should NOT be counted as operating expenses. KOR defaults: 7290
    /// (Deltek suspense — pre-posting accrual bucket that never reaches GL,
    /// verified Feb 2026 via SSMS: $111K in sub-ledger, $0 in GLSummary) and
    /// 7970 (Realized / Unrealized Gain &amp; Loss — non-operating; the accountant
    /// reports this under Other Income / Other Charges).
    /// </summary>
    public string BilledExpenseAccountExcludes { get; init; } = "";

    /// <summary>
    /// Comma-separated 4-char account prefixes OUTSIDE the expense ranges
    /// that should be ADDED to operating expenses (KOR's accountant
    /// categorizes these as operating expense even though they sit in the
    /// 8xxx "other" range). Defaults: 8200 (Goodwill / Settlement) and 8300
    /// (Bank Charges).
    /// </summary>
    public string BilledExpenseAccountIncludes { get; init; } = "";

    /// <summary>
    /// Comma-separated 4-char account prefixes inside the other-income range
    /// that should NOT be counted as other income (mirror of the expense
    /// include list — these get reclassified UP to operating expenses).
    /// KOR defaults: 8200, 8300.
    /// </summary>
    public string BilledOtherIncomeAccountExcludes { get; init; } = "";

    /// <summary>
    /// Comma-separated 4-char account prefixes OUTSIDE the other-income range
    /// that should be ADDED to other income (mirror of the expense exclude
    /// list — these get reclassified DOWN to other income).
    /// KOR default: 7970 (Realized / Unrealized G&amp;L).
    /// </summary>
    public string BilledOtherIncomeAccountIncludes { get; init; } = "";
    public string BilledUsdToCadRate { get; init; } = "";
    public string BilledDefaultOrg { get; init; } = "";
    public string CashAccountWhitelist { get; init; } = "";
    public string CashUsdToCadRate { get; init; } = "";
    /// <summary>
    /// Comma-separated GL account numbers that should be classified as USD regardless of
    /// CFGBanks.Org. Lets the cash tile distinguish a USD-denominated bank account that
    /// lives inside a CAD entity (e.g., 1120 Scotiabank Partnership USD CHQ).
    /// Falls back to Org-based bucketing when this list is empty.
    /// </summary>
    public string CashUsdAccounts { get; init; } = "";
}
