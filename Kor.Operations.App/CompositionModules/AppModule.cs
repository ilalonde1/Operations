#nullable enable
using System;
using Kor.Operations.App.PMTools;
using Kor.Operations.App.FeeProposal;
using Kor.Operations.App.FileSync;
using Kor.Operations.App.Email;
using Kor.Operations.App.Options;
using Kor.Operations.App.Services;
using Kor.Operations.Core;
using Kor.Operations.Core.Services;
using Kor.Operations.Data;
using Kor.Operations.Brochures;
using Kor.Operations.Financials;
using Kor.Operations.Graph;
using Kor.Operations.Rendering.Proposal;
using Kor.Operations.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Kor.Operations;

internal static class AppModule
{
    internal static IServiceCollection AddAppServices(this IServiceCollection services)
    {
        var storageOptions = CompositionHelpers.GetStorageOptions();
        var userOptions = CompositionHelpers.GetUserOptions();
        var databaseOptions = CompositionHelpers.GetDatabaseOptions();
        var graphOptions = CompositionHelpers.GetGraphOptions();
        var serilogLogger = CompositionHelpers.GetSerilogLogger();
        var userUpn = AppAuthBootstrapper.ResolveUserUpn(userOptions);
        var anthropicApiKey =
            Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.Machine)
            ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY", EnvironmentVariableTarget.User)
            ?? Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY")
            ?? string.Empty;
        anthropicApiKey = anthropicApiKey.Trim();
        var hasAnthropicKey = !string.IsNullOrWhiteSpace(anthropicApiKey);
        var anthropicKeyLooksInvalid = hasAnthropicKey && anthropicApiKey.IndexOfAny([' ', '\t', '\r', '\n']) >= 0;
        if (!hasAnthropicKey)
        {
            serilogLogger.Warning("Anthropic API key is missing. Anthropic-dependent features will be disabled.");
        }
        else if (anthropicKeyLooksInvalid)
        {
            serilogLogger.Warning("Anthropic API key format appears invalid. Anthropic-dependent features will be disabled.");
            anthropicApiKey = string.Empty;
        }

        services.AddSingleton<IServiceProvider>(sp => sp);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(serilogLogger, dispose: true);
        });
        services.AddSingleton(storageOptions);
        services.AddSingleton(userOptions);
        var watchlistSyncOptions = CompositionHelpers.GetWatchlistSyncOptions();
        services.AddSingleton(watchlistSyncOptions);
        services.AddSingleton(sp => new Kor.Operations.Financials.WatchlistSyncClient(watchlistSyncOptions));
        services.AddSingleton(new BrochureAnalysisService(anthropicApiKey));
        services.AddTransient<IUploadOrchestrator, UploadOrchestrator>();
        services.AddTransient<IProjectSearchService>(sp =>
            new ProjectSearchService(
                sp.GetRequiredService<PreferencesRepository>(),
                storageOptions.ProjectsRoot,
                mustContainSubfolder: null));
        services.AddSingleton<ITransmittalService>(sp =>
            new TransmittalService(
                sp.GetRequiredService<IGraphFacade>(),
                sp.GetRequiredService<IUploadOrchestrator>(),
                sp.GetRequiredService<ITransmittalsStore>(),
                databaseOptions.KorTransmittalsDb,
                graphOptions.RedirectorBaseUrl,
                typeof(MainWindow).Assembly.GetName().Version?.ToString(),
                sp.GetRequiredService<ILogger<TransmittalService>>()));
        services.AddTransient(sp =>
            new MainWindowWorkflowService(
                userUpn,
                sp.GetRequiredService<IUserPreferencesStore>(),
                sp.GetRequiredService<IUploadOrchestrator>(),
                sp.GetRequiredService<ITransmittalService>(),
                sp.GetRequiredService<DatabaseOptions>(),
                sp.GetRequiredService<UserOptions>()));

        services.AddTransient<BrochureBuilderViewModel>();
        services.AddTransient<FeeProposalBuilderViewModel>();
        services.AddTransient(sp => new WorkloadMeetingPanelViewModel(
            sp.GetRequiredService<IWorkloadMeetingStore>(),
            userUpn,
            sp.GetRequiredService<ILogger<WorkloadMeetingPanelViewModel>>()));
        services.AddSingleton<AppAiContextBuilder>();
        services.AddSingleton<FirmContextProvider>();
        services.AddSingleton(sp => new AppAiService(
            anthropicApiKey,
            sp.GetRequiredService<AppAiContextBuilder>()));
        services.AddSingleton<EmailSubjectExtractor>();
        services.AddSingleton<ProjectFolderCatalogService>();
        services.AddSingleton<FavoriteProjectsService>();
        services.AddSingleton<EmailFilingService>();
        services.AddSingleton<EmailAttachmentService>();
        services.AddSingleton<FolderPickerService>();
        services.AddSingleton<IBrochureContactStore, BrochureContactStore>();
        services.AddSingleton<IFeeProposalDocxRenderer, FeeProposalDocxRenderer>();
        services.AddSingleton<IFeeProposalRenderer, FeeProposalRenderer>();
        services.AddSingleton<PreferencesFavoritesService>();
        services.AddSingleton<PeopleLookupService>();
        services.AddSingleton<PreferencesTeamsService>();
        services.AddSingleton<SignatureEditorService>();
        services.AddTransient<MainWindow>();
        services.AddTransient<HomeWindow>();
        services.AddTransient<DashboardWindow>();
        services.AddTransient<EmailSearchWindow>();
        services.AddTransient<FinancialsWindow>();
        services.AddTransient<EmailFilePickerWindow>(sp => new EmailFilePickerWindow(
            sp.GetRequiredService<FavoriteProjectsService>(),
            sp.GetRequiredService<EmailSubjectExtractor>(),
            sp.GetRequiredService<ProjectFolderCatalogService>(),
            sp.GetRequiredService<EmailFilingService>(),
            sp.GetRequiredService<EmailAttachmentService>(),
            sp.GetRequiredService<FolderPickerService>(),
            sp.GetRequiredService<ILogger<EmailFilePickerWindow>>()));
        services.AddTransient<GeneralToolsWindow>();
        services.AddTransient<BrochureBuilderWindow>();
        services.AddTransient<PMTools.PmToolsWindow>();
        services.AddTransient<EngineeringTools.EngineeringToolsWindow>(sp =>
            new EngineeringTools.EngineeringToolsWindow(sp));
        services.AddTransient<EngineeringTools.PdfToSafe.PdfToSafeWindow>();
        services.AddTransient<FeeProposalBuilderWindow>();
        services.AddTransient<Func<GeneralToolsWindow>>(sp => () => sp.GetRequiredService<GeneralToolsWindow>());
        services.AddTransient<Func<BrochureBuilderWindow>>(sp => () => sp.GetRequiredService<BrochureBuilderWindow>());
        services.AddTransient<Func<TeamsPickerWindow>>(sp => () => sp.GetRequiredService<TeamsPickerWindow>());
        services.AddTransient<QuickTransferWindow>();
        services.AddTransient<PreferencesWindow>();
        services.AddTransient<TeamsPickerWindow>();
        services.AddTransient<InboundUploadRunner>();
        services.AddTransient<QuickTransferRunner>();

        services.AddSingleton(sp => new FileSyncControlPlaneReader(databaseOptions.KorTransmittalsDb));
        services.AddTransient<FileSyncCommandCenterViewModel>();
        services.AddTransient<FileSyncCommandCenterWindow>();

        // BD hub - bundles Opportunities, CRM (Phase 5), FeeProposal, Brochure
        // under one HomeWindow tile.
        services.AddTransient<App.BusinessDevelopment.BusinessDevelopmentWindow>();

        return services;
    }
}
