#nullable enable
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// The numbers that decide what a drawing's linework becomes are settings, and their defaults are
/// exactly what the code did before they were settings.
/// </summary>
/// <remarks>
/// Every defect found reading drawings this session was a convention compiled into a binary: a
/// metric-only size pattern that read one of five KOR jobs; a 2.5 aspect ratio that discarded 54
/// shapes at exactly 14x36 on one sheet because 31168 draws PC01 at 14" x 36"; a 260-point heading
/// scope that fit one sheet size and returned nothing for a larger one.
///
/// The DXF-to-ETABS side already had the answer — 62 `dxf.*` keys in KorStandards — and the PDF
/// intake path had none. These are its keys.
///
/// WHAT THIS COVERS: that every default equals the literal it replaced, so nothing moved by being
/// made settable; that a banked rule actually overrides it; and that a rule nobody has banked leaves
/// the default alone rather than throwing, because refusing to read a drawing over an unbanked
/// threshold would be worse than using the value the build already had.
///
/// WHAT IT DOES NOT COVER: whether any given value is RIGHT for a job. It cannot be — that is what
/// the setting is for. It also does not reach KorStandards; the plumbing is exercised, the database
/// is not.
/// </remarks>
public sealed class AJobCanStateHowItIsDrawnTests
{
    /// <summary>
    /// The literals as they stood before PdfIntakeOptions existed. If one of these has to change to
    /// make this pass, behaviour moved when it was supposed to be a pure lift.
    /// </summary>
    [Fact]
    public void EveryDefaultIsTheValueTheCodeAlreadyUsed()
    {
        var d = PdfIntakeOptions.Default;

        Assert.Equal(1000.0, d.SlabMinDiagonalMm);      // PdfToSafeConstants.DefaultSlabMinDiagonalMm
        Assert.Equal(200.0,  d.LineMinLengthMm);        // PdfToSafeConstants.DefaultLineMinLengthMm
        Assert.Equal(1500.0, d.ColumnMaxSizeMm);        // Classify's columnMaxSizeMm
        Assert.Equal(200.0,  d.ColumnMinDimMm);         // Classify's columnMinDimMm
        Assert.Equal(3.0,    d.ColumnMaxAspect);        // == banked dxf.max-column-aspect
        Assert.Equal(25.0,   d.AgreementToleranceMm);   // PlanAgreesWithItsSchedule.DefaultToleranceMm
        Assert.Equal(1500.0, d.AgreementLabelReachMm);  // PlanAgreesWithItsSchedule.DefaultLabelReachMm
    }

    /// <summary>The defaults must track the constants, not drift into a second copy of them.</summary>
    [Fact]
    public void TheDefaultsAreTakenFromTheConstantsRatherThanRestated()
    {
        Assert.Equal(PdfToSafeConstants.DefaultSlabMinDiagonalMm, PdfIntakeOptions.Default.SlabMinDiagonalMm);
        Assert.Equal(PdfToSafeConstants.DefaultLineMinLengthMm,   PdfIntakeOptions.Default.LineMinLengthMm);
        Assert.Equal(GeometryFilterService.DefaultMaxColumnAspect, PdfIntakeOptions.Default.ColumnMaxAspect);
    }

    private static RuleSetting Number(string key, double value)
        => new(key, value, "mm", "test", "test", "test");

    /// <summary>
    /// 31168's PC01 is 14" x 36", aspect 2.571 — the case that started this. A job drawing more
    /// slender columns states so, and the reader believes the job.
    /// </summary>
    [Fact]
    public void ABankedRuleOverridesTheDefault()
    {
        var settings = new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase)
        {
            [PdfIntakeOptions.SharedMaxColumnAspect] = Number(PdfIntakeOptions.SharedMaxColumnAspect, 4.5),
            ["dxf.pdf.column-min-dim-mm"] = Number("dxf.pdf.column-min-dim-mm", 120),
        };

        var applied = PdfIntakeOptions.ApplyRules(PdfIntakeOptions.Default, settings);

        Assert.Equal(4.5, applied.ColumnMaxAspect);
        Assert.Equal(120.0, applied.ColumnMinDimMm);
        // and everything unbanked is untouched
        Assert.Equal(PdfIntakeOptions.Default.SlabMinDiagonalMm, applied.SlabMinDiagonalMm);
    }

    /// <summary>
    /// An unbanked key must not throw. A missing rule stops a DXF-to-ETABS production run by design,
    /// because those numbers were argued over one at a time; these have not been, and refusing to
    /// read a drawing because nobody has banked a threshold yet is the worse failure.
    /// </summary>
    [Fact]
    public void AnUnbankedRuleLeavesTheDefaultAloneRatherThanThrowing()
    {
        var applied = PdfIntakeOptions.ApplyRules(
            PdfIntakeOptions.Default,
            new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase));

        Assert.Equal(PdfIntakeOptions.Default, applied);
    }

    [Fact]
    public void NoConnectionMeansDefaultsAndSaysSo()
    {
        var (options, source) = PdfIntakeOptions.For(null);

        Assert.Equal(PdfIntakeOptions.Default, options);
        Assert.Contains("defaults", source, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Every settable number is named in SettingKeys, or it cannot be banked.</summary>
    [Fact]
    public void EverySettingIsDiscoverableByItsKey()
    {
        Assert.Equal(7, PdfIntakeOptions.SettingKeys.Count);
        Assert.All(PdfIntakeOptions.SettingKeys,
            k => Assert.StartsWith("dxf.", k, StringComparison.Ordinal));
    }

    /// <summary>
    /// A column's SHAPE is shared with the DXF side; its SIZE WINDOW deliberately is not.
    /// </summary>
    /// <remarks>
    /// `dxf.min-column-size` and `dxf.max-column-size` exist and were adopted here, then measured and
    /// backed out: on the DXF side the LAYER says whether a shape is a column, so those bounds are a
    /// plausibility check; PdfToSafe has no layers, so the size window is the DISCRIMINATOR and the
    /// same numbers do a different job. Raising the ceiling to the banked 132in swallowed slabs whole
    /// — 31130 p13 45 to 23, 31138 p11 102 to 58 — with coverage identical on every sheet.
    ///
    /// If a future change makes this test fail by adopting them, that measurement has to be redone.
    /// </remarks>
    [Fact]
    public void TheColumnSizeWindowIsThisProjectsOwnAndTheAspectIsShared()
    {
        Assert.Contains("dxf.max-column-aspect", PdfIntakeOptions.SettingKeys);
        Assert.DoesNotContain("dxf.min-column-size", PdfIntakeOptions.SettingKeys);
        Assert.DoesNotContain("dxf.max-column-size", PdfIntakeOptions.SettingKeys);

        // and a banked DXF-side size bound must not move this project's window
        var settings = new Dictionary<string, RuleSetting>(StringComparer.OrdinalIgnoreCase)
        {
            ["dxf.max-column-size"] = Number("dxf.max-column-size", 132),
            ["dxf.min-column-size"] = Number("dxf.min-column-size", 6),
        };

        var applied = PdfIntakeOptions.ApplyRules(PdfIntakeOptions.Default, settings);

        Assert.Equal(PdfIntakeOptions.Default.ColumnMaxSizeMm, applied.ColumnMaxSizeMm);
        Assert.Equal(PdfIntakeOptions.Default.ColumnMinDimMm, applied.ColumnMinDimMm);
    }
}
