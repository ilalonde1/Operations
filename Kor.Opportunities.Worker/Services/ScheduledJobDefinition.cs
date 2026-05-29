#nullable enable
using System;
using System.Collections.Generic;
using Kor.Opportunities.Worker.Options;

namespace Kor.Opportunities.Worker.Services;

internal sealed record ScheduledJobDefinition(
    string JobName,
    string CronConfigKey,
    string DefaultCron,
    Func<OpportunitiesWorkerOptions, bool> Enabled);

internal static class ScheduledJobDefinitions
{
    public static IReadOnlyList<ScheduledJobDefinition> All { get; } =
    [
        new(nameof(AwardAgentEnrichmentJob), "AwardAgentEnrichmentCronSchedule", "0 7 * * * ?", o => o.AwardAgentEnrichmentEnabled),
        new(nameof(VendorSiteCrawlJob), "VendorSiteCrawlCronSchedule", "0 5/15 * * * ?", o => o.VendorSiteCrawlEnabled),
        new(nameof(VendorSiteExtractionJob), "VendorSiteExtractionCronSchedule", "0 2/5 * * * ?", o => o.VendorSiteExtractionEnabled),
        new(nameof(EnrichmentDispatchJob), "EnrichmentDispatchCronSchedule", "0 9/10 * * * ?", o => o.EnrichmentDispatchEnabled),
        new(nameof(NewsFeedPollJob), "NewsFeedPollCronSchedule", "0 12/30 * * * ?", o => o.NewsFeedPollEnabled),
        new(nameof(NewsMentionClassifyJob), "NewsClassificationCronSchedule", "0 3/5 * * * ?", o => o.NewsClassificationEnabled),
        new(nameof(BuildingPermitsImportJob), "BuildingPermitsCronSchedule", "0 30 6 * * ?", o => o.BuildingPermitsImportEnabled),
        new(nameof(DataRetirementJob), "DataRetirementCronSchedule", "0 30 4 * * ?", o => o.DataRetirementEnabled),
        new(nameof(CanonicalOrgKorProjectSignalRefreshJob), "CanonicalOrgKorProjectSignalRefreshCronSchedule", "0 0 5 * * ?", o => o.CanonicalOrgKorProjectSignalRefreshEnabled),
        new(nameof(KorPursuitDeltekSyncJob), "KorPursuitDeltekSyncCronSchedule", "0 30 5 * * ?", o => o.KorPursuitDeltekSyncEnabled),
        new(nameof(AbMajorProjectsInventoryJob), "AbMajorProjectsInventoryCronSchedule", "0 30 3 ? * SUN", _ => true),
        new(nameof(BcBidHistoricalDocumentDownloadJob), "BcBidHistoricalDocumentCronSchedule", "0 2/10 * * * ?", _ => true),
        new(nameof(CanadaBuysIngestionJob), "CanadaBuysCronSchedule", "0 0 0/2 * * ?", _ => true),
        new(nameof(CanadaBuysNewIngestionJob), "CanadaBuysNewCronSchedule", "0 15 0/2 * * ?", _ => true),
        new(nameof(SamGovIngestionJob), "SamGovCronSchedule", "0 0 6 * * ?", _ => true),
        new(nameof(GraphEmailIngestionJob), "GraphEmailCronSchedule", "0 0/15 * * * ?", _ => true),
        new(nameof(BcBidHistoricalEnrichmentJob), "BcBidHistoricalEnrichmentCronSchedule", "0 */5 * * * ?", _ => true),
    ];
}
