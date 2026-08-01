#nullable enable
using System.Collections.Generic;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>
    /// Rough rebar weight for one element type, both issues.
    /// Weight = density (kg/m³) x concrete volume (m³). The density is a standard reinforcing
    /// ratio, scaled per issue by the reinforcing INTENSITY extracted from that issue's own
    /// call-outs - so the before/after tonnage actually moves when the detailing changes.
    /// </summary>
    public sealed record RebarWeightLine(
        string Element,
        double StdDensityKgM3,
        double DensityBeforeKgM3,
        double VolBeforeM3,
        double TonnesBefore,
        double DensityAfterKgM3,
        double VolAfterM3,
        double TonnesAfter,
        double DeltaTonnes,
        string IntensityNote,
        string Corroboration);

    public sealed record RebarWeightResult(
        IReadOnlyList<RebarWeightLine> Lines,
        double TotalBefore,
        double TotalAfter,
        double TotalDelta,
        string BeforeLabel,
        string AfterLabel);
}
