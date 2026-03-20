#nullable enable
using System;
using Kor.Operations.App.Email;
using Kor.Operations.App.Options;
using Kor.Operations.App.Services;
using Kor.Operations.Core;
using Kor.Operations.Core.Services;
using Kor.Operations.Data;
using Kor.Operations.GeneralTools;
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
        var anthropicApiKey = System.Configuration.ConfigurationManager.AppSettings["AnthropicApiKey"] ?? string.Empty;

        services.AddSingleton<IServiceProvider>(sp => sp);
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(serilogLogger, dispose: true);
        });
        services.AddSingleton(storageOptions);
        services.AddSingleton(userOptions);
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
                typeof(MainWindow).Assembly.GetName().Version?.ToString()));
        services.AddTransient(sp =>
            new MainWindowWorkflowService(
                userUpn,
                sp.GetRequiredService<IUserPreferencesStore>(),
                sp.GetRequiredService<IUploadOrchestrator>(),
                sp.GetRequiredService<ITransmittalService>(),
                sp.GetRequiredService<DatabaseOptions>(),
                sp.GetRequiredService<UserOptions>()));

        services.AddTransient<BrochureBuilderViewModel>();
        services.AddSingleton<EmailSubjectExtractor>();
        services.AddSingleton<ProjectFolderCatalogService>();
        services.AddSingleton<FavoriteProjectsService>();
        services.AddSingleton<EmailFilingService>();
        services.AddSingleton<EmailAttachmentService>();
        services.AddSingleton<FolderPickerService>();
        services.AddSingleton<IBrochureContactStore, BrochureContactStore>();
        services.AddSingleton<Kor.Operations.Core.Services.FeeProposalStore>();
        services.AddSingleton<Kor.Operations.Core.Services.ProposalBlockLibraryStore>();
        services.AddSingleton<Kor.Operations.Core.Services.ProposalStaffStore>();
        services.AddSingleton<IFeeProposalRenderer, FeeProposalRenderer>();
        services.AddSingleton<PreferencesFavoritesService>();
        services.AddSingleton<PeopleLookupService>();
        services.AddSingleton<PreferencesTeamsService>();
        services.AddSingleton<SignatureEditorService>();
        services.AddTransient<MainWindow>();
        services.AddTransient<HomeWindow>();
        services.AddTransient<DashboardWindow>();
        services.AddTransient<EmailSearchWindow>();
        services.AddTransient<EmailFilePickerWindow>();
        services.AddTransient<GeneralToolsWindow>();
        services.AddTransient<BrochureBuilderWindow>();
        services.AddTransient<Func<GeneralToolsWindow>>(sp => () => sp.GetRequiredService<GeneralToolsWindow>());
        services.AddTransient<Func<BrochureBuilderWindow>>(sp => () => sp.GetRequiredService<BrochureBuilderWindow>());
        services.AddTransient<QuickTransferWindow>();
        services.AddTransient<PreferencesWindow>();
        services.AddTransient<TeamsPickerWindow>();
        services.AddTransient<InboundUploadRunner>();
        services.AddTransient<QuickTransferRunner>();

        return services;
    }
}
