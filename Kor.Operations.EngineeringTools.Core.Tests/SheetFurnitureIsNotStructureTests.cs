#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;
using GP = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.GeomPath;
using PC = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.PageContent;
using TT = Kor.Operations.EngineeringTools.QuantityTakeoff.VectorPageReader.TextToken;

namespace Kor.Operations.EngineeringTools.Core.Tests;

/// <summary>
/// What a sheet draws that is not the structure, and the four statements that keep it out of a
/// model: a paper-coloured unstroked fill draws nothing; the frame is the size of the paper; a
/// schedule's border holds table furniture; the title block is the strip beyond the long edge
/// nearest the sheet number.
/// </summary>
/// <remarks>
/// Each was measured before it was written. On the five KOR jobs' schedule pages every closed
/// shape whose size matched a declared column was grey-filled and unstroked, and the paper-filled
/// ones matched none: 192 of 31168 p11's 266 "columns" were 36" and 46" white squares around each
/// real one (over-detection 3.6x → 0.9x with coverage unchanged). Schedule tables put their tie
/// sketches, cell boxes and rules into every DXF as columns, slabs and beams; 31130 p11's slabs
/// went 65 → 28 when the borders were excluded.
///
/// WHAT THIS COVERS: each rule on a synthetic page, in isolation, and that a grey column beside
/// each excluded thing survives it.
///
/// WHAT IT DOES NOT: notes, legends and key plans that are not ruled tables — they still read as
/// lines; a sheet with no readable sheet number, which keeps its title block; and whether the
/// numbers on the real sheets hold, which is tools/Measure-StickFileSchedules.ps1's job. A fault
/// it would NOT catch: a real column drawn with a white fill and NO stroke, which would vanish
/// with the invisible ink — none of the five jobs draws one, and the invariant would be the first
/// thing to move if a practice did.
/// </remarks>
public sealed class SheetFurnitureIsNotStructureTests
{
    private const double W = 3024, H = 2160;
    private const double Scale = 96 * 25.4 / 72.0;   // mm per point at 1:96

    private static readonly (byte, byte, byte) Grey = (0xD0, 0xD0, 0xD0);
    private static readonly (byte, byte, byte) Paper = (0xF0, 0xF0, 0xF0);
    private static readonly (byte, byte, byte) Black = (0, 0, 0);

    private static RawSubpath Rect(double x0, double y0, double x1, double y1, (byte, byte, byte) fill, bool stroked = false, bool filled = true) =>
        new(new List<(double X, double Y)> { (x0 * Scale, y0 * Scale), (x1 * Scale, y0 * Scale), (x1 * Scale, y1 * Scale), (x0 * Scale, y1 * Scale) },
            IsClosed: true, Color: fill, IsFilled: filled, IsStroked: stroked, LineWidth: 0.5, IsAnnotation: false);

    private static RawSubpath Line(double x0, double y0, double x1, double y1) =>
        new(new List<(double X, double Y)> { (x0 * Scale, y0 * Scale), (x1 * Scale, y1 * Scale) },
            IsClosed: false, Color: Black, IsFilled: false, IsStroked: true, LineWidth: 0.5, IsAnnotation: false);

    private static ExtractedGeometry Classify(IEnumerable<RawSubpath> subpaths, IReadOnlyList<SheetFurniture.Region>? furniture = null)
    {
        var result = new ExtractedGeometry { ScaleDenominator = 96 };
        GeometryFilterService.Classify(subpaths.ToList(), result,
            slabMinDiagonalMm: 3000, lineMinLengthMm: 500, excludeGridLines: false,
            pageWidthMm: W * Scale, pageHeightMm: H * Scale,
            annotationsOnly: false, furniture: furniture);
        return result;
    }

    // a 14" x 36" column is 12 x 31 points at 1:96
    private static RawSubpath Column(double x, double y) => Rect(x, y, x + 12, y + 31, Grey);

    [Fact]
    public void APaperColouredFillWithNoStrokeIsInvisibleInk()
    {
        // the real column, and the two white squares 31168 draws around every one of them
        var geo = Classify([
            Column(1000, 1000),
            Rect(985, 985, 1023, 1023, Paper),
            Rect(980, 980, 1028, 1028, Paper),
        ]);

        Assert.Single(geo.Columns);
    }

    [Fact]
    public void AGreyFillIsAColumnAndAWhiteFillWithAStrokeIsStillAShape()
    {
        var geo = Classify([
            Column(1000, 1000),
            Rect(1100, 1000, 1112, 1031, Paper, stroked: true),   // outlined, white inside: visible
        ]);

        Assert.Equal(2, geo.Columns.Count);
    }

    [Fact]
    public void TheSheetFrameIsNotASlabOrABeam()
    {
        var geo = Classify([
            Rect(30, 30, W - 30, H - 30, Black, stroked: true, filled: false),  // one closed frame
            Line(30, 30, W - 30, 30),                                            // or four strokes
            Line(30, 30, 30, H - 30),
            Column(1000, 1000),
        ]);

        Assert.Empty(geo.Slabs);
        Assert.Empty(geo.Lines);
        Assert.Single(geo.Columns);
    }

    [Fact]
    public void WhatSitsInsideAScheduleBorderOrTheTitleBlockIsNotStructure()
    {
        var furniture = new List<SheetFurniture.Region>
        {
            new("schedule: COLUMN SCHEDULE", 2000, 300, 2400, 600),
            new("title block", 2700, 0, W, H),
        }.Select(r => r.Scaled(Scale)).ToList();

        var geo = Classify([
            Column(1000, 1000),                       // on the plan
            Column(2100, 400),                        // a tie sketch inside the schedule
            Rect(2010, 310, 2390, 590, Black, stroked: true, filled: false),   // the table's box
            Line(2010, 450, 2390, 450),               // a row rule
            Column(2800, 500),                        // a logo box in the title block
            Line(2720, 800, 2980, 800),               // a title-block rule
        ], furniture);

        Assert.Single(geo.Columns);
        Assert.Empty(geo.Slabs);
        Assert.Empty(geo.Lines);
    }

    // ── the regions themselves, from a page ─────────────────────────────────────────────────

    private static TT Tok(string text, double x, double y, double h = 6) => new(text, x, y, x - 8, y - h / 2, x + 8, y + h / 2);

    private static GP HRule(double x0, double x1, double y) => new(
        new List<(double X, double Y)> { (x0, y), (x1, y) }, false, false, true, Math.Min(x0, x1), y, Math.Max(x0, x1), y);

    private static GP VRule(double x, double y0, double y1) => new(
        new List<(double X, double Y)> { (x, y0), (x, y1) }, false, false, true, x, Math.Min(y0, y1), x, Math.Max(y0, y1));

    [Fact]
    public void AScheduleAndItsTitleRowAreOneRegion()
    {
        var words = new List<TT>
        {
            Tok("COLUMN", 1520, 700), Tok("SCHEDULE", 1600, 700),
            Tok("MARK", 1520, 680), Tok("SIZE", 1600, 680),
        };
        var paths = new List<GP>
        {
            HRule(1495, 1990, 690), HRule(1495, 1990, 668), HRule(1495, 1990, 640),
            VRule(1495, 640, 690), VRule(1990, 640, 690), VRule(1570, 640, 690),
        };

        var region = Assert.Single(SheetFurniture.On(new PC(1, W, H, words, paths)));

        Assert.StartsWith("schedule", region.Kind);
        Assert.Equal(1495, region.MinX, 0.5);
        Assert.Equal(1990, region.MaxX, 0.5);
        Assert.Equal(640, region.MinY, 0.5);
        Assert.True(region.MaxY >= 703, "the title row above the top rule is part of the table");
    }

    /// <summary>
    /// A title block's side is drawn box by box: twenty short verticals at one x, none of them
    /// the length of the sheet, all of them together the sheet's edge.
    /// </summary>
    [Fact]
    public void TheTitleBlockIsTheStripBeyondTheEdgeNearestTheSheetNumber()
    {
        var words = new List<TT> { new("S2.02", 2900, 80, 2870, 70, 2930, 90) };   // large, bottom right
        var paths = new List<GP>();
        for (int i = 0; i < 20; i++)                     // the strip's left side, in 20 pieces with gaps
            paths.Add(VRule(2700, 40 + i * 105, 40 + i * 105 + 95));
        paths.Add(VRule(1500, 900, 1500));               // a grid line: short, and on the plan

        var region = Assert.Single(SheetFurniture.On(new PC(1, W, H, words, paths)));

        Assert.Equal("title block", region.Kind);
        Assert.Equal(2700, region.MinX, 0.5);
        Assert.Equal(W, region.MaxX, 0.5);
    }

    [Fact]
    public void AGridLineIsNotATitleBlockEdge()
    {
        // the long rule nearest the number cuts off half the sheet: that is the plan, not a strip
        var words = new List<TT> { new("S2.02", 2900, 80, 2870, 70, 2930, 90) };
        var paths = new List<GP> { VRule(1700, 100, 2000) };

        Assert.Empty(SheetFurniture.On(new PC(1, W, H, words, paths)));
    }

    [Fact]
    public void EdgesAreCoverageNotLength()
    {
        var pieces = new List<ScheduleTableBorder.Rule>
        {
            new(0, 400, 100.0), new(410, 800, 100.0), new(810, 1300, 100.2),   // one edge, 1,300 of 2,000
            new(0, 300, 500.0),                                                 // not an edge
        };

        var edges = SheetFurniture.Edges(pieces, minCover: 0.6 * 2000);

        Assert.Equal(100.0, Assert.Single(edges), 0.5);
    }
}
