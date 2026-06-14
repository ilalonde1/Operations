#nullable enable
using Kor.Operations.App.Crm;
using Kor.Operations.App.Opportunities;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Data.Deltek;
using Kor.Opportunities.Core.Deltek;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.IndustryEvents;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
using Kor.Opportunities.Data.Projects;
using Kor.Opportunities.Data.Scoring;
using Kor.Opportunities.Data.Sources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kor.Operations;

/// <summary>
/// DI registrations for the BD opportunity-pipeline module.
/// Plan: <c>~/.claude/plans/kor-opportunities-architecture-plan.md</c>.
/// </summary>
internal static class OpportunitiesModule
{
    internal static IServiceCollection AddOpportunitiesServices(this IServiceCollection services)
    {
        var options = CompositionHelpers.GetOpportunitiesOptions();
        services.AddSingleton(options);
        services.AddMemoryCache();

        // Stores take a plain connection string so Kor.Opportunities.Data stays
        // free of any host-specific Options class. The Worker registers its own
        // copy of these via Kor.Opportunities.Worker; the App registers them
        // here for the WPF feature window.
        services.AddSingleton<IHeartbeatStore>(_ => new SqlHeartbeatStore(options.OpportunitiesDb));
        services.AddSingleton<IOpportunityStore>(sp => new SqlOpportunityStore(
            options.OpportunitiesDb,
            sp.GetRequiredService<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>(),
            sp.GetService<ILogger<SqlOpportunityStore>>()));

        // Phase 4A/D: ingestion-side stores. Singletons - all stateless; the App
        // reads observations + ingestion-run history for the admin viewer and
        // writes trigger rows for the "Run Now" button.
        services.AddSingleton<IOpportunitySourceStore>(_ => new SqlOpportunitySourceStore(options.OpportunitiesDb));
        services.AddSingleton<IOpportunityObservationStore>(_ => new SqlOpportunityObservationStore(options.OpportunitiesDb));
        services.AddSingleton<IMajorProjectsInventoryStore>(_ => new SqlMajorProjectsInventoryStore(options.OpportunitiesDb));
        services.AddSingleton<IPrimePipelineStore>(_ => new SqlPrimePipelineStore(options.OpportunitiesDb));
        services.AddSingleton<IBdDashboardStore>(_ => new SqlBdDashboardStore(options.OpportunitiesDb));
        // BD Reports (BD-UI-Plan-2026-06-08): dashboard cards, report generators
        // and MCP BD tools all read through this one service.
        services.AddSingleton<Kor.Opportunities.Data.BdReports.IBdReportService>(
            _ => new Kor.Opportunities.Data.BdReports.SqlBdReportService(options.OpportunitiesDb));
        services.AddSingleton<IPursuitBriefStore>(_ => new SqlPursuitBriefStore(options.OpportunitiesDb));
        services.AddSingleton<IIndustryEventStore>(_ => new SqlIndustryEventStore(options.OpportunitiesDb));
        services.AddSingleton<IntelReadService>(_ => new IntelReadService(options.OpportunitiesDb));

        // Phase 1A/B/C: BD Pursuit Brief — data store + Word generator + region picker.
        services.AddSingleton<Kor.Opportunities.Data.Briefs.IBriefDataStore>(sp =>
            new Kor.Opportunities.Data.Briefs.SqlBriefDataStore(
                options.OpportunitiesDb,
                sp.GetRequiredService<IntelReadService>()));
        services.AddSingleton<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefGenerator,
            Kor.Operations.App.BusinessDevelopment.Briefs.BriefGenerator>();
        services.AddSingleton<Kor.Operations.App.BusinessDevelopment.Briefs.IBriefPdfGenerator,
            Kor.Operations.App.BusinessDevelopment.Briefs.BriefPdfGenerator>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Briefs.RegionBriefDialog>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Briefs.BriefsMakerWindow>();

        services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityStore>(
            _ => new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityObservationStore>(
            _ => new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityObservationStore(options.OpportunitiesDb));
        services.AddSingleton<IIngestionRunStore>(_ => new SqlIngestionRunStore(options.OpportunitiesDb));
        services.AddSingleton<IJobScheduleStore>(_ => new SqlJobScheduleStore(options.OpportunitiesDb));
        services.AddSingleton<IIngestionTriggerStore>(sp => new SqlIngestionTriggerStore(
            options.OpportunitiesDb,
            sp.GetRequiredService<ILogger<SqlIngestionTriggerStore>>()));
        services.AddSingleton<Kor.Opportunities.Data.Awards.IBdResearchTriggerStore>(sp =>
            new Kor.Opportunities.Data.Awards.SqlBdResearchTriggerStore(
                options.OpportunitiesDb,
                sp.GetService<ILogger<Kor.Opportunities.Data.Awards.SqlBdResearchTriggerStore>>()));
        services.AddSingleton<Kor.Opportunities.Data.Awards.IOpportunityInterestedFirmStore>(
            _ => new Kor.Opportunities.Data.Awards.SqlOpportunityInterestedFirmStore(options.OpportunitiesDb));

        // Phase 2B: WPF feature window. Both transient so a Close+reopen cycle
        // gets a fresh VM (no stale data from a previous session). Mirrors the
        // FileSync Command Center registration in AppModule.cs.
        services.AddTransient<OpportunitiesViewModel>();
        services.AddTransient<OpportunitiesView>();
        services.AddTransient<OpportunitiesWindow>();
        services.AddTransient<MajorProjectsInventoryViewModel>();
        services.AddTransient<MajorProjectsInventoryWindow>();
        services.AddTransient<MajorProjectsInventoryView>();
        services.AddTransient<PrimePipelineViewModel>();
        services.AddTransient<PrimePipelineWindow>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.DashboardViewModel>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.DashboardView>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.RelationshipsViewModel>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.RelationshipsView>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.EventsViewModel>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.EventsView>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.AdminViewModel>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.AdminView>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.PursuitBriefViewModel>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.PursuitBriefWindow>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Workspace.BdWorkspaceWindow>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Reports.BdReportsViewModel>();
        services.AddTransient<Kor.Operations.App.BusinessDevelopment.Reports.BdReportsWindow>();
        services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.ICompetitionInfoQueryStore>(
            _ => new Kor.Opportunities.Data.HistoricalOpportunities.SqlCompetitionInfoQueryStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.Awards.IAwardQueryStore>(
            _ => new Kor.Opportunities.Data.Awards.SqlAwardQueryStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.Awards.IVendorAnalyticsStore>(
            _ => new Kor.Opportunities.Data.Awards.SqlVendorAnalyticsStore(options.OpportunitiesDb));
        services.AddTransient<Kor.Operations.App.Opportunities.CompetitorProfileViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.BuyerProfileViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.CompetitionRfpsViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.CompetitionAwardsViewModel>();
        services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityDocumentStore>(
            _ => new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityDocumentStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.Awards.IKorPursuitStore>(
            _ => new Kor.Opportunities.Data.Awards.SqlKorPursuitStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.Awards.ICanonicalOrgStore>(
            _ => new Kor.Opportunities.Data.Awards.SqlCanonicalOrgStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.Awards.IEnrichmentTrackingStore>(sp =>
            new Kor.Opportunities.Data.Awards.SqlEnrichmentTrackingStore(
                options.OpportunitiesDb,
                sp.GetRequiredService<IntelExtractorRegistry>(),
                sp.GetRequiredService<IntelPersistenceService>()));
        services.AddSingleton<Kor.Opportunities.Data.Awards.IKorClientBdIntelligenceStore>(
            _ => new Kor.Opportunities.Data.Awards.SqlKorClientBdIntelligenceStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.Awards.IArchitectDisplacementBriefStore>(
            _ => new Kor.Opportunities.Data.Awards.SqlArchitectDisplacementBriefStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>();

        // Intel extractors — single source of truth in IntelExtractorBootstrap.
        // Adding a new extractor goes there, not here.
        foreach (var ex in IntelExtractorBootstrap.GetDefaultExtractors())
        {
            var captured = ex;
            services.AddSingleton<IIntelExtractor>(_ => captured);
        }
        services.AddSingleton<DefaultIntelExtractor>();
        services.AddSingleton<IntelExtractorRegistry>();
        services.AddSingleton<IntelPersistenceService>(_ => new IntelPersistenceService(options.OpportunitiesDb));
        services.AddSingleton<IProjectIntelExtractor, ProjectBriefExtractor>();
        services.AddSingleton<IProjectIntelExtractor, ProjectBriefHoningExtractor>();
        services.AddSingleton<DefaultProjectIntelExtractor>();
        services.AddSingleton<ProjectIntelExtractorRegistry>();
        services.AddSingleton<ProjectIntelPersistenceService>(sp =>
            new ProjectIntelPersistenceService(
                options.OpportunitiesDb,
                sp.GetRequiredService<ILogger<ProjectIntelPersistenceService>>()));
        services.AddSingleton<IMajorProjectEnrichmentTrackingStore>(sp =>
            new SqlMajorProjectEnrichmentTrackingStore(
                options.OpportunitiesDb,
                sp.GetRequiredService<ProjectIntelExtractorRegistry>(),
                sp.GetRequiredService<ProjectIntelPersistenceService>(),
                sp.GetRequiredService<ILogger<SqlMajorProjectEnrichmentTrackingStore>>()));
        services.AddSingleton<PersonBriefExtractor>();
        services.AddSingleton<Kor.Opportunities.Data.People.IPersonRefreshChokepoint>(sp =>
            new Kor.Opportunities.Data.People.SqlPersonRefreshChokepoint(
                options.OpportunitiesDb,
                sp.GetRequiredService<PersonBriefExtractor>(),
                sp.GetRequiredService<IntelPersistenceService>(),
                sp.GetRequiredService<ILogger<Kor.Opportunities.Data.People.SqlPersonRefreshChokepoint>>()));
        services.AddSingleton<Kor.Operations.App.Opportunities.CustomProposalImportService>();
        services.AddTransient<Kor.Operations.App.Opportunities.OrgDossierViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.OrgDossierView>();
        services.AddTransient<Kor.Operations.App.Opportunities.CompetitionInfoViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.HistoricalOpportunityDetailViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.KorPursuitDialogViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.CompetitionInfoWindow>();
        services.AddTransient<Kor.Operations.App.Opportunities.CompetitionInfoView>();
        services.AddTransient<IngestionRunsWindow>();

        // Phase 3A: rules-based scoring. Singletons everywhere - the accessor
        // holds the 10 s cache, the scorer is pure, the store is stateless. No
        // scope factory needed (CR's port has one; we don't because none of
        // these have scoped dependencies).
        services.AddSingleton<IScoringProfileStore>(_ => new SqlScoringProfileStore(options.OpportunitiesDb));
        services.AddSingleton<IScoringOptionsAccessor, ScoringOptionsAccessor>();
        // Phase 5 (Commit 5): Deltek-driven scoring bonuses. The accessor wraps
        // IDeltekClientContextService through its 5-min cache; the Worker
        // registers a NullDeltekClientFactsAccessor instead.
        services.AddSingleton<IDeltekClientFactsAccessor, DeltekClientFactsAccessor>();
        services.AddSingleton<IOpportunityScoringService, RuleBasedOpportunityScoringService>();

        // Phase 3C: admin profile editor + recalc-all.
        services.AddTransient<ScoringProfileViewModel>();
        services.AddTransient<ScoringProfileWindow>();

        // Phase 5: CRM stores + window. Engagements/Activities/Contacts hang
        // off opportunities.Opportunities; same connection string + concurrency
        // discipline as the rest of the schema.
        services.AddSingleton<ICrmEngagementStore>(_ => new SqlCrmEngagementStore(options.OpportunitiesDb));
        services.AddSingleton<ICrmActivityStore>(_ => new SqlCrmActivityStore(options.OpportunitiesDb));
        services.AddSingleton<ICrmContactStore>(_ => new SqlCrmContactStore(options.OpportunitiesDb));

        // Phase 5c: Deltek client roll-up (lifetime fee, project count, last
        // engagement) shown on the CRM detail panel for any engagement whose
        // linked Opportunity has DeltekClientId set. Reuses VpOdbcDsnFactory +
        // DeltekOdbcOptions registered by FinancialsModule.
        services.AddSingleton<IDeltekClientContextService, DeltekClientContextService>();

        // Commit 2: fuzzy Deltek Clendor/Contacts lookup for the BD seed importer.
        services.AddSingleton<IDeltekLookupService, DeltekLookupService>();

        // Round 16a: read-through won-project history from Deltek. The public
        // accessor is cached for UI responsiveness; the nightly Worker signal
        // refresh uses the same contract with its own null/real registration.
        services.AddSingleton<DeltekKorWonProjectAccessor>(sp =>
        {
            var deltekOptions = sp.GetRequiredService<DeltekOdbcOptions>();
            return new DeltekKorWonProjectAccessor(
                sp.GetRequiredService<VpOdbcDsnFactory>(),
                deltekOptions.Catalog);
        });
        services.AddSingleton<IKorWonProjectAccessor>(sp =>
            new CachingKorWonProjectAccessor(
                sp.GetRequiredService<DeltekKorWonProjectAccessor>(),
                sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
        services.AddSingleton<IKorPursuitDeltekAccessor>(sp =>
        {
            var deltekOptions = sp.GetRequiredService<DeltekOdbcOptions>();
            return new DeltekKorPursuitDeltekAccessor(
                sp.GetRequiredService<VpOdbcDsnFactory>(),
                deltekOptions.Catalog);
        });

        services.AddTransient<CrmViewModel>();
        services.AddTransient<CrmWindow>();
        services.AddTransient<CrmView>();

        // BD Tracking spreadsheet replica (migration 48-49; 70 engagements ingested
        // by tools/BdTrackingImport). Region tabs + per-initiator filter + rollup
        // + drill-detail panel with Activities / Contacts / Linked MPI Projects.
        services.AddTransient<BdTrackingViewModel>(sp =>
            new BdTrackingViewModel(options.OpportunitiesDb, sp.GetService<Microsoft.Extensions.Logging.ILogger<BdTrackingViewModel>>()));
        services.AddTransient<BdTrackingView>();
        services.AddTransient<CrmEngagementDialog>();
        services.AddTransient<ClientIntelligenceViewModel>();
        services.AddTransient<ClientIntelligenceWindow>();

        return services;
    }
}
