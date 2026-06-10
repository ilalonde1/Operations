#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Ten-target strategic relationships deep-dive (BD-UI-Plan Phase B items
/// 4+8), structure mirrored from build-strategic-relationships.ps1: per
/// target — type, why-it-matters, named contacts, KOR angle, 12-month plan
/// (config) + a live pipeline project sample from honed briefs.
/// </summary>
public static class StrategicRelationshipsReportGenerator
{
    public static BdReportDocument Build(
        IReadOnlyList<(StrategicTargetDefinition Definition, IReadOnlyList<string> Projects)> targets,
        DateTimeOffset generatedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(targets);

        var b = new BdReportDocumentBuilder("KOR Structural — Strategic Relationships Deep-Dive");

        b.Italic(
            $"{targets.Count} named compounding relationships — one relationship = N projects across multi-year, multi-sector pipelines. " +
            "Each target has named contacts (where known), KOR's positioning angle, and a 12-month engagement plan. " +
            "The action sheet for the next 30-60 days of partner-level outreach. " +
            $"Generated {generatedAtUtc:yyyy-MM-dd HH:mm} UTC from live KorOpportunitiesDb.");

        foreach (var (def, projects) in targets)
        {
            b.H2(def.Name);
            b.Italic($"Type: {def.Type}");
            b.B("Why this matters: ", def.Why);
            b.P("Named contacts to engage:");
            foreach (var contact in def.Contacts)
            {
                b.P($"  - {contact}");
            }

            b.P("KOR angle:");
            b.P($"  {def.Angle}");
            b.P("12-month engagement plan:");
            foreach (var step in def.Timeline)
            {
                b.P($"  - {step}");
            }

            b.P("Projects in pipeline (sample, from honed briefs):");
            if (projects.Count > 0)
            {
                foreach (var p in projects.Take(10))
                {
                    b.P($"  - {p}");
                }
            }
            else
            {
                b.P("  (No direct keyword match — see deep-dive briefs / Org Dossier in app)");
            }

            b.P(string.Empty);
        }

        b.H2($"Summary — {targets.Count} strategic targets");
        b.Table(
            new[] { "Target", "Type", "Live pipeline matches" },
            targets.Select(t => (IReadOnlyList<string>)new[]
            {
                t.Definition.Name,
                t.Definition.Type,
                t.Projects.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
            }).ToList());
        b.P("Every target compounds: the relationship cost is one outreach campaign; the return is standing access to a multi-project pipeline. Track engagement actions in the BD Dashboard Priority Actions queue.");

        return b.Build();
    }
}
