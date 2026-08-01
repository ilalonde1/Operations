#nullable enable
namespace Kor.Opportunities.Data.Intel;

public interface IIntelExtractor
{
    string ProviderName { get; } // matches CanonicalOrgEnrichment.ProviderName exactly

    ExtractedIntel Extract(IntelExtractionContext ctx);
}

public sealed record IntelExtractionContext(
    long CanonicalOrgId,
    long SourceEnrichmentId,
    string ProviderName,
    System.Text.Json.JsonDocument ResultJson,
    DateTimeOffset RefreshedAtUtc);
