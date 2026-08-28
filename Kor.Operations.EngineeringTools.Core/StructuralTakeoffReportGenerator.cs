#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    public sealed record StructuralTakeoffReportModel(
        string ProjectWbs1,
        string ProjectName,
        string IssueLabel,
        DateTime GeneratedUtc,
        StructuralTakeoffResult Result,
        // Where the CONCRETE numbers came from, stated on the workbook. The default wording is the
        // model path ("from the model (exact)"); a drawing-measured takeoff MUST override it — the
        // report must never claim model exactness for numbers measured off plans.
        string? ConcreteBasis = null,
        // Set when foundations were NOT measured: the Foundation column is annotated and this note is
        // printed, so a structural 0 reads as "not measured", never as "no foundation concrete".
        string? FoundationNote = null,
        // Per-level sum of the QUANTITY-BEARING reinforcing call-outs readable on that level's sheets
        // (count × length × CSA mass) — an independent cross-check on the density estimate. Not a bar
        // list: mats-by-area, ties and continuous bars carry no computable weight and are excluded.
        IReadOnlyDictionary<string, double>? CalloutRebarLbByLevel = null,
        // Everything the numbers rest on that was inferred rather than read, and everything the
        // takeoff left out. Printed in the flag colour on its own block, because a silent assumption
        // is a defect even when the number is right, and a reader of the WORKBOOK must see what a
        // reader of the console saw.
        IReadOnlyList<string>? Assumptions = null);

    /// <summary>
    /// Per-floor absolute takeoff workbook — concrete + reinforcing + formwork by level, in the
    /// result's unit system (metric kg/m³/m² or imperial lb/cu.yd/sq.ft).
    ///
    /// The workbook is LIVE and calibratable. Reinforcing is not baked in: each element/variant has
    /// one editable density on the "Basis &amp; Density" sheet; a "Detail (calc)" sheet multiplies
    /// each line's concrete by that density; and the per-floor table rolls the detail up with SUMIFS.
    /// Edit a density (the orange cells) — calibrate it against a hand-checked level or the
    /// fabricator's bar list — and every floor's tonnage and the totals recompute. Concrete is exact
    /// (from the model); reinforcing is a calibrated estimate until the fabricator's schedule is in.
    /// </summary>
    public static class StructuralTakeoffReportGenerator
    {
        private static readonly XLColor Navy = XLColor.FromHtml("#1F3864");
        private static readonly XLColor Light = XLColor.FromHtml("#D9E1F2");
        private static readonly XLColor Grey = XLColor.FromHtml("#808080");
        private static readonly XLColor EditOrange = XLColor.FromHtml("#F4B183");

        private static readonly string[] Buckets = { "Slab", "Wall", "Column", "Foundation" };

        private const string DetailSheet = "Detail (calc)";
        private const string BasisSheet = "Basis & Density";

        public static byte[] BuildXlsx(StructuralTakeoffReportModel model)
        {
            ArgumentNullException.ThrowIfNull(model);
            var r = model.Result;
            bool imp = r.Unit == UnitSystem.Imperial;
            string vU = imp ? "cu.yd" : "m³";
            string wU = imp ? "lb" : "kg";
            string aU = imp ? "sq.ft" : "m²";
            string dU = imp ? "lb/cu.yd" : "kg/m³";

            using var wb = new XLWorkbook();

            // Distinct (element, variant) → one editable density row. Each line maps to exactly one.
            var keyed = model.Result.Lines
                .GroupBy(l => (l.Element, Variant: VariantLabel(l.Variant)))
                .OrderBy(g => g.Key.Element).ThenBy(g => g.Key.Variant)
                .Select((g, i) => (g.Key.Element, g.Key.Variant, Density: g.First().DensityUsed, BasisRow: i))
                .ToList();
            var basisRowOf = keyed.ToDictionary(k => Key(k.Element, k.Variant), k => k.BasisRow);

            var ws = wb.Worksheets.Add("Takeoff");
            var detail = wb.Worksheets.Add(DetailSheet);
            var basis = wb.Worksheets.Add(BasisSheet);

            BuildBasis(basis, model, keyed, dU);
            int detailFirst = BuildDetail(detail, model);
            int detailLast = detailFirst + model.Result.Lines.Count - 1;
            BuildTakeoff(ws, model, vU, wU, aU, detailFirst, detailLast);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static string VariantLabel(string? v) => string.IsNullOrWhiteSpace(v) ? "(default)" : v.Trim();
        private static string Key(TakeoffElementType e, string variantLabel) => $"{e}|{variantLabel}";

        private static string Bucket(TakeoffElementType e) => e switch
        {
            TakeoffElementType.Wall => "Wall",
            TakeoffElementType.Column => "Column",
            TakeoffElementType.Foundation => "Foundation",
            _ => "Slab", // Slab, Beam, DropPanel folded into Slab for the floor table
        };

        // ---- Detail (calc): one row per line; density looked up from Basis; reinforcing = conc × density ----
        // Columns: A Level | B Bucket | C Element | D Variant | E Concrete | F key | G Density | H Reinforcing
        private static int BuildDetail(IXLWorksheet ws, StructuralTakeoffReportModel model)
        {
            ws.Cell(1, 1).Value = "Live calc — concrete × density (density from the Basis & Density sheet). Do not edit; calibrate on Basis & Density.";
            ws.Cell(1, 1).Style.Font.SetItalic().Font.FontColor = Grey;
            string[] head = { "Level", "Bucket", "Element", "Variant", "Concrete", "key", "Density", "Reinforcing" };
            for (int c = 0; c < head.Length; c++)
            {
                ws.Cell(2, c + 1).Value = head[c];
                ws.Cell(2, c + 1).Style.Fill.BackgroundColor = Navy;
                ws.Cell(2, c + 1).Style.Font.SetBold().Font.FontColor = XLColor.White;
            }

            int first = 3, row = first;
            foreach (var l in model.Result.Lines)
            {
                string variant = VariantLabel(l.Variant);
                ws.Cell(row, 1).Value = l.Level;
                ws.Cell(row, 2).Value = Bucket(l.Element);
                ws.Cell(row, 3).Value = l.Element.ToString();
                ws.Cell(row, 4).Value = variant;
                ws.Cell(row, 5).Value = Math.Round(l.ConcreteVolume, 2);
                ws.Cell(row, 6).Value = Key(l.Element, variant);
                ws.Cell(row, 7).FormulaA1 = $"=VLOOKUP(F{row},'{BasisSheet}'!$A:$D,4,FALSE)";
                ws.Cell(row, 8).FormulaA1 = $"=E{row}*G{row}";
                row++;
            }
            ws.Columns(1, 8).AdjustToContents();
            ws.Column(6).Hide(); // key column — internal
            ws.Hide();           // calc engine — kept out of the presented workbook
            return first;
        }

        // ---- Basis & Density: the editable density panel (the calibration knobs) ----
        // Columns: A key (hidden) | B Element | C Variant | D Density (editable orange)
        private static void BuildBasis(
            IXLWorksheet ws, StructuralTakeoffReportModel model,
            List<(TakeoffElementType Element, string Variant, double Density, int BasisRow)> keyed, string dU)
        {
            ws.Cell(1, 1).Value = "Basis, Density & Calibration";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.FontColor = Navy;
            ws.Cell(2, 2).Value = $"Edit the orange density cells ({dU}) to calibrate — every floor's reinforcing and the totals recompute.";
            ws.Cell(2, 2).Style.Font.SetItalic().Font.FontColor = Grey;

            int hr = 4;
            ws.Cell(hr, 1).Value = "key";
            foreach (var (c, t) in new[] { (2, "Element"), (3, "Variant"), (4, $"Density ({dU})") })
            {
                ws.Cell(hr, c).Value = t;
                ws.Cell(hr, c).Style.Fill.BackgroundColor = Navy;
                ws.Cell(hr, c).Style.Font.SetBold().Font.FontColor = XLColor.White;
            }

            foreach (var k in keyed)
            {
                int row = hr + 1 + k.BasisRow;
                ws.Cell(row, 1).Value = Key(k.Element, k.Variant);
                ws.Cell(row, 2).Value = k.Element.ToString();
                ws.Cell(row, 3).Value = k.Variant;
                ws.Cell(row, 4).Value = Math.Round(k.Density, 1);
                ws.Cell(row, 4).Style.Fill.BackgroundColor = EditOrange;          // editable knob
                ws.Cell(row, 4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Range(row, 1, row, 4).Style.NumberFormat.Format = "#,##0.0";
            }
            ws.Column(1).Hide();
            ws.Column(2).Width = 14; ws.Column(3).Width = 18; ws.Column(4).Width = 16;

            int n = hr + keyed.Count + 3;
            ws.Cell(n, 2).Value = "How to use & basis";
            ws.Cell(n, 2).Style.Font.Bold = true;
            foreach (var note in new[]
            {
                model.ConcreteBasis ?? "Concrete volume comes from the model schedule — modelled solid geometry, exact.",
                model.FoundationNote,
                "Reinforcing = concrete volume × the density above. Columns, walls and foundations are the firm's ratio method (exact); the slab density is BASE flexural steel.",
                "For slabs with diaphragm / collector / post-tensioning steel beyond the base ratio, raise the slab density (or add a slab variant row) until the per-floor intensity matches your experience.",
                "Calibrate against one hand-checked level or the fabricator's bar list, then the whole takeoff is tuned to the job.",
                "High-level estimate for budgeting — the firm figure comes from the fabricator's bar schedule.",
                "Issued by Kor Structural • EGBC Permit 1000378",
            }.Where(s => !string.IsNullOrWhiteSpace(s)).Cast<string>())
            {
                n++;
                ws.Range(n, 2, n, 8).Merge();          // contain long notes within the page width
                ws.Cell(n, 2).Value = note;
                ws.Cell(n, 2).Style.Font.SetItalic().Font.FontColor = Grey;
                ws.Cell(n, 2).Style.Alignment.WrapText = true;
                ws.Row(n).Height = 28;
            }

            // What was inferred, and what was left out — in the flag colour, above the fold of the
            // reader's attention rather than in a console they never saw.
            if (model.Assumptions is { Count: > 0 })
            {
                n += 2;
                ws.Cell(n, 2).Value = "What these numbers rest on";
                ws.Cell(n, 2).Style.Font.Bold = true;
                ws.Cell(n, 2).Style.Font.FontColor = XLColor.FromHtml("#843C0C");

                foreach (string note in model.Assumptions.Where(s => !string.IsNullOrWhiteSpace(s)))
                {
                    n++;
                    ws.Range(n, 2, n, 8).Merge();
                    ws.Cell(n, 2).Value = note;
                    ws.Cell(n, 2).Style.Fill.BackgroundColor = EditOrange;
                    ws.Cell(n, 2).Style.Alignment.WrapText = true;
                    ws.Cell(n, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    ws.Row(n).Height = 30;
                }
            }

            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.PagesWide = 1;
            ws.PageSetup.PagesTall = 0;
            ws.PageSetup.Margins.Left = 0.5; ws.PageSetup.Margins.Right = 0.5;
            ws.PageSetup.PrintAreas.Add(ws.Range(1, 1, n, 8).RangeAddress.ToString());
        }

        // ---- Takeoff: per-floor pivot, reinforcing rolled up live from the detail via SUMIFS ----
        private static void BuildTakeoff(
            IXLWorksheet ws, StructuralTakeoffReportModel model, string vU, string wU, string aU,
            int detailFirst, int detailLast)
        {
            var r = model.Result;

            var levels = new List<string>();
            var form = new Dictionary<string, double>();
            foreach (var l in r.Lines)
            {
                if (!levels.Contains(l.Level)) levels.Add(l.Level);
                form[l.Level] = form.GetValueOrDefault(l.Level) + l.FormworkArea;
            }

            int hr = 4;
            int cConcStart = 2, cConcTotal = cConcStart + Buckets.Length;     // 2..6
            int cRebStart = cConcTotal + 1, cRebTotal = cRebStart + Buckets.Length; // 7..11
            int cIntensity = cRebTotal + 1; // 12
            int cForm = cIntensity + 1;     // 13
            bool hasCallout = model.CalloutRebarLbByLevel is { Count: > 0 };
            int cCallout = hasCallout ? cForm + 1 : -1;
            int cLastCol = hasCallout ? cCallout : cForm;

            // Titles span the table width (merged) so long text never overflows off the printed page.
            ws.Range(1, 1, 1, cLastCol).Merge();
            ws.Cell(1, 1).Value = $"{model.ProjectWbs1} {model.ProjectName}  Structural Quantity Takeoff".Trim();
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15).Font.FontColor = Navy;
            ws.Range(2, 1, 2, cLastCol).Merge();
            ws.Cell(2, 1).Value = $"{model.IssueLabel}    |    Generated {model.GeneratedUtc:yyyy-MM-dd}    |    {(r.Unit == UnitSystem.Imperial ? "Imperial" : "Metric")} units";
            ws.Cell(2, 1).Style.Font.SetItalic().Font.FontColor = Grey;
            ws.Range(3, 1, 3, cLastCol).Merge();
            ws.Cell(3, 1).Value = $"Reinforcing is a calibrated estimate — edit the orange densities on '{BasisSheet}' and every floor recomputes. "
                + (model.ConcreteBasis ?? "Concrete is from the model (exact).");
            ws.Cell(3, 1).Style.Font.SetItalic().Font.FontColor = Grey;

            ws.Range(hr, 1, hr, cLastCol).Style.Fill.BackgroundColor = Navy;
            ws.Range(hr, cConcStart, hr, cConcTotal).Merge();
            ws.Cell(hr, cConcStart).Value = $"Concrete ({vU})";
            ws.Range(hr, cRebStart, hr, cRebTotal).Merge();
            ws.Cell(hr, cRebStart).Value = $"Reinforcing ({wU}) — calibratable";
            foreach (var (cs, ce) in new[] { (cConcStart, cConcTotal), (cRebStart, cRebTotal) })
            {
                var rng = ws.Range(hr, cs, hr, ce);
                rng.Style.Fill.BackgroundColor = Navy;
                rng.Style.Font.SetBold().Font.FontColor = XLColor.White;
                rng.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int sr = hr + 1;
            ws.Cell(sr, 1).Value = "Level";
            for (int i = 0; i < Buckets.Length; i++)
            {
                ws.Cell(sr, cConcStart + i).Value = Buckets[i];
                ws.Cell(sr, cRebStart + i).Value = Buckets[i];
            }
            // An unmeasured Foundation column is annotated, so its zeros read as "not measured",
            // never as "the building has no foundation concrete".
            if (model.FoundationNote is not null)
            {
                int fi = Array.IndexOf(Buckets, "Foundation");
                ws.Cell(sr, cConcStart + fi).Value = "Foundation*";
                ws.Cell(sr, cRebStart + fi).Value = "Foundation*";
            }
            ws.Cell(sr, cConcTotal).Value = "Total";
            ws.Cell(sr, cRebTotal).Value = "Total";
            ws.Cell(sr, cIntensity).Value = $"Reinf.\nintensity\n({wU}/{vU})";
            ws.Cell(sr, cForm).Value = $"Formwork\n({aU})";
            if (hasCallout) ws.Cell(sr, cCallout).Value = $"Call-out\nreinf. x-chk\n({wU})";
            var hdr = ws.Range(sr, 1, sr, cLastCol);
            hdr.Style.Fill.BackgroundColor = Navy;
            hdr.Style.Font.SetBold().Font.FontColor = XLColor.White;
            hdr.Style.Alignment.WrapText = true;
            hdr.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(sr).Height = 34;

            string Dcol(int c) => XLHelper.GetColumnLetterFromNumber(c);
            string LevelRange = $"'{DetailSheet}'!$A${detailFirst}:$A${detailLast}";
            string BucketRange = $"'{DetailSheet}'!$B${detailFirst}:$B${detailLast}";
            string ConcRange = $"'{DetailSheet}'!$E${detailFirst}:$E${detailLast}";
            string RebRange = $"'{DetailSheet}'!$H${detailFirst}:$H${detailLast}";

            int row = sr + 1, first = row;
            foreach (var lvl in levels)
            {
                ws.Cell(row, 1).Value = lvl;
                string lvlRef = $"$A{row}";
                for (int i = 0; i < Buckets.Length; i++)
                {
                    string bucket = Buckets[i];
                    ws.Cell(row, cConcStart + i).FormulaA1 =
                        $"=SUMIFS({ConcRange},{LevelRange},{lvlRef},{BucketRange},\"{bucket}\")";
                    ws.Cell(row, cRebStart + i).FormulaA1 =
                        $"=SUMIFS({RebRange},{LevelRange},{lvlRef},{BucketRange},\"{bucket}\")";
                }
                ws.Cell(row, cConcTotal).FormulaA1 = $"=SUM({Dcol(cConcStart)}{row}:{Dcol(cConcTotal - 1)}{row})";
                ws.Cell(row, cRebTotal).FormulaA1 = $"=SUM({Dcol(cRebStart)}{row}:{Dcol(cRebTotal - 1)}{row})";
                ws.Cell(row, cIntensity).FormulaA1 =
                    $"=IF({Dcol(cConcTotal)}{row}>0,{Dcol(cRebTotal)}{row}/{Dcol(cConcTotal)}{row},0)";
                ws.Cell(row, cForm).Value = Math.Round(form.GetValueOrDefault(lvl));
                if (hasCallout) ws.Cell(row, cCallout).Value = Math.Round(model.CalloutRebarLbByLevel!.GetValueOrDefault(lvl));
                ws.Range(row, 1, row, cLastCol).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                row++;
            }

            int last = row - 1;
            ws.Cell(row, 1).Value = "TOTAL";
            for (int i = 0; i < Buckets.Length; i++)
            {
                ws.Cell(row, cConcStart + i).FormulaA1 = SumCol(cConcStart + i, first, last);
                ws.Cell(row, cRebStart + i).FormulaA1 = SumCol(cRebStart + i, first, last);
            }
            ws.Cell(row, cConcTotal).FormulaA1 = SumCol(cConcTotal, first, last);
            ws.Cell(row, cRebTotal).FormulaA1 = SumCol(cRebTotal, first, last);
            ws.Cell(row, cIntensity).FormulaA1 =
                $"=IF({Dcol(cConcTotal)}{row}>0,{Dcol(cRebTotal)}{row}/{Dcol(cConcTotal)}{row},0)";
            ws.Cell(row, cForm).FormulaA1 = SumCol(cForm, first, last);
            if (hasCallout) ws.Cell(row, cCallout).FormulaA1 = SumCol(cCallout, first, last);
            var tot = ws.Range(row, 1, row, cLastCol);
            tot.Style.Fill.BackgroundColor = Light;
            tot.Style.Font.Bold = true;
            tot.Style.Border.TopBorder = XLBorderStyleValues.Medium;

            ws.Range(first, cConcStart, row, cConcTotal).Style.NumberFormat.Format = "#,##0.0";
            ws.Range(first, cRebStart, row, cLastCol).Style.NumberFormat.Format = "#,##0";
            if (hasCallout)
            {
                int fnRow = row + 1;
                ws.Range(fnRow, 1, fnRow, cLastCol).Merge();
                ws.Cell(fnRow, 1).Value = "Call-out cross-check = the quantity-bearing reinforcing call-outs readable on that level's sheets (count × length × CSA mass) — an independent check on the density estimate, NOT a bar list (mats-by-area, ties and continuous bars carry no computable weight and are excluded).";
                ws.Cell(fnRow, 1).Style.Font.SetItalic().Font.FontColor = Grey;
                ws.Cell(fnRow, 1).Style.Alignment.WrapText = true;
                ws.Row(fnRow).Height = 26;
            }

            int n = row + 2;
            ws.Cell(n, 1).Value = "Total concrete"; ws.Cell(n, 2).FormulaA1 = $"={Dcol(cConcTotal)}{row}"; ws.Cell(n, 3).Value = vU;
            ws.Cell(n + 1, 1).Value = "Total reinforcing"; ws.Cell(n + 1, 2).FormulaA1 = $"={Dcol(cRebTotal)}{row}"; ws.Cell(n + 1, 3).Value = wU;
            ws.Cell(n + 2, 1).Value = "Overall intensity"; ws.Cell(n + 2, 2).FormulaA1 = $"=IF(B{n}>0,B{n + 1}/B{n},0)"; ws.Cell(n + 2, 3).Value = $"{wU}/{vU}";
            ws.Range(n, 1, n + 2, 1).Style.Font.Bold = true;
            ws.Range(n, 2, n + 2, 2).Style.NumberFormat.Format = "#,##0";
            if (model.FoundationNote is not null)
            {
                n++;
                ws.Range(n + 2, 1, n + 2, cLastCol).Merge();
                ws.Cell(n + 2, 1).Value = $"* {model.FoundationNote}";
                ws.Cell(n + 2, 1).Style.Font.SetItalic().Font.FontColor = Grey;
                ws.Cell(n + 2, 1).Style.Alignment.WrapText = true;
                ws.Row(n + 2).Height = 26;
            }

            ws.Column(1).Width = 16;
            for (int c = 2; c <= cLastCol; c++) ws.Column(c).Width = 11;
            ws.SheetView.FreezeRows(sr);

            // Print layout — landscape, scaled to one page wide, grouped header repeated on each page.
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PagesWide = 1;
            ws.PageSetup.PagesTall = 0;
            ws.PageSetup.CenterHorizontally = true;
            ws.PageSetup.Margins.Left = 0.4; ws.PageSetup.Margins.Right = 0.4;
            ws.PageSetup.Margins.Top = 0.5; ws.PageSetup.Margins.Bottom = 0.5;
            ws.PageSetup.SetRowsToRepeatAtTop(hr, sr);
            ws.PageSetup.PrintAreas.Add(ws.Range(1, 1, n + 2, cLastCol).RangeAddress.ToString());
        }

        private static string SumCol(int col, int first, int last)
        {
            string c = XLHelper.GetColumnLetterFromNumber(col);
            return $"=SUM({c}{first}:{c}{last})";
        }
    }
}
