using System.Globalization;
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;
using Kor.Operations.EngineeringTools.QuantityTakeoff;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Tokens;

namespace Kor.Operations.EngineeringTools.Intake;

public sealed record IntakeRequest(int? ScaleDenominator, PdfIntakeOptions Options, bool MarkupOnly = false);

/// <summary>The document entry point: retain one vector read and the existing readers' decisions per sheet.</summary>
public static class DrawingIntake
{
    public static DrawingSetRecord Read(string pdfPath, IntakeRequest request)
    {
        using var doc = PdfDocument.Open(pdfPath);
        var facts = DocumentFacts.From(doc);
        var sheets = new List<SheetRecord>();
        for (int page = 1; page <= facts.Pages; page++)
            sheets.Add(ReadSheet(doc, page, request, facts));
        return new DrawingSetRecord(facts, sheets);
    }

    public static SheetRecord ReadSheet(PdfDocument doc, int page, IntakeRequest request, DocumentFacts facts)
    {
        ArgumentNullException.ThrowIfNull(doc);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Options);
        ArgumentNullException.ThrowIfNull(facts);
        return ReadPage(doc.GetPage(page), request, facts);
    }

    private static SheetRecord ReadPage(Page page, IntakeRequest request, DocumentFacts facts)
    {
        int pageNumber = page.Number;
        var options = request.Options;
        double scaleFactor = request.ScaleDenominator.GetValueOrDefault() * PdfToSafeConstants.PointsToMm;
        bool classify = request.ScaleDenominator is > 0;

        // THE POPULATION IS THE UNTHINNED READ.
        //
        // The classifier reads the page with scale-dependent vertex and closure tolerances, and a
        // subpath that thins to fewer than two points is dropped before any rule sees it. On
        // 31130's hatched parkade plans that is 85–91% of the paths on the page (page 13: 45,515
        // drawn, 4,129 classified). A ledger built on the thinned read reported every one of its
        // paths accounted for and was silent about the 41,386 it never saw. So the record's
        // Content is the full read, the classifier still receives exactly the thinned read it
        // always did (the DXF baseline depends on it), and every fate is mapped back onto the
        // population by subpath ordinal; what the thinning removed is CollapsedByThinning.
        var fullKept = new List<int>();
        var full = VectorPageReader.ReadPage(page, includeAnnotations: true,
            curveSegments: PdfToSafeConstants.BezierSegments, keptSubpathOrdinals: fullKept);

        VectorPageReader.PageContent content;
        var thinnedKept = new List<int>();
        List<RawSubpath> raw;
        if (classify)
            raw = PdfPlanReader.ParsePage(page, scaleFactor, out content, thinnedKept);
        else
        {
            content = full;
            thinnedKept.AddRange(fullKept);
            raw = [];
        }

        var furniture = SheetFurniture.On(content, options.AgreementToleranceMm);
        var grid = GridBubbles.On(content);
        SheetTitle? title = null;
        string? scale = null;
        try { title = SheetTitleReader.FromPage(content); } catch { /* Preserve the existing optional title read. */ }
        try { scale = SheetScaleReader.FromPage(content); } catch { /* No readable scale note. */ }
        string? bookmark = facts.Bookmarks.TryGetValue(pageNumber, out var bt) ? bt : null;
        // The sheet's own words about itself: the bookmark, else the title block's text.
        var titleBlock = furniture.Regions.Where(r => r.Kind == "title block").ToList();
        string titleBlockText = string.Join(" ", content.Words
            .Where(w => titleBlock.Count > 0
                ? titleBlock.Any(r => r.Contains(w.Cx, w.Cy))
                : w.Cx > 0.8 * content.WidthPts && w.Cy < 0.2 * content.HeightPts)
            .Select(w => w.Text));
        // THE SHEET'S OWN TITLE FIRST. SheetTitleReader anchors on the sheet-title PLAN or LEVEL
        // token and keeps the raw text; the bookmark is Bluebeam's index of the same; the region's
        // words are the fallback. On 31138 (no bookmarks) the title sits at 82–87% of the width
        // and the furniture's title-block strip starts at 91%, so the region fallback typed 20 plan
        // sheets "other" (2026-09-08, intake convergence brief 17).
        // A SHEET WHOSE TITLE BLOCK NAMES A STOREY IS A PLAN: that is SheetTitleReader's own
        // contract (its wrapped parse demands a plan-ish descriptor around the level). Otherwise
        // the bookmark, then the sheet's own title text, then the title block's words. The title
        // text alone mistyped plans as notes on 2026-09-08, because a notes column often sits in
        // the right fifth of a KOR sheet in title-size type.
        // WHAT THE SHEET CALLS ITSELF, THEN WHAT IT CONTAINS. The drafter's index (bookmark) and
        // the SHEET TITLE field are statements and type a sheet exactly (31065: 73 of 73 pages
        // against the index; 31202: 58 of 59). The level rule and the right-edge text are
        // inferences, and the level rule alone typed 14 of 31168's 41 sheets plan — wall
        // elevations and typical details name storeys too. Measured 2026-09-08.
        var fields = TitleBlockFields.Read(content);
        string? fieldTitle = fields.TryGetValue("SHEET TITLE", out var ft) ? ft
                           : fields.TryGetValue("DRAWING TITLE", out ft) ? ft : null;
        // THE SCALE FIELD IS THE SCALE. SheetScaleReader reads the same field by its own search and
        // declined 80 of 294 pages on 2026-09-08; 45 of those state "AS NOTED" (details, notes,
        // schedules — a statement, kept as ScaleStatement), the rest state a ratio the field yields.
        string? scaleStatement = fields.TryGetValue("SCALE", out var sf) ? sf : null;
        scale ??= SheetScaleReader.RatioOf(scaleStatement);
        string? titleText = null;
        try { titleText = SheetTitleReader.TitleText(content); } catch { /* a title the reader cannot form is a fact, not a failure */ }
        string sheetType = FirstTyped(bookmark, fieldTitle);
        if (sheetType is "other" or "unknown")
            sheetType = title is not null ? "plan" : FirstTyped(titleText, titleBlockText.Length > 0 ? titleBlockText : null);

        IReadOnlyList<ColumnScheduleRow> columns = [];
        IReadOnlyList<FootingScheduleReader.FootingType> footings = [];
        IReadOnlyList<ScheduleGridReader.FlatWallScheduleRow> walls = [];
        IReadOnlyList<MarkRowScheduleReader.ScheduleHeading> headings = [];
        string? agreementError = null;
        try { columns = ColumnScheduleReader.ReadSchedule(content); }
        catch (Exception ex) { agreementError = ex.GetType().Name; }
        try { footings = FootingScheduleReader.ReadSchedule(content).Types; } catch { /* Optional schedule unread. */ }
        try { walls = ScheduleGridReader.ReadFlatWallRows(content); } catch { /* Optional schedule unread. */ }
        try { headings = MarkRowScheduleReader.SchedulesOn(content, MarkRowScheduleReader.ColumnDefaults() with { HeadingWords = Array.Empty<string>() }); }
        catch { /* Optional heading read unavailable. */ }
        var schedules = ScheduleTables(columns, footings, walls, headings);
        var marks = content.Words.Where(w => !furniture.IsFurniture(w.Cx, w.Cy) && KindOf(w.Text) == "mark")
            .Select(w => new PlanMark(w.Text, w.Cx, w.Cy)).ToList();
        IReadOnlyList<SlabThicknessZoner.Callout> callouts = [];
        try { callouts = SlabThicknessZoner.ReadCallouts(content); } catch { /* No readable callouts. */ }

        var geometry = new ExtractedGeometry
        {
            ScaleDenominator = request.ScaleDenominator.GetValueOrDefault(),
            PageWidthPts = page.Width, PageHeightPts = page.Height, PageCount = facts.Pages,
            RawPathCount = content.Paths.Count,
        };
        IReadOnlyList<PathFate> pathFates = [];
        if (classify)
        {
            int meaningfulCount = raw.Count(s => s.Points.Count > 3 ||
                (s.IsClosed && GeometryFilterService.BoundingBoxDiagonal(s.Points) > 10.0));
            geometry.IsVectorPdf = meaningfulCount >= 5;
            var thinnedFates = new List<PathFate>();
            var footingPieces = PdfPlanReader.ReadFootings(raw, content, geometry, request.MarkupOnly, scaleFactor, furniture);
            GeometryFilterService.Classify(raw, geometry,
                options.SlabMinDiagonalMm, options.LineMinLengthMm, false,
                geometry.PageWidthPts * scaleFactor, geometry.PageHeightPts * scaleFactor,
                request.MarkupOnly, options.ColumnMaxSizeMm, options.ColumnMinDimMm, options.ColumnMaxAspect,
                furniture.Scaled(scaleFactor), thinnedFates,
                options.MinWallThicknessMm, options.MaxWallThicknessMm, options.MinWallLengthMm, options.MinWallAspect,
                footingPieces);
            pathFates = RemapToPopulation(thinnedFates, thinnedKept, fullKept, content.Paths.Count, full.Paths.Count);
        }
        var wordFates = WordFates(content, furniture, grid, columns.Count, footings.Count, walls.Count);
        int inked = 0, noInk = 0, paper = 0, annotationPaths = 0;
        var inkedPathIndices = new HashSet<int>();
        for (int pathIndex = 0; pathIndex < full.Paths.Count; pathIndex++)
        {
            var path = full.Paths[pathIndex];
            if (path.IsAnnotation) { annotationPaths++; continue; }
            if (!path.IsFilled && !path.IsStroked) { noInk++; continue; }
            if (path.IsFilled && !path.IsStroked && path.Color.R >= 0xF0 && path.Color.G >= 0xF0 && path.Color.B >= 0xF0)
            { paper++; continue; }
            inked++;
            inkedPathIndices.Add(pathIndex);
        }
        var markup = new List<MarkupNote>();
        int links = 0, linksWithTarget = 0;
        var annotTypes = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            foreach (var a in page.ExperimentalAccess.GetAnnotations())
            {
                if (a.Type == AnnotationType.Link)
                {
                    links++;
                    if (a.AnnotationDictionary.Data.ContainsKey("Dest") || a.AnnotationDictionary.Data.ContainsKey("A")) linksWithTarget++;
                    continue;
                }
                string type = a.Type.ToString();
                annotTypes[type] = annotTypes.GetValueOrDefault(type) + 1;
                string text = (a.Content ?? "").Trim();
                string author = a.AnnotationDictionary.Data.TryGetValue("T", out var t) && t is StringToken st ? st.Data : "";
                if (text.Length > 0) markup.Add(new MarkupNote(type, text, author, pageNumber));
            }
        }
        catch { }

        int invisible = 0, nonHorizontal = 0, nonRgb = 0;
        var fonts = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in page.Letters)
        {
            if (l.RenderingMode is TextRenderingMode.Neither or TextRenderingMode.NeitherClip) invisible++;
            if (l.TextOrientation != TextOrientation.Horizontal) nonHorizontal++;
            if (l.FontName is not null) fonts.Add(l.FontName);
            try { if (l.FillColor is not null && l.FillColor.ColorSpace != ColorSpace.DeviceRGB && l.FillColor.ColorSpace != ColorSpace.DeviceGray) nonRgb++; } catch { }
        }
        int clipOps = 0;
        try { clipOps = page.Operations.Count(op => op.Operator is "W" or "W*"); } catch { }

        PlanScheduleAgreement? agreement = null;
        if (columns.Count > 0 && geometry.Columns.Count > 0)
        {
            try { agreement = PlanAgreesWithItsSchedule.Check(geometry, columns, content,
                options.AgreementToleranceMm, options.AgreementLabelReachMm); }
            catch (Exception ex) { agreementError = ex.GetType().Name; }
        }
        return new SheetRecord(pageNumber, page.Width, page.Height, page.Rotation.Value,
            SheetTitleReader.SheetNumberToken(content), bookmark, sheetType, title?.Level, title?.Zone,
            scale, request.ScaleDenominator, geometry, schedules, marks, callouts, grid, furniture,
            markup, links, full, pathFates, wordFates)
        {
            ColumnAgreement = agreement, ColumnAgreementError = agreementError,
            TitleBlock = fields, ScaleStatement = scaleStatement,
            Context = new SheetContext
            {
                OutlinesPresent = facts.OutlinesPresent, ScheduleHeadings = headings.Count,
                AnnotationTypes = annotTypes, LinksWithTarget = linksWithTarget, Images = page.NumberOfImages,
                NonHorizontalLetters = nonHorizontal, InvisibleLetters = invisible, NonRgbLetters = nonRgb,
                ClippingOperations = clipOps, Fonts = fonts.Count,
                AnnotationPaths = annotationPaths, NoInkPaths = noInk, PaperPaths = paper, InkedPaths = inked,
                InkedPathIndices = inkedPathIndices,
            },
        };
    }

    /// <summary>
    /// Every fate the classifier recorded against the thinned read, re-indexed onto the unthinned
    /// population by subpath ordinal, plus a <see cref="PathReason.CollapsedByThinning"/> fate for
    /// every population path the thinning removed. Annotation paths are appended after content
    /// paths by both reads in the same order, so the k-th annotation path is the k-th in each.
    /// </summary>
    /// <remarks>
    /// Every index in [0, fullPathCount) comes back exactly once. A thinned path whose ordinal is
    /// not in the full read cannot exist (thinning only removes points, so the thinned set is a
    /// subset of the full one) and is skipped rather than invented if it ever does.
    /// </remarks>
    public static IReadOnlyList<PathFate> RemapToPopulation(
        IReadOnlyList<PathFate> thinnedFates,
        IReadOnlyList<int> thinnedKeptOrdinals,
        IReadOnlyList<int> fullKeptOrdinals,
        int thinnedPathCount,
        int fullPathCount)
    {
        ArgumentNullException.ThrowIfNull(thinnedFates);
        ArgumentNullException.ThrowIfNull(thinnedKeptOrdinals);
        ArgumentNullException.ThrowIfNull(fullKeptOrdinals);

        var fullIndexOfOrdinal = new Dictionary<int, int>(fullKeptOrdinals.Count);
        for (int i = 0; i < fullKeptOrdinals.Count; i++) fullIndexOfOrdinal[fullKeptOrdinals[i]] = i;

        var result = new PathFate?[fullPathCount];
        foreach (var fate in thinnedFates)
        {
            int j = fate.PathIndex;
            int fullIndex;
            if (j < thinnedKeptOrdinals.Count)
            {
                if (!fullIndexOfOrdinal.TryGetValue(thinnedKeptOrdinals[j], out fullIndex)) continue;
            }
            else
            {
                // an annotation path: the k-th after the content paths, in both reads
                int k = j - thinnedKeptOrdinals.Count;
                fullIndex = fullKeptOrdinals.Count + k;
                if (k < 0 || fullIndex >= fullPathCount || j >= thinnedPathCount) continue;
            }
            result[fullIndex] = fate with { PathIndex = fullIndex };
        }
        for (int i = 0; i < fullPathCount; i++)
            result[i] ??= new PathFate(i, Disposition.Discarded, PathReason.CollapsedByThinning, null);
        return result!;
    }

    // The existing adapters collapse repeated marks and do not retain row-to-heading ownership.
    // Keep one table per kind, with all matching printed headings, rather than inventing ownership.
    private static IReadOnlyList<ScheduleTable> ScheduleTables(
        IReadOnlyList<ColumnScheduleRow> columns, IReadOnlyList<FootingScheduleReader.FootingType> footings,
        IReadOnlyList<ScheduleGridReader.FlatWallScheduleRow> walls,
        IReadOnlyList<MarkRowScheduleReader.ScheduleHeading> headings)
    {
        string Heading(Func<string, bool> matches) => string.Join(" | ",
            headings.Select(h => h.Title).Where(t => matches(t.ToUpperInvariant())).Distinct(StringComparer.Ordinal));
        static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        static string Optional(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "";
        var tables = new List<ScheduleTable>();
        if (columns.Count > 0)
            tables.Add(new ScheduleTable(Heading(t => t.Contains("COLUMN")), "column",
                columns.Select(r => new ScheduleRow(r.Mark, new Dictionary<string, string>
                {
                    ["WidthMm"] = Number(r.WidthMm), ["DepthMm"] = Number(r.DepthMm),
                    ["StrengthMPa"] = Optional(r.StrengthMPa), ["Reinforcing"] = r.Reinforcing ?? "",
                    ["Ties"] = r.Ties ?? "", ["SizeVaries"] = r.SizeVaries.ToString(),
                }, r.Route.ToString())).ToList()));
        if (footings.Count > 0)
            tables.Add(new ScheduleTable(Heading(t => t.Contains("FOUNDATION") || t.Contains("FOOTING")), "footing",
                footings.Select(r => new ScheduleRow(r.Mark, new Dictionary<string, string>
                {
                    ["LengthMm"] = Number(r.LengthMm), ["WidthMm"] = Number(r.WidthMm), ["DepthMm"] = Number(r.DepthMm),
                }, r.Route.ToString())).ToList()));
        if (walls.Count > 0)
            tables.Add(new ScheduleTable(Heading(t => t.Contains("WALL") && !t.Contains("ZONE")), "shear-wall",
                walls.Select(r => new ScheduleRow(r.Mark, new Dictionary<string, string>
                {
                    ["ThicknessIn"] = Number(r.ThicknessIn), ["StrengthMPa"] = Optional(r.StrengthMPa),
                    ["RowText"] = r.RowText,
                }, r.Route.ToString())).ToList()));
        return tables;
    }

    private static IReadOnlyList<WordFate> WordFates(VectorPageReader.PageContent content,
        SheetFurniture.Set furniture, GridBubbles.Grid grid, int colRows, int footRows, int wallRows)
    {
        bool ReaderClaims(string regionKind)
        {
            string t = regionKind.ToUpperInvariant();
            if (colRows > 0 && t.Contains("COLUMN")) return true;
            if (footRows > 0 && (t.Contains("FOUNDATION") || t.Contains("FOOTING"))) return true;
            if (wallRows > 0 && t.Contains("WALL") && !t.Contains("ZONE")) return true;
            return false;
        }

        var wordFates = new List<WordFate>();
        int wordIndex = 0;
        void Word(string cls, Disposition d, string by) => wordFates.Add(new WordFate(wordIndex, d, cls, by));
        for (wordIndex = 0; wordIndex < content.Words.Count; wordIndex++)
        {
            var w = content.Words[wordIndex];
            var region = furniture.Regions.FirstOrDefault(r => r.Contains(w.Cx, w.Cy));
            if (region.Kind is not null)
            {
                if (region.Kind == "title block")
                    Word("words: title block (number, title, revisions, dates, drawn by …)", Disposition.Unread, "SheetTitleReader/SheetScaleReader read level, zone and scale only");
                else if (region.Kind.StartsWith("schedule:", StringComparison.Ordinal))
                {
                    if (ReaderClaims(region.Kind)) Word("words: in a schedule a reader read", Disposition.Read, "MarkRowScheduleReader (column, footing, flat shear wall)");
                    else { Word("words: in a schedule with no reader (stirrup, zone, beam, slab reinforcing …)", Disposition.Unread, "no reader"); }
                }
                else
                    Word("words: in a notes box, legend or titled furniture", Disposition.Unread, "SheetFurniture drops the box; the notes inside are not read");
                continue;
            }
            if (grid.Bubbles.Any(b => (b.OnVerticalAxis || b.OnHorizontalAxis)
                                       && Math.Abs(b.Cx - w.Cx) <= b.Radius && Math.Abs(b.Cy - w.Cy) <= b.Radius))
            {
                Word("words: grid axis names", Disposition.Discarded, "GridBubbles — the name is not kept");
                continue;
            }
            string kind = KindOf(w.Text);
            var (disp, by) = kind switch
            {
                "mark"              => (Disposition.Read,        "PlanAgreesWithItsSchedule (as a label to check columns against; not kept as data)"),
                "thickness callout" => (Disposition.Read,        "SlabThicknessZoner"),
                "scale"             => (Disposition.Unread,      "SheetScaleReader reads the title block only"),
                "level/storey"      => (Disposition.Unread,      "SheetTitleReader reads the title block only"),
                "grid label"        => (Disposition.Unaccounted, "a letter or two outside any bubble: grid name, or a fragment"),
                "note"              => (Disposition.Unread,      "no reader"),
                "dimension (ft-in)" => (Disposition.Unread,      "no reader"),
                "dimension (mm)"    => (Disposition.Unread,      "no reader"),
                "rebar"             => (Disposition.Unread,      "RebarPdfReader reads it, in another tool, through another extractor"),
                "strength/material" => (Disposition.Unread,      "no reader outside a schedule cell"),
                "sheet reference"   => (Disposition.Unread,      "no reader"),
                "elevation"         => (Disposition.Unread,      "elev-scan exists; not part of intake"),
                _                   => (Disposition.Unaccounted, "no kind matched"),
            };
            Word($"words on the plan: {kind}", disp, by);
        }
        return wordFates;

    }

    // ── Text kinds ───────────────────────────────────────────────────────────────────────────
    //
    // First match wins, so the order is from most to least specific. Measured on the five sets
    // before being written: a mark is checked before a grid label because 31202 circles its
    // column marks as bare numerals, and a sheet reference before a note because "S3.01" also
    // reads as a word.
    public static readonly (string Kind, Regex Rx)[] TextKinds =
    {
        ("scale",             new Regex(@"^\s*\d+/\d+""\s*=\s*1'-0""\s*$|^\s*1\s*:\s*\d{2,4}\s*$|^\s*SCALE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("sheet reference",   new Regex(@"^\s*[SA]-?\d{1,2}\.\d{1,2}(\.\d)?\s*$|^\s*\d{1,2}\s*/\s*S\d", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("dimension (ft-in)", new Regex(@"^\s*\d+'(?:-\s*\d+(?:\s*\d+/\d+)?"")?\s*$|^\s*\d+(?:\s*\d+/\d+)?""\s*$", RegexOptions.Compiled)),
        ("dimension (mm)",    new Regex(@"^\s*\d{3,5}\s*$", RegexOptions.Compiled)),
        ("mark",              new Regex(@"^(?:[A-Z]{1,3}\d{1,2}[A-Z]?(?:-[A-Z0-9]{1,3})?|\d{1,2})$", RegexOptions.Compiled)),
        ("grid label",        new Regex(@"^\s*[A-Z]{1,2}\s*$", RegexOptions.Compiled)),
        ("rebar",             new Regex(@"\b\d{2}M\b|#\d\b|\b\d+\s*-\s*\d{2}M|@\s*\d+""|\bEF\b|\bE\.F\.|\bT&B\b|\bVERT\.?|\bHORIZ\.?", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("strength/material", new Regex(@"\b(MPa|ksi|psi|f'c|fy|GRADE\s*\d{2,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("thickness callout", new Regex(@"^\s*\d{1,3}\s*(""|mm)?\s*(THK|THICK|SLAB|S\.?O\.?G)\b|^\s*\d+""\s*(x|X)\s*\d+""", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("elevation",         new Regex(@"^(EL\.?|ELEV\.?|T\.?O\.?S\.?|T/O|U/S|B/S|TOP OF|BOT\.? OF)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("level/storey",      new Regex(@"\b(LEVEL|LVL|PARKADE|ROOF|MEZZ|MEZZANINE|BASEMENT|GROUND|P\d)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("note",              new Regex(@"[A-Za-z]{3,}\s+[A-Za-z]{3,}\s+[A-Za-z]{2,}", RegexOptions.Compiled)),
    };

    // First match wins. Schedule first; then plan, tested before section so that "LEVEL 19 PLAN AND
    // ROOF PLAN AND ELEVATIONS" (31065) is a plan; FOUNDATION(S) is a plan word — 31138 titles its
    // foundation sheet "FOUNDATIONS" and 31168 titles S1.11 "FOUNDATION PLANS" — unless DETAILS or
    // SCHEDULE follows it.
    public static readonly (string Type, Regex Rx)[] SheetTypes =
    {
        ("schedule",          new Regex(@"SCHEDULE", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("plan",              new Regex(@"\bPLANS?\b|\bFOUNDATIONS?\b(?!\s+(DETAILS?|SCHEDULES?|NOTES?)\b)", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("section/elevation", new Regex(@"SECTION|ELEVATION", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("details",           new Regex(@"DETAIL", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        ("notes/general",     new Regex(@"NOTES|GENERAL|LEGEND|ABBREV", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        // not TITLE: every title block on every sheet says "SHEET TITLE", and it classified 11 of
        // 31130's 60 sheets as covers before that was seen
        ("cover/index",       new Regex(@"COVER SHEET|COVER PAGE|DRAWING (LIST|INDEX)|SHEET INDEX|\b3D\b.*\bVIEW|PRESENTATION VIEW", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
    };

    /// <summary>
    /// The first source that names a kind wins; a source that exists but says nothing recognisable
    /// yields to the next. Chaining by nullness instead stopped at a title text of "S4.01 REVISIONS"
    /// and typed 21 of 31130's sheets "other" that the title block's words had typed correctly.
    /// </summary>
    public static string FirstTyped(params string?[] sources)
    {
        bool any = false;
        foreach (var s in sources)
        {
            if (string.IsNullOrWhiteSpace(s)) continue;
            any = true;
            string t = SheetTypeOf(s);
            if (t != "other" && t != "unknown") return t;
        }
        return any ? "other" : "unknown";
    }

    /// <summary>The kind of sheet a title names; "unknown" when the title says nothing recognisable.</summary>
    public static string SheetTypeOf(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "unknown";
        foreach (var (type, rx) in SheetTypes)
            if (rx.IsMatch(title)) return type;
        return "other";
    }

    /// <summary>The kind of thing a word is, by its shape alone.</summary>
    public static string KindOf(string text)
    {
        foreach (var (kind, rx) in TextKinds)
            if (rx.IsMatch(text)) return kind;
        return "other";
    }

}
