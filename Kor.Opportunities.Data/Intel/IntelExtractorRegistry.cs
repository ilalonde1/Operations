#nullable enable
namespace Kor.Opportunities.Data.Intel;

public sealed class IntelExtractorRegistry
{
    private readonly IReadOnlyDictionary<string, IIntelExtractor> _byProvider;
    private readonly IIntelExtractor _default;

    public IntelExtractorRegistry(IEnumerable<IIntelExtractor> extractors, DefaultIntelExtractor fallback)
    {
        _byProvider = extractors
            .Where(e => !ReferenceEquals(e, fallback))
            .ToDictionary(e => e.ProviderName, e => e, StringComparer.Ordinal);
        _default = fallback;
    }

    public IIntelExtractor Resolve(string providerName) =>
        _byProvider.TryGetValue(providerName, out var x) ? x : _default;
}
