#nullable enable
using System.Text.Json;

namespace Kor.Opportunities.Data.Intel;

public interface IProjectIntelExtractor
{
    string ProviderName { get; }

    ExtractedProjectIntel Extract(ProjectIntelExtractionContext ctx);
}

public sealed record ProjectIntelExtractionContext(
    long MajorProjectsInventoryId,
    long SourceEnrichmentId,
    string ProviderName,
    JsonDocument ResultJson,
    DateTimeOffset RefreshedAtUtc);

public sealed class DefaultProjectIntelExtractor : IProjectIntelExtractor
{
    public string ProviderName => "__default__";

    public ExtractedProjectIntel Extract(ProjectIntelExtractionContext _) =>
        ExtractedProjectIntel.Empty;
}

public sealed class ProjectIntelExtractorRegistry
{
    private readonly IReadOnlyDictionary<string, IProjectIntelExtractor> _byProvider;
    private readonly IProjectIntelExtractor _default;

    public ProjectIntelExtractorRegistry(IEnumerable<IProjectIntelExtractor> extractors, DefaultProjectIntelExtractor fallback)
    {
        _byProvider = extractors
            .Where(e => !ReferenceEquals(e, fallback))
            .ToDictionary(e => e.ProviderName, e => e, StringComparer.Ordinal);
        _default = fallback;
    }

    public IProjectIntelExtractor Resolve(string providerName) =>
        _byProvider.TryGetValue(providerName, out var x) ? x : _default;
}
