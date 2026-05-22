#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Core.Models;

namespace Kor.Opportunities.Data.Awards;

public sealed record AwardQueryFilter
{
    public string? KeywordLike { get; init; }
    public string? VendorLike { get; init; }
    public int? Year { get; init; }
    public string? SourceName { get; init; }
    public decimal? MinContractValue { get; init; }
    public int? MaxRows { get; init; }
    public bool? CompetesWithKorOnly { get; init; }
}

public interface IAwardQueryStore
{
    Task<IReadOnlyList<AwardListing>> ListAsync(AwardQueryFilter filter, CancellationToken ct);
    Task<AwardQueryFacets> GetFacetsAsync(CancellationToken ct);
}

public sealed record AwardQueryFacets
{
    public IReadOnlyList<int> Years { get; init; } = Array.Empty<int>();
    public IReadOnlyList<string> SourceNames { get; init; } = Array.Empty<string>();
}
