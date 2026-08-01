#nullable enable
using System;
using System.Collections.Generic;
using Kor.Opportunities.Worker.Options;

namespace Kor.Opportunities.Worker.Services;

internal sealed record ScheduledJobDefinition(
    string JobName,
    string CronConfigKey,
    string DefaultCron,
    Func<OpportunitiesWorkerOptions, bool> Enabled,
    string Category);

internal static class ScheduledJobDefinitions
{
    public static IReadOnlyList<ScheduledJobDefinition> All { get; } =
    [
        new(nameof(AwardAgentEnrichmentJob), "AwardAgentEnrichmentCronSchedule", "0 7 * * * ?", o => o.AwardAgentEnrichmentEnabled, "Enrichment"),
        new(nameof(VendorSiteCrawlJob), "VendorSiteCrawlCronSchedule", "0 5/15 * * * ?", o => o.VendorSiteCrawlEnabled, "Enrichment"),
        new(nameof(VendorSiteExtractionJob), "VendorSiteExtractionCronSchedule", "0 2/5 * * * ?", o => o.VendorSiteExtractionEnabled, "Enrichment"),
        new(nameof(EnrichmentDispatchJob), "EnrichmentDispatchCronSchedule", "0 9/10 * * * ?", o => o.EnrichmentDispatchEnabled, "Enrichment"),
        new(nameof(NewsFeedPollJob), "NewsFeedPollCronSchedule", "0 12/30 * * * ?", o => o.NewsFeedPollEnabled, "Ingestion"),
        new(nameof(NewsMentionClassifyJob), "NewsClassificationCronSchedule", "0 3/5 * * * ?", o => o.NewsClassificationEnabled, "Enrichment"),
        new(nameof(BuildingPermitsImportJob), "BuildingPermitsCronSchedule", "0 30 6 * * ?", o => o.BuildingPermitsImportEnabled, "Ingestion"),
        new(nameof(DataRetirementJob), "DataRetirementCronSchedule", "0 30 4 * * ?", o => o.DataRetirementEnabled, "Cleanup"),
        new(nameof(BdDeltekLinkDryRunJob), "BdDeltekLinkDryRunCronSchedule", "0 0 4 * * ?", o => o.BdDeltekLinkDryRunEnabled, "Sync"),
        // 2026-06-12: runs the ported nightly-refresh trigger at 21:30 (the
        // cadence of the removed dev-box scheduled task) so batches are ready
        // for the evening's drain sessions.
        new(nameof(BdResearchQueueBuilderJob), "BdResearchQueueBuilderCronSchedule", "0 30 21 * * ?", o => o.BdResearchQueueEnabled, "Workflow"),
        new(nameof(DataHealthAuditJob), "DataHealthAuditCronSchedule", "0 0 7 ? * SUN", o => o.DataHealthAuditEnabled, "Audit"),
        new(nameof(CanonicalOrgKorProjectSignalRefreshJob), "CanonicalOrgKorProjectSignalRefreshCronSchedule", "0 0 5 * * ?", o => o.CanonicalOrgKorProjectSignalRefreshEnabled, "Enrichment"),
        new(nameof(KorPursuitDeltekSyncJob), "KorPursuitDeltekSyncCronSchedule", "0 30 5 * * ?", o => o.KorPursuitDeltekSyncEnabled, "Sync"),
        new(nameof(AwardProgramFinderJob), "AwardProgramFinderCronSchedule", "0 0 5 ? * MON", o => o.AwardProgramFinderEnabled, "Ingestion"),
        new(nameof(AbMajorProjectsInventoryJob), "AbMajorProjectsInventoryCronSchedule", "0 30 3 ? * SUN", _ => true, "Ingestion"),
        new(nameof(BcMajorProjectsInventoryJob), "BcMajorProjectsInventoryCronSchedule", "0 0 4 ? * SUN", _ => true, "Ingestion"),
        new(nameof(CaMajorProjectsInventoryJob), "CaMajorProjectsInventoryCronSchedule", "0 30 4 ? * SUN", _ => true, "Ingestion"),
        new(nameof(BcBidHistoricalDocumentDownloadJob), "BcBidHistoricalDocumentCronSchedule", "0 2/10 * * * ?", _ => true, "Ingestion"),
        new(nameof(CanadaBuysIngestionJob), "CanadaBuysCronSchedule", "0 0 0/2 * * ?", _ => true, "Ingestion"),
        new(nameof(CanadaBuysNewIngestionJob), "CanadaBuysNewCronSchedule", "0 15 0/2 * * ?", _ => true, "Ingestion"),
        new(nameof(SamGovIngestionJob), "SamGovCronSchedule", "0 0 6 * * ?", _ => true, "Ingestion"),
        new(nameof(GraphEmailIngestionJob), "GraphEmailCronSchedule", "0 0/15 * * * ?", _ => true, "Ingestion"),
        new(nameof(BcBidHistoricalEnrichmentJob), "BcBidHistoricalEnrichmentCronSchedule", "0 */5 * * * ?", _ => true, "Enrichment"),
        new(nameof(JobTriggerPollerJob), "JobTriggerPollerCronSchedule", "0/15 * * * * ?", o => o.JobTriggerPollerEnabled, "Maintenance"),
        // Round 61b (2026-06-03): continuous APC detail-page enrichment.
        // Default cadence: 30 minutes (matches APC list-scrape pace + leaves
        // breathing room for the source query to find new unenriched opps).
        new(nameof(ApcInterestEnrichmentJob), "ApcInterestEnrichmentCronSchedule", "0 17 0/1 * * ?", o => o.ApcInterestEnrichmentEnabled, "Enrichment"),
        // 2026-06-11: daily 6am BD system-status email to Ian.
        new(nameof(Reporting.BdMorningReportJob), "MorningReportCronSchedule", "0 0 6 * * ?", o => o.MorningReportEnabled, "Reporting"),
        // Audit-v2 #15: these ran real schedules but were invisible in the
        // Admin job registry — an operator couldn't see, disable, or reschedule them.
        // (BcBidPlanTakerEnrichmentJob caught by DoctrineTests D3 on first run.)
        new(nameof(CanonicalLinkBackfillJob), "CanonicalLinkBackfillCronSchedule", "0 0 5 ? * SUN", o => o.CanonicalLinkBackfillEnabled, "Cleanup"),
        new(nameof(Jobs.IntelRetirementJob), "IntelRetirementCronSchedule", "0 0 2 * * ?", _ => true, "Cleanup"),
        new(nameof(BcBidPlanTakerEnrichmentJob), "BcBidPlanTakerEnrichmentCronSchedule", "0 43 0/2 * * ?", o => o.BcBidPlanTakerEnrichmentEnabled, "Enrichment"),
        // 2026-07-13: Monday 06:30 Weekly Attack Sheet email (fresh-at-send PDF).
        new(nameof(Reporting.WeeklyAttackSheetJob), "WeeklyAttackSheetCronSchedule", "0 30 6 ? * MON", o => o.WeeklyAttackSheetEnabled, "Reporting"),
        // 2026-07-13: pursuit lifecycle — auto-release owned-but-unconverted plays.
        new(nameof(Jobs.MpiOwnershipReaperJob), "MpiOwnershipReaperCronSchedule", "0 40 2 * * ?", o => o.MpiOwnershipReaperEnabled, "Cleanup"),
        new(nameof(LiveOppDetailEnrichmentJob), "LiveOppDetailEnrichmentCronSchedule", "0 7 0/1 * * ?", o => o.LiveOppDetailEnrichmentEnabled, "Enrichment"),
    ];
}
