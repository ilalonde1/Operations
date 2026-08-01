#nullable enable

namespace Kor.Opportunities.Data.Ingestion;

public sealed record BareBdTargetRow(
    string Kind,
    long Id,
    string DisplayName,
    long MpiRefs);
