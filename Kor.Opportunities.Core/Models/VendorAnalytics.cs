#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Core.Models;

public sealed record YearBucket(int Year, decimal TotalValue, int ContractCount);

public sealed record OrgRollup(string Name, decimal TotalValue, int ContractCount);

public sealed record CompetitorProfile
{
    public string VendorName { get; init; } = "";
    public decimal LifetimeValue { get; init; }
    public int LifetimeCount { get; init; }
    public decimal? AvgContractValue { get; init; }
    public DateTimeOffset? FirstWinAtUtc { get; init; }
    public DateTimeOffset? LastWinAtUtc { get; init; }
    public IReadOnlyList<YearBucket> ByYear { get; init; } = Array.Empty<YearBucket>();
    public IReadOnlyList<OrgRollup> TopBuyers { get; init; } = Array.Empty<OrgRollup>();
    public IReadOnlyList<OrgRollup> BySource { get; init; } = Array.Empty<OrgRollup>();
    public IReadOnlyList<AwardListing> RecentWins { get; init; } = Array.Empty<AwardListing>();
}

public sealed record BuyerProfile
{
    public string BuyerName { get; init; } = "";
    public decimal LifetimeValue { get; init; }
    public int LifetimeCount { get; init; }
    public decimal? AvgContractValue { get; init; }
    public DateTimeOffset? FirstAwardAtUtc { get; init; }
    public DateTimeOffset? LastAwardAtUtc { get; init; }
    public IReadOnlyList<YearBucket> ByYear { get; init; } = Array.Empty<YearBucket>();
    public IReadOnlyList<OrgRollup> TopWinners { get; init; } = Array.Empty<OrgRollup>();
    public IReadOnlyList<OrgRollup> BySource { get; init; } = Array.Empty<OrgRollup>();
    public IReadOnlyList<AwardListing> RecentAwards { get; init; } = Array.Empty<AwardListing>();
}
