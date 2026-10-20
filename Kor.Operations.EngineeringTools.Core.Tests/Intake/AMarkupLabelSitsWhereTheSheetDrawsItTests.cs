using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using UglyToad.PdfPig;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// A MARKUP LABEL SITS WHERE THE SHEET DRAWS IT, AT THE ANGLE THE SHEET DRAWS IT.
///
/// Omar's A105 came out with "4,436 sf" on the top edge of the floor (the sheet has it mid-corridor),
/// "14" wall" and the stair dimensions lying flat (the sheet turns them 90°), and ten "504 sq in"
/// column notes that Bluebeam keeps in the popup and never draws. The appearance stream is what the
/// sheet shows, so it decides. A note the sheet does not draw is kept on a layer that opens switched
/// off — Ian, 2026-10-19 — because Parcel 11 shipped its hidden notes as text.
///
/// Covers: which labels are drawn, the position and angle of three of them, the uniform height, and
/// the DXF's off layer and rotation. Does NOT cover: whether the text fits its gap in AutoCAD (that
/// depends on the font CAD substitutes), or any PDF whose appearance is not Bluebeam's.
/// </summary>
public sealed class AMarkupLabelSitsWhereTheSheetDrawsItTests
{
    private static readonly string OmarsPdf = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        "Omar", "143 E 3rd Hotel Architectural 2026-10-01 - KOR prelim markup.pdf");

    [Fact]
    public void OmarsA105LabelsAreWhereHisSheetDrawsThem()
    {
        // Missing is a failure, not a pass: a silent return is how the Parcel 11 gate went blind.
        Assert.True(File.Exists(OmarsPdf), $"fixture missing: {OmarsPdf}");

        double scale = 96 * PdfToSafeConstants.PointsToMm;
        using var doc = PdfDocument.Open(OmarsPdf);
        var labels = MarkupText.ExtractMarkupTextAnnotations(doc, doc.GetPage(6), scale);

        Assert.Equal(8, labels.Count(l => !l.HiddenOnSheet));
        Assert.Equal(10, labels.Count(l => l.HiddenOnSheet));
        Assert.All(labels.Where(l => l.HiddenOnSheet), l => Assert.Equal("504 sq in", l.Text));

        void At(TextAnnotation l, double x, double y, double angle)
        {
            Assert.InRange(l.LeftX / scale, x - 0.5, x + 0.5);
            Assert.InRange(l.BottomY / scale, y - 0.5, y + 0.5);
            Assert.InRange(l.RotationDeg, angle - 0.5, angle + 0.5);
        }
        At(Assert.Single(labels, l => l.Text == "4,436 sf"), 1687.67, 774.05, 0);
        At(Assert.Single(labels, l => l.Text == "14\" wall"), 1513.75, 689.93, 90);
        At(labels.Where(l => l.Text == "3'-0\"").OrderByDescending(l => l.BottomY).First(), 1662.96, 1173.76, 90);

        // A hidden note has no size on the sheet: 2.5 mm on paper. A drawn label takes the sheet's own
        // size — Bluebeam's Tf as a Helvetica capital height — and is fitted to its Helvetica width,
        // because at 2.5 mm in AutoCAD's wider font the stacked "3'-4"" and "3'-0"" ran together.
        double paper = PdfToSafeConstants.PaperTextHeightMm * scale / PdfToSafeConstants.PointsToMm;
        Assert.All(labels.Where(l => l.HiddenOnSheet), l => Assert.InRange(l.HeightMm, paper - 0.001, paper + 0.001));
        Assert.All(labels.Where(l => l.HiddenOnSheet), l => Assert.Equal(0.0, l.FitWidthMm));
        Assert.All(labels.Where(l => l.Text.EndsWith("\"") && l.Text.Contains('\'')),
            l => Assert.InRange(l.HeightMm / scale, 8 * 0.718 - 0.01, 8 * 0.718 + 0.01));
        Assert.InRange(Assert.Single(labels, l => l.Text == "4,436 sf").HeightMm / scale, 24 * 0.718 - 0.01, 24 * 0.718 + 0.01);
        Assert.All(labels.Where(l => !l.HiddenOnSheet), l => Assert.True(l.FitWidthMm > 0, l.Text));

        // The stacked stair pairs: each label ends before the next one starts, along the line.
        foreach (double near in new[] { 1173.76, 437.0 })
        {
            var pair = labels.Where(l => !l.HiddenOnSheet && l.RotationDeg > 89 && Math.Abs(l.LeftX / scale - 1662.96) < 1
                                         && Math.Abs(l.BottomY / scale - near) < 60)
                .OrderBy(l => l.BottomY).ToList();
            Assert.Equal(2, pair.Count);
            Assert.True(pair[0].BottomY + pair[0].FitWidthMm < pair[1].BottomY,
                $"{pair[0].Text} runs into {pair[1].Text}");
        }
    }

    [Fact]
    public void AHiddenNoteIsOnAnOffLayerAndARotatedLabelKeepsItsAngle()
    {
        var geometry = new ExtractedGeometry { IsVectorPdf = true, ScaleDenominator = 96 };
        geometry.Slabs.Add(new List<(double X, double Y)> { (0, 0), (10000, 0), (10000, 10000), (0, 10000) });
        geometry.SlabColors.Add((0x80, 0x00, 0x00));
        geometry.TextAnnotations = new List<TextAnnotation>
        {
            new("TURNED", 2000, 2000, 2000, 2000, 240, RotationDeg: 90, FitWidthMm: 1000),
            new("POPUP", 5000, 5000, 5000, 5000, 240, HiddenOnSheet: true),
        };

        var lines = DxfExporter.ExportLines(geometry);
        var groups = Enumerable.Range(0, lines.Count / 2)
            .Select(i => (Code: int.Parse(lines[2 * i], CultureInfo.InvariantCulture), Value: lines[2 * i + 1].Trim()))
            .ToList();
        Dictionary<int, string> Entity(string type, Func<Dictionary<int, string>, bool> match)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Code != 0 || groups[i].Value != type) continue;
                var d = new Dictionary<int, string>();
                for (int j = i + 1; j < groups.Count && groups[j].Code != 0; j++) d[groups[j].Code] = groups[j].Value;
                if (match(d)) return d;
            }
            throw new Xunit.Sdk.XunitException($"no {type} matched");
        }

        Assert.Equal("-7", Entity("LAYER", d => d.GetValueOrDefault(2) == "MARKUP-NOTES")[62]);
        Assert.Equal("MARKUP-NOTES", Entity("TEXT", d => d.GetValueOrDefault(1) == "POPUP")[8]);
        var turned = Entity("TEXT", d => d.GetValueOrDefault(1) == "TURNED");
        Assert.NotEqual("MARKUP-NOTES", turned[8]);
        Assert.Equal(90.0, double.Parse(turned[50], CultureInfo.InvariantCulture), 3);
        Assert.Equal("5", turned[72]);   // Fit: CAD's font cannot make it longer than the sheet set it
        Assert.Equal(double.Parse(turned[20], CultureInfo.InvariantCulture) + 1000,
                     double.Parse(turned[21], CultureInfo.InvariantCulture), 3);
        Assert.False(Entity("TEXT", d => d.GetValueOrDefault(1) == "POPUP").ContainsKey(72));
    }
}
