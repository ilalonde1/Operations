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
    public string? AgentVendorProfile { get; init; }
    public string? AgentCompetitionNotes { get; init; }
    public bool? AgentCompetesWithKor { get; init; }
    public DateTimeOffset? AgentEnrichedAtUtc { get; init; }
    public string? AgentVendorWebsite { get; init; }
    public string? AgentVendorHqLocation { get; init; }
    public string? AgentVendorSizeBand { get; init; }
    public int? AgentVendorFoundedYear { get; init; }
    public IReadOnlyList<string> AgentVendorSpecialties { get; init; } = Array.Empty<string>();
    public IReadOnlyList<VendorLeader> AgentVendorLeadership { get; init; } = Array.Empty<VendorLeader>();
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
