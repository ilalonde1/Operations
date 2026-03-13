#nullable enable
using System;
using System.Collections.Generic;
using System.Configuration;
using Kor.EmailSearch.Core;
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using Kor.Operations.Graph;
using Kor.Operations.Rendering;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations
{
    internal static class AppCompositionRoot
    {
        internal static IServiceProvider BuildServiceProvider()
        {
            var services = new ServiceCollection();
            var transmittalsConnectionString = GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorTransmittalsDb);
            var emailIndexConnectionString = GetRequiredConnectionString(AppConfigKeys.ConnectionStrings.KorEmailIndex);
            var projectsRoot = GetRequiredAppSetting(AppConfigKeys.ProjectsRoot);
            var redirectorBaseUrl = ConfigurationManager.AppSettings[AppConfigKeys.RedirectorBaseUrl];
            var userUpn = AppAuthBootstrapper.ResolveUserUpn();

            services.AddSingleton<IServiceProvider>(sp => sp);
            services.AddSingleton<IGraphFacade>(_ => GraphFacade.Instance);
            services.AddTransient(typeof(CoverSheetRenderer), _ => throw new NotSupportedException("CoverSheetRenderer is static and is not constructed through DI."));
            services.AddTransient<IEmailMetadataExtractor, BasicEmailMetadataExtractor>();
            services.AddTransient<GlProfitLossService>();
            services.AddTransient<FinancialsService>();
            services.AddTransient(_ => new SqlFinancialPortfolioSnapshotStore(transmittalsConnectionString));
            services.AddTransient<ExecutiveSummaryDeltekLoader>();
            services.AddTransient<ExecutiveSummaryService>();
            services.AddTransient<EmailIndexWriter>(sp =>
                new EmailIndexWriter(emailIndexConnectionString, sp.GetRequiredService<IEmailMetadataExtractor>()));
            services.AddTransient(_ => new EmailSearchService(emailIndexConnectionString));

            services.AddSingleton<VpOdbcDsnFactory>(_ =>
            {
                var dsn = ConfigurationManager.AppSettings[AppConfigKeys.VpDsn] ?? "Deltek";
                var user = ConfigurationManager.AppSettings[AppConfigKeys.VpUser] ?? string.Empty;
                var pwd = ConfigurationManager.AppSettings[AppConfigKeys.VpPassword] ?? string.Empty;
                return new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());
            });
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<SqlTransmittalsStore>(sp, transmittalsConnectionString));
            services.AddSingleton<ITransmittalsStore>(sp => sp.GetRequiredService<SqlTransmittalsStore>());
            services.AddSingleton(sp => new SqlUserPreferencesStore(transmittalsConnectionString));
            services.AddSingleton<IUserPreferencesStore>(sp => sp.GetRequiredService<SqlUserPreferencesStore>());
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<PreferencesRepository>(sp, transmittalsConnectionString));
            services.AddSingleton(_ => new SqlEmailIndexStore(emailIndexConnectionString));
            services.AddSingleton(sp => ActivatorUtilities.CreateInstance<VantagepointRepository>(sp, sp.GetRequiredService<VpOdbcDsnFactory>()));

            services.AddTransient<IUploadOrchestrator, UploadOrchestrator>();
            services.AddTransient<IRecipientResolver, RecipientResolver>();
            services.AddTransient<IProjectSearchService>(sp =>
                new ProjectSearchService(
                    sp.GetRequiredService<PreferencesRepository>(),
                    projectsRoot,
                    mustContainSubfolder: null));
            services.AddTransient<ITransmittalService>(sp =>
                new TransmittalService(
                    sp.GetRequiredService<IGraphFacade>(),
                    sp.GetRequiredService<IUploadOrchestrator>(),
                    sp.GetRequiredService<ITransmittalsStore>(),
                    transmittalsConnectionString,
                    redirectorBaseUrl,
                    typeof(MainWindow).Assembly.GetName().Version?.ToString()));
            services.AddTransient(sp =>
                new MainWindowWorkflowService(
                    userUpn,
                    sp.GetRequiredService<IUserPreferencesStore>(),
                    sp.GetRequiredService<IUploadOrchestrator>(),
                    sp.GetRequiredService<ITransmittalService>()));

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
