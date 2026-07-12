#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Playwright;

namespace Kor.Opportunities.Data.Ingestion.Scraping;

public sealed record DetailLink(string Text, string Href);

public sealed record DetailDocument(string Name, string Url);

/// <summary>Structured result of reading a live opportunity detail page. All
/// fields optional — a page may expose some and not others.</summary>
public sealed record LiveDetailResult(
    IReadOnlyList<string> CommodityCodes,
    string? Description,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    IReadOnlyList<DetailDocument> Documents);

/// <summary>
/// A per-portal detail-page reader. The generic LiveOppDetailEnrichmentJob owns
/// the loop (pull URL from the observation, fill-only persist, mark attempted);
/// each implementation only knows how to log into and parse ONE portal's DOM.
/// Adding a source = one new implementation + a DI registration. Nothing in the
/// job changes. This is what makes enrichment genuinely source-agnostic.
/// </summary>
public interface ILiveOppDetailExtractor
{
    /// <summary>Portal tag for logging/SourcePortal, e.g. "BCBID", "BIDSTENDERS".</summary>
    string Name { get; }

    /// <summary>SQL LIKE pattern matched against the observation Url to select this
    /// extractor's targets, e.g. "%bcbid.gov.bc.ca%" / "%bidsandtenders.ca%".</summary>
    string UrlHostLike { get; }

    bool RequiresLogin { get; }

    /// <summary>False when this extractor can't run (e.g. login creds missing) —
    /// the job skips it instead of burning navigations on a login wall.</summary>
    bool IsAvailable { get; }

    Task LoginAsync(IPage page, CancellationToken ct);

    /// <summary>Read the detail page. Returns null on a hard failure (the job still
    /// marks the opp attempted so it is never re-queued).</summary>
    Task<LiveDetailResult?> ExtractAsync(IPage page, string detailUrl, CancellationToken ct);
}
