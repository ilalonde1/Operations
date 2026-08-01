#nullable enable
namespace Kor.Opportunities.Data.Intel;

public sealed class DefaultIntelExtractor : IIntelExtractor
{
    public string ProviderName => "*";

    public ExtractedIntel Extract(IntelExtractionContext ctx)
    {
        System.Diagnostics.Trace.TraceInformation(
            "Intel extractor missing for provider {0}; row archived only.",
            ctx.ProviderName);

        return ExtractedIntel.Empty;
    }
}
