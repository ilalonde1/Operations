using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.RebarChange;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// THE SUMMARY'S TONNES ARE THE WEIGHT SHEET'S OWN ARITHMETIC (2026-10-21). The workbook writes each issue's density as a
/// whole kg/m³ in an editable cell and computes tonnes from it; the Summary page printed tonnes from the unrounded
/// density. On 31065 IFT -> IFC the slab density stood either side of 120 (both cells showed 120), so the Summary said
/// 2,057 -> 2,041 t (-16.2) while the sheet below it added up to 2,052 -> 2,047 t (-4.8).
/// WHAT THIS COVERS: RebarWeightEstimator.Estimate - every line's tonnes are its whole-number density x its volume to
/// 0.1 m³, and the totals are the sum of those lines. WHAT IT DOES NOT: the workbook's formulas themselves (ClosedXML is
/// not evaluated here), or whether the intensity scaling is the right model. A same-class fault it would NOT catch: a
/// new Summary line computed from some other figure than the result's totals.
/// </summary>
public sealed class TheSummaryTonnesAreTheWeightSheetsOwnArithmeticTests
{
    [Fact]
    public void ADensityEitherSideOfAWholeNumberMovesNoTonnes()
    {
        var vol = new Dictionary<string, double> { ["Slab"] = 10135.04 };
        var w = RebarWeightEstimator.Estimate(vol, vol, intensityBefore: new Dictionary<string, double> { ["Slab"] = 16.05 },
                                              intensityAfter: new Dictionary<string, double> { ["Slab"] = 15.95 });

        var slab = w.Lines.Single(l => l.Element == "Slab");
        Assert.Equal(120, slab.DensityBeforeKgM3);
        Assert.Equal(120, slab.DensityAfterKgM3);
        Assert.Equal(0, slab.DeltaTonnes);
        foreach (var l in w.Lines)
        {
            Assert.Equal(l.DensityBeforeKgM3 * l.VolBeforeM3 / 1000.0, l.TonnesBefore, 9);
            Assert.Equal(System.Math.Round(l.DensityBeforeKgM3), l.DensityBeforeKgM3);
            Assert.Equal(System.Math.Round(l.VolBeforeM3, 1), l.VolBeforeM3);
        }
        Assert.Equal(w.Lines.Sum(l => l.TonnesAfter) - w.Lines.Sum(l => l.TonnesBefore), w.TotalDelta, 9);
    }
}
