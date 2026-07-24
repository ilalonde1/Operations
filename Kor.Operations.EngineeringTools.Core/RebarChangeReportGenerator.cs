#nullable enable
using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;

namespace Kor.Operations.EngineeringTools.RebarChange
{
    /// <summary>Builds the rebar workbook - change detection on its own, or the full takeoff + change.</summary>
    public static class RebarChangeReportGenerator
    {
        private static readonly XLColor Navy = XLColor.FromHtml("#1F3864");
        private static readonly XLColor Light = XLColor.FromHtml("#D9E1F2");
        private static readonly XLColor Amber = XLColor.FromHtml("#FFF2CC");
        private static readonly XLColor EditOrange = XLColor.FromHtml("#F4B183"); // editable input (has a value)
        private static readonly XLColor Action = XLColor.FromHtml("#ED7D31");     // editable input (required, empty)
        private static readonly XLColor Red = XLColor.FromHtml("#C00000");
        private static readonly XLColor Green = XLColor.FromHtml("#375623");
        private static readonly XLColor Grey = XLColor.FromHtml("#808080");

        /// <summary>Change-detection only (no weight).</summary>
        public static byte[] BuildXlsx(RebarChangeResult r, string projectName)
        {
            using var wb = new XLWorkbook();
            BuildChangeSummary(wb, r, projectName);
            BuildChanges(wb, r);
            BuildAudit(wb, r);
            return Save(wb);
        }

        /// <summary>Change detection + the ΔAs grid sheet with areas pre-filled from MEASURED extents
        /// (the takeoff's own plate areas). The grid sheet's lb are an extent-based ESTIMATE, kept on
        /// their own sheet with editable orange areas — they never blend into the exact call-out delta.</summary>
        public static byte[] BuildWithPricedGrids(RebarChangeResult r, RebarPricedResult priced, string projectName)
        {
            using var wb = new XLWorkbook();
            BuildChangeSummary(wb, r, projectName);
            BuildPricedChanges(wb, priced);
            BuildChanges(wb, r);
            BuildAudit(wb, r);
            return Save(wb);
        }

        /// <summary>The Cadillac: weight takeoff + change detection + basis + audit in one book.</summary>
        public static byte[] BuildFull(RebarChangeResult r, RebarWeightResult w, string projectName,
            RebarPricedResult? priced = null)
        {
            using var wb = new XLWorkbook();
            BuildExecSummary(wb, r, w, projectName, priced);
            if (priced != null) BuildPricedChanges(wb, priced);
            BuildWeight(wb, w);
            BuildDensityBasis(wb, w);
            BuildChanges(wb, r);
            BuildAudit(wb, r);
            return Save(wb);
        }

        private static byte[] Save(XLWorkbook wb)
        {
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // Print layout so a sheet always fits the page width and long text never runs off the page.
        private static void Finish(IXLWorksheet ws, bool landscape)
        {
            ws.PageSetup.PageOrientation = landscape ? XLPageOrientation.Landscape : XLPageOrientation.Portrait;
            ws.PageSetup.PagesWide = 1;
            ws.PageSetup.PagesTall = 0;
            ws.PageSetup.CenterHorizontally = true;
            ws.PageSetup.Margins.Left = 0.4; ws.PageSetup.Margins.Right = 0.4;
            ws.PageSetup.Margins.Top = 0.5; ws.PageSetup.Margins.Bottom = 0.5;
            var used = ws.RangeUsed();
            if (used != null) ws.PageSetup.PrintAreas.Add(used.RangeAddress.ToString());
        }

        // A full-width note row: merged across the sheet so long text wraps instead of overflowing.
        private static void Note(IXLWorksheet ws, int row, int lastCol, string text)
        {
            ws.Range(row, 1, row, lastCol).Merge();
            ws.Cell(row, 1).Value = text;
            ws.Cell(row, 1).Style.Font.Italic = true;
            ws.Cell(row, 1).Style.Font.FontColor = Grey;
            ws.Cell(row, 1).Style.Alignment.WrapText = true;
            ws.Row(row).Height = 28;
        }

        private static void Header(IXLWorksheet ws, int row, params (string text, int width)[] cells)
        {
            for (int c = 0; c < cells.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = cells[c].text;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.White;
                cell.Style.Fill.BackgroundColor = Navy;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                ws.Column(c + 1).Width = cells[c].width;
            }
        }

        // ---------- Priced field-grid changes (the precise, area-driven numbers) ----------
        private static void BuildPricedChanges(XLWorkbook wb, RebarPricedResult p)
        {
            var ws = wb.Worksheets.Add("Priced changes");
            ws.Cell(1, 1).Value = "PRICED FIELD-GRID CHANGES — exact ΔAs × area";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.FontColor = Navy;
            Note(ws, 2, 9,
                "ΔAs (kg/m²) is EXACT from the call-out. Multiply it by the area to get the weight change. A SLAB grid's area " +
                "is its own floor plate (from Revit); a WALL/typical grid's area is the wall run it governs (your manual length × height).");

            // Legend so the editable cells are unmistakable.
            ws.Cell(3, 1).Value = "▮ ORANGE CELLS ARE EDITABLE — type or change the Area and the kg / lb update automatically.";
            ws.Cell(3, 1).Style.Font.Bold = true; ws.Cell(3, 1).Style.Font.FontColor = XLColor.White;
            ws.Range(3, 1, 3, 9).Merge().Style.Fill.BackgroundColor = Action;
            ws.Range(3, 1, 3, 9).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(3).Height = 18;

            Header(ws, 4,
                ("Sheet", 11), ("Title", 30), ("Type", 16),
                ($"{p.BeforeLabel}", 16), ($"{p.AfterLabel}", 16),
                ("ΔAs\n(kg/m²)", 10), ("✎ Area\n(m²) — EDIT", 12), ("Δ steel\n(kg)", 11), ("Δ steel\n(lb)", 11));

            int row = 5, first = row;
            foreach (var c in p.Changes)
            {
                bool neutral = Math.Abs(c.DeltaAsKgPerM2) < 0.02; // same steel, just re-detailed
                ws.Cell(row, 1).Value = c.Sheet;
                ws.Cell(row, 2).Value = c.Title;
                ws.Cell(row, 3).Value = c.Kind;
                ws.Cell(row, 4).Value = c.Before?.Display ?? "—";
                ws.Cell(row, 5).Value = c.After?.Display ?? "—";
                ws.Cell(row, 6).Value = c.DeltaAsKgPerM2; // full precision; cell format rounds the display
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                for (int cc = 1; cc <= 9; cc++)
                    ws.Cell(row, cc).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                if (neutral && !c.AreaM2.HasValue)
                {
                    // ΔAs ≈ 0: area can't change the answer, so don't ask for one.
                    ws.Cell(row, 7).Value = "n/a";
                    ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Cell(row, 8).Value = "weight-neutral";
                    for (int cc = 6; cc <= 9; cc++) ws.Cell(row, cc).Style.Font.FontColor = Grey;
                    row++;
                    continue;
                }

                // Area input cell (G): always an obvious orange editable field.
                ws.Cell(row, 7).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;
                ws.Cell(row, 7).Style.Border.OutsideBorderColor = XLColor.FromHtml("#C55A11");
                ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                if (c.AreaM2.HasValue)
                {
                    ws.Cell(row, 7).Value = Math.Round(c.AreaM2.Value);
                    ws.Cell(row, 7).Style.Fill.BackgroundColor = EditOrange; // filled, still editable
                    ws.Cell(row, 7).Style.Font.Bold = true;
                }
                else
                {
                    ws.Cell(row, 7).Style.Fill.BackgroundColor = Action; // empty -> type the area here
                }
                // LIVE: type an area into G and the kg/lb recompute instantly.
                ws.Cell(row, 8).FormulaA1 = $"=IF(G{row}=\"\",\"← enter area\",F{row}*G{row})";
                ws.Cell(row, 9).FormulaA1 = $"=IF(G{row}=\"\",\"\",F{row}*G{row}*2.20462)";
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
                var col = c.DeltaAsKgPerM2 > 0 ? Red : Green;
                ws.Cell(row, 6).Style.Font.FontColor = col;
                ws.Cell(row, 8).Style.Font.FontColor = col;
                ws.Cell(row, 9).Style.Font.FontColor = col;
                row++;
            }
            int last = row - 1;
            if (last >= first)
            {
                ws.Cell(row, 1).Value = "TOTAL (priced)";
                ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 8).FormulaA1 = $"=SUM(H{first}:H{last})";
                ws.Cell(row, 9).FormulaA1 = $"=SUM(I{first}:I{last})";
                for (int cc = 1; cc <= 9; cc++)
                {
                    ws.Cell(row, cc).Style.Fill.BackgroundColor = Light;
                    ws.Cell(row, cc).Style.Font.Bold = true;
                    ws.Cell(row, cc).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }
                ws.Cell(row, 8).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, 9).Style.NumberFormat.Format = "#,##0";
            }
            row += 2;
            foreach (var note in new[]
            {
                "ΔAs is EXACT — bars/m × CSA bar mass × layers, straight off the call-out. Negative = steel saved.",
                "SLAB grids: area = the sheet's own floor plate from Revit (precise). WALL/typical grids: area = your manual extent — confirm which walls the typical governs before trusting the lb.",
                "Spot call-outs and detail changes are flagged on 'Changes by sheet' — they need a manual extent before they can be priced.",
            })
            {
                Note(ws, row, 9, note);
                row++;
            }
            ws.SheetView.FreezeRows(4);
            Finish(ws, landscape: true);
        }

        // ---------- Executive summary (full) ----------
        private static void BuildExecSummary(XLWorkbook wb, RebarChangeResult r, RebarWeightResult w, string project,
            RebarPricedResult? priced)
        {
            var ws = wb.Worksheets.Add("Summary");
            ws.Cell(1, 1).Value = "REBAR TAKEOFF & CHANGE — ISSUE TO ISSUE";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15).Font.FontColor = Navy;
            ws.Cell(2, 1).Value = $"{project}   |   {r.BeforeLabel}  ->  {r.AfterLabel}";
            ws.Cell(2, 1).Style.Font.Italic = true;

            ws.Cell(4, 1).Value = "REBAR WEIGHT (high-level takeoff)";
            ws.Cell(4, 1).Style.Font.SetBold().Font.FontColor = Navy;
            ws.Cell(5, 1).Value = r.BeforeLabel; ws.Cell(5, 2).Value = Math.Round(w.TotalBefore); ws.Cell(5, 3).Value = "t";
            ws.Cell(6, 1).Value = r.AfterLabel; ws.Cell(6, 2).Value = Math.Round(w.TotalAfter); ws.Cell(6, 3).Value = "t";
            ws.Cell(7, 1).Value = "Change"; ws.Cell(7, 2).Value = Math.Round(w.TotalDelta, 1); ws.Cell(7, 3).Value = "t";
            ws.Cell(7, 1).Style.Font.Bold = true; ws.Cell(7, 2).Style.Font.Bold = true;

            ws.Cell(9, 1).Value = "REINFORCING CHANGES (detected)";
            ws.Cell(9, 1).Style.Font.SetBold().Font.FontColor = Navy;
            ws.Cell(10, 1).Value = "Sheets compared"; ws.Cell(10, 2).Value = r.SheetsCompared;
            ws.Cell(11, 1).Value = "Sheets with rebar changes"; ws.Cell(11, 2).Value = r.SheetsChanged;
            ws.Cell(12, 1).Value = "  content changed / new / removed";
            ws.Cell(12, 2).Value = $"{r.ContentChanged} / {r.NewSheets} / {r.RemovedSheets}";

            int row = 14;
            ws.Cell(row, 1).Value = "THE STORY";
            ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(12).Font.FontColor = Navy;
            // Flag outstanding manual inputs up front so nothing is silently left unpriced.
            if (priced != null && priced.UnpricedCount > 0)
            {
                ws.Cell(3, 1).Value =
                    $"⚠ {priced.UnpricedCount} field-grid change(s) need a manual area before they can be priced — " +
                    "see the orange 'ENTER AREA' cells on 'Priced changes'.";
                ws.Cell(3, 1).Style.Font.SetBold().Font.FontColor = Action;
            }

            // Headline the biggest priced field-grid change so a high-value spacing change is never buried.
            var top = priced?.Changes.FirstOrDefault(c => c.DeltaKg.HasValue);
            if (top != null)
            {
                ws.Cell(8, 1).Value =
                    $"Biggest priced change: {top.Sheet} {top.Kind} {top.Before?.Display} -> {top.After?.Display}  =  " +
                    $"{top.DeltaLb:+#,##0;-#,##0;0} lb ({top.DeltaKg:+#,##0;-#,##0;0} kg) on {top.AreaM2:#,##0} m².";
                ws.Cell(8, 1).Style.Font.SetBold().Font.FontColor = top.DeltaAsKgPerM2 > 0 ? Red : Green;
            }

            bool flat = Math.Abs(w.TotalDelta) < 0.02 * Math.Max(1, w.TotalAfter);
            double pct = w.TotalBefore > 0 ? w.TotalDelta / w.TotalBefore * 100 : 0;
            string[] story =
            {
                $"Rough rebar weight {(flat ? "is essentially unchanged" : (w.TotalDelta > 0 ? "rises" : "drops"))} issue to issue " +
                    $"({Math.Round(w.TotalBefore)} t -> {Math.Round(w.TotalAfter)} t, Δ {w.TotalDelta:+0.0;-0.0;0} t).",
                $"{r.SheetsChanged} of {r.SheetsCompared} sheets carry a reinforcing change (a mix of increases and reductions) - see 'Changes by sheet'.",
                "Weight = standard density scaled per issue by the reinforcing intensity in each issue's own call-outs, x concrete volume (see 'Density basis').",
                flat
                    ? "The detailing changed, but the rough total steel did not move materially - so a budget increase is unit-rate / estimating basis, not net added steel."
                    : $"The rough total steel moved ~{Math.Abs(w.TotalDelta):0.0} t ({pct:+0.#;-0.#;0}%) - part of the budget change is real material; the rest is unit-rate / basis.",
            };
            foreach (var s in story) { row++; ws.Cell(row, 1).Value = s; }

            row += 2;
            ws.Cell(row, 1).Value = "Weight is a high-level / order-of-magnitude takeoff (calibratable). Change detection is exact text comparison.";
            ws.Cell(row, 1).Style.Font.Italic = true; ws.Cell(row, 1).Style.Font.FontColor = Grey;

            ws.Column(1).Width = 78; ws.Column(2).Width = 12; ws.Column(3).Width = 6;
            ws.Column(1).Style.Alignment.WrapText = true; // long narrative wraps instead of running off the page
            Finish(ws, landscape: false);
        }

        // ---------- Weight ----------
        // Columns: A Element | B dens(b) | C vol(b) | D t(b) | E dens(a) | F vol(a) | G t(a) | H Δt
        private static void BuildWeight(XLWorkbook wb, RebarWeightResult w)
        {
            var ws = wb.Worksheets.Add("Rebar weight");
            Header(ws, 1,
                ("Element", 14),
                ($"{w.BeforeLabel}\ndensity\n(kg/m³)", 11), ($"{w.BeforeLabel}\nvol (m³)", 11), ($"{w.BeforeLabel}\nrebar (t)", 11),
                ($"{w.AfterLabel}\ndensity\n(kg/m³)", 11), ($"{w.AfterLabel}\nvol (m³)", 11), ($"{w.AfterLabel}\nrebar (t)", 11),
                ("Δ\n(t)", 9));
            int row = 2, first = row;
            foreach (var l in w.Lines)
            {
                ws.Cell(row, 1).Value = l.Element;
                ws.Cell(row, 2).Value = Math.Round(l.DensityBeforeKgM3); ws.Cell(row, 2).Style.Fill.BackgroundColor = EditOrange;
                ws.Cell(row, 2).Style.Border.OutsideBorder = XLBorderStyleValues.Medium; ws.Cell(row, 2).Style.Border.OutsideBorderColor = XLColor.FromHtml("#C55A11");
                ws.Cell(row, 3).Value = Math.Round(l.VolBeforeM3, 1);
                ws.Cell(row, 4).FormulaA1 = $"=B{row}*C{row}/1000";
                ws.Cell(row, 5).Value = Math.Round(l.DensityAfterKgM3); ws.Cell(row, 5).Style.Fill.BackgroundColor = EditOrange;
                ws.Cell(row, 5).Style.Border.OutsideBorder = XLBorderStyleValues.Medium; ws.Cell(row, 5).Style.Border.OutsideBorderColor = XLColor.FromHtml("#C55A11");
                ws.Cell(row, 6).Value = Math.Round(l.VolAfterM3, 1);
                ws.Cell(row, 7).FormulaA1 = $"=E{row}*F{row}/1000";
                ws.Cell(row, 8).FormulaA1 = $"=G{row}-D{row}";
                ws.Cell(row, 4).Style.Font.Bold = true; ws.Cell(row, 7).Style.Font.Bold = true;
                for (int c = 1; c <= 8; c++)
                {
                    ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    if (c == 4 || c == 7 || c == 8) ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.0";
                    if (c == 2 || c == 3 || c == 5 || c == 6) ws.Cell(row, c).Style.NumberFormat.Format = "#,##0";
                }
                ws.Cell(row, 8).Style.Font.FontColor = l.DeltaTonnes > 0.05 ? Red : (l.DeltaTonnes < -0.05 ? Green : XLColor.Black);
                row++;
            }
            int last = row - 1;
            ws.Cell(row, 1).Value = "TOTAL"; ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 4).FormulaA1 = $"=SUM(D{first}:D{last})";
            ws.Cell(row, 7).FormulaA1 = $"=SUM(G{first}:G{last})";
            ws.Cell(row, 8).FormulaA1 = $"=SUM(H{first}:H{last})";
            for (int c = 1; c <= 8; c++)
            {
                ws.Cell(row, c).Style.Fill.BackgroundColor = Light;
                ws.Cell(row, c).Style.Font.Bold = true;
                ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                if (c == 4 || c == 7 || c == 8) ws.Cell(row, c).Style.NumberFormat.Format = "#,##0.0";
            }
            row += 2;
            Note(ws, row, 8, "Density per issue = standard ratio scaled by the reinforcing intensity read off that issue's own call-outs (see 'Density basis').");
            row++;
            Note(ws, row, 8, "▮ ORANGE density cells are editable — calibrate against one hand-checked level and the tonnes recompute.");
            Finish(ws, landscape: true);
        }

        // ---------- Density basis ----------
        private static void BuildDensityBasis(XLWorkbook wb, RebarWeightResult w)
        {
            var ws = wb.Worksheets.Add("Density basis");
            ws.Cell(1, 1).Value = "DENSITY BASIS — standard ratio, scaled per issue by the drawings' own call-outs";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.FontColor = Navy;
            Header(ws, 3,
                ("Element", 14), ("Standard\n(kg/m³)", 11),
                ($"{w.BeforeLabel}\n(kg/m³)", 11), ($"{w.AfterLabel}\n(kg/m³)", 11),
                ("How the density was scaled (from extracted call-outs)", 64),
                ("Consistent with the drawings (extracted)", 60));
            int row = 4;
            foreach (var l in w.Lines)
            {
                ws.Cell(row, 1).Value = l.Element;
                ws.Cell(row, 2).Value = l.StdDensityKgM3;
                ws.Cell(row, 3).Value = Math.Round(l.DensityBeforeKgM3);
                ws.Cell(row, 4).Value = Math.Round(l.DensityAfterKgM3);
                ws.Cell(row, 5).Value = l.IntensityNote;
                ws.Cell(row, 6).Value = l.Corroboration;
                for (int c = 2; c <= 4; c++) ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                for (int c = 1; c <= 6; c++) ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(row, 5).Style.Alignment.WrapText = true;
                ws.Cell(row, 6).Style.Alignment.WrapText = true;
                row++;
            }
            row++;
            string bars = string.Join(" · ", RebarWeightEstimator.BarMassKgM.OrderBy(k => k.Key).Select(k => $"{k.Key}M {k.Value}"));
            Note(ws, row, 6, "CSA bar mass (kg/m): " + bars);
            row += 2;
            Note(ws, row, 6, "Weight = density × concrete volume (from the Revit model). The standard ratio sets the absolute level; the per-issue density is that ratio scaled by the reinforcing intensity read off each issue's own call-outs, so the before/after tonnage moves with the detailing — not with concrete volume alone.");
            Finish(ws, landscape: true);
        }

        // ---------- Change-detection summary (change-only workbook) ----------
        private static void BuildChangeSummary(XLWorkbook wb, RebarChangeResult r, string project)
        {
            var ws = wb.Worksheets.Add("Summary");
            ws.Cell(1, 1).Value = "REBAR CHANGE DETECTION";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15).Font.FontColor = Navy;
            ws.Cell(2, 1).Value = $"{project}   |   {r.BeforeLabel}  ->  {r.AfterLabel}";
            ws.Cell(2, 1).Style.Font.Italic = true;
            var stats = new (string, object)[]
            {
                ("Sheets compared", r.SheetsCompared),
                ("Sheets with rebar changes", r.SheetsChanged),
                ("  - content changed", r.ContentChanged),
                ("  - new sheet (verify - may be renumber)", r.NewSheets),
                ("  - removed sheet (verify - may be renumber)", r.RemovedSheets),
                ("Call-outs added (later issue)", r.CalloutsAdded),
                ("Call-outs removed", r.CalloutsRemoved),
            };
            int row = 4;
            foreach (var (k, v) in stats)
            {
                ws.Cell(row, 1).Value = k;
                if (k.StartsWith("Sheets")) ws.Cell(row, 1).Style.Font.Bold = true;
                ws.Cell(row, 2).Value = XLCellValue.FromObject(v);
                row++;
            }
            row++;
            ws.Cell(row, 1).Value = "WHAT THIS IS - AND IS NOT";
            ws.Cell(row, 1).Style.Font.SetBold().Font.SetFontSize(12).Font.FontColor = Navy;
            foreach (var c in Caveats) { row++; ws.Cell(row, 1).Value = c; ws.Cell(row, 1).Style.Font.Italic = true; ws.Cell(row, 1).Style.Font.FontColor = Grey; }
            ws.Column(1).Width = 78; ws.Column(2).Width = 14;
            ws.Column(1).Style.Alignment.WrapText = true;
            Finish(ws, landscape: false);
        }

        private static readonly string[] Caveats =
        {
            "Compares rebar TEXT CALL-OUTS (size M @ spacing) extracted from the PDF text layer, sheet by sheet.",
            "It DETECTS what reinforcing call-outs changed, and on which sheet - the slow manual compare, automated.",
            "It does NOT by itself compute weight - see the 'Rebar weight' tab for the high-level tonnage takeoff.",
            "Call-out COUNT change is not proportional to steel change - one label can govern a large area.",
            "Sheets flagged NEW/REMOVED may be drawing renumbering between issues - verify before relying on them.",
            "Spacings outside 75-750 mm are filtered as likely non-rebar text (detail / reference numbers).",
        };

        // ---------- Changes by sheet (shared) ----------
        private static void BuildChanges(XLWorkbook wb, RebarChangeResult r)
        {
            var ws = wb.Worksheets.Add("Changes by sheet");
            Header(ws, 1, ("Sheet", 12), ("Title", 46), ("Status", 24),
                ($"{r.BeforeLabel}\ncall-outs", 12), ($"{r.AfterLabel}\ncall-outs", 12),
                ("Net Δ", 9), ("Key changes (+ later / - earlier)", 70));
            int row = 2;
            foreach (var s in r.Sheets.Where(x => x.Status != RebarChangeStatus.Unchanged)
                                      .OrderBy(x => x.Status == RebarChangeStatus.Changed ? 0 : 1))
            {
                ws.Cell(row, 1).Value = s.Sheet;
                ws.Cell(row, 2).Value = s.Title;
                ws.Cell(row, 3).Value = StatusText(s.Status);
                ws.Cell(row, 4).Value = s.BeforeCount;
                ws.Cell(row, 5).Value = s.AfterCount;
                ws.Cell(row, 6).Value = s.NetDelta;
                ws.Cell(row, 6).Style.Font.Bold = true;
                ws.Cell(row, 6).Style.Font.FontColor = s.NetDelta > 0 ? Red : (s.NetDelta < 0 ? Green : XLColor.Black);
                ws.Cell(row, 7).Value = string.Join("; ", s.Added.Concat(s.Removed));
                var fill = s.Status == RebarChangeStatus.Changed ? Amber : Light;
                for (int c = 1; c <= 7; c++)
                {
                    ws.Cell(row, c).Style.Fill.BackgroundColor = fill;
                    ws.Cell(row, c).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                }
                ws.Cell(row, 2).Style.Alignment.WrapText = true;
                ws.Cell(row, 7).Style.Alignment.WrapText = true;
                row++;
            }
            ws.SheetView.FreezeRows(1);
            Finish(ws, landscape: true);
        }

        // ---------- Audit ----------
        private static void BuildAudit(XLWorkbook wb, RebarChangeResult r)
        {
            var ws = wb.Worksheets.Add("Audit - changed call-outs");
            Header(ws, 1, ("Sheet", 12), ("Title", 40), ("Change", 18));
            int row = 2;
            foreach (var s in r.Sheets)
                foreach (var item in s.Added.Concat(s.Removed))
                {
                    ws.Cell(row, 1).Value = s.Sheet;
                    ws.Cell(row, 2).Value = s.Title;
                    ws.Cell(row, 3).Value = item;
                    ws.Cell(row, 1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    row++;
                }
            ws.SheetView.FreezeRows(1);
            Finish(ws, landscape: false);
        }

        private static string StatusText(RebarChangeStatus s) => s switch
        {
            RebarChangeStatus.Changed => "CHANGED",
            RebarChangeStatus.NewSheet => "NEW (verify - renumber?)",
            RebarChangeStatus.RemovedSheet => "REMOVED (verify - renumber?)",
            _ => "unchanged"
        };
    }
}
