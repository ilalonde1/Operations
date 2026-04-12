#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using Kor.Operations.App.PMTools;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    internal static class PmToolsExportService
    {
        internal static void ExportUtilization(string path, string label, bool isEngineering,
            IReadOnlyList<UtilizationRow>? engRows, IReadOnlyList<DraftUtilizationRow>? draftRows)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add($"{label} Utilization");

            WriteTitle(ws, $"{label} Capacity — {DateTime.Now:MMM d, yyyy}", 5);

            string[] headers = isEngineering
                ? new[] { "Project", "Project #", "PM", "Phase", "Const Type", "Fee", "% Billed", "Eng Budget", "Eng Hours", "Remaining", "% Used", "Fee/Hrs", "Billed/Hrs", "Risk" }
                : new[] { "Project", "Project #", "PM", "Phase", "Const Type", "Fee", "% Billed", "Draft Budget", "Draft Hours", "Remaining", "% Used", "Fee/Hrs", "Billed/Hrs", "Risk" };

            WriteHeaderRow(ws, 3, headers);
            ws.SheetView.FreezeRows(3);

            var ri = 4;
            if (isEngineering && engRows is not null)
            {
                foreach (var r in engRows)
                {
                    ws.Cell(ri, 1).Value = r.ProjectName;
                    ws.Cell(ri, 2).Value = r.Wbs1;
                    ws.Cell(ri, 3).Value = r.Pm;
                    ws.Cell(ri, 4).Value = r.Phase;
                    ws.Cell(ri, 5).Value = r.ConstructionType;
                    ws.Cell(ri, 6).Value = r.Fee;              ws.Cell(ri, 6).Style.NumberFormat.Format = "$#,##0";
                    WritePctCell(ws.Cell(ri, 7), r.PercentBilled, r.PercentBilled.ToString("P0"));
                    WriteBudgetCell(ws.Cell(ri, 8), r.EngBudget, r.Project.EngBudgetActual <= 0);
                    ws.Cell(ri, 9).Value = r.EngHours;         ws.Cell(ri, 9).Style.NumberFormat.Format = "0.0";
                    ws.Cell(ri, 10).Value = r.RemainingEngHours; ws.Cell(ri, 10).Style.NumberFormat.Format = "0.0";
                    if (r.RemainingEngHours < 0) { ws.Cell(ri, 10).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 10).Style.Font.Bold = true; }
                    WritePctCell(ws.Cell(ri, 11), r.PercentEngUsed, r.PercentEngUsed.ToString("P0"), isBudgetBurn: true);
                    ws.Cell(ri, 12).Value = r.Project.FeePerHours;    ws.Cell(ri, 12).Style.NumberFormat.Format = "$#,##0";
                    ws.Cell(ri, 13).Value = r.Project.BilledPerHours; ws.Cell(ri, 13).Style.NumberFormat.Format = "$#,##0";
                    WriteRiskCell(ws.Cell(ri, 14), r.DeliveryConfidence);
                    ri++;
                }
            }
            else if (draftRows is not null)
            {
                foreach (var r in draftRows)
                {
                    ws.Cell(ri, 1).Value = r.ProjectName;
                    ws.Cell(ri, 2).Value = r.Wbs1;
                    ws.Cell(ri, 3).Value = r.Pm;
                    ws.Cell(ri, 4).Value = r.Phase;
                    ws.Cell(ri, 5).Value = r.ConstructionType;
                    ws.Cell(ri, 6).Value = r.Fee;                ws.Cell(ri, 6).Style.NumberFormat.Format = "$#,##0";
                    WritePctCell(ws.Cell(ri, 7), r.PercentBilled, r.PercentBilled.ToString("P0"));
                    WriteBudgetCell(ws.Cell(ri, 8), r.DraftBudget, r.Project.DraftBudgetActual <= 0);
                    ws.Cell(ri, 9).Value = r.DraftHours;         ws.Cell(ri, 9).Style.NumberFormat.Format = "0.0";
                    ws.Cell(ri, 10).Value = r.RemainingDraftHours; ws.Cell(ri, 10).Style.NumberFormat.Format = "0.0";
                    if (r.RemainingDraftHours < 0) { ws.Cell(ri, 10).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 10).Style.Font.Bold = true; }
                    WritePctCell(ws.Cell(ri, 11), r.PercentDraftUsed, r.PercentDraftUsed.ToString("P0"), isBudgetBurn: true);
                    ws.Cell(ri, 12).Value = r.Project.FeePerHours;    ws.Cell(ri, 12).Style.NumberFormat.Format = "$#,##0";
                    ws.Cell(ri, 13).Value = r.Project.BilledPerHours; ws.Cell(ri, 13).Style.NumberFormat.Format = "$#,##0";
                    WriteRiskCell(ws.Cell(ri, 14), r.DeliveryConfidence);
                    ri++;
                }
            }

            Finalize(ws, 3, headers.Length);
            wb.SaveAs(path);
        }

        internal static void ExportPmGroups(string path, IReadOnlyList<PmGroupViewModel> groups)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("PM Groups");

            WriteTitle(ws, $"PM Groups — {DateTime.Now:MMM d, yyyy}", 5);

            string[] headers = { "PM", "Project #", "Project Name", "Phase", "Const Type", "Category", "Draft Type",
                                  "Fee", "% Billed", "Unbilled",
                                  "Drafting Mgr", "Eng Budget", "Eng Hrs", "Eng %", "Eng Remaining",
                                  "Draft Budget", "Draft Hrs", "Draft %", "Draft Remaining",
                                  "Insp", "Fee/Hrs", "Billed/Hrs", "Delivery Risk" };

            WriteHeaderRow(ws, 3, headers);
            ws.SheetView.FreezeRows(3);

            var ri = 4;
            foreach (var group in groups)
            {
                var sumRow = ws.Range(ri, 1, ri, headers.Length);
                sumRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
                sumRow.Style.Font.Bold = true;
                ws.Cell(ri, 1).Value = group.PmName;
                ws.Cell(ri, 2).Value = $"{group.ProjectCount} projects";
                ws.Cell(ri, 8).Value = group.TotalFee;         ws.Cell(ri, 8).Style.NumberFormat.Format = "$#,##0";
                ws.Cell(ri, 10).Value = group.TotalUnbilled;   ws.Cell(ri, 10).Style.NumberFormat.Format = "$#,##0";
                ws.Cell(ri, 12).Value = group.TotalEngBudget;  ws.Cell(ri, 12).Style.NumberFormat.Format = "0.0";
                ws.Cell(ri, 13).Value = group.TotalEngHrs;     ws.Cell(ri, 13).Style.NumberFormat.Format = "0.0";
                ws.Cell(ri, 16).Value = group.TotalDraftBudget; ws.Cell(ri, 16).Style.NumberFormat.Format = "0.0";
                ws.Cell(ri, 17).Value = group.TotalDraftHrs;   ws.Cell(ri, 17).Style.NumberFormat.Format = "0.0";
                WriteGroupRisk(ws.Cell(ri, 23), group.AtRiskOrCriticalCount);
                ri++;

                foreach (var p in group.Projects)
                {
                    ws.Cell(ri, 1).Value = "";
                    ws.Cell(ri, 2).Value = p.Wbs1;
                    ws.Cell(ri, 3).Value = p.Name;
                    ws.Cell(ri, 4).Value = p.Phase;
                    ws.Cell(ri, 5).Value = p.ConstructionType;
                    ws.Cell(ri, 6).Value = p.ProjectCategory;
                    ws.Cell(ri, 7).Value = p.DraftingType;
                    ws.Cell(ri, 8).Value = p.Fee;              ws.Cell(ri, 8).Style.NumberFormat.Format = "$#,##0";
                    WritePctCell(ws.Cell(ri, 9), p.PercentBilled, p.PercentBilledText);
                    ws.Cell(ri, 10).Value = p.FeeRemaining;    ws.Cell(ri, 10).Style.NumberFormat.Format = "$#,##0";
                    if (p.FeeRemaining < 0) { ws.Cell(ri, 10).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 10).Style.Font.Bold = true; }
                    ws.Cell(ri, 11).Value = p.DraftingManager;
                    WriteBudgetCell(ws.Cell(ri, 12), p.EngBudget, p.IsEngBudgetEstimated);
                    ws.Cell(ri, 13).Value = p.EngHrs;          ws.Cell(ri, 13).Style.NumberFormat.Format = "0.0";
                    WritePctCell(ws.Cell(ri, 14), p.EngPercent, p.EngPercentText, isBudgetBurn: true);
                    ws.Cell(ri, 15).Value = p.RemainingEngHours; ws.Cell(ri, 15).Style.NumberFormat.Format = "0.0";
                    if (p.IsEngOverBudget) { ws.Cell(ri, 15).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 15).Style.Font.Bold = true; }
                    WriteBudgetCell(ws.Cell(ri, 16), p.DraftBudget, p.IsDraftBudgetEstimated);
                    ws.Cell(ri, 17).Value = p.DraftHrs;        ws.Cell(ri, 17).Style.NumberFormat.Format = "0.0";
                    WritePctCell(ws.Cell(ri, 18), p.DraftPercent, p.DraftPercentText, isBudgetBurn: true);
                    ws.Cell(ri, 19).Value = p.RemainingDraftHours; ws.Cell(ri, 19).Style.NumberFormat.Format = "0.0";
                    if (p.IsDraftOverBudget) { ws.Cell(ri, 19).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 19).Style.Font.Bold = true; }
                    ws.Cell(ri, 20).Value = p.InspHrs;        ws.Cell(ri, 20).Style.NumberFormat.Format = "0.0";
                    ws.Cell(ri, 21).Value = p.FeePerHours;    ws.Cell(ri, 21).Style.NumberFormat.Format = "$#,##0";
                    ws.Cell(ri, 22).Value = p.BilledPerHours; ws.Cell(ri, 22).Style.NumberFormat.Format = "$#,##0";
                    WriteRiskCell(ws.Cell(ri, 23), p.DeliveryRisk);
                    ri++;
                }
            }

            Finalize(ws, 3, headers.Length);
            wb.SaveAs(path);
        }

        internal static void ExportMeeting(string path, string dateLabel,
            IReadOnlyList<PmGroupViewModel> groups,
            IReadOnlyDictionary<string, WorkloadMeetingProjectRow> priorityByWbs1,
            string? overallNotes)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Workload Meeting");

            WriteTitle(ws, $"Workload Meeting — {dateLabel}", 6);

            string[] headers = { "Priority", "Project #", "Project Name", "PM", "Phase", "Const Type",
                                  "Fee", "% Billed", "Unbilled",
                                  "Drafting Mgr", "Eng %", "Eng Remaining", "Draft %", "Draft Remaining",
                                  "Fee/Hrs", "Billed/Hrs", "Delivery Risk", "Notes" };
            WriteHeaderRow(ws, 3, headers);
            ws.SheetView.FreezeRows(3);

            var ri = 4;
            foreach (var group in groups)
            {
                var sumRow = ws.Range(ri, 1, ri, headers.Length);
                sumRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
                sumRow.Style.Font.Bold = true;
                ws.Cell(ri, 4).Value = group.PmName;
                ws.Cell(ri, 2).Value = $"{group.ProjectCount} projects";
                ws.Cell(ri, 7).Value = group.TotalFee;       ws.Cell(ri, 7).Style.NumberFormat.Format = "$#,##0";
                ws.Cell(ri, 9).Value = group.TotalUnbilled;  ws.Cell(ri, 9).Style.NumberFormat.Format = "$#,##0";
                WriteGroupRisk(ws.Cell(ri, 17), group.AtRiskOrCriticalCount);
                ri++;

                foreach (var p in group.Projects)
                {
                    if (priorityByWbs1.TryGetValue(p.Wbs1, out var mp) && mp.Priority >= 1 && mp.Priority <= 5)
                    {
                        var pCell = ws.Cell(ri, 1);
                        pCell.Value = mp.PriorityLabel;
                        pCell.Style.Font.Bold = true;
                        pCell.Style.Font.FontColor = XLColor.White;
                        pCell.Style.Fill.BackgroundColor = PriorityBg(mp.Priority);
                        pCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    }

                    ws.Cell(ri, 2).Value = p.Wbs1;
                    ws.Cell(ri, 3).Value = p.Name;
                    ws.Cell(ri, 4).Value = p.Pm;
                    ws.Cell(ri, 5).Value = p.Phase;
                    ws.Cell(ri, 6).Value = p.ConstructionType;
                    ws.Cell(ri, 7).Value = p.Fee;              ws.Cell(ri, 7).Style.NumberFormat.Format = "$#,##0";
                    WritePctCell(ws.Cell(ri, 8), p.PercentBilled, p.PercentBilledText);
                    ws.Cell(ri, 9).Value = p.FeeRemaining;     ws.Cell(ri, 9).Style.NumberFormat.Format = "$#,##0";
                    if (p.FeeRemaining < 0) { ws.Cell(ri, 9).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 9).Style.Font.Bold = true; }
                    ws.Cell(ri, 10).Value = p.DraftingManager;
                    WritePctCell(ws.Cell(ri, 11), p.EngPercent, p.EngPercentText, isBudgetBurn: true);
                    ws.Cell(ri, 12).Value = p.RemainingEngHours; ws.Cell(ri, 12).Style.NumberFormat.Format = "0.0";
                    if (p.IsEngOverBudget) { ws.Cell(ri, 12).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 12).Style.Font.Bold = true; }
                    WritePctCell(ws.Cell(ri, 13), p.DraftPercent, p.DraftPercentText, isBudgetBurn: true);
                    ws.Cell(ri, 14).Value = p.RemainingDraftHours; ws.Cell(ri, 14).Style.NumberFormat.Format = "0.0";
                    if (p.IsDraftOverBudget) { ws.Cell(ri, 14).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 14).Style.Font.Bold = true; }
                    ws.Cell(ri, 15).Value = p.FeePerHours;     ws.Cell(ri, 15).Style.NumberFormat.Format = "$#,##0";
                    ws.Cell(ri, 16).Value = p.BilledPerHours;  ws.Cell(ri, 16).Style.NumberFormat.Format = "$#,##0";
                    WriteRiskCell(ws.Cell(ri, 17), p.DeliveryRisk);

                    if (priorityByWbs1.TryGetValue(p.Wbs1, out var mn) && !string.IsNullOrWhiteSpace(mn.Notes))
                    {
                        var notesCell = ws.Cell(ri, 18);
                        notesCell.Value = mn.Notes;
                        notesCell.Style.Alignment.WrapText = true;
                    }

                    ri++;
                }
            }

            if (!string.IsNullOrWhiteSpace(overallNotes))
            {
                ri += 1;
                var lbl = ws.Cell(ri, 1);
                lbl.Value = "Overall Notes:";
                lbl.Style.Font.Bold = true;
                ws.Range(ri, 1, ri, 2).Merge();

                ri++;
                var nCell = ws.Cell(ri, 1);
                nCell.Value = overallNotes;
                nCell.Style.Alignment.WrapText = true;
                ws.Range(ri, 1, ri, headers.Length).Merge();
            }

            Finalize(ws, 3, headers.Length);
            ws.Column(18).Width = 50;
            wb.SaveAs(path);
        }

        private static void WriteTitle(IXLWorksheet ws, string title, int mergeColumns)
        {
            var cell = ws.Cell(1, 1);
            cell.Value = title;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, mergeColumns).Merge();
        }

        private static void WriteHeaderRow(IXLWorksheet ws, int row, string[] headers)
        {
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#E5E7EB");
            }
        }

        private static void Finalize(IXLWorksheet ws, int headerRow, int columnCount)
        {
            var lastRow = ws.LastRowUsed()?.RowNumber() ?? headerRow;
            ws.Range(headerRow, 1, lastRow, columnCount).SetAutoFilter();
            ws.Columns(1, columnCount).AdjustToContents();
        }

        private static void WriteBudgetCell(IXLCell cell, double budget, bool isEstimated)
        {
            cell.Value = budget;
            cell.Style.NumberFormat.Format = "0.0";
            if (isEstimated)
            {
                cell.Style.Font.FontColor = XLColor.FromHtml("#7C3AED");
                cell.Style.Font.Italic = true;
            }
        }

        private static void WritePctCell(IXLCell cell, double pct, string text, bool isBudgetBurn = false)
        {
            cell.Value = text;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = PctBarColor(pct, isBudgetBurn);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void WriteRiskCell(IXLCell cell, string risk)
        {
            cell.Value = risk;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = RiskColor(risk);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void WriteGroupRisk(IXLCell cell, int atRiskOrCriticalCount)
        {
            if (atRiskOrCriticalCount > 0)
                WriteRiskCell(cell, $"{atRiskOrCriticalCount} at risk");
            else
            {
                cell.Value = "Healthy";
                cell.Style.Font.FontColor = XLColor.FromHtml("#166534");
            }
        }

        private static XLColor PctBarColor(double pct, bool isBudgetBurn = false)
        {
            if (isBudgetBurn)
                return pct >= 1.0 ? XLColor.FromHtml("#DC2626") : pct >= 0.85 ? XLColor.FromHtml("#EA580C") : pct >= 0.50 ? XLColor.FromHtml("#16A34A") : XLColor.FromHtml("#6B7280");
            return pct >= 0.95 ? XLColor.FromHtml("#DC2626") : pct >= 0.85 ? XLColor.FromHtml("#EA580C") : pct >= 0.50 ? XLColor.FromHtml("#16A34A") : XLColor.FromHtml("#6B7280");
        }

        private static XLColor RiskColor(string risk) => risk switch
        {
            "Critical" => XLColor.FromHtml("#DC2626"),
            "At Risk" => XLColor.FromHtml("#EA580C"),
            "Watch" => XLColor.FromHtml("#D97706"),
            _ => XLColor.FromHtml("#16A34A"),
        };

        private static XLColor PriorityBg(int p) => p switch
        {
            1 => XLColor.FromHtml("#DC2626"),
            2 => XLColor.FromHtml("#EA580C"),
            3 => XLColor.FromHtml("#D97706"),
            4 => XLColor.FromHtml("#2563EB"),
            5 => XLColor.FromHtml("#6B7280"),
            _ => XLColor.NoColor,
        };
    }
}
