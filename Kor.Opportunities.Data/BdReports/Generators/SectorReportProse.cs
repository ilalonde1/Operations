#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Per-sector framing prose, ported verbatim from the shipped PowerShell
/// builders in tools/BdReportBuilders (BD-UI-Plan parity note: read each
/// builder for section structure and framing prose). The verdict DATA is
/// always live from IBdReportService; this prose is the analyst framing
/// captured at the last honing pass and is refreshed when a sector re-hones.
/// </summary>
public sealed record SectorReportProse(
    string IntroNote,
    IReadOnlyList<string> SummaryNarrative,
    IReadOnlyList<SynthesisSection> Synthesis,
    IReadOnlyList<string> RecommendedActions);

/// <summary>An H3 subsection under "Strategic Synthesis".</summary>
public sealed record SynthesisSection(string Title, IReadOnlyList<string> Paragraphs);
