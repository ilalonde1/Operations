#nullable enable
using System.Collections.Generic;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// One strategic compounding-relationship target (BD-UI-Plan Phase B items
/// 4+8), ported from tools/BdReportBuilders/build-strategic-relationships.ps1.
/// Config prose captured at the last honing sweep; the pipeline project list
/// is pulled live by matching <see cref="LikePattern"/> against honed briefs.
/// </summary>
/// <param name="LikePattern">
/// Substring matched (escaped) against ProjectBriefHoning ResultJson to pull
/// the target's live project sample — the builder's own lookup recipe.
/// </param>
public sealed record StrategicTargetDefinition(
    string Name,
    string Type,
    string Why,
    IReadOnlyList<string> Contacts,
    string LikePattern,
    string Angle,
    IReadOnlyList<string> Timeline);
