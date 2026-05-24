#nullable enable
using Kor.Operations.App.Crm;
using Kor.Operations.App.Opportunities;
using Kor.Operations.App.Options;
using Kor.Opportunities.Core.Scoring;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Heartbeat;
using Kor.Opportunities.Data.Ingestion;
using Kor.Opportunities.Data.Observations;
using Kor.Opportunities.Data.Opportunities;
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
        services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityStore>(
            _ => new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.HistoricalOpportunities.IHistoricalOpportunityObservationStore>(
            _ => new Kor.Opportunities.Data.HistoricalOpportunities.SqlHistoricalOpportunityObservationStore(options.OpportunitiesDb));
        services.AddSingleton<IIngestionRunStore>(_ => new SqlIngestionRunStore(options.OpportunitiesDb));
        services.AddSingleton<IIngestionTriggerStore>(sp => new SqlIngestionTriggerStore(
            options.OpportunitiesDb,
            sp.GetRequiredService<ILogger<SqlIngestionTriggerStore>>()));

        // Phase 2B: WPF feature window. Both transient so a Close+reopen cycle
        // gets a fresh VM (no stale data from a previous session). Mirrors the
        // FileSync Command Center registration in AppModule.cs.
        services.AddTransient<OpportunitiesViewModel>();
        services.AddTransient<OpportunitiesWindow>();
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
        services.AddSingleton<Kor.Opportunities.Data.Awards.IKorClientBdIntelligenceStore>(
            _ => new Kor.Opportunities.Data.Awards.SqlKorClientBdIntelligenceStore(options.OpportunitiesDb));
        services.AddSingleton<Kor.Opportunities.Data.Awards.CanonicalOrgResolver>();
        services.AddSingleton<Kor.Operations.App.Opportunities.CustomProposalImportService>();
        services.AddTransient<Kor.Operations.App.Opportunities.CompetitionInfoViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.HistoricalOpportunityDetailViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.KorPursuitDialogViewModel>();
        services.AddTransient<Kor.Operations.App.Opportunities.CompetitionInfoWindow>();
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

        services.AddTransient<CrmViewModel>();
        services.AddTransient<CrmWindow>();
        services.AddTransient<CrmEngagementDialog>();
        services.AddTransient<ClientIntelligenceViewModel>();
        services.AddTransient<ClientIntelligenceWindow>();

        return services;
    }
}
