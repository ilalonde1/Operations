#nullable enable
using System;
using System.Collections.Generic;

namespace Kor.Opportunities.Core.Models;

public sealed record VendorSiteCrawlRow(
    long Id,
    string VendorWebsite,
    string Status,
    DateTimeOffset? CrawledAtUtc,
    DateTimeOffset? LastAttemptAtUtc,
    int Attempts,
    string? ErrorMessage);

public sealed record PageCapture(string Url, string? Title, string Text);

public sealed record RawSiteCapture(
    IReadOnlyList<PageCapture> Pages,
    IReadOnlyList<string> Discovered);
