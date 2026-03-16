#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using Kor.EmailSearch.Core;
using Kor.Operations.App.Options;
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using Kor.Operations.Graph;
using Kor.Operations.Rendering;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Kor.Operations
{
    internal static class AppCompositionRoot
    {
        internal static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDirectory = Path.Combine(appDataPath, "KorOperations", "logs");
            Directory.CreateDirectory(logDirectory);

            var serilogLogger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: Path.Combine(logDirectory, "app-.log"),
                    rollingInterval: RollingInterval.Day,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            var graphOptions = new GraphOptions
            {
                TenantId = GetRequiredAppSetting(AppConfigKeys.GraphTenantId),
                ClientId = GetRequiredAppSetting(AppConfigKeys.GraphClientId),
                DriveId = GetRequiredAppSetting(AppConfigKeys.GraphDriveId),
                RedirectorBaseUrl = GetRequiredAppSetting(AppConfigKeys.RedirectorBaseUrl)
            };
            var deltekOdbcOptions = new DeltekOdbcOptions
            {
                Dsn = ConfigurationManager.AppSettings[AppConfigKeys.VpDsn] ?? "",
                OdbcDsn = ConfigurationManager.AppSettings[AppConfigKeys.DeltekOdbcDsn] ?? "",
                User = ConfigurationManager.AppSettings[AppConfigKeys.VpUser] ?? "",
                Password = ConfigurationManager.AppSettings[AppConfigKeys.VpPassword] ?? ""
            };
            var databaseOptions = new DatabaseOptions
            {
                KorTransmittalsDb = GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorTransmittalsDb),
                KorTransmittals = ConfigurationManager.ConnectionStrings[AppConfigKeys.ConnectionStrings.KorTransmittals]?.ConnectionString ?? ""
            };
            var storageOptions = new StorageOptions
            {
                ProjectsRoot = GetRequiredAppSetting(AppConfigKeys.ProjectsRoot),
                StandardDetailsFileStorageRootPath = ConfigurationManager.AppSettings[AppConfigKeys.StandardDetailsFileStorageRootPath] ?? ""
            };
            var userOptions = new UserOptions
            {
                UserUpnOverride = ConfigurationManager.AppSettings[AppConfigKeys.UserUpnOverride] ?? "",
                DefaultFromEmail = ConfigurationManager.AppSettings[AppConfigKeys.DefaultFromEmail] ?? "",
                DefaultFromDomain = ConfigurationManager.AppSettings[AppConfigKeys.DefaultFromDomain] ?? ""
            };
            var financialsOptions = new FinancialsOptions
            {
                PnLGlFlipSign = ConfigurationManager.AppSettings[AppConfigKeys.FinancialsPnLGlFlipSign] ?? "",
                FiscalYearStartMonth = ConfigurationManager.AppSettings["Financials.PnL.FiscalYearStartMonth"] ?? "",
                PnLEngRate = ConfigurationManager.AppSettings["Financials.PnL.EngRate"] ?? "",
                PnLDraftRate = ConfigurationManager.AppSettings["Financials.PnL.DraftRate"] ?? "",
                PnLOtherDirectRate = ConfigurationManager.AppSettings["Financials.PnL.OtherDirectRate"] ?? "",
                PnLOverheadRate = ConfigurationManager.AppSettings["Financials.PnL.OverheadRate"] ?? ""
            };
            var userUpn = AppAuthBootstrapper.ResolveUserUpn(userOptions);

            services.AddSingleton<IServiceProvider>(sp => sp);
            services.AddLogging(builder =>
            {
                builder.ClearProviders();
                builder.AddSerilog(serilogLogger, dispose: true);
            });
            services.AddSingleton(graphOptions);
            services.AddSingleton(deltekOdbcOptions);
            services.AddSingleton(databaseOptions);
            services.AddSingleton(storageOptions);
            services.AddSingleton(userOptions);
            services.AddSingleton(financialsOptions);
            services.AddSingleton<IGraphFacade>(_ => GraphFacade.Instance);
            services.AddTransient(typeof(CoverSheetRenderer), _ => throw new NotSupportedException("CoverSheetRenderer is static and is not constructed through DI."));
            services.AddTransient<IEmailMetadataExtractor, BasicEmailMetadataExtractor>();
            services.AddTransient<GlProfitLossService>();
            services.AddTransient<FinancialsService>();
            services.AddTransient<ProfitLossReportService>();
            services.AddTransient(_ => new SqlFinancialPortfolioSnapshotStore(databaseOptions.KorTransmittalsDb));
            services.AddTransient<ExecutiveSummaryDeltekLoader>();
            services.AddTransient<ExecutiveSummaryService>();
            services.AddTransient<EmailIndexWriter>(sp =>
                new EmailIndexWriter(GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex), sp.GetRequiredService<IEmailMetadataExtractor>()));
            services.AddTransient(_ => new EmailSearchService(GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex)));

            services.AddSingleton<VpOdbcDsnFactory>(_ =>
            {
                var dsn = string.IsNullOrWhiteSpace(deltekOdbcOptions.Dsn) ? "Deltek" : deltekOdbcOptions.Dsn;
                return new VpOdbcDsnFactory(dsn, deltekOdbcOptions.User, deltekOdbcOptions.Password, () => new Dictionary<string, string>());
            });
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<SqlTransmittalsStore>(sp, databaseOptions.KorTransmittalsDb));
            services.AddSingleton<ITransmittalsStore>(sp => sp.GetRequiredService<SqlTransmittalsStore>());
            services.AddSingleton(sp => new SqlUserPreferencesStore(databaseOptions.KorTransmittalsDb));
            services.AddSingleton<IUserPreferencesStore>(sp => sp.GetRequiredService<SqlUserPreferencesStore>());
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<PreferencesRepository>(sp, databaseOptions.KorTransmittalsDb));
            services.AddSingleton(_ => new SqlEmailIndexStore(GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex)));
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<VantagepointRepository>(sp, sp.GetRequiredService<VpOdbcDsnFactory>()));

            services.AddTransient<IUploadOrchestrator, UploadOrchestrator>();
            services.AddTransient<IRecipientResolver, RecipientResolver>();
            services.AddTransient<IProjectSearchService>(sp =>
                new ProjectSearchService(
                    sp.GetRequiredService<PreferencesRepository>(),
                    storageOptions.ProjectsRoot,
                    mustContainSubfolder: null));
            services.AddTransient<ITransmittalService>(sp =>
                new TransmittalService(
                    sp.GetRequiredService<IGraphFacade>(),
                    sp.GetRequiredService<IUploadOrchestrator>(),
                    sp.GetRequiredService<ITransmittalsStore>(),
                    databaseOptions.KorTransmittalsDb,
                    graphOptions.RedirectorBaseUrl,
                    typeof(MainWindow).Assembly.GetName().Version?.ToString()));
            services.AddTransient(sp =>
                new MainWindowWorkflowService(
                    userUpn,
                    sp.GetRequiredService<IUserPreferencesStore>(),
                    sp.GetRequiredService<IUploadOrchestrator>(),
                    sp.GetRequiredService<ITransmittalService>(),
                    sp.GetRequiredService<DatabaseOptions>(),
                    sp.GetRequiredService<UserOptions>()));

            services.AddTransient<MainWindow>();
            services.AddTransient<HomeWindow>();
            services.AddTransient<DashboardWindow>();
            services.AddTransient<EmailSearchWindow>();
            services.AddTransient<EmailFilePickerWindow>();
            services.AddTransient<QuickTransferWindow>();
            services.AddTransient<PreferencesWindow>();
            services.AddTransient<TeamsPickerWindow>();
            services.AddTransient<InboundUploadRunner>();
            services.AddTransient<QuickTransferRunner>();

            return services.BuildServiceProvider();
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
}
