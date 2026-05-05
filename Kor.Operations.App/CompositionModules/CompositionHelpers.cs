#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using Kor.Operations.App.Options;
using Kor.Operations.Logging;
using Kor.Operations.Services;
using Serilog;
using Serilog.Events;

namespace Kor.Operations;

internal static class CompositionHelpers
{
    private static GraphOptions? _graphOptions;
    private static DeltekOdbcOptions? _deltekOdbcOptions;
    private static DatabaseOptions? _databaseOptions;
    private static StorageOptions? _storageOptions;
    private static UserOptions? _userOptions;
    private static FinancialsOptions? _financialsOptions;
    private static CompensationOptions? _compensationOptions;
    private static OpportunitiesOptions? _opportunitiesOptions;
    private static WatchlistSyncOptions? _watchlistSyncOptions;
    private static Serilog.Core.Logger? _serilogLogger;

    internal static GraphOptions GetGraphOptions() => _graphOptions ??= new GraphOptions
    {
        TenantId = GetRequiredAppSetting(AppConfigKeys.GraphTenantId),
        ClientId = GetRequiredAppSetting(AppConfigKeys.GraphClientId),
        DriveId = GetRequiredAppSetting(AppConfigKeys.GraphDriveId),
        RedirectorBaseUrl = GetRequiredAppSetting(AppConfigKeys.RedirectorBaseUrl)
    };

    internal static DeltekOdbcOptions GetDeltekOdbcOptions() => _deltekOdbcOptions ??= new DeltekOdbcOptions
    {
        Dsn           = ConfigurationManager.AppSettings[AppConfigKeys.VpDsn]           ?? "",
        OdbcDsn       = ConfigurationManager.AppSettings[AppConfigKeys.DeltekOdbcDsn]   ?? "",
        User          = ConfigurationManager.AppSettings[AppConfigKeys.VpUser]          ?? "",
        Password      = ConfigurationManager.AppSettings[AppConfigKeys.VpPassword]      ?? "",
        Catalog       = ConfigurationManager.AppSettings[AppConfigKeys.VpCatalog]       ?? "",
        PrLaborIdEng  = ConfigurationManager.AppSettings[AppConfigKeys.VpPrLaborIdEng]  ?? "ENG",
        PrLaborIdDraft = ConfigurationManager.AppSettings[AppConfigKeys.VpPrLaborIdDraft] ?? "DRAFT",
        EngRate       = double.TryParse(ConfigurationManager.AppSettings[AppConfigKeys.VpEngRate],   System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var er)  ? er  : 474,
        DraftRate     = double.TryParse(ConfigurationManager.AppSettings[AppConfigKeys.VpDraftRate], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var dr)  ? dr  : 655,
        TargetBillingRate = double.TryParse(ConfigurationManager.AppSettings[AppConfigKeys.VpTargetBillingRate], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var tbr) ? tbr : 185,
        PartnerImputedCostRate = double.TryParse(ConfigurationManager.AppSettings[AppConfigKeys.VpPartnerImputedCostRate], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var picr) ? picr : 250,
    };

    internal static DatabaseOptions GetDatabaseOptions() => _databaseOptions ??= new DatabaseOptions
    {
        KorTransmittalsDb = GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorTransmittalsDb),
        KorTransmittals = ConfigurationManager.ConnectionStrings[AppConfigKeys.ConnectionStrings.KorTransmittals]?.ConnectionString ?? ""
    };

    internal static StorageOptions GetStorageOptions() => _storageOptions ??= new StorageOptions
    {
        ProjectsRoot = GetRequiredAppSetting(AppConfigKeys.ProjectsRoot),
        StandardDetailsFileStorageRootPath = ConfigurationManager.AppSettings[AppConfigKeys.StandardDetailsFileStorageRootPath] ?? "",
        BrochureSharedProposalsRootPath = ConfigurationManager.AppSettings[AppConfigKeys.BrochureSharedProposalsRootPath] ?? ""
    };

    internal static UserOptions GetUserOptions() => _userOptions ??= new UserOptions
    {
        UserUpnOverride = ConfigurationManager.AppSettings[AppConfigKeys.UserUpnOverride] ?? "",
        DefaultFromEmail = ConfigurationManager.AppSettings[AppConfigKeys.DefaultFromEmail] ?? "",
        DefaultFromDomain = ConfigurationManager.AppSettings[AppConfigKeys.DefaultFromDomain] ?? ""
    };

    internal static FinancialsOptions GetFinancialsOptions() => _financialsOptions ??= new FinancialsOptions
    {
        PnLGlFlipSign = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsPnLGlFlipSign] ?? "",
        FiscalYearStartMonth = ConfigurationManager.AppSettings["Financials.PnL.FiscalYearStartMonth"] ?? "",
        PnLEngRate = ConfigurationManager.AppSettings["Financials.PnL.EngRate"] ?? "",
        PnLDraftRate = ConfigurationManager.AppSettings["Financials.PnL.DraftRate"] ?? "",
        PnLOtherDirectRate = ConfigurationManager.AppSettings["Financials.PnL.OtherDirectRate"] ?? "",
        PnLOverheadRate = ConfigurationManager.AppSettings["Financials.PnL.OverheadRate"] ?? "",
        PnLIncomeGroupTypes = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsPnLIncomeGroupTypes] ?? "",
        PnLExpenseGroupTypes = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsPnLExpenseGroupTypes] ?? "",
        PnLGlTableNameLike = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsPnLGlTableNameLike] ?? "",
        BilledRevenueAccounts = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsBilledRevenueAccounts] ?? "",
        BilledExpenseAccountRanges = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsBilledExpenseAccountRanges] ?? "",
        BilledOtherIncomeAccountRanges = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsBilledOtherIncomeAccountRanges] ?? "",
        BilledUsdToCadRate = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsBilledUsdToCadRate] ?? "",
        BilledDefaultOrg = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsBilledDefaultOrg] ?? "",
        CashAccountWhitelist = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsCashAccountWhitelist] ?? "",
        CashUsdToCadRate = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsCashUsdToCadRate] ?? ""
    };

    internal static CompensationOptions GetCompensationOptions() => _compensationOptions ??= new CompensationOptions
    {
        PoolRate = double.TryParse(
            ConfigurationManager.AppSettings[AppConfigKeys.CompensationPoolRate],
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out var rate)
            ? rate
            : 0.10
    };

    internal static OpportunitiesOptions GetOpportunitiesOptions() => _opportunitiesOptions ??= new OpportunitiesOptions
    {
        OpportunitiesDb = GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorOpportunitiesDb)
    };

    internal static WatchlistSyncOptions GetWatchlistSyncOptions() => _watchlistSyncOptions ??= new WatchlistSyncOptions
    {
        ServiceUrl = ConfigurationManager.AppSettings[AppConfigKeys.WatchlistSyncServiceUrl] ?? "",
        Username   = ConfigurationManager.AppSettings[AppConfigKeys.WatchlistSyncUsername] ?? "",
        Password   = ConfigurationManager.AppSettings[AppConfigKeys.WatchlistSyncPassword] ?? ""
    };

    internal static Serilog.Core.Logger GetSerilogLogger()
    {
        if (_serilogLogger != null)
            return _serilogLogger;

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logDirectory = Path.Combine(appDataPath, "KorOperations", "logs");
        Directory.CreateDirectory(logDirectory);

        _serilogLogger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Destructure.With<CredentialRedactingPolicy>()
            .Enrich.With<CredentialRedactingEnricher>()
            .WriteTo.File(
                path: Path.Combine(logDirectory, "app-.log"),
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj} {Properties:j}{NewLine}{SanitizedExceptionMessage}{NewLine}{Exception}",
                retainedFileCountLimit: 30,
                fileSizeLimitBytes: 10L * 1024 * 1024)
            .CreateLogger();

        return _serilogLogger;
    }

    internal static string GetRequiredAppSetting(string key)
    {
        var value = (ConfigurationManager.AppSettings[key] ?? string.Empty).Trim();
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"App.config appSetting '{key}' is missing or empty.");
    }

    internal static string GetRequiredConnectionString(string key)
    {
        var value = ConfigurationManager.ConnectionStrings[key]?.ConnectionString;
        return !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"App.config connection string '{key}' is missing or empty.");
    }
}
