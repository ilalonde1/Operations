#nullable enable
#pragma warning disable SA1649
namespace Kor.Operations.App.Options;

public sealed class GraphOptions
{
    public string TenantId { get; init; } = "";
    public string ClientId { get; init; } = "";
    public string DriveId { get; init; } = "";
    public string RedirectorBaseUrl { get; init; } = "";
}

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

public sealed class DatabaseOptions
{
    public string KorTransmittalsDb { get; init; } = "";
    public string KorTransmittals { get; init; } = "";
}

public sealed class StorageOptions
{
    public string ProjectsRoot { get; init; } = "";
    public string StandardDetailsFileStorageRootPath { get; init; } = "";
    public string BrochureSharedProposalsRootPath { get; init; } = "";
}

public sealed class UserOptions
{
    public string UserUpnOverride { get; init; } = "";
    public string DefaultFromEmail { get; init; } = "";
    public string DefaultFromDomain { get; init; } = "";
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
    public string BilledUsdToCadRate { get; init; } = "";
    public string BilledDefaultOrg { get; init; } = "";
}

public sealed class CompensationOptions
{
    public double PoolRate { get; init; } = 0.10;
}

public sealed class OpportunitiesOptions
{
    /// <summary>SQL Server connection string for KorOpportunitiesDb.</summary>
    public string OpportunitiesDb { get; init; } = "";
}

public sealed class WatchlistSyncOptions
{
    /// <summary>Base URL of the deltek-webhook service, e.g. https://deltek-webhook.korstructural.com</summary>
    public string ServiceUrl { get; init; } = "";
    /// <summary>Basic-auth username (AppApi credential on the service).</summary>
    public string Username { get; init; } = "";
    /// <summary>Basic-auth password (AppApi credential on the service).</summary>
    public string Password { get; init; } = "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ServiceUrl)
                             && !string.IsNullOrWhiteSpace(Username)
                             && !string.IsNullOrWhiteSpace(Password);
}
