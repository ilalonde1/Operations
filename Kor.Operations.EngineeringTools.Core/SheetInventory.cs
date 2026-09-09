using Kor.Operations.EngineeringTools.Intake;
using Kor.Operations.EngineeringTools.PdfToSafe;
using UglyToad.PdfPig;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>A report over a retained sheet read: each word and classified path is counted once.</summary>
    /// <remarks>
    /// Covers the recorded decision for each classifier input, word rules, and retained page context.
    /// It cannot detect an incorrect decision shared by the reader and its ledger, or judge whether a
    /// line represents a wall. EmittedAsLine remains Unaccounted until a reader gives it meaning.
    /// Path indices refer to Content.Paths, including annotation and invisible paths. The report
    /// preserves the annotation/no-ink/paper partition; the remaining inked paths group by fate.
    /// The complete classifier decisions, including those provenance groups, remain in PathFates.
    /// </remarks>
    public static class SheetInventory
    {
        public sealed record LedgerRow(string Class, int Count, Disposition Disposition, string By, bool Primary = true);

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
            IReadOnlyList<MarkupNote> Markup,
            IReadOnlyList<GridAxisNote> GridAxes);

        /// <summary>A named grid axis on the page, in millimetres; Dir "X" is a vertical line at x = AtMm, "Y" a horizontal one.</summary>
        public sealed record GridAxisNote(string Name, string Dir, double AtMm);


        // Forwarding entry points retain existing callers outside this step's permitted file scope.
        public static DocumentFacts Facts(PdfDocument doc) => DocumentFacts.From(doc);
        public static SheetLedger Of(PdfDocument doc, int pageNumber, int? scaleDenominator, PdfIntakeOptions options, DocumentFacts facts)
            => Of(DrawingIntake.ReadSheet(doc, pageNumber, new IntakeRequest(scaleDenominator, options), facts));

        public static SheetLedger Of(SheetRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            var rows = new List<LedgerRow>();
            void Row(string cls, int n, Disposition d, string by) => rows.Add(new LedgerRow(cls, n, d, by));
            void Note(string cls, int n, Disposition d, string by) => rows.Add(new LedgerRow(cls, n, d, by, Primary: false));
            string? title = record.Level is null ? null : record.Zone is null ? record.Level : $"{record.Level} {record.Zone}";
            Row("sheet title (level and zone)", 1, title is null ? Disposition.Unread : Disposition.Read, title is null ? "SheetTitleReader found none" : "SheetTitleReader");
            Note("title block fields read (SHEET TITLE, SCALE, PROJECT NO, DRAWN BY …)", record.TitleBlock.Count,
                record.TitleBlock.Count > 0 ? Disposition.Read : Disposition.Unread,
                record.TitleBlock.Count > 0 ? "TitleBlockFields: " + string.Join(", ", record.TitleBlock.Keys.OrderBy(k => k)) : "no labelled title block on this sheet");
            if (record.ScaleConflict)
                Row("scale: the title block states two different scales", 1, Disposition.Unaccounted, "SheetScaleReader refused to guess; the SCALE field is not a fallback for a conflict");
            else if (record.ScaleNote is not null)
                Row("scale: ratio read", 1, Disposition.Read, "SheetScaleReader, or the title block's SCALE field");
            else if (!string.IsNullOrWhiteSpace(record.ScaleStatement))
                Row($"scale: stated without a ratio (\"{record.ScaleStatement.Trim()}\")", 1, Disposition.Read, "TitleBlockFields — the sheet says its scale varies; details and notes sheets do");
            else
                Row("scale: none stated", 1, Disposition.Unread, "neither SheetScaleReader nor a SCALE field");
            if (record.BookmarkTitle is not null)
            {
                // the bookmark is the first source the sheet type comes from; when it named a kind it was read (audit F10)
                bool typedTheSheet = DrawingIntake.FirstTyped(record.BookmarkTitle) is not ("other" or "unknown");
                Row("bookmark (sheet index entry)", 1, typedTheSheet ? Disposition.Read : Disposition.Unread,
                    typedTheSheet ? "DrawingIntake.FirstTyped — it typed the sheet" : "names no kind the typing knows; no other reader");
            }
            else if (record.Context.OutlinesPresent) Row("bookmark present in the file, unreadable by PdfPig", 1, Disposition.Unaccounted, "PdfDocument.TryGetBookmarks returned none for an /Outlines tree");
            if (record.Rotation != 0) Row("page rotation != 0", 1, Disposition.Unaccounted, "no reader corrects for it");

            foreach (var group in record.WordFates.GroupBy(f => (f.Kind, f.Disposition, f.Reason)).OrderByDescending(g => g.Count()))
                Row(group.Key.Kind, group.Count(), group.Key.Disposition, group.Key.Reason);

            bool classified = record.ScaleDenominator is > 0;
            if (classified)
            {
                // EVERY PATH'S FATE IS PRIMARY, INK OR NO INK. The first version of this report
                // printed fates for inked paths only and kept "no ink: discarded" as its own row;
                // on 31130 that hid 518 slabs the classifier had emitted from stroke-less,
                // fill-less closed paths — clipping rectangles — because their fate said
                // BecameSlab and the row said Discarded. The fate is the truth; the ink groups
                // are context below.
                foreach (var group in record.PathFates.GroupBy(f => f.Reason).OrderBy(g => g.Key))
                    Row($"paths: {group.Key}", group.Count(), PathFate.DispositionOf(group.Key),
                        group.Key == PathReason.CollapsedByThinning
                            ? "VectorPageReader: fewer than two points after thinning at MinVertexDistanceMm; never reached the classifier"
                            : $"GeometryFilterService.{group.Key}");

                int noInkEmitted = record.PathFates.Count(f =>
                    f.PathIndex < record.Content.Paths.Count
                    && !record.Content.Paths[f.PathIndex].IsFilled && !record.Content.Paths[f.PathIndex].IsStroked
                    && !record.Content.Paths[f.PathIndex].IsAnnotation
                    && f.Reason != PathReason.ClipOfWall   // a clip a wall is drawn through shapes the wall; it is read as that, not emitted
                    && (f.Disposition == Disposition.Read || f.Reason == PathReason.EmittedAsLine));
                Note("no-ink paths (clip or invisible) the classifier emitted as slabs, columns or lines", noInkEmitted,
                    noInkEmitted > 0 ? Disposition.Unaccounted : Disposition.Read,
                    noInkEmitted > 0 ? "a path that draws nothing became geometry; the invisible-ink rule covers paper FILLS only" : "none");
                Note("emitted: walls", record.Geometry.Walls.Count, Disposition.Read, "GeometryFilterService");
                if (record.Geometry.WallFaceLines.Count > 0)
                    Note("emitted: walls read from two face lines a wall's thickness apart (step 20)", record.Geometry.WallFaceLines.Count / 2,
                        Disposition.Read, "GeometryFilterService.WallsFromFaceLines");
                if (record.Geometry.Doorways.Count > 0)
                    Note("emitted: doorways (paper fills knocked out of walls; the walls are their piers)", record.Geometry.Doorways.Count, Disposition.Read, "GeometryFilterService.DoorwaysOn");
                if (record.SheetType != "plan")
                {
                    int onNonPlan = record.Geometry.Walls.Count + record.Geometry.Columns.Count + record.Geometry.Slabs.Count;
                    Note($"geometry emitted on a non-plan sheet ({record.SheetType}): walls + columns + slabs", onNonPlan,
                        onNonPlan > 0 ? Disposition.Unaccounted : Disposition.Read,
                        "the classifier runs on every page; pdf-takeoff writes a DXF from plan sheets only (brief 17)");
                }
                Note("filled wall-thickness shapes with more than four vertices — ribbons, not split",
                    record.Geometry.WallRibbonsNotSplit, Disposition.Unread, "GeometryFilterService: retained with their existing fate");
                // From the record, re-reading nothing (audit F9): the labels the footing reader placed
                // and the schedule table the intake typed.
                int marksPlaced = record.FootingLabels.Count;
                bool scheduled = record.Schedules.Any(t => t.Kind == "footing" && t.Rows.Count > 0);
                string byMark = "";
                if (marksPlaced > 0 || record.Geometry.Footings.Count > 0)
                {
                    var placed = record.FootingLabels.GroupBy(l => l.Mark, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
                    var marks = placed.Keys.Concat(record.Geometry.Footings.Select(f => f.Mark)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
                    // labelled footings of placed labels, per mark, so the ledger says which mark the
                    // chaining is short on; a box of a scheduled size that no label names is listed apart
                    byMark = string.Join(", ", marks.Select(m =>
                        $"{m} {record.Geometry.Footings.Count(f => string.Equals(f.Mark, m, StringComparison.OrdinalIgnoreCase) && f.LabelledOnThePlan)} of {(placed.TryGetValue(m, out int n) ? n : 0)}"));
                    var unlabelled = record.Geometry.Footings.Where(f => !f.LabelledOnThePlan).GroupBy(f => f.Mark).ToList();
                    if (unlabelled.Count > 0)
                        byMark += "; no label names " + string.Join(", ", unlabelled.Select(g => $"{g.Count()} {g.Key}-sized box(es)"));
                }
                // a footing read on a sheet whose plan places no footing mark is a box the size of a
                // footing, and nothing on the sheet says it is one: unaccounted, not read
                Note("footings read as dashed outlines of a scheduled size", record.Geometry.Footings.Count,
                    record.Geometry.Footings.Count > 0 ? (marksPlaced > 0 ? Disposition.Read : Disposition.Unaccounted)
                        : (marksPlaced > 0 ? Disposition.Unread : Disposition.Read),
                    marksPlaced > 0 ? $"FootingOutlines; the plan places {marksPlaced} spread-footing mark(s): {byMark}"
                    : scheduled ? $"the schedule declares spread footings and the plan places no mark{(byMark.Length > 0 ? ": " + byMark : "")}"
                    : "no spread footing scheduled on this sheet");
                if (record.Dimensions.Count > 0)
                {
                    var typed = record.Dimensions.Where(d => !d.BareNumber || d.Agrees).ToList();
                    int agree = typed.Count(d => d.Agrees), disagree = typed.Count(d => d.Disagrees);
                    var off = typed.Where(d => d.Disagrees).Take(4).Select(d => $"{d.Text} between {d.SpansFrom}–{d.SpansTo} {d.AxisGapMm:0} mm");
                    Note("dimension strings typed with a value", typed.Count, Disposition.Read,
                        $"DimensionStrings; {agree} agree with a span of grid axes at the sheet's scale (±{DimensionStrings.AgreeMm:0} mm), {disagree} sit between adjacent axes and state another length"
                        + (disagree > 0 ? ": " + string.Join(", ", off) + (disagree > 4 ? ", ..." : "") : ""));
                    if (agree >= 3)
                        Note("scale confirmed by the drawing's own grid dimensions", agree, Disposition.Read, "DimensionStrings — written lengths between grid axes match the axes' spacing at the stated scale");
                }
                if (record.Storeys.Count > 0)
                {
                    double? typical = StoreyLadder.Typical(record.Storeys);
                    Note("storey heights from the level ladder at the sheet's scale", record.Storeys.Count, Disposition.Read,
                        $"StoreyLadder; typical {typical:0} mm; " + string.Join(", ", record.Storeys.Take(6).Select(s => $"{s.Level} -> {s.LevelBelow} {s.HeightMm:0}")) + (record.Storeys.Count > 6 ? ", ..." : ""));
                }
                if (record.ColumnAgreement is { } check)
                {
                    Note("plan labels standing at an emitted column", check.MatchedToTheirOwnMark, Disposition.Read, $"PlanAgreesWithItsSchedule, of {check.LabelsOnThePlan} labels");
                    Note("schedule marks never placed on the plan", check.MarksDeclaredButNeverFound.Count, Disposition.Unread, string.Join(",", check.MarksDeclaredButNeverFound));
                }
            }

            // Ink provenance. With a classifier run every path already has a primary fate row, so
            // these are context; without one they ARE the partition, and the inked paths are the
            // unaccounted remainder.
            void Provenance(string cls, int n, Disposition d, string by)
            {
                if (classified) Note(cls, n, d, by); else Row(cls, n, d, by);
            }
            Provenance("paths: from annotations (Bluebeam geometry)", record.Context.AnnotationPaths, Disposition.Read, "VectorPageReader.ReadAnnotationPaths");
            int clipsOfWalls = record.PathFates.Count(f => f.Reason == PathReason.ClipOfWall);
            Provenance(clipsOfWalls > 0
                    ? $"paths: no ink (clip or invisible; {clipsOfWalls} of them clips walls are drawn through, read as the walls' piers)"
                    : "paths: no ink (clip or invisible)",
                record.Context.NoInkPaths, Disposition.Discarded, "VectorPageReader ink flags");
            Provenance(record.Geometry.Doorways.Count > 0
                    ? $"paths: paper-coloured fill, no stroke (invisible ink; {record.Geometry.Doorways.Count} of them doorways, read)"
                    : "paths: paper-coloured fill, no stroke (invisible ink)",
                record.Context.PaperPaths, Disposition.Discarded, "GeometryFilterService.IsPaper");
            if (!classified)
                Row("paths: inked, not classified (no scale given)", record.Context.InkedPaths, Disposition.Unaccounted, "pass --scale, or read one off the sheet");

            foreach (var (type, n) in record.Context.AnnotationTypes.OrderByDescending(kv => kv.Value))
                Note($"annotations: {type}", n, Disposition.Read, "VectorPageReader.ReadAnnotationPaths (geometry only)");
            if (record.Context.ScheduleRowsNotMarks > 0)
                Row("schedule rows whose mark is not a mark (a NOTE line, a heading word)", record.Context.ScheduleRowsNotMarks, Disposition.Discarded, "DrawingIntake: a row is a row whose mark is shaped like one");
            Row("annotation text (engineer's comments)", record.Markup.Count, Disposition.Read, "DrawingIntake: PdfPig Annotation.Content and author");
            if (record.Annotations.Count > record.Markup.Count)
                Row("annotations without words (ticks, shapes)", record.Annotations.Count - record.Markup.Count, Disposition.Read, "DrawingIntake: position and author kept; a tick is a reply (MarkupReconcile)");
            Row("links (callout → sheet)", record.Links, Disposition.Unread, "no reader");
            Row("images", record.Context.Images, Disposition.Ignored, "by design: logos, stamps, 3D-view tiles; none structural on the five sets");

            int ScheduleRows(string kind) => record.Schedules.Where(t => t.Kind == kind).Sum(t => t.Rows.Count);
            int unreadTableWords = record.WordFates.Count(f => f.Kind == "words: in a schedule with no reader (stirrup, zone, beam, slab reinforcing …)");
            var grid = record.Grid;
            var furniture = record.Furniture;
            Note("schedule headings (the word SCHEDULE at the head of a line)", record.Context.ScheduleHeadings, Disposition.Read, "MarkRowScheduleReader.SchedulesOn");
            Note("schedule rows read: column", ScheduleRows("column"), Disposition.Read, "ColumnScheduleReader");
            Note("schedule rows read: footing", ScheduleRows("footing"), Disposition.Read, "FootingScheduleReader");
            Note("schedule rows read: flat shear wall", ScheduleRows("shear-wall"), Disposition.Read, "ScheduleGridReader.ReadFlatWallRows");
            Note("words in schedules with no reader", unreadTableWords, Disposition.Unread, "no reader");
            Note("thickness callouts", record.ThicknessCallouts.Count, Disposition.Read, "SlabThicknessZoner");
            int bubblesWithAxis = grid.Bubbles.Count(b => b.OnVerticalAxis || b.OnHorizontalAxis);
            Note("grid bubbles with an axis", bubblesWithAxis, Disposition.Read, "GridBubbles — the ends of the named axes");
            Note("labelled circles without an axis (circled marks, callouts)", grid.Bubbles.Count - bubblesWithAxis, Disposition.Unaccounted, "kept as labels; not typed");
            int disagreeing = grid.Axes.Count(a => a.LabelsDisagree);
            // a name used twice in one direction is a second view on the sheet (a section, a key plan)
            // — the per-view split's signal, listed, not merged
            var twice = grid.Axes.GroupBy(a => (a.Vertical, a.Name), (k, g) => (k.Name, N: g.Count())).Where(t => t.N > 1).Select(t => t.Name).ToList();
            Note("grid axes, named (vertical + horizontal)", grid.Axes.Count, Disposition.Read,
                grid.Axes.Count == 0 ? "GridBubbles — none on this sheet"
                : $"GridBubbles.Axes → Geometry.GridAxes, the DXF's GRID layer: X {string.Join(",", grid.Axes.Where(a => a.Vertical).Select(a => a.Name))}; "
                  + $"Y {string.Join(",", grid.Axes.Where(a => !a.Vertical).Select(a => a.Name))}"
                  + (disagreeing > 0 ? $"; {disagreeing} with disagreeing end labels" : "")
                  + (twice.Count > 0 ? $"; names used twice — a second view on the sheet: {string.Join(",", twice)}" : ""));
            Note("furniture regions (schedules, titled boxes, title block)", furniture.Regions.Count, Disposition.Discarded, "SheetFurniture");
            Note("underlines", furniture.Underlines.Count, Disposition.Discarded, "SheetFurniture");
            Note("letters, non-horizontal", record.Context.NonHorizontalLetters, Disposition.Read, "VectorPageReader (nearest-neighbour extractor keeps orientation)");
            Note("letters, invisible rendering mode (OCR layer / hidden text)", record.Context.InvisibleLetters, record.Context.InvisibleLetters > 0 ? Disposition.Unaccounted : Disposition.Read, record.Context.InvisibleLetters > 0 ? "read as ordinary text; provenance not recorded" : "none present");
            Note("letters filled in a non-RGB/Gray colour space", record.Context.NonRgbLetters, record.Context.NonRgbLetters > 0 ? Disposition.Unaccounted : Disposition.Read, record.Context.NonRgbLetters > 0 ? "paper-fill rule assumes RGB" : "none present");
            Note("clipping operations (content may lie outside its viewport)", record.Context.ClippingOperations, Disposition.Unaccounted, "no reader honours clips");
            Note("fonts, distinct", record.Context.Fonts, Disposition.Ignored, "by design");


            return new SheetLedger(record.PageNumber, record.WidthPts, record.HeightPts, record.Rotation,
                record.BookmarkTitle, record.SheetType, title, record.ScaleNote,
                record.Content.Words.Count, record.Content.Paths.Count, rows, record.Markup,
                record.Geometry.GridAxes.Select(a => new GridAxisNote(a.Name, a.Vertical ? "X" : "Y", Math.Round(a.AtMm, 1))).ToList());
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
