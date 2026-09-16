using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.EngineeringTools;
using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using Xunit;

namespace Kor.Operations.EngineeringTools.Core.Tests.Intake;

/// <summary>
/// Token evidence from CODEX-PDF-INTAKE-TITLE-BLOCK-FIELDS.md, not drawing reads.
/// Assumptions where the brief omits boxes: labels 8.1 pt, horizontal titles 12.5 pt (30926)
/// or 13.8 pt (30912), other titles 13.8 pt; glyph widths half the font height per character.
/// Rotated labels have an 8.1 pt X thickness; rotated title words have a 13.8 pt X thickness.
/// Unreported X positions are synthetic column placements; all reported centres/baselines are kept.
/// </summary>
public class ATitleBlocksFieldsAreItsOwnTests
{
    /// <summary>
    /// 30878-02 p35. WHAT THIS COVERS: PROJ. # and DRAWING NUMBER end the roof title;
    /// the brief's companion office labels also bound a field. WHAT IT DOES NOT: reproduce
    /// the vertical revision/address lettering, whose full boxes were not supplied.
    /// </summary>
    [Fact]
    public void AnOfficeLabelEndsTheTitleBeforeTheNextFields()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.AddRange(Line("SHEET TITLE", 3160, 281.2, 8.1));
        var title = Line("ROOF PLAN", 3160, 253.9, 13.8)
            .Concat(Line("- BUILDING K", 3160, 232.2, 13.8)).ToList();
        words.AddRange(title);
        words.AddRange(Line("PROJ. #", 3160, 120.1, 8.1));
        words.AddRange(Line("30878-02", 3220, 120.1, 8.1));
        words.AddRange(Line("DRAWING NUMBER", 3290, 120.1, 8.1));
        words.AddRange(Line("SCALE 1/8\"=1'-0\"", 3160, 81.7, 8.1));
        words.AddRange(Line("S2.22", 3350, 79.0, 13.8));
        words.AddRange(Line("DRAWN K. FRANK", 3160, 62.4, 8.1));

        var fields = TitleBlockFields.Read(Page(3456, 2592, words), out var consumed);

        Assert.Equal("ROOF PLAN - BUILDING K", fields["SHEET TITLE"]);
        Assert.Equal("30878-02", fields["PROJ. #"]);
        Assert.All(title, t => Assert.Contains((t.Cx, t.Cy), consumed));

        foreach (string label in new[] { "DRAWING NO:", "DRAWING NUMBER", "DRAWING #", "PROJECT #",
                     "JOB NO.", "JOB NUMBER", "JOB TITLE", "PLOT DATE", "FILE", "SHEET" })
        {
            var bounded = words.Where(t => t.Cy > 120.1).Concat(Line(label, 3160, 120.1, 8.1))
                .Concat(Line("S2.22", 3160, 79.0, 13.8));
            Assert.Equal("ROOF PLAN - BUILDING K", TitleBlockFields.Read(Page(3456, 2592, bounded))["SHEET TITLE"]);
        }
    }

    /// <summary>
    /// 30912-01 p20. WHAT THIS COVERS: the right-edge 1 between CONCRETE and OUTLINE is a
    /// revision mark; the trailing dash stays on LEVEL -4 PLAN, DRAWING NO: stops the field,
    /// and an already-labelled neighbouring title column bounds CHECKED BY.
    /// WHAT IT DOES NOT: prove the unreported CHECKED BY coordinates (placed synthetically here).
    /// A final lone number is deliberately preserved, including at the right edge.
    /// </summary>
    [Fact]
    public void OnlyAnInteriorRightEdgeLoneDigitIsARevisionMark()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.AddRange(Line("TITLE:", 3150, 255.2, 8.1));
        words.AddRange(Line("LEVEL -4 PLAN", 3160, 230.6, 13.8));
        words.Add(At("-", 3393.9, 226.7, 4, 1.8));
        words.AddRange(Line("CONCRETE", 3160, 209.5, 13.8));
        var mark = At("1", 3377.1, 197.7, 6.9, 13.8);
        words.Add(mark);
        words.AddRange(Line("OUTLINE", 3160, 188.3, 13.8));
        words.AddRange(Line("DRAWING NO:", 3150, 124.7, 8.1));
        words.AddRange(Line("S2.02.1", 3160, 90.1, 13.8));
        words.AddRange(Line("CHECKED BY", 3020, 248, 8.1));
        words.AddRange(Line("Checker", 3020, 230.6, 8.1));

        var fields = TitleBlockFields.Read(Page(3456, 2592, words), out var consumed);

        Assert.Equal("LEVEL -4 PLAN - CONCRETE OUTLINE", fields["SHEET TITLE"]);
        Assert.Equal("Checker", fields["CHECKED BY"]);
        Assert.DoesNotContain((mark.Cx, mark.Cy), consumed);

        var lastNumber = words.Where(t => t.Text != "OUTLINE");
        Assert.Equal("LEVEL -4 PLAN - CONCRETE 1", TitleBlockFields.Read(Page(3456, 2592, lastNumber))["SHEET TITLE"]);
        var interiorNumber = words.Select(t => t == mark ? At("1", 3200, 197.7, 6.9, 13.8) : t);
        Assert.Equal("LEVEL -4 PLAN - CONCRETE 1 OUTLINE", TitleBlockFields.Read(Page(3456, 2592, interiorNumber))["SHEET TITLE"]);
    }

    /// <summary>
    /// 30926-01 p18. WHAT THIS COVERS: a 1.8 pt-high dash 3.4 pt below 12.5 pt words stays
    /// between the two levels in X order, independent of input order. WHAT IT DOES NOT: reproduce
    /// the old reported failure: these centres/heights already pass its half-height comparison;
    /// the actual line seed and hyphen box are absent from the brief. A separate, explicitly synthetic
    /// 3 pt '+' above the words in another column demonstrates the old arbitrary-seed failure mode.
    /// </summary>
    [Fact]
    public void AShortGlyphUsesTheWordsBaselineAndItsOwnHorizontalPosition()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.AddRange(Line("Title", 2330, 228.1, 8.1));
        words.Add(At("LEVEL", 2370, 210.5, 31.25, 12.5));
        words.Add(At("5", 2408, 210.5, 6.25, 12.5));
        words.Add(At("LEVEL", 2456, 210.5, 31.25, 12.5));
        words.Add(At("14", 2499, 210.5, 12.5, 12.5));
        var dash = At("-", 2420.9, 207.1, 4, 1.8);
        words.Add(dash);
        words.Add(At("PLAN", 2370, 190.2, 25, 12.5));

        var fields = TitleBlockFields.Read(Page(2592, 1728, words), out var consumed);

        Assert.Equal("LEVEL 5 - LEVEL 14 PLAN", fields["SHEET TITLE"]);
        Assert.Contains((dash.Cx, dash.Cy), consumed);
        Assert.Equal(fields["SHEET TITLE"], TitleBlockFields.Read(Page(2592, 1728, words.AsEnumerable().Reverse()))["SHEET TITLE"]);

        // '+' occurs in the brief's vertical revision lettering, but THIS placement is synthetic,
        // not a claim about 30926: it used to seed a line at 214, accept LEVEL at 210.5 using LEVEL's
        // height, then reject '-' at 207.1 using the seed's 3 pt height instead of the word's 12.5.
        var withEarlierSmallSeed = words.Append(At("+", 2200, 214, 3, 3));
        Assert.Equal("LEVEL 5 - LEVEL 14 PLAN", TitleBlockFields.Read(Page(2592, 1728, withEarlierSmallSeed))["SHEET TITLE"]);
    }

    /// <summary>
    /// 01589-01 p7. WHAT THIS COVERS: tall labels establish a rotated block for both readers;
    /// the unlabelled title reads bottom-up on its own X column, away from revision/project words;
    /// consumed labels retain their original PDF centres. WHAT IT DOES NOT: infer rotation direction,
    /// top-down or wrapped unlabelled titles, or all field values from the incomplete revision boxes.
    /// Revision-row Y positions within the supplied 526..793 range are synthetic.
    /// </summary>
    [Fact]
    public void ARotatedStripUsesOneReadingCoordinateSystem()
    {
        var words = new List<VectorPageReader.TextToken>
        {
            Rotated("ISSUES:", 2270.3, 528.9), Rotated("DATE:", 2270.4, 759.3),
            Rotated("DRAWN", 2277.3, 165.9), Rotated("BY:", 2311.9, 165.9),
            Rotated("SCALE:", 2277.7, 111.7), Rotated("JOB", 2270.5, 191.6),
            Rotated("#:", 2293.9, 191.6), Rotated("01589-01", 2360.7, 191.6),
            Rotated("1/4\"", 2341, 111.7), Rotated("=", 2356, 111.7), Rotated("1'-0\"", 2371, 111.7),
            Rotated("FOUNDATION", 2439.1, 311.0, 13.8), Rotated("PLAN", 2439.1, 424.8, 13.8),
            Rotated("RESIDENCE", 2284.2, 403.9, 13.8), Rotated("PRIVATE", 2284.2, 275.6, 13.8),
            Rotated("Proposed", 2322, 268, 13.8), Rotated("Renovation", 2322, 363, 13.8),
        };
        foreach (var row in new[] { (X: 2292.3, Month: "DEC.", Day: "6,", Year: "2021"),
                     (X: 2316.3, Month: "APR.", Day: "12,", Year: "2022"),
                     (X: 2340.2, Month: "JUN.", Day: "6,", Year: "2022") })
        {
            words.Add(Rotated("ISSUED", row.X, 526));
            words.Add(Rotated("FOR", row.X, 570));
            words.Add(Rotated("BUILDING", row.X, 620));
            words.Add(Rotated("PERMIT", row.X, 675));
            words.Add(Rotated(row.Month, row.X, 720));
            words.Add(Rotated(row.Day, row.X, 754));
            words.Add(Rotated(row.Year, row.X, 793));
        }
        var page = Page(2592, 1728, words);

        var fields = TitleBlockFields.Read(page, out var consumed);

        Assert.False(fields.ContainsKey("SHEET TITLE")); // This strip has no title label.
        Assert.Contains((2270.4, 759.3), consumed);
        Assert.Equal("FOUNDATION PLAN", SheetTitleReader.TitleText(page));
        Assert.Equal("FOUNDATION PLAN", SheetTitleReader.TitleText(Page(2592, 1728, words.AsEnumerable().Reverse())));
    }

    /// <summary>
    /// 01379-01 p77. WHAT THIS COVERS: split JOB TITLE retains its identity; DRAWING TITLE is
    /// SHEET TITLE; both naming overloads reject stated project names, including repeated fallback
    /// text/bookmarks; synthetic adjacent-field variants check each compound label away from the
    /// start of a line. WHAT IT DOES NOT: recognise an unlabelled project name by its meaning.
    /// </summary>
    [Fact]
    public void AJobTitleCannotReplaceTheDrawingsTitleOrNameAnUntitledSheet()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.AddRange(Line("JOB TITLE", 3160, 561.5, 8.1));
        words.AddRange(Line("1200 STEWART", 3160, 542, 13.8));
        words.AddRange(Line("DRAWING TITLE", 3160, 377.2, 8.1));
        words.AddRange(Line("GALLERIA PART PLANS", 3160, 353.6, 13.8));
        words.AddRange(Line("DATE", 3160, 292.3, 8.1));
        words.AddRange(Line("DRAWN", 3280, 292.3, 8.1));
        words.AddRange(Line("S212.9", 3160, 126.9, 13.8));
        var page = Page(3456, 2592, words);

        var fields = TitleBlockFields.Read(page);

        Assert.Equal("1200 STEWART", fields["JOB TITLE"]);
        Assert.Equal("GALLERIA PART PLANS", fields["SHEET TITLE"]);
        Assert.Equal("GALLERIA PART PLANS", SheetTitleReader.TitleText(page));
        Assert.Equal("S212.9_1_GALLERIA PART PLANS.dxf", SheetDxfName.For("S212.9", fields, "01379-01-p77"));
        Assert.Equal("S212.9_1_GALLERIA PART PLANS.dxf", SheetDxfName.For("S212.9", fields, "01379-01-p77", null, "1200 STEWART"));

        foreach (string label in new[] { "JOB TITLE", "PROJECT TITLE", "SHEET TITLE", "DRAWING TITLE" })
        {
            var paired = Line("DATE", 2860, 561.5, 8.1).Concat(Line(label, 3160, 561.5, 8.1))
                .Concat(Line("1200 STEWART", 3160, 542, 13.8));
            var pairFields = TitleBlockFields.Read(Page(3456, 2592, paired));
            string key = label == "DRAWING TITLE" ? "SHEET TITLE" : label;
            Assert.Equal("1200 STEWART", pairFields[key]);
            if (label is "JOB TITLE" or "PROJECT TITLE") Assert.False(pairFields.ContainsKey("SHEET TITLE"));
        }

        foreach (string projectLabel in new[] { "JOB TITLE", "PROJECT TITLE" })
        {
            var untitled = new Dictionary<string, string> { [projectLabel] = "1200 STEWART" };
            Assert.Equal("01379-01-p77.dxf", SheetDxfName.For("S212.9", untitled, "01379-01-p77"));
            Assert.Equal("01379-01-p77.dxf", SheetDxfName.For("S212.9", untitled, "01379-01-p77",
                "S212.9 - 1200 STEWART", "S212.9 - 1200 STEWART"));
            untitled["SHEET TITLE"] = "1200   Stewart"; // A previously misread field cannot revive the project name.
            Assert.Equal("01379-01-p77.dxf", SheetDxfName.For("S212.9", untitled, "01379-01-p77"));
            untitled["DRAWING TITLE"] = "GALLERIA PART PLANS";
            Assert.Equal("S212.9_1_GALLERIA PART PLANS.dxf", SheetDxfName.For("S212.9", untitled, "01379-01-p77"));
        }
    }

    /// <summary>
    /// 30816-01 p8 (run 19, 2026-09-15): the older KOR block writes DRAWING TITLE: with its two title lines below
    /// it and, far above, a revision table whose SHEET column header is a label too. Taken as the neighbouring
    /// column, that header bounded the title to one word a line ("Level (Concrete") on every sheet of 82 sets.
    /// WHAT THIS COVERS: a label far above the field does not bound it; one beside its rows (the 30912 case,
    /// kept above) does. WHAT IT DOES NOT: the revision table's own reading.
    /// </summary>
    [Fact]
    public void ALabelFarAboveTheFieldIsNotItsNeighbouringColumn()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.AddRange(Line("SHEET NO.", 3243, 620, 6.0));          // the revision table's column header, high in the block
        words.AddRange(Line("DRAWING TITLE:", 3209, 197, 6.5));
        words.AddRange(Line("Level 1 Floor Plan", 3210, 183, 11));
        words.AddRange(Line("(Concrete Outline)", 3210, 167, 11));
        words.AddRange(Line("DRAWING #:", 3209, 117, 6.5));
        words.AddRange(Line("S2.08.1", 3210, 88, 28));

        var fields = TitleBlockFields.Read(Page(3456, 2592, words));

        Assert.Equal("Level 1 Floor Plan (Concrete Outline)", fields["SHEET TITLE"]);
    }

    /// <summary>
    /// A TITLE NAMES A PLAN; A PROJECT NAME DOES NOT (step 85, WP6a item 5). The older KOR block (30994-01, Calgary,
    /// 2024) labels no field and stacks the project name over the title: "BELVEDERE PLACE" at 11.5 pt, then
    /// KINGSLAND and the address at 9, then "PARKADE FLOOR PLAN / FOUNDATION PLAN (west)" at 10.1 - under the 11 pt
    /// title floor - so the reader titled every plan with the project and the set named no storey. The block that
    /// names a plan is the title whatever its size; the project name is not. At the page's own positions and heights.
    /// WHAT IT DOES NOT: a block with a SHEET TITLE label (the field wins first); a rotated block; a note on the
    /// right edge that names a plan in title-size capitals - none of the six draws one.
    /// </summary>
    [Fact]
    public void ATitleNamesAPlanAndAProjectNameDoesNot()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.AddRange(Line("BELVEDERE PLACE", 2800, 301.3, 11.5));
        words.AddRange(Line("KINGSLAND", 2830, 280.2, 9.0));
        words.AddRange(Line("555 73A AVENUE SW", 2792, 256.3, 9.0));
        words.AddRange(Line("CALGARY, ALBERTA", 2808, 245.4, 9.0));
        words.AddRange(Line("PARKADE FLOOR PLAN", 2764, 195.8, 10.1));
        words.AddRange(Line("/ FOUNDATION PLAN (west)", 2733, 178.8, 10.3));
        words.AddRange(Line("Scale", 2738, 153.3, 6.0));
        words.AddRange(Line("1/8\" = 1'-0\"", 2782, 152.4, 6.0));
        words.AddRange(Line("Date", 2736, 129.4, 6.0));
        words.AddRange(Line("DEC. 7, 22", 2785, 129.2, 6.0));
        words.AddRange(Line("S2.01", 2882, 108, 14));
        var page = Page(3024, 1728, words);

        // the "/" carries no letter and "(west)" is not set in capitals: both fall to the block's own token rules, as
        // they did before this step - the zone is a known limit of the upper-case rule, stated here
        Assert.Equal("PARKADE FLOOR PLAN FOUNDATION PLAN", SheetTitleReader.TitleText(page));
    }

    private static VectorPageReader.PageContent Page(double width, double height, IEnumerable<VectorPageReader.TextToken> words) =>
        new(1, width, height, words.ToList(), new List<VectorPageReader.GeomPath>());

    private static VectorPageReader.TextToken At(string text, double x, double y, double width, double height) =>
        new(text, x, y, x - width / 2, y - height / 2, x + width / 2, y + height / 2);

    private static VectorPageReader.TextToken Rotated(string text, double x, double y, double height = 8.1) =>
        At(text, x, y, height, text.Length * height * 0.5);

    private static IEnumerable<VectorPageReader.TextToken> Line(string text, double x, double y, double height)
    {
        foreach (string word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            double width = word.Length * height * 0.5;
            yield return At(word, x + width / 2, y, width, height);
            x += width + height * 0.5;
        }
    }
}
