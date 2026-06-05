#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.Intel;

/// <summary>
/// Single source of truth for the canonical extractor set. Three previous
/// copies of this registration list lived in OpportunitiesModule (DI),
/// BdIntelExtract Program (CLI), and IntelExtractorTests (registry tests).
/// All three now call <see cref="GetDefaultExtractors"/> so adding a new
/// extractor requires updating one file.
///
/// The list maps 1:1 to live provider names — the constructor-parameterized
/// extractors (CanonicalSchemaExtractor, CompetitorProfileExtractor,
/// PersonListExtractor, MarketResearchExtractor, IndigenousResearchExtractor)
/// are instantiated once per provider they handle.
/// </summary>
public static class IntelExtractorBootstrap
{
    /// <summary>
    /// Returns one instance per supported provider, in stable order.
    /// Excludes <see cref="DefaultIntelExtractor"/> — that's the registry's
    /// fallback and is constructed separately.
    /// </summary>
    public static IReadOnlyList<IIntelExtractor> GetDefaultExtractors() => new IIntelExtractor[]
    {
        // Hard-coded ProviderName extractors
        new DataHoningExtractor(),
        new PublicSectorResearchExtractor(),
        new FirmNarrativeExtractor(),
        new ArchitectPipelineResearchExtractor(),
        new DeveloperPipelineResearchExtractor(),
        new StructuralPartnerMapExtractor(),
        new IncumbentRostersExtractor(),
        new InstitutionalOwnerResearchExtractor(),
        new PrimeTargetingExtractor(),
        new SubConsultantExtractor(),
        new CompetitorSignalsExtractor(),
        new MassTimberProjectsCatalogExtractor(),

        // Reusable-class extractors with constructor providerName
        new CompetitorProfileExtractor("CompetitorProfile"),
        new CompetitorProfileExtractor("ContractorResearch"),
        new CompetitorProfileExtractor("ProcurementProfile"),
        new CompetitorProfileExtractor("CompetitionResearch"),

        new CanonicalSchemaExtractor("PacNWMarketResearch"),
        new CanonicalSchemaExtractor("MassTimberResearch"),

        new PersonListExtractor("DecisionMakers"),
        new PersonListExtractor("PrimeContacts"),
        new PersonListExtractor("IndigenousDev"),

        new MarketResearchExtractor("AlbertaMarketResearch"),
        new MarketResearchExtractor("LAMarketResearch"),
        new MarketResearchExtractor("IslandOkanaganEcosystem"),

        new IndigenousResearchExtractor("IndigenousDevResearch"),
        new IndigenousResearchExtractor("IndigenousPartnerGraph"),
        new IndigenousResearchExtractor("DesignBuildContractors"),
    };
}
