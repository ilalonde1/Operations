#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Core.Models;

public sealed record PageCapture(string Url, string? Title, string Text);

public sealed record RawSiteCapture(
    IReadOnlyList<PageCapture> Pages,
    IReadOnlyList<string> Discovered);
