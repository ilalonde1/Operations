#nullable enable
using System.Text.RegularExpressions;
using Kor.Operations.EngineeringTools.Dxf;
using Kor.Operations.EngineeringTools.PdfToSafe;
using UglyToad.PdfPig;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    /// <summary>
    /// The ground truth we already own: a job whose drawings exist BOTH as a stick-file PDF and as a
    /// Revit DXF export of the same sheets. Sheet by sheet, what the PDF side reads must carry what
    /// the DXF side reads from the same drawing, because they are one drawing.
    /// </summary>
    /// <remarks>
    /// 31168 is that job: the stick file has 41 pages and <c>_DXF-from-Revit-2026-08-26</c> has 139
    /// sheets, and the plan sheets share their sheet numbers (S2.05.1 …). Nothing in this repo
    /// compared the two before 2026-09-08, which is how the PDF side shipped without a wall reader:
    /// the DXF side reads walls from the Revit layers and the PDF side had nothing to be measured
    /// against.
    ///
    /// WHAT IT COVERS: for every DXF sheet whose number appears in the PDF's bookmarks (or, when
    /// there are none, in the title block), the counts of slabs, columns, lines and walls the PDF
    /// side emits against the walls, columns, slabs and openings the DXF classifier reads. The DXF
    /// is classified with the compiled default rules in its own unit, not the job's KorStandards
    /// rules — a differential needs the same rule on both days, not the production rule.
    ///
    /// WHAT IT DOES NOT COVER: position. The PDF geometry is in page millimetres and the Revit DXF is
    /// in project coordinates, and until the grid is read as data (step 3) there is no registration
    /// between them, so a column counted on both sides is not shown to be the SAME column. A
    /// same-class fault it would not catch: a PDF column emitted at the wrong place with the right
    /// count. Nor does it see a sheet the PDF has and the DXF set does not.
    /// </remarks>
    public static class PdfVersusDxf
    {
        public sealed record SheetPair(
            string DxfFile, string SheetNumber, int Page, string PageTitle,
            int PdfSlabs, int PdfColumns, int PdfLines, int PdfWalls,
            int DxfWalls, int DxfColumns, int DxfSlabs, int DxfOpenings)
        {
            /// <summary>The PDF page's sheet type; only a plan is compared (brief 17).</summary>
            public string SheetType { get; init; } = "plan";
            public bool IsPlan => SheetType == "plan";
        }

        public sealed record Result(IReadOnlyList<SheetPair> Pairs, IReadOnlyList<string> Unmatched, string PageIndexSource);

        /// <summary>
        /// A sheet number as KOR writes one: S2.05.1, S4.02, S1.01. No trailing word boundary: in a
        /// Revit export's file name the number is followed by an underscore ("S2.05.1_1_LEVEL P1"),
        /// which is a word character, and a boundary there made the DXF key "S2.05" against the
        /// PDF's "S2.05.1" — 0 of 62 issued sheets matched on 2026-09-08 before this was seen.
        /// </summary>
        public static readonly Regex SheetNumber = new(@"(?<![A-Z0-9])S\d{1,2}\.\d{2}(?:\.\d+)?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static Result Compare(string pdfPath, string dxfFolder, int scaleDenominator, PdfIntakeOptions options,
                                     PlanClassificationOptions? dxfRules = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (!File.Exists(pdfPath)) throw new FileNotFoundException("PDF not found", pdfPath);
            if (!Directory.Exists(dxfFolder)) throw new DirectoryNotFoundException($"DXF folder not found: {dxfFolder}");

            using var doc = PdfDocument.Open(pdfPath);
            var (pageBySheet, source) = PageIndex(doc);
            var facts = Kor.Operations.EngineeringTools.Intake.DocumentFacts.From(doc);

            var pairs = new List<SheetPair>();
            var unmatched = new List<string>();
            foreach (string dxf in Directory.EnumerateFiles(dxfFolder, "*.dxf", SearchOption.TopDirectoryOnly).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileName(dxf);
                var m = SheetNumber.Match(name);
                if (!m.Success) { unmatched.Add($"{name}: no sheet number in its name"); continue; }
                if (!pageBySheet.TryGetValue(m.Value, out var hit)) { unmatched.Add($"{name}: sheet {m.Value} has no page in the PDF"); continue; }

                // The DXF side, the way dxf-to-etabs reads it, in the drawing's own unit.
                double unit = DxfPlanReader.UnitInInches(dxf) ?? 1.0;
                var rules = dxfRules ?? new PlanClassificationOptions();
                if (Math.Abs(unit - 1.0) > 1e-9) rules = rules.InUnitOf(unit);
                var segments = DxfPlanReader.ReadSegments(dxf);
                var tags = DxfPlanReader.ReadPositionedTags(dxf);
                var dxfGeo = StructuralPlanClassifier.Classify(segments, rules, PlanSheetNaming.Parse(dxf), tags);

                // The PDF side, the way pdf-takeoff reads it — and the sheet's type, the way the
                // record types it, so a section sheet's cut poché is listed and not compared.
                var pdfGeo = PdfPlanReader.Read(doc, scaleDenominator, hit.Page, options, annotationsOnly: false);
                var content = VectorPageReader.ReadPage(doc.GetPage(hit.Page));
                // The sheet's type is the record's business — one rule, in one place (DrawingIntake),
                // with the same fallbacks the ledger and pdf-takeoff use. Typed without a scale so
                // nothing is classified twice.
                string sheetType;
                try
                {
                    sheetType = Kor.Operations.EngineeringTools.Intake.DrawingIntake
                        .ReadSheet(doc, hit.Page, new Kor.Operations.EngineeringTools.Intake.IntakeRequest(null, options), facts)
                        .SheetType;
                }
                catch { sheetType = "unknown"; }

                pairs.Add(new SheetPair(name, m.Value.ToUpperInvariant(), hit.Page, hit.Title,
                    pdfGeo.Slabs.Count, pdfGeo.Columns.Count, pdfGeo.Lines.Count, pdfGeo.Walls.Count,
                    dxfGeo.Walls.Count, dxfGeo.Columns.Count, dxfGeo.Slabs.Count, dxfGeo.Openings.Count) { SheetType = sheetType });
            }
            return new Result(pairs, unmatched, source);
        }

        /// <summary>Which page each sheet number is on: from the bookmarks, else from the title block.</summary>
        public static (IReadOnlyDictionary<string, (int Page, string Title)> Index, string Source) PageIndex(PdfDocument doc)
        {
            var index = new Dictionary<string, (int Page, string Title)>(StringComparer.OrdinalIgnoreCase);
            var facts = SheetInventory.Facts(doc);
            foreach (var (page, title) in facts.Bookmarks.OrderBy(kv => kv.Key))
            {
                var m = SheetNumber.Match(title);
                if (m.Success && !index.ContainsKey(m.Value)) index[m.Value] = (page, title);
            }
            if (index.Count > 0) return (index, "bookmarks");

            // No sheet index in the file: the number is printed in the title block, bottom-right.
            for (int p = 1; p <= doc.NumberOfPages; p++)
            {
                var content = VectorPageReader.ReadPage(doc.GetPage(p));
                var token = content.Words
                    .Where(w => w.Cx > 0.8 * content.WidthPts && w.Cy < 0.15 * content.HeightPts)
                    .Select(w => SheetNumber.Match(w.Text))
                    .FirstOrDefault(m => m.Success);
                if (token is not null && !index.ContainsKey(token.Value))
                {
                    string title = SheetTitleReader.FromPage(content)?.Display ?? "";
                    index[token.Value] = (p, title);
                }
            }
            return (index, "title block");
        }
    }
}
