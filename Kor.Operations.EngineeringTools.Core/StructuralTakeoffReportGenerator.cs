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
        StructuralTakeoffResult Result);

    /// <summary>
    /// Per-floor absolute takeoff workbook — concrete + reinforcing + formwork by level, in the
    /// result's unit system (metric kg/m³/m² or imperial lb/cu.yd/sq.ft). Mirrors the standard
    /// structural quantity-estimate layout. Reinforcing is the calibrated density × volume estimate;
    /// the firm figure still comes from the fabricator's bar schedule (basis sheet states this).
    /// </summary>
    public static class StructuralTakeoffReportGenerator
    {
        private static readonly XLColor Navy = XLColor.FromHtml("#1F3864");
        private static readonly XLColor Light = XLColor.FromHtml("#D9E1F2");
        private static readonly XLColor Grey = XLColor.FromHtml("#808080");
        private static readonly XLColor EditOrange = XLColor.FromHtml("#F4B183");

        private static readonly string[] Buckets = { "Slab", "Wall", "Column", "Foundation" };

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
            BuildTakeoff(wb, model, vU, wU, aU);
            BuildBasis(wb, model, dU);

            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        private static string Bucket(TakeoffElementType e) => e switch
        {
            TakeoffElementType.Wall => "Wall",
            TakeoffElementType.Column => "Column",
            TakeoffElementType.Foundation => "Foundation",
            _ => "Slab", // Slab, Beam, DropPanel folded into Slab for the floor table
        };

        private static void BuildTakeoff(XLWorkbook wb, StructuralTakeoffReportModel model, string vU, string wU, string aU)
        {
            var r = model.Result;
            var ws = wb.Worksheets.Add("Takeoff");

            // Levels in first-seen order; concrete/rebar/formwork pivoted by level x bucket.
            var levels = new List<string>();
            var conc = new Dictionary<(string, string), double>();
            var reb = new Dictionary<(string, string), double>();
            var form = new Dictionary<string, double>();
            foreach (var l in r.Lines)
            {
                if (!levels.Contains(l.Level)) levels.Add(l.Level);
                var b = Bucket(l.Element);
                conc[(l.Level, b)] = conc.GetValueOrDefault((l.Level, b)) + l.ConcreteVolume;
                reb[(l.Level, b)] = reb.GetValueOrDefault((l.Level, b)) + l.RebarWeight;
                form[l.Level] = form.GetValueOrDefault(l.Level) + l.FormworkArea;
            }

            ws.Cell(1, 1).Value = $"{model.ProjectWbs1} {model.ProjectName}  Structural Quantity Takeoff".Trim();
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15).Font.FontColor = Navy;
            ws.Cell(2, 1).Value = $"{model.IssueLabel}   |   Generated {model.GeneratedUtc:yyyy-MM-dd}   |   {(r.Unit == UnitSystem.Imperial ? "Imperial" : "Metric")} units   |   quantities for budgeting — reinforcing is a calibrated estimate";
            ws.Cell(2, 1).Style.Font.SetItalic().Font.FontColor = Grey;

            // Two-row grouped header: Concrete (4 buckets + total) | Reinforcing (4 + total) | intensity | formwork
            int hr = 4;
            int cConcStart = 2, cConcTotal = cConcStart + Buckets.Length;     // 2..6
            int cRebStart = cConcTotal + 1, cRebTotal = cRebStart + Buckets.Length; // 7..11
            int cIntensity = cRebTotal + 1; // 12
            int cForm = cIntensity + 1;     // 13

            ws.Range(hr, 1, hr, cForm).Style.Fill.BackgroundColor = Navy; // full band, no white gaps
            ws.Range(hr, cConcStart, hr, cConcTotal).Merge();
            ws.Cell(hr, cConcStart).Value = $"Concrete ({vU})";
            ws.Range(hr, cRebStart, hr, cRebTotal).Merge();
            ws.Cell(hr, cRebStart).Value = $"Reinforcing ({wU})";
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
            ws.Cell(sr, cConcTotal).Value = "Total";
            ws.Cell(sr, cRebTotal).Value = "Total";
            ws.Cell(sr, cIntensity).Value = $"Reinf.\nintensity\n({wU}/{vU})";
            ws.Cell(sr, cForm).Value = $"Formwork\n({aU})";
            var hdr = ws.Range(sr, 1, sr, cForm);
            hdr.Style.Fill.BackgroundColor = Navy;
            hdr.Style.Font.SetBold().Font.FontColor = XLColor.White;
            hdr.Style.Alignment.WrapText = true;
            hdr.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(sr).Height = 34;

            int row = sr + 1, first = row;
            foreach (var lvl in levels)
            {
                ws.Cell(row, 1).Value = lvl;
                double cTot = 0, rTot = 0;
                for (int i = 0; i < Buckets.Length; i++)
                {
                    double cv = conc.GetValueOrDefault((lvl, Buckets[i]));
                    double rv = reb.GetValueOrDefault((lvl, Buckets[i]));
                    ws.Cell(row, cConcStart + i).Value = Math.Round(cv, 1);
                    ws.Cell(row, cRebStart + i).Value = Math.Round(rv);
                    cTot += cv; rTot += rv;
                }
                ws.Cell(row, cConcTotal).Value = Math.Round(cTot, 1);
                ws.Cell(row, cRebTotal).Value = Math.Round(rTot);
                ws.Cell(row, cIntensity).Value = cTot > 0 ? Math.Round(rTot / cTot) : 0;
                ws.Cell(row, cForm).Value = Math.Round(form.GetValueOrDefault(lvl));
                ws.Range(row, 1, row, cForm).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
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
            ws.Cell(row, cForm).FormulaA1 = SumCol(cForm, first, last);
            var tot = ws.Range(row, 1, row, cForm);
            tot.Style.Fill.BackgroundColor = Light;
            tot.Style.Font.Bold = true;
            tot.Style.Border.TopBorder = XLBorderStyleValues.Medium;

            // Number formats (thousands separators) across data + total rows.
            ws.Range(first, cConcStart, row, cConcTotal).Style.NumberFormat.Format = "#,##0.0";
            ws.Range(first, cRebStart, row, cForm).Style.NumberFormat.Format = "#,##0";

            // Headline totals (authoritative, from the engine) below the table.
            int n = row + 2;
            ws.Cell(n, 1).Value = "Total concrete"; ws.Cell(n, 2).Value = Math.Round(r.TotalConcreteVolume, 1); ws.Cell(n, 3).Value = vU;
            ws.Cell(n + 1, 1).Value = "Total reinforcing"; ws.Cell(n + 1, 2).Value = Math.Round(r.TotalRebarWeight); ws.Cell(n + 1, 3).Value = wU;
            ws.Cell(n + 2, 1).Value = "Overall intensity"; ws.Cell(n + 2, 2).Value = r.TotalConcreteVolume > 0 ? Math.Round(r.TotalRebarWeight / r.TotalConcreteVolume) : 0; ws.Cell(n + 2, 3).Value = $"{wU}/{vU}";
            ws.Range(n, 1, n + 2, 1).Style.Font.Bold = true;

            ws.Column(1).Width = 14;
            for (int c = 2; c <= cForm; c++) ws.Column(c).Width = 11;
            ws.SheetView.FreezeRows(sr);
        }

        private static void BuildBasis(XLWorkbook wb, StructuralTakeoffReportModel model, string dU)
        {
            var ws = wb.Worksheets.Add("Basis & Density");
            ws.Cell(1, 1).Value = "Basis, Density & Reconciliation";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.FontColor = Navy;

            int hr = 3;
            foreach (var (c, t) in new[] { (1, "Element"), (2, "Variant"), (3, $"Density used ({dU})") })
            {
                ws.Cell(hr, c).Value = t;
                ws.Cell(hr, c).Style.Fill.BackgroundColor = Navy;
                ws.Cell(hr, c).Style.Font.SetBold().Font.FontColor = XLColor.White;
            }
            int row = hr + 1;
            foreach (var g in model.Result.Lines
                         .GroupBy(l => (l.Element, l.Variant ?? "(default)", l.DensityUsed))
                         .OrderBy(g => g.Key.Item1).ThenBy(g => g.Key.Item2))
            {
                ws.Cell(row, 1).Value = g.Key.Item1.ToString();
                ws.Cell(row, 2).Value = g.Key.Item2;
                ws.Cell(row, 3).Value = Math.Round(g.Key.Item3, 1);
                row++;
            }
            ws.Column(1).Width = 14; ws.Column(2).Width = 16; ws.Column(3).Width = 18;

            row += 1;
            ws.Cell(row, 1).Value = "Fabricator final (enter):";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 3).Style.Fill.BackgroundColor = EditOrange;
            ws.Cell(row, 3).Style.Border.OutsideBorder = XLBorderStyleValues.Medium;

            row += 2;
            foreach (var note in new[]
            {
                "Concrete volume is taken from the model schedule (modelled solid geometry) — exact.",
                "Reinforcing = concrete volume × standard density per element/variant; a calibrated high-level estimate.",
                "Calibrate the densities against one hand-checked level or the fabricator's bar list; the firm figure comes from the fabricator's schedule.",
                "High-level / order-of-magnitude, for information only. To be verified against the issued set.",
                "Issued by Kor Structural • EGBC Permit 1000378",
            })
            {
                ws.Cell(row, 1).Value = note;
                ws.Cell(row, 1).Style.Font.SetItalic().Font.FontColor = Grey;
                row++;
            }
        }

        private static string SumCol(int col, int first, int last)
        {
            string c = XLHelper.GetColumnLetterFromNumber(col);
            return $"=SUM({c}{first}:{c}{last})";
        }
    }
}
