#nullable enable

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Kor.Operations.EngineeringTools.QuantityTakeoff
{
    public sealed record TakeoffReportModel(
        string ProjectWbs1,
        string ProjectName,
        string IssueBeforeLabel,
        string IssueAfterLabel,
        DateTime GeneratedAtUtc,
        TakeoffDiff Diff);

    public static class TakeoffReportGenerator
    {
        private const string CaveatText =
            "Quantities only  high-level / order-of-magnitude, for information only. Quantities to be verified by the trades against the issued set. Unit rates and pricing to be applied by the recipient's estimator.";

        private const string BasisMismatchWarning =
            "WARNING: the two snapshots were measured on different bases  delta may not be comparable.";

        private static readonly string[] Headers =
        {
            "Level",
            "Element",
            "Grade",
            "Concrete Before (m3)",
            "Concrete After (m3)",
            "Concrete Delta (m3)",
            "Formwork Delta (m2)",
            "Status"
        };

        public static string BuildHtml(TakeoffReportModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var html = new StringBuilder();
            html.AppendLine("<!doctype html>");
            html.AppendLine("<html>");
            html.AppendLine("<head>");
            html.AppendLine("<meta charset=\"utf-8\">");
            html.Append("<title>").Append(Html(Title(model))).AppendLine("</title>");
            html.AppendLine("<style>body{font-family:Arial,sans-serif;color:#111;}table{border-collapse:collapse;}th,td{border:1px solid #999;padding:4px 6px;}th{background:#eee;}td.num{text-align:right;}.caveat{border:1px solid #777;padding:8px;margin:12px 0;}.warning{font-weight:bold;color:#9b1c1c;}</style>");
            html.AppendLine("</head>");
            html.AppendLine("<body>");
            html.Append("<h1>").Append(Html(Title(model))).AppendLine("</h1>");
            html.Append("<p>").Append(Html(Subtitle(model))).Append("; Generated ").Append(Html(GeneratedDate(model))).AppendLine("</p>");
            html.Append("<div class=\"caveat\">").Append(Html(CaveatText)).AppendLine("</div>");

            if (model.Diff.BasisMismatch)
            {
                html.Append("<p class=\"warning\">").Append(Html(BasisMismatchWarning)).AppendLine("</p>");
            }

            html.AppendLine("<table>");
            html.AppendLine("<thead><tr>");
            foreach (var header in Headers)
            {
                html.Append("<th>").Append(Html(header)).AppendLine("</th>");
            }

            html.AppendLine("</tr></thead>");
            html.AppendLine("<tbody>");
            foreach (var line in model.Diff.Lines)
            {
                html.AppendLine("<tr>");
                AppendTextCell(html, line.Level);
                AppendTextCell(html, line.ElementType.ToString());
                AppendTextCell(html, line.GradeCode);
                AppendNumberCell(html, line.ConcreteBeforeM3);
                AppendNumberCell(html, line.ConcreteAfterM3);
                AppendNumberCell(html, line.ConcreteDeltaM3);
                AppendNumberCell(html, line.FormworkDeltaM2);
                AppendTextCell(html, line.Status.ToString());
                html.AppendLine("</tr>");
            }

            html.AppendLine("<tr>");
            AppendTextCell(html, "TOTAL");
            AppendTextCell(html, string.Empty);
            AppendTextCell(html, string.Empty);
            AppendTextCell(html, string.Empty);
            AppendTextCell(html, string.Empty);
            AppendNumberCell(html, model.Diff.TotalConcreteDeltaM3);
            AppendNumberCell(html, model.Diff.TotalFormworkDeltaM2);
            AppendTextCell(html, string.Empty);
            html.AppendLine("</tr>");
            html.AppendLine("</tbody>");
            html.AppendLine("</table>");
            AppendLevelNotes(html, model);
            html.AppendLine("</body>");
            html.AppendLine("</html>");
            return html.ToString();
        }

        public static byte[] BuildDocx(TakeoffReportModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            using var stream = new MemoryStream();
            using (var document = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
            {
                var mainPart = document.AddMainDocumentPart();
                var body = new Body();
                mainPart.Document = new Document(body);

                body.Append(Paragraph(Title(model)));
                body.Append(Paragraph($"{Subtitle(model)}; Generated {GeneratedDate(model)}"));
                body.Append(Paragraph(CaveatText));

                if (model.Diff.BasisMismatch)
                {
                    body.Append(Paragraph(BasisMismatchWarning));
                }

                body.Append(BuildDocxTable(model));
                AppendDocxLevelNotes(body, model);
                mainPart.Document.Save();
            }

            return stream.ToArray();
        }

        public static byte[] BuildXlsx(TakeoffReportModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            var navy  = XLColor.FromArgb(31, 56, 100);
            var sub   = XLColor.FromArgb(89, 89, 89);
            var red   = XLColor.FromArgb(192, 57, 43);
            var green = XLColor.FromArgb(30, 125, 52);
            var hilite= XLColor.FromArgb(255, 246, 204);
            var grey  = XLColor.FromArgb(242, 242, 242);

            var head = new[]
            {
                "Level", "Element", "Grade", "Primary change", "Category",
                $"Concrete\n{model.IssueBeforeLabel} (m³)",
                $"Concrete\n{model.IssueAfterLabel} (m³)",
                "Δ Concrete (m³)",
                "Status",
            };
            int cols = head.Length;

            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Delta");

            ws.Range(1, 1, 1, cols).Merge();
            ws.Cell(1, 1).Value = Title(model);
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(15).Font.SetFontColor(navy);

            ws.Range(2, 1, 2, cols).Merge();
            ws.Cell(2, 1).Value = $"{Subtitle(model)}     |     Generated {GeneratedDate(model)}     |     quantities only — for information";
            ws.Cell(2, 1).Style.Font.SetItalic().Font.SetFontColor(sub);

            ws.Range(3, 1, 3, cols).Merge();
            ws.Cell(3, 1).Value = CaveatText;
            ws.Cell(3, 1).Style.Font.SetItalic().Font.SetFontSize(9);
            ws.Cell(3, 1).Style.Fill.BackgroundColor = grey;
            ws.Cell(3, 1).Style.Alignment.WrapText = true;
            ws.Row(3).Height = 26;

            if (model.Diff.BasisMismatch)
            {
                ws.Range(4, 1, 4, cols).Merge();
                ws.Cell(4, 1).Value = BasisMismatchWarning;
                ws.Cell(4, 1).Style.Font.SetBold().Font.SetFontColor(red);
            }

            int hr = 5;
            for (int c = 0; c < cols; c++)
            {
                var cell = ws.Cell(hr, c + 1);
                cell.Value = head[c];
                cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White);
                cell.Style.Fill.BackgroundColor = navy;
                cell.Style.Alignment.WrapText = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            }
            ws.Row(hr).Height = 30;

            int r = hr + 1;
            foreach (var line in model.Diff.Lines)
            {
                ws.Cell(r, 1).Value = line.Level;
                ws.Cell(r, 2).Value = line.ElementType.ToString();
                ws.Cell(r, 3).Value = line.GradeCode;
                ws.Cell(r, 4).Value = line.Change ?? string.Empty;
                ws.Cell(r, 5).Value = line.Category ?? string.Empty;
                ws.Cell(r, 6).Value = line.ConcreteBeforeM3;
                ws.Cell(r, 7).Value = line.ConcreteAfterM3;
                ws.Cell(r, 8).Value = line.ConcreteDeltaM3;
                ws.Cell(r, 9).Value = line.Status.ToString();

                ws.Cell(r, 4).Style.Alignment.WrapText = true;
                ws.Range(r, 6, r, 7).Style.NumberFormat.Format = "#,##0.0";
                ws.Cell(r, 8).Style.NumberFormat.Format = "+#,##0.0;-#,##0.0;0.0";
                ws.Cell(r, 8).Style.Font.SetBold().Font.SetFontColor(line.ConcreteDeltaM3 > 0 ? red : line.ConcreteDeltaM3 < 0 ? green : XLColor.Black);
                if (line.Status != TakeoffDiffStatus.Matched)
                    ws.Range(r, 1, r, cols).Style.Fill.BackgroundColor = hilite;
                ws.Range(r, 1, r, cols).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                r++;
            }

            ws.Cell(r, 1).Value = "TOTAL";
            ws.Cell(r, 6).Value = model.Diff.Lines.Sum(l => l.ConcreteBeforeM3);
            ws.Cell(r, 7).Value = model.Diff.Lines.Sum(l => l.ConcreteAfterM3);
            ws.Cell(r, 8).Value = model.Diff.TotalConcreteDeltaM3;
            ws.Range(r, 1, r, cols).Style.Font.Bold = true;
            ws.Range(r, 1, r, cols).Style.Border.TopBorder = XLBorderStyleValues.Medium;
            ws.Range(r, 6, r, 7).Style.NumberFormat.Format = "#,##0.0";
            ws.Cell(r, 8).Style.NumberFormat.Format = "+#,##0.0;-#,##0.0;0.0";

            int nr = r + 2;
            if (model.Diff.AddedLevels.Count > 0)
            {
                ws.Cell(nr, 1).Value = $"Added levels: {string.Join(", ", model.Diff.AddedLevels)}";
                nr++;
            }

            if (model.Diff.RemovedLevels.Count > 0)
            {
                ws.Cell(nr, 1).Value = $"Removed levels: {string.Join(", ", model.Diff.RemovedLevels)}";
                nr++;
            }

            // Story box — derived straight from the data.
            string Fmt(double v) => v.ToString("+#,##0.0;-#,##0.0;0.0", CultureInfo.InvariantCulture);
            nr += 1;
            ws.Range(nr, 1, nr, cols).Merge();
            ws.Cell(nr, 1).Value = "THE STORY THE NUMBERS TELL";
            ws.Cell(nr, 1).Style.Font.SetBold().Font.SetFontSize(11).Font.SetFontColor(navy);
            nr++;

            var story = new System.Collections.Generic.List<string>
            {
                $"• Concrete net change: {Fmt(model.Diff.TotalConcreteDeltaM3)} m³ — measured from the model.",
                "• Rebar is handled separately by the Rebar Takeoff & Change Detection tool (PDF issue-to-issue).",
            };
            if (model.Diff.AddedLevels.Count > 0 || model.Diff.RemovedLevels.Count > 0)
            {
                string a = model.Diff.AddedLevels.Count > 0 ? "added " + string.Join(", ", model.Diff.AddedLevels) : string.Empty;
                string rm = model.Diff.RemovedLevels.Count > 0 ? "removed " + string.Join(", ", model.Diff.RemovedLevels) : string.Empty;
                story.Add("• Levels " + string.Join("; ", new[] { a, rm }.Where(s => s.Length > 0)) + ".");
            }

            story.Add("• Quantity basis only — apply the recipient's unit rates. A budget move on an unchanged quantity basis points to unit-rate escalation and coordination/detailing, not an enlargement of the structure.");
            foreach (var s in story)
            {
                ws.Range(nr, 1, nr, cols).Merge();
                ws.Cell(nr, 1).Value = s;
                ws.Cell(nr, 1).Style.Alignment.WrapText = true;
                ws.Row(nr).Height = 26;
                nr++;
            }

            ws.Columns(1, 3).AdjustToContents();
            ws.Column(2).Width = 12;
            ws.Column(4).Width = 42;
            ws.Column(4).Style.Alignment.WrapText = true;
            ws.Column(5).Width = 20;
            for (int c = 6; c <= cols - 1; c++) ws.Column(c).Width = 13;
            ws.Column(cols).Width = 11;
            ws.SheetView.FreezeRows(hr);

            BuildBasisSheet(workbook);

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        private static void BuildBasisSheet(XLWorkbook workbook)
        {
            var navy = XLColor.FromArgb(31, 56, 100);
            var b = workbook.Worksheets.Add("Basis & Assumptions");
            b.Cell(1, 1).Value = "Basis, Assumptions & Caveats";
            b.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(navy);

            var rows = new[]
            {
                ("Concrete source", "Concrete volume by Level + element (m³), e.g. a Revit concrete schedule. Net of openings."),
                ("Rebar", "Handled separately — see the Rebar Takeoff & Change Detection tool (PDF issue-to-issue). Not included in this concrete delta."),
                ("Quantity ≠ budget", "This delta is QUANTITY only. The $ movement is part scope, part unit-rate escalation — apply the recipient's rates."),
                ("Status", "High-level / rough order of magnitude. For information only. To be verified by the trades against the issued set."),
                ("Issued by", "Kor Structural • EGBC Permit 1000378"),
            };
            int rr = 3;
            foreach (var (k, v) in rows)
            {
                b.Cell(rr, 1).Value = k;
                b.Cell(rr, 1).Style.Font.SetBold().Font.SetFontColor(navy);
                b.Cell(rr, 2).Value = v;
                b.Cell(rr, 2).Style.Alignment.WrapText = true;
                b.Row(rr).Height = 28;
                rr++;
            }

            b.Column(1).Width = 26;
            b.Column(2).Width = 84;
        }

        private static Table BuildDocxTable(TakeoffReportModel model)
        {
            var table = new Table();
            table.AppendChild(new TableProperties(
                new TableBorders(
                    new TopBorder { Val = BorderValues.Single, Size = 4 },
                    new BottomBorder { Val = BorderValues.Single, Size = 4 },
                    new LeftBorder { Val = BorderValues.Single, Size = 4 },
                    new RightBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 },
                    new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));

            table.Append(TableRow(Headers));

            foreach (var line in model.Diff.Lines)
            {
                table.Append(TableRow(
                    line.Level,
                    line.ElementType.ToString(),
                    line.GradeCode,
                    Format(line.ConcreteBeforeM3),
                    Format(line.ConcreteAfterM3),
                    Format(line.ConcreteDeltaM3),
                    Format(line.FormworkDeltaM2),
                    line.Status.ToString()));
            }

            table.Append(TableRow(
                "TOTAL",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                Format(model.Diff.TotalConcreteDeltaM3),
                Format(model.Diff.TotalFormworkDeltaM2),
                string.Empty));

            return table;
        }

        private static void AppendLevelNotes(StringBuilder html, TakeoffReportModel model)
        {
            if (model.Diff.AddedLevels.Count > 0)
            {
                html.Append("<p>").Append(Html($"Added levels: {string.Join(", ", model.Diff.AddedLevels)}")).AppendLine("</p>");
            }

            if (model.Diff.RemovedLevels.Count > 0)
            {
                html.Append("<p>").Append(Html($"Removed levels: {string.Join(", ", model.Diff.RemovedLevels)}")).AppendLine("</p>");
            }
        }

        private static void AppendDocxLevelNotes(Body body, TakeoffReportModel model)
        {
            if (model.Diff.AddedLevels.Count > 0)
            {
                body.Append(Paragraph($"Added levels: {string.Join(", ", model.Diff.AddedLevels)}"));
            }

            if (model.Diff.RemovedLevels.Count > 0)
            {
                body.Append(Paragraph($"Removed levels: {string.Join(", ", model.Diff.RemovedLevels)}"));
            }
        }

        private static void AppendTextCell(StringBuilder html, string value)
        {
            html.Append("<td>").Append(Html(value)).AppendLine("</td>");
        }

        private static void AppendNumberCell(StringBuilder html, double value)
        {
            html.Append("<td class=\"num\">").Append(Format(value)).AppendLine("</td>");
        }

        private static string Title(TakeoffReportModel model)
        {
            return $"{model.ProjectWbs1} {model.ProjectName}  Quantity Delta";
        }

        private static string Subtitle(TakeoffReportModel model)
        {
            return $"{model.IssueBeforeLabel} -> {model.IssueAfterLabel}";
        }

        private static string GeneratedDate(TakeoffReportModel model)
        {
            return model.GeneratedAtUtc.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        private static string Format(double value)
        {
            return value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        private static string Html(string value)
        {
            return WebUtility.HtmlEncode(value);
        }

        private static Paragraph Paragraph(string text)
        {
            return new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static TableRow TableRow(params string[] values)
        {
            return new TableRow(values.Select(value => new TableCell(Paragraph(value))).ToArray());
        }
    }
}