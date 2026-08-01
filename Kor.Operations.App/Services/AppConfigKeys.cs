#nullable enable
namespace Kor.Operations.Services
{
    internal static class AppConfigKeys
    {
        public const string DefaultFromDomain = "DefaultFromDomain";
        public const string DefaultFromEmail = "DefaultFromEmail";
        public const string DeltekOdbcDsn = "DeltekOdbcDsn";
        public const string BrochureSharedProposalsRootPath = "Brochure.SharedProposalsRootPath";
        public const string PursuitFilesRoot = "BD.PursuitFilesRoot";
        public const string CompensationPoolRate = "Compensation.PoolRate";
        public const string FinancialsPnLGlFlipSign = "Financials.PnL.GlFlipSign";
        public const string FinancialsPnLIncomeGroupTypes = "Financials.PnL.IncomeGroupTypes";
        public const string FinancialsPnLExpenseGroupTypes = "Financials.PnL.ExpenseGroupTypes";
        public const string FinancialsPnLGlTableNameLike = "Financials.PnL.GlTableNameLike";
        public const string FinancialsBilledRevenueAccounts = "Financials.Billed.RevenueAccounts";
        public const string FinancialsBilledExpenseAccountRanges = "Financials.Billed.ExpenseAccountRanges";
        public const string FinancialsBilledExpenseAccountExcludes = "Financials.Billed.ExpenseAccountExcludes";
        public const string FinancialsBilledExpenseAccountIncludes = "Financials.Billed.ExpenseAccountIncludes";
        public const string FinancialsBilledOtherIncomeAccountRanges = "Financials.Billed.OtherIncomeAccountRanges";
        public const string FinancialsBilledOtherIncomeAccountExcludes = "Financials.Billed.OtherIncomeAccountExcludes";
        public const string FinancialsBilledOtherIncomeAccountIncludes = "Financials.Billed.OtherIncomeAccountIncludes";
        public const string FinancialsBilledUsdToCadRate = "Financials.Billed.UsdToCadRate";
        public const string FinancialsBilledUsdToCadRateByYear = "Financials.Billed.UsdToCadRateByYear";
        public const string FinancialsBilledDefaultOrg = "Financials.Billed.DefaultOrg";
        public const string FinancialsCashAccountWhitelist = "Financials.Cash.AccountWhitelist";
        public const string FinancialsCashUsdToCadRate = "Financials.Cash.UsdToCadRate";
        public const string FinancialsCashUsdAccounts = "Financials.Cash.UsdAccounts";
        public const string GraphClientId = "Graph.ClientId";
        public const string GraphDriveId = "Graph.DriveId";
        public const string GraphTenantId = "Graph.TenantId";
        public const string ProjectsRoot = "ProjectsRoot";
        public const string RedirectorBaseUrl = "RedirectorBaseUrl";
        public const string StandardDetailsFileStorageRootPath = "StandardDetails.FileStorageRootPath";
        public const string UserUpnOverride = "UserUpnOverride";
        public const string VpCatalog       = "Vp.Catalog";
        public const string VpDraftRate     = "Vp.DraftRate";
        public const string VpDsn           = "Vp.Dsn";
        public const string VpEngRate       = "Vp.EngRate";
        public const string VpPartnerImputedCostRate = "Vp.PartnerImputedCostRate";
        public const string VpPassword      = "Vp.Password";
        public const string VpPrLaborIdDraft = "Vp.PrLaborIdDraft";
        public const string VpPrLaborIdEng  = "Vp.PrLaborIdEng";
        public const string VpTargetBillingRate = "Vp.TargetBillingRate";
        public const string VpUser          = "Vp.User";
        public const string WatchlistSyncServiceUrl = "WatchlistSync.ServiceUrl";
        public const string WatchlistSyncUsername   = "WatchlistSync.Username";
        public const string WatchlistSyncPassword   = "WatchlistSync.Password";
        public const string McpServerServiceUrl = "McpServer.ServiceUrl";
        public const string McpServerUsername   = "McpServer.Username";
        public const string McpServerPassword   = "McpServer.Password";

        internal static class ConnectionStrings
        {
            public const string KorEmailIndex = "KorEmailIndex";
            public const string KorOpportunitiesDb = "KorOpportunitiesDb";
            public const string KorTransmittals = "KorTransmittals";
            public const string KorTransmittalsDb = "KorTransmittalsDb";
        }
    }
}
