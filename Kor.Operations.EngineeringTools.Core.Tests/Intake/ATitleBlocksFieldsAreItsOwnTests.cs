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
    /// A strip written wholly up the page. WHAT THIS COVERS: tall labels establish a rotated block for both readers;
    /// the unlabelled title reads bottom-up on its own X column, away from revision/project words;
    /// consumed labels retain their original PDF centres. WHAT IT DOES NOT: infer rotation direction,
    /// top-down or wrapped unlabelled titles, or all field values from the incomplete revision boxes.
    /// Revision-row Y positions within the supplied 526..793 range are synthetic.
    /// ⚠ THIS IS NOT 01589-01 p7 (found 2026-09-16, step 87): the page's SCALE:, DATE:, DRAWN BY:, JOB #: and SIGNED
    /// BY: are HORIZONTAL at the foot of the strip (w 37, h 7.1) and only ISSUES: and DATE: are upright, so the page
    /// is never a rotated block and this fixture's rotated labels are a shape no page of the corpus has been seen to
    /// draw. The page itself is <see cref="AMixedStripReadsItsUprightWordsUpThePage"/>. Kept as the rotated-strip
    /// fixture (30888 is the measured one, below).
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
    /// WORDS WRITTEN UP THE PAGE ARE READ UP THE PAGE (step 87, WP6a item 5). 01589-01 p7 as the page draws it: the
    /// labels SCALE: / DATE: / DRAWN BY: / JOB #: horizontal at the foot of the strip, the title "FOUNDATION PLAN"
    /// (20.7 pt) and the project "PRIVATE RESIDENCE" written up the page above them, with three revision columns
    /// (7.4 pt) between. Read as horizontal lines an upright word's height is its length, so every one was a
    /// title-size candidate and the guess was "PERMIT PERMIT PERMIT BUILDING BUILDING BUILDING FOR FOR FOR ISSUED
    /// ISSUED ISSUED RESIDENCE PLAN PRIVATE FOUNDATION" on every plan of the set through run 22. The upright words
    /// are read in their own frame - a column is a line, bottom-up, the font size is the height - and the largest
    /// block that names a plan wins. 30941's KOR strip is the other shape of the class: "LEVEL B4 RAFT" and
    /// "FOUNDATION PLAN" up the page in two columns 41 pt apart at 26.8 pt, under horizontal labels, with "B4" too
    /// short to have a shape of its own - it is upright because it stands in LEVEL's column at LEVEL's size.
    /// WHAT IT DOES NOT: a title written top-down (no page seen); the level reader FromPage (still by position).
    /// </summary>
    [Fact]
    public void AMixedStripReadsItsUprightWordsUpThePage()
    {
        var words = new List<VectorPageReader.TextToken>
        {
            At("SIGNED", 2278.2, 86.0, 38.2, 7.1), At("BY:", 2313.7, 86.0, 18.3, 7.1),
            At("SCALE:", 2277.7, 111.7, 37.2, 7.1), At("1/4\"", 2341.7, 112.2, 17.4, 7.3), At("=", 2356.1, 111.1, 5.8, 5.0), At("1'-0\"", 2371.7, 112.2, 19.8, 7.2),
            At("DATE:", 2273.6, 138.8, 29.0, 7.1), At("JUNE", 2332.7, 139.2, 26.0, 7.1), At("6,", 2352.7, 139.2, 8.3, 7.2), At("2022", 2370.7, 139.2, 22.2, 7.2),
            At("DRAWN", 2277.3, 165.9, 36.4, 7.1), At("BY:", 2311.9, 165.9, 18.3, 7.1), At("N.Y.", 2372.0, 165.3, 19.4, 7.1),
            At("JOB", 2270.5, 191.6, 21.6, 7.1), At("#:", 2293.9, 192.2, 10.7, 8.4), At("01589-01", 2360.7, 191.5, 42.1, 7.2),
            At("S2.01", 2458.2, 140.6, 117.3, 27.0),
            At("ISSUES:", 2270.3, 528.9, 8.6, 47.7), At("DATE:", 2270.4, 759.3, 7.2, 27.7),
            At("PRIVATE", 2284.5, 275.6, 17.1, 102.5), At("RESIDENCE", 2284.2, 403.9, 17.7, 138.4),
            At("Proposed", 2322.0, 268.1, 13.1, 81.6), At("Renovation", 2322.1, 363.3, 12.8, 96.5),
            At("629", 2342.8, 242.4, 13.1, 29.1), At("East", 2342.9, 281.6, 12.8, 37.7), At("12th", 2342.9, 323.5, 12.9, 35.7), At("Street,", 2342.7, 374.8, 13.3, 54.5),
            At("City", 2363.5, 244.6, 13.3, 33.6), At("of", 2363.6, 275.4, 13.1, 17.5), At("North", 2363.7, 311.9, 12.8, 46.6), At("Vancouver", 2363.7, 387.4, 12.8, 92.0),
            At("FOUNDATION", 2439.1, 311.0, 20.7, 150.7), At("PLAN", 2439.5, 424.8, 20.0, 60.5),
        };
        foreach (var (x, month, year) in new[] { (2292.3, "DEC.", "2021"), (2316.3, "APR.", "2022"), (2340.2, "JUN.", "2022") })
        {
            words.Add(At("ISSUED", x, 526.0, 7.4, 36.6)); words.Add(At("FOR", x, 558.1, 7.4, 20.9));
            words.Add(At("BUILDING", x, 594.6, 7.4, 46.5)); words.Add(At("PERMIT", x, 640.0, 7.1, 37.5));
            words.Add(At(month, x, 756.9, 7.4, 22.9)); words.Add(At(year, x, 793.3, 7.3, 20.3));
        }
        var page = Page(2592, 1728, words);

        var fields = TitleBlockFields.Read(page, out _, out var labels);
        Assert.DoesNotContain("SHEET TITLE", labels);
        Assert.Equal("1/4\" = 1'-0\"", fields["SCALE"]);
        Assert.Equal("FOUNDATION PLAN", SheetTitleReader.TitleText(page));

        // 30941 p16: the title up the page in two columns under horizontal labels, with a two-glyph level
        var kor = new List<VectorPageReader.TextToken>
        {
            At("Scale", 2762.0, 133.6, 22, 9.0), At("3/32", 2846.8, 131.8, 20, 9), At("=", 2875.5, 129.7, 6, 6), At("S2.01.1", 2834.6, 176.5, 70, 14),
            At("Checked", 2769.6, 232.7, 34, 9), At("By", 2800.0, 232.6, 10, 9), At("Drawn", 2763.8, 259.5, 28, 9), At("By", 2788.3, 259.5, 10, 9),
            At("Date", 2759.8, 286.5, 20, 9), At("02/05/2024", 2877.6, 284.1, 50, 9), At("Project", 2765.2, 313.5, 33.2, 9.0), At("Number", 2803.9, 313.5, 37.9, 9.0), At("30941-01", 2884.1, 311.0, 70.4, 13.6),
            At("LEVEL", 2778.4, 377.4, 26.8, 97.8), At("B4", 2778.4, 455.0, 26.8, 37.4), At("RAFT", 2778.4, 525.3, 26.8, 82.4),
            At("FOUNDATION", 2819.8, 432.4, 27.8, 208.0), At("PLAN", 2820.3, 588.1, 26.8, 80.4),
            At("CONSULTANT:", 2758.1, 854.6, 55, 8),
        };
        Assert.Equal("LEVEL B4 RAFT FOUNDATION PLAN", SheetTitleReader.TitleText(Page(3024, 2160, kor)));
    }

    /// <summary>
    /// THE BLOCK'S OWN WAY OF WRITING COMES FIRST (step 92, 2026-09-16; Codex's counterexample to step 87): a
    /// horizontal title "ROOF PLAN" at 12 pt beside an upright section marker "FOUNDATION PLAN" at 24 pt — step 87
    /// let the two readings compete by font size alone, and the marker won. The horizontal reading is the block's
    /// own; the upright reading is asked only when the horizontal names no plan (30941 and 01589: their horizontal
    /// words are labels and a number, and their titles are upright). WHAT IT DOES NOT: a horizontal note that names
    /// a plan on a block whose title is upright (none seen; the old rule had the same limit).
    /// </summary>
    [Fact]
    public void TheHorizontalTitleOutranksALargerUprightMarker()
    {
        var page = Page(2592, 1728, new List<VectorPageReader.TextToken>
        {
            At("ROOF", 2280, 200, 40, 12), At("PLAN", 2330, 200, 40, 12),
            At("FOUNDATION", 2440, 500, 24, 168), At("PLAN", 2440, 630, 24, 72),
        });
        Assert.Equal("ROOF PLAN", SheetTitleReader.TitleText(page));

        // and with no horizontal plan-naming block the upright one still reads (30941's shape)
        var upright = Page(2592, 1728, new List<VectorPageReader.TextToken>
        {
            At("Checked", 2280, 200, 34, 9), At("By", 2320, 200, 10, 9),
            At("FOUNDATION", 2440, 500, 24, 168), At("PLAN", 2440, 630, 24, 72),
        });
        Assert.Equal("FOUNDATION PLAN", SheetTitleReader.TitleText(upright));
    }

    /// <summary>
    /// A ROTATED STRIP'S LABELS STAND IN THE STRIP, NOT IN ONE COLUMN (step 87, WP6a item 5; 30888-01 p16, Duffy
    /// Hills, 2020). 01589 stacks its labels in one text column and the detection asked for that; 30888 writes JOB
    /// NO. / DRAWING NAME (x 2369), SCALE (2397), REVISIONS (2417) and DATE (2423, 2435) each up the page in its
    /// own column, 6 to 27 pt apart, and was read as a horizontal block: every plan titled "HILLS ARCHITECTURE
    /// DUFFY DRAWING LANDSCAPE PERMIT REVIEW ..." in y order. Three upright labels spread along the strip say
    /// rotated, wherever their columns are; DRAWING NAME is the sheet's title label and the title reads up the
    /// page under it, across its two lines, to the REVISIONS label that closes it. At the page's own positions and
    /// heights (6.7 pt labels, PDF y up). WHAT IT DOES NOT: the PDF word builder's own fault on p17, where
    /// "(REINFORCING" and "REVISIONS" overlap and arrive as one token; the level reader FromPage (still by
    /// position; the DXF name carries the storey from the field).
    /// </summary>
    [Fact]
    public void ARotatedStripsLabelsStandInTheStripNotInOneColumn()
    {
        var words = new List<VectorPageReader.TextToken>
        {
            Rotated("CRAVEN", 2328.4, 100.9, 6.7), Rotated("HUSTON", 2328.4, 197.9, 6.7), Rotated("POWERS", 2328.4, 295.3, 6.7),
            Rotated("ARCHITECTS", 2328.4, 413.7, 6.7), Rotated("ARCHITECTURE", 2328.4, 567.4, 6.7), Rotated("AND", 2328.5, 667.3, 6.7),
            Rotated("LANDSCAPE", 2328.4, 750.8, 6.7), Rotated("ARCHITECTURE", 2328.4, 887.1, 6.7),
            Rotated("JOB", 2369.0, 259.9, 6.7), Rotated("NO.", 2369.0, 279.2, 6.7),
            Rotated("DRAWING", 2369.0, 496.7, 6.7), Rotated("NAME", 2369.1, 537.0, 6.7),
            Rotated("DRAWING", 2369.0, 826.1, 6.7), Rotated("NAME", 2369.1, 866.4, 6.7),      // the page labels the project DRAWING NAME too
            Rotated("30888-01", 2375.6, 407.4, 6.7),
            Rotated("P1", 2389.2, 499.4, 9.0), Rotated("PARKADE", 2389.2, 575.5, 9.0), Rotated("PLAN", 2389.2, 664.4, 9.0), Rotated("WEST", 2388.8, 733.7, 9.0),
            Rotated("SCALE", 2396.7, 266.4, 6.7),
            Rotated("DUFFY", 2399.5, 882.6, 9.0), Rotated("HILLS", 2399.3, 979.9, 9.0), Rotated("BUILDINGS", 2399.3, 1133.1, 9.0), Rotated("E", 2399.7, 1233.6, 9.0),
            Rotated("1/8\"", 2402.3, 394.0, 6.7), Rotated("=", 2405.0, 409.6, 6.7), Rotated("1'-0\"", 2402.8, 427.5, 6.7),
            Rotated("(CONCRETE", 2409.7, 554.4, 9.0), Rotated("OUTLINE)", 2409.7, 681.0, 9.0),
            Rotated("REVISIONS", 2416.8, 498.4, 6.7),
            Rotated("DATE", 2423.2, 264.5, 6.7), Rotated("OCTOBER", 2429.7, 365.5, 6.7), Rotated("20,", 2429.8, 401.8, 6.7), Rotated("2020", 2429.8, 425.5, 6.7),
            Rotated("S2.03.1", 2435.1, 156.4, 12.0),
            Rotated("NO.", 2435.4, 481.3, 6.7), Rotated("DATE", 2435.5, 516.5, 6.7), Rotated("DESCRIPTION", 2435.4, 596.6, 6.7),
        };
        foreach (var row in new[] { (X: 2454.5, Month: "DEC.", Day: "5,", Year: "18", What: "ISSUED FOR BUILDING PERMIT"),
                     (X: 2465.1, Month: "OCT.", Day: "10,", Year: "19", What: "RE-ISSUED FOR BUILDING PERMIT"),
                     (X: 2475.7, Month: "APR.", Day: "30,", Year: "20", What: "ISSUED FOR CONSTRUCTION REVIEW"),
                     (X: 2486.4, Month: "JUNE", Day: "1,", Year: "20", What: "ISSUED FOR CONSTRUCTION REVIEW") })
        {
            words.Add(Rotated(row.Month, row.X, 515)); words.Add(Rotated(row.Day, row.X, 534)); words.Add(Rotated(row.Year, row.X, 548));
            double y = 584;
            foreach (string word in row.What.Split(' ')) { words.Add(Rotated(word, row.X, y, 6.7)); y += 8 + word.Length * 3.4; }
        }
        var page = Page(2592, 1728, words);

        var fields = TitleBlockFields.Read(page, out _, out var labels);

        Assert.Contains("SHEET TITLE", labels);
        // two DRAWING NAME labels on one strip: the first is the sheet's, and the second closes its column
        Assert.Equal("P1 PARKADE PLAN WEST (CONCRETE OUTLINE)", fields["SHEET TITLE"]);
        Assert.DoesNotContain("DUFFY", fields["SHEET TITLE"]);
        Assert.Equal("P1 PARKADE PLAN WEST (CONCRETE OUTLINE)", SheetTitleReader.TitleText(page));
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

    /// <summary>
    /// A LETTER-SPACED LABEL IS A LABEL, AND AN EMPTY TITLE BOX IS NO TITLE (step 86, WP6a item 5). 30980-01 (880 W
    /// 15, 2025) sets its block's labels one letter per token - "S H E E T  T I T L E" at 6.8 pt over a box that holds
    /// the title as plotted glyph outlines and not one word - and "P R O J E C T" over MIXED USE DEVELOPMENT at 15 pt.
    /// Token by token no label matched, so the block was unlabelled to the reader and the largest capitals in the
    /// strip, the project name, titled 17 of the set's 27 pages "USE MIXED DEVELOPMENT". At p16's own positions
    /// and heights (PDF y, up from the bottom): the letters spell their labels, the title box is seen empty, and
    /// the answer is no title - not the project, not the scale, not the sheet number under it.
    /// WHAT IT DOES NOT: letters stacked up the page (an architect's "A R C H I T E C T U R E" tagline on 30941 is
    /// one letter per line, and joins nothing); a block whose title is written up the page under a horizontal
    /// label (the second look with the upright words is exercised by no fixture here).
    /// </summary>
    [Fact]
    public void ALetterSpacedLabelIsALabelAndAnEmptyTitleBoxIsNoTitle()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.AddRange(Line("Checked:", 2347, 62.6, 6.5));
        words.AddRange(Line("Drawn:", 2342, 80.5, 6.5));
        words.Add(At("S2.00", 2480.6, 96.7, 48, 19.8));
        words.AddRange(Line("Scale:", 2340, 116.6, 6.5));
        words.Add(At("1/8\"=1'-0\"", 2410, 116.0, 40, 7.5));
        words.AddRange(Line("Job No:", 2335, 134.5, 6.5));
        words.AddRange(Letters("DRAWING", 2441.9, 134.7, 9.3));
        words.AddRange(Letters("NO.", 2512.6, 134.7, 11.1));
        words.AddRange(Letters("SHEET", 2388.9, 206.8, 9.1));
        words.AddRange(Letters("TITLE", 2439.1, 206.8, 7.8));
        words.AddRange(Line("NORTH VANCOUVER, B.C.", 2370, 225.7, 9));
        words.AddRange(Line("880 WEST 15 STREET", 2373, 241.6, 9));
        words.Add(At("DEVELOPMENT", 2421.2, 291.9, 90, 15.3));
        words.Add(At("MIXED", 2397.4, 316.8, 42, 14.9));
        words.Add(At("USE", 2457.4, 317.0, 26, 15.3));
        words.AddRange(Letters("PROJECT", 2401.9, 341.8, 9.2));
        var page = Page(2592, 1728, words);

        var fields = TitleBlockFields.Read(page, out _, out var labels);

        Assert.Contains("SHEET TITLE", labels);
        Assert.Contains("DRAWING NO", labels);
        Assert.False(fields.ContainsKey("SHEET TITLE"), $"the empty title box read as \"{(fields.TryGetValue("SHEET TITLE", out var v) ? v : "")}\"");
        Assert.Null(SheetTitleReader.TitleText(page));
    }

    /// <summary>
    /// A LABEL'S OWN COLON IS NOT ITS VALUE (step 91, 2026-09-16): KOR's 2022 upright strip (50026, 30985) sets "SHEET
    /// TITLE" and ":" as two tokens on one line, and the ":" beside the label was the title of every plan - two models
    /// lost on run 24 the moment step 87 read the strip in its own frame (before that the guess had read a salad with
    /// the storey's word in it). A token with no letter or digit is the form's own mark; the value is below the label.
    /// WHAT IT DOES NOT: a value that is genuinely punctuation (none exists on a title block).
    /// </summary>
    [Fact]
    public void ALabelsOwnColonIsNotItsValue()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.AddRange(Line("SHEET TITLE", 3209, 197, 6.5));
        words.Add(At(":", 3272, 197, 1.9, 4.3));
        words.AddRange(Line("FOUNDATION PLAN", 3210, 183, 11));
        words.AddRange(Line("PARKING LEVEL P3", 3210, 167, 11));
        words.AddRange(Line("SCALE :", 3209, 117, 6.5));
        words.Add(At("S2.01", 3260, 88, 60, 28));
        var page = Page(3456, 2592, words);

        Assert.Equal("FOUNDATION PLAN PARKING LEVEL P3", TitleBlockFields.Read(page)["SHEET TITLE"]);
        Assert.Equal("FOUNDATION PLAN PARKING LEVEL P3", SheetTitleReader.TitleText(page));
    }

    /// <summary>
    /// A RIGHT-ALIGNED LABEL'S VALUE LIES TO ITS LEFT (step 91, 2026-09-16; 30985-01 p7, Rock Ridge, 2022): the block
    /// sets its labels letter by letter in brackets against the strip's right edge — [ D R A W I N G ], [ I S S U E ],
    /// [ D A T E ], [ S C A L E ], [ P R O J E C T ], [ T I T L E ] — and writes the values to their left: "1st Floor /
    /// Foundation Plan (West)" at 20 pt under [ T I T L E ], starting 130 pt left of it. A column bounded at the
    /// label's left edge held only the next bracketed label's letters ("R O J E C T ]" was the title, and the set's one
    /// storey went with it on run 24). When a label ends at the strip's edge and nothing stands beside it, the column
    /// runs from the strip's left; PROJECT and ISSUE are labels, so their letters end the field and are never its value.
    /// At p7's own positions and heights. WHAT IT DOES NOT: "Flr." as a floor noun (a vocabulary row).
    /// </summary>
    [Fact]
    public void ARightAlignedLabelsValueLiesToItsLeft()
    {
        var words = new List<VectorPageReader.TextToken>();
        words.Add(At("S-2.01", 2479.4, 110.6, 67.4, 20.3));
        words.AddRange(Bracketed("DRAWING", 2479.5, 146.0));
        words.AddRange(Bracketed("ISSUE", 2494.9, 172.2));
        words.AddRange(Line("Apr. 14, 2022", 2347, 192.8, 8.5));
        words.AddRange(Bracketed("DATE", 2498.5, 198.1));
        words.Add(At("1/8\"=1'-0\"", 2373.3, 217.8, 54.7, 8.6));
        words.AddRange(Bracketed("SCALE", 2490.8, 224.1));
        words.Add(At("30985-01", 2371.6, 244.9, 50.2, 8.5));
        words.AddRange(Bracketed("PROJECT", 2478.8, 250.2));
        words.Add(At("(West)", 2381.9, 279.7, 69.9, 20.3));
        words.Add(At("Foundation", 2409.3, 308.3, 124.6, 20.0)); words.Add(At("Plan", 2502.1, 308.3, 48.3, 20.0));
        words.Add(At("1st", 2363.5, 337.3, 33.1, 20.1)); words.Add(At("Floor", 2415.0, 337.2, 57.2, 20.0)); words.Add(At("/", 2453.2, 337.4, 6.4, 20.3));
        words.AddRange(Bracketed("TITLE", 2494.6, 360.7));
        words.AddRange(Line("Port Moody, BC", 2346, 380.3, 8.0));
        var page = Page(2592, 1728, words);

        var fields = TitleBlockFields.Read(page, out _, out var labels);
        Assert.Contains("PROJECT", labels);
        Assert.Contains("ISSUE", labels);
        Assert.Equal("1st Floor / Foundation Plan (West)", fields["SHEET TITLE"]);
        Assert.Equal("1st Floor / Foundation Plan (West)", SheetTitleReader.TitleText(page));
    }

    /// <summary>"[ T I T L E ]": a bracket, one letter per token at a 6.2 pt pitch, a bracket — right-aligned to x = 2528.</summary>
    private static IEnumerable<VectorPageReader.TextToken> Bracketed(string word, double x, double y)
    {
        yield return At("[", x, y, 2.1, 4.5);
        double cx = x + 5.0;
        foreach (char c in word) { yield return At(c.ToString(), cx, y, 4.4, 4.5); cx += 6.2; }
        yield return At("]", 2526.3, y, 2.1, 4.5);
    }

    /// <summary>One letter per token at a fixed pitch, as 30980's block sets its labels (6.8 pt letters).</summary>
    private static IEnumerable<VectorPageReader.TextToken> Letters(string word, double x, double y, double pitch)
    {
        for (int i = 0; i < word.Length; i++)
            yield return At(word[i].ToString(), x + i * pitch + 2.5, y, 5.0, 6.8);
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
