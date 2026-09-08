#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.PdfToSafe;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Annotations;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Graphics.Colors;
using UglyToad.PdfPig.Outline;
using UglyToad.PdfPig.Tokens;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// THE LEDGER: everything a drawing page carries, by kind, and what the intake did with each kind.
    /// </summary>
    /// <remarks>
    /// The intake's purpose is to pull everything a drawing set carries that any downstream tool
    /// could need, once, and to ACCOUNT for what it did not pull. Until 2026-09-08 nothing in this
    /// repo could say what a page carried that no reader consumed; every gap was found by an
    /// engineer opening the file. This class is the instrument that turns "pulling everything it
    /// can" from a claim into a number.
    ///
    /// Every word and every vector path on the page is counted ONCE, in a primary row, under one of
    /// five dispositions:
    ///   Read        — a named reader consumed it and produced something from it
    ///   Discarded   — a named rule decided it is not structure (furniture, frame, grid line)
    ///   Unread      — a class the intake knows about and has no reader for
    ///   Ignored     — by design: nothing structural is ever in it (logos, 3D-view image tiles)
    ///   Unaccounted — the ledger cannot yet say; this is the class step 1 exists to empty
    /// Context rows (<see cref="LedgerRow.Primary"/> false) carry derived facts — rows a reader
    /// produced, bubbles found, letters by orientation — and are NOT summed into the totals.
    ///
    /// WHAT IT COVERS (measured on the five stick files, 294 pages, 2026-09-08): words by kind and
    /// by where they sit (a table a reader claimed, a table nobody reads, a notes box, the title
    /// block, a grid bubble, the plan); paths by ink and by whether the classifier emitted them;
    /// annotations by type with their text and author; links; the sheet index (bookmarks), and
    /// whether the file has an outline tree PdfPig cannot read; images; clipping operations; page
    /// rotation; letters by orientation and rendering mode; fonts; the document's producer.
    ///
    /// WHAT IT DOES NOT COVER, YET: which PATH became which object. "Paths not emitted" is one
    /// number for grid lines the furniture rule dropped and wall faces nothing read; both count as
    /// Unaccounted here, and separating them is step 1. Emitted objects are not paths one for one
    /// (a column is one path, a slab may be several), so the path rows are the classifier's output
    /// counted against its input, not a per-path audit. A same-class fault it would NOT catch: a
    /// reader that consumes a word and produces the wrong value from it — that is the harness's
    /// job (<c>FiveStickFilesTests</c>). Text drawn as outlines (glyphs exported as paths) shows
    /// here as few words and many small fills; the ledger makes that visible and does not name it.
    /// </remarks>
    public static class SheetInventory
    {
        public enum Disposition { Read, Discarded, Unread, Ignored, Unaccounted }

        public sealed record LedgerRow(string Class, int Count, Disposition Disposition, string By, bool Primary = true);

        public sealed record MarkupNote(string Type, string Text, string Author, int PageNumber);

        public sealed record SheetLedger(
            int PageNumber,
            double WidthPts,
            double HeightPts,
            int Rotation,
            string? BookmarkTitle,
            string SheetType,
            string? Title,
            string? ScaleNote,
            int Words,
            int Paths,
            IReadOnlyList<LedgerRow> Rows,
            IReadOnlyList<MarkupNote> Markup);

        public sealed record DocumentFacts(
            int Pages,
            string? Producer,
            string? Creator,
            IReadOnlyDictionary<int, string> Bookmarks,
            bool OutlinesPresent,
            bool Encrypted);

        // ── Text kinds ───────────────────────────────────────────────────────────────────────────
        //
        // First match wins, so the order is from most to least specific. Measured on the five sets
        // before being written: a mark is checked before a grid label because 31202 circles its
        // column marks as bare numerals, and a sheet reference before a note because "S3.01" also
        // reads as a word.
        private static readonly (string Kind, Regex Rx)[] TextKinds =
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

        private static readonly (string Type, Regex Rx)[] SheetTypes =
        {
            ("schedule",          new Regex(@"SCHEDULE", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("plan",              new Regex(@"\bPLAN\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("section/elevation", new Regex(@"SECTION|ELEVATION", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("details",           new Regex(@"DETAIL", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            ("notes/general",     new Regex(@"NOTES|GENERAL|LEGEND|ABBREV", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
            // not TITLE: every title block on every sheet says "SHEET TITLE", and it classified 11 of
            // 31130's 60 sheets as covers before that was seen
            ("cover/index",       new Regex(@"COVER SHEET|COVER PAGE|DRAWING (LIST|INDEX)|SHEET INDEX|3D VIEW", RegexOptions.IgnoreCase | RegexOptions.Compiled)),
        };

        /// <summary>What the document says about itself, and its sheet index if it carries one.</summary>
        public static DocumentFacts Facts(PdfDocument doc)
        {
            ArgumentNullException.ThrowIfNull(doc);
            var marks = new Dictionary<int, string>();
            if (doc.TryGetBookmarks(out var bookmarks))
            {
                void Walk(IEnumerable<BookmarkNode> nodes)
                {
                    foreach (var n in nodes)
                    {
                        if (n is DocumentBookmarkNode d && !marks.ContainsKey(d.PageNumber))
                            marks[d.PageNumber] = d.Title ?? "";
                        Walk(n.Children);
                    }
                }
                Walk(bookmarks.Roots);
            }
            bool outlines = false;
            try { outlines = doc.Structure.Catalog.CatalogDictionary.Data.ContainsKey("Outlines"); } catch { }
            var info = doc.Information;
            return new DocumentFacts(doc.NumberOfPages, info?.Producer, info?.Creator, marks, outlines, doc.IsEncrypted);
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

        /// <summary>
        /// Ledger one page. With a scale the geometry is classified the way <c>pdf-takeoff</c>
        /// classifies it and the emitted counts are Read rows; without one the paths are counted
        /// but not classified, and the ledger says so.
        /// </summary>
        public static SheetLedger Of(PdfDocument doc, int pageNumber, int? scaleDenominator, PdfIntakeOptions options, DocumentFacts facts)
        {
            ArgumentNullException.ThrowIfNull(doc);
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(facts);

            var page = doc.GetPage(pageNumber);
            var content = VectorPageReader.ReadPage(page, includeAnnotations: true, curveSegments: PdfToSafeConstants.BezierSegments);
            var rows = new List<LedgerRow>();
            void Row(string cls, int n, Disposition d, string by) => rows.Add(new LedgerRow(cls, n, d, by));
            void Note(string cls, int n, Disposition d, string by) => rows.Add(new LedgerRow(cls, n, d, by, Primary: false));

            // ── the sheet ───────────────────────────────────────────────────────────────────────
            string? bookmark = facts.Bookmarks.TryGetValue(pageNumber, out var bt) ? bt : null;
            string? title = null, scale = null;
            try { title = SheetTitleReader.FromPage(content)?.Display; } catch { /* a title the reader cannot form is a ledger fact, not a failure */ }
            try { scale = SheetScaleReader.FromPage(content); } catch { }

            var furniture = SheetFurniture.On(content);
            var grid = GridBubbles.On(content);

            // The sheet's own words about itself: the bookmark, else the title block's text.
            var titleBlock = furniture.Regions.Where(r => r.Kind == "title block").ToList();
            string titleBlockText = string.Join(" ", content.Words
                .Where(w => titleBlock.Count > 0
                    ? titleBlock.Any(r => r.Contains(w.Cx, w.Cy))
                    : w.Cx > 0.8 * content.WidthPts && w.Cy < 0.2 * content.HeightPts)
                .Select(w => w.Text));
            string sheetType = SheetTypeOf(bookmark ?? (titleBlockText.Length > 0 ? titleBlockText : title));

            Row("sheet title (level and zone)", 1, title is null ? Disposition.Unread : Disposition.Read, title is null ? "SheetTitleReader found none" : "SheetTitleReader");
            Row("scale note", 1, scale is null ? Disposition.Unread : Disposition.Read, scale is null ? "SheetScaleReader found none" : "SheetScaleReader");
            if (bookmark is not null) Row("bookmark (sheet index entry)", 1, Disposition.Unread, "no reader");
            else if (facts.OutlinesPresent) Row("bookmark present in the file, unreadable by PdfPig", 1, Disposition.Unaccounted, "PdfDocument.TryGetBookmarks returned none for an /Outlines tree");
            if (page.Rotation.Value != 0) Row("page rotation != 0", 1, Disposition.Unaccounted, "no reader corrects for it");

            // ── words, each once ────────────────────────────────────────────────────────────────
            int colRows = 0, footRows = 0, wallRows = 0, headings = 0, callouts = 0;
            try { colRows = ColumnScheduleReader.ReadSchedule(content).Count; } catch { }
            try { footRows = FootingScheduleReader.ReadSchedule(content).Types.Count; } catch { }
            try { wallRows = ScheduleGridReader.ReadFlatWallRows(content).Count; } catch { }
            try { headings = MarkRowScheduleReader.SchedulesOn(content, MarkRowScheduleReader.ColumnDefaults() with { HeadingWords = Array.Empty<string>() }).Count; } catch { }
            try { callouts = SlabThicknessZoner.ReadCallouts(content).Count; } catch { }

            bool ReaderClaims(string regionKind)
            {
                string t = regionKind.ToUpperInvariant();
                if (colRows > 0 && t.Contains("COLUMN")) return true;
                if (footRows > 0 && (t.Contains("FOUNDATION") || t.Contains("FOOTING"))) return true;
                if (wallRows > 0 && t.Contains("WALL") && !t.Contains("ZONE")) return true;
                return false;
            }

            var wordRows = new Dictionary<(string Class, Disposition D, string By), int>();
            void Word(string cls, Disposition d, string by) { var k = (cls, d, by); wordRows[k] = wordRows.GetValueOrDefault(k) + 1; }
            int unreadTableWords = 0;
            foreach (var w in content.Words)
            {
                var region = furniture.Regions.FirstOrDefault(r => r.Contains(w.Cx, w.Cy));
                if (region.Kind is not null)
                {
                    if (region.Kind == "title block")
                        Word("words: title block (number, title, revisions, dates, drawn by …)", Disposition.Unread, "SheetTitleReader/SheetScaleReader read level, zone and scale only");
                    else if (region.Kind.StartsWith("schedule:", StringComparison.Ordinal))
                    {
                        if (ReaderClaims(region.Kind)) Word("words: in a schedule a reader read", Disposition.Read, "MarkRowScheduleReader (column, footing, flat shear wall)");
                        else { unreadTableWords++; Word("words: in a schedule with no reader (stirrup, zone, beam, slab reinforcing …)", Disposition.Unread, "no reader"); }
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
            foreach (var (k, n) in wordRows.OrderByDescending(kv => kv.Value)) Row(k.Class, n, k.D, k.By);

            // ── paths, each once (as the classifier's output against its input) ─────────────────
            int inked = 0, noInk = 0, paper = 0, annotationPaths = 0;
            foreach (var p in content.Paths)
            {
                if (p.IsAnnotation) { annotationPaths++; continue; }
                if (!p.IsFilled && !p.IsStroked) { noInk++; continue; }
                if (p.IsFilled && !p.IsStroked && p.Color.R >= 0xF0 && p.Color.G >= 0xF0 && p.Color.B >= 0xF0) { paper++; continue; }
                inked++;
            }
            Row("paths: from annotations (Bluebeam geometry)", annotationPaths, Disposition.Read, "VectorPageReader.ReadAnnotationPaths");
            Row("paths: no ink (clip or invisible)", noInk, Disposition.Discarded, "GeometryFilterService");
            Row("paths: paper-coloured fill, no stroke (invisible ink)", paper, Disposition.Discarded, "GeometryFilterService.IsPaper");

            ExtractedGeometry? geo = null;
            if (scaleDenominator is int denom && denom > 0)
            {
                try { geo = PdfPlanReader.Read(doc, denom, pageNumber, options, annotationsOnly: false); } catch { }
            }
            if (geo is not null)
            {
                int emitted = geo.Slabs.Count + geo.Columns.Count + geo.Lines.Count;
                Row("paths: emitted as slabs", geo.Slabs.Count, Disposition.Read, "GeometryFilterService → DXF layer SLAB");
                Row("paths: emitted as columns", geo.Columns.Count, Disposition.Read, "GeometryFilterService → DXF layer COLUMN");
                Row("paths: emitted as lines", geo.Lines.Count, Disposition.Read, "GeometryFilterService → DXF layer BEAM (every leftover line)");
                Row("paths: not emitted (furniture, frame, grid, wall faces, dimensions — undifferentiated)", Math.Max(0, inked - emitted), Disposition.Unaccounted, "per-path disposition is step 1");
                Note("emitted: walls", 0, Disposition.Unread, "no wall reader on the PDF side");
                Note("emitted: footings as objects", 0, Disposition.Unread, "FootingScheduleReader counts placements; nothing is emitted");
                try
                {
                    var declared = ColumnScheduleReader.ReadSchedule(content);
                    if (declared.Count > 0 && geo.Columns.Count > 0)
                    {
                        var check = PlanAgreesWithItsSchedule.Check(geo, declared, content, options.AgreementToleranceMm, options.AgreementLabelReachMm);
                        Note("plan labels standing at an emitted column", check.MatchedToTheirOwnMark, Disposition.Read, $"PlanAgreesWithItsSchedule, of {check.LabelsOnThePlan} labels");
                        Note("schedule marks never placed on the plan", check.MarksDeclaredButNeverFound.Count, Disposition.Unread, string.Join(",", check.MarksDeclaredButNeverFound));
                    }
                }
                catch { }
            }
            else
            {
                Row("paths: inked, not classified (no scale given)", inked, Disposition.Unaccounted, "pass --scale, or read one off the sheet");
            }

            // ── annotations, links, images ──────────────────────────────────────────────────────
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
            foreach (var (type, n) in annotTypes.OrderByDescending(kv => kv.Value))
                Note($"annotations: {type}", n, Disposition.Read, "VectorPageReader.ReadAnnotationPaths (geometry only)");
            Row("annotation text (engineer's comments)", markup.Count, Disposition.Unread, "read only by the WPF PdfGeometryParser; Core never sees it");
            Row("links (callout → sheet)", links, Disposition.Unread, "no reader");
            Row("images", page.NumberOfImages, Disposition.Ignored, "by design: logos, stamps, 3D-view tiles; none structural on the five sets");

            // ── context: what the readers produced, and what the page is made of ────────────────
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

            Note("schedule headings (the word SCHEDULE at the head of a line)", headings, Disposition.Read, "MarkRowScheduleReader.SchedulesOn");
            Note("schedule rows read: column", colRows, Disposition.Read, "ColumnScheduleReader");
            Note("schedule rows read: footing", footRows, Disposition.Read, "FootingScheduleReader");
            Note("schedule rows read: flat shear wall", wallRows, Disposition.Read, "ScheduleGridReader.ReadFlatWallRows");
            Note("words in schedules with no reader", unreadTableWords, Disposition.Unread, "no reader");
            Note("thickness callouts", callouts, Disposition.Read, "SlabThicknessZoner");
            int bubblesWithAxis = grid.Bubbles.Count(b => b.OnVerticalAxis || b.OnHorizontalAxis);
            Note("grid bubbles with an axis", bubblesWithAxis, Disposition.Discarded, "GridBubbles — drops grid lines; names and positions not exported");
            Note("labelled circles without an axis (circled marks, callouts)", grid.Bubbles.Count - bubblesWithAxis, Disposition.Unaccounted, "kept as labels; not typed");
            Note("grid axes (vertical + horizontal)", grid.VerticalAxesX.Count + grid.HorizontalAxesY.Count, Disposition.Discarded, "GridBubbles — not exported");
            Note("furniture regions (schedules, titled boxes, title block)", furniture.Regions.Count, Disposition.Discarded, "SheetFurniture");
            Note("underlines", furniture.Underlines.Count, Disposition.Discarded, "SheetFurniture");
            Note("letters, non-horizontal", nonHorizontal, Disposition.Read, "VectorPageReader (nearest-neighbour extractor keeps orientation)");
            Note("letters, invisible rendering mode (OCR layer / hidden text)", invisible, invisible > 0 ? Disposition.Unaccounted : Disposition.Read, invisible > 0 ? "read as ordinary text; provenance not recorded" : "none present");
            Note("letters filled in a non-RGB/Gray colour space", nonRgb, nonRgb > 0 ? Disposition.Unaccounted : Disposition.Read, nonRgb > 0 ? "paper-fill rule assumes RGB" : "none present");
            Note("clipping operations (content may lie outside its viewport)", clipOps, Disposition.Unaccounted, "no reader honours clips");
            Note("fonts, distinct", fonts.Count, Disposition.Ignored, "by design");

            return new SheetLedger(pageNumber, page.Width, page.Height, page.Rotation.Value, bookmark, sheetType, title, scale,
                                   content.Words.Count, content.Paths.Count, rows, markup);
        }

        /// <summary>Sum a set of page ledgers by class and disposition, primary and context kept apart.</summary>
        public static IReadOnlyList<LedgerRow> Summarise(IEnumerable<SheetLedger> ledgers)
        {
            return ledgers.SelectMany(l => l.Rows)
                .GroupBy(r => (r.Class, r.Disposition, r.Primary))
                .Select(g => new LedgerRow(g.Key.Class, g.Sum(r => r.Count), g.Key.Disposition,
                    string.Join(" | ", g.Select(r => r.By).Distinct(StringComparer.Ordinal).Take(2)), g.Key.Primary))
                .OrderByDescending(r => r.Primary)
                .ThenBy(r => r.Disposition)
                .ThenByDescending(r => r.Count)
                .ToList();
        }

        /// <summary>The five dispositions as totals over PRIMARY rows only, so one line can say where a document stands.</summary>
        public static IReadOnlyDictionary<Disposition, int> Totals(IEnumerable<LedgerRow> rows)
        {
            var t = new Dictionary<Disposition, int>();
            foreach (Disposition d in Enum.GetValues<Disposition>()) t[d] = 0;
            foreach (var r in rows.Where(r => r.Primary)) t[r.Disposition] += r.Count;
            return t;
        }
    }
}
