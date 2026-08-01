#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Core.Models;

public sealed record VendorSiteExtractionPayload
{
    public IReadOnlyList<PortfolioItem> Portfolio { get; init; } = Array.Empty<PortfolioItem>();
    public IReadOnlyList<string> SpecificServices { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> SectorFocus { get; init; } = Array.Empty<string>();
    public IReadOnlyList<OpenPosition> OpenPositions { get; init; } = Array.Empty<OpenPosition>();
    public IReadOnlyList<LeadershipBio> LeadershipDetail { get; init; } = Array.Empty<LeadershipBio>();
    public string? BondingCapacity { get; init; }
    public string? Tagline { get; init; }
}

public sealed record PortfolioItem(
    string ProjectName,
    string? Client,
    string? Location,
    int? Year,
    string? Value,
    string? Summary);

public sealed record OpenPosition(string Title, string? Location, string? Discipline);

public sealed record LeadershipBio(
    string Name,
    string? Title,
    string? Background,
    bool? PEng,
    int? JoinedYear);

public sealed record PendingExtractionRow(
    long CrawlId,
    string VendorWebsite,
    string RawCaptureJson,
    int Attempts);
