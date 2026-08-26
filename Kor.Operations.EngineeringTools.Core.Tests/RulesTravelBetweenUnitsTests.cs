using System.Reflection;
using Kor.Operations.EngineeringTools.Dxf;
using Xunit;
using Xunit.Abstractions;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// A rule stated in inches has to arrive as the same LENGTH in a drawing that counts in
/// something else.
///
/// InUnitOf converts each threshold by hand, and three were missed: FloodFillBridge on the
/// classifier, JointMergeTolerance and SelfTouchReportGap on the composer. A bridge of 36 inches
/// became 36 millimetres, and nothing anywhere said so — 31168 is drawn in inches, so the whole
/// portfolio this was measured on could not see it. An adversarial audit found it by reading the
/// method, which is the only way it could have been found.
///
/// Written by REFLECTION rather than by listing the properties, because a list is the thing that
/// went wrong. A new length option added to either record fails here until it is converted or
/// named below, which is the same shape as the required-rule gate.
/// </summary>
public class RulesTravelBetweenUnitsTests
{
    private readonly ITestOutputHelper _out;

    public RulesTravelBetweenUnitsTests(ITestOutputHelper output) => _out = output;

    /// <summary>
    /// Options that are numbers but not lengths, each with the reason. Being here is a claim that
    /// the value means the same thing whatever the drawing counts in.
    /// </summary>
    private static readonly Dictionary<string, string> NotALength = new(StringComparer.Ordinal)
    {
        ["MinWallAspect"] = "a ratio",
        ["MinPanelAspect"] = "a ratio",
        ["MaxColumnAspect"] = "a ratio",
        ["PierFillRatio"] = "a ratio",
        ["DoubledEdgeParallelRatio"] = "a ratio",
        ["DoubledEdgeCoverage"] = "a fraction of an area",
        ["MinFloorCoverage"] = "a fraction of an area",
        ["DonorPlateLikenessMargin"] = "a ratio",
        ["OffsetX"] = "a position in the drawing's own units, applied after conversion",
        ["OffsetY"] = "a position in the drawing's own units, applied after conversion",
        ["ModelUnitInInches"] = "what a unit MEASURES, not a length measured in one — scaling it " +
                               "would make the tag-thickness conversion wrong by the square",
        ["DefaultSlabThicknessInches"] = "stated in inches on purpose, for the report's wording",
    };

    public static TheoryData<string> Records => new() { "classification", "compose" };

    [Theory]
    [MemberData(nameof(Records))]
    public void EveryLengthRuleIsConvertedWithTheDrawingsUnit(string which)
    {
        const double mm = 25.4;

        object before = which == "classification"
            ? new PlanClassificationOptions()
            : new ComposeOptions();

        object after = which == "classification"
            ? ((PlanClassificationOptions)before).InUnitOf(mm)
            : ((ComposeOptions)before).InUnitOf(mm);

        var unconverted = new List<string>();

        foreach (var prop in before.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (prop.PropertyType != typeof(double)) continue;
            if (NotALength.ContainsKey(prop.Name)) continue;

            double a = (double)prop.GetValue(before)!;
            double b = (double)prop.GetValue(after)!;

            if (a == 0) continue;               // nothing to scale, and nothing to get wrong
            if (Math.Abs(a - b) < 1e-9) unconverted.Add($"{prop.Name} stayed {a:0.###}");
        }

        _out.WriteLine($"{which}: {unconverted.Count} unconverted length(s)");

        Assert.True(unconverted.Count == 0,
            $"These {which} rules are lengths and did not change when the drawing's unit did:\n  " +
            string.Join("\n  ", unconverted) +
            "\n\nConvert them in InUnitOf, or name them in NotALength with the reason they mean " +
            "the same number in every unit. A rule that silently keeps its inch value in a " +
            "millimetre drawing is off by a factor of 25.");
    }
}
