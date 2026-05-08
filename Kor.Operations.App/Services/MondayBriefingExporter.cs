#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;

namespace Kor.Operations.Services;

internal sealed class MondayBriefingExporter
{
    internal void Export(string path, IReadOnlyList<BriefDto> brief, IReadOnlyList<AlertDto> alerts)
    {
        using var wb = new XLWorkbook();
        BuildCoverSheet(wb, brief, alerts);
        BuildStrategicBriefSheet(wb, brief);
        BuildActionItemsSheet(wb, alerts);
        wb.SaveAs(path);
    }

    private static void BuildCoverSheet(XLWorkbook wb, IReadOnlyList<BriefDto> brief, IReadOnlyList<AlertDto> alerts)
    {
        var ws = wb.Worksheets.Add("Cover");
        var weekOf = brief.FirstOrDefault()?.weekOf ?? MostRecentMonday(DateTime.Today);
        var inputTokens = brief.Sum(b => b.inputTokens);
        var outputTokens = brief.Sum(b => b.outputTokens);
        var estimatedCost = (inputTokens / 1_000_000.0 * 3.0) + (outputTokens / 1_000_000.0 * 15.0);

        ws.Cell(1, 1).Value = $"KOR Monday Briefing - Week of {weekOf:yyyy-MM-dd}";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 16;
        ws.Range(1, 1, 1, 4).Merge();

        ws.Cell(3, 1).Value = "Generated";
        ws.Cell(3, 2).Value = DateTime.Now;
        ws.Cell(3, 2).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
        ws.Cell(4, 1).Value = "Unacknowledged action items";
        ws.Cell(4, 2).Value = alerts.Count(a => a.acknowledgedAt is null);
        ws.Cell(5, 1).Value = "Strategic brief sections";
        ws.Cell(5, 2).Value = brief.Count;
        ws.Cell(6, 1).Value = "Input tokens";
        ws.Cell(6, 2).Value = inputTokens;
        ws.Cell(7, 1).Value = "Output tokens";
        ws.Cell(7, 2).Value = outputTokens;
        ws.Cell(8, 1).Value = "Estimated AI cost";
        ws.Cell(8, 2).Value = estimatedCost;
        ws.Cell(8, 2).Style.NumberFormat.Format = "$0.00";

        ws.Cell(11, 1).Value = "How to read this";
        ws.Cell(11, 1).Style.Font.Bold = true;
        ws.Cell(12, 1).Value =
            "Action Items are tactical issues that should be acknowledged when handled. " +
            "Strategic Brief sections are AI-generated synthesis for the COO: headline, supporting reasoning, and recommended action.";
        ws.Range(12, 1, 12, 4).Merge();
        ws.Cell(12, 1).Style.Alignment.WrapText = true;

        ws.Columns(1, 4).AdjustToContents();
        ws.Column(1).Width = Math.Max(ws.Column(1).Width, 28);
        ws.Column(2).Width = Math.Max(ws.Column(2).Width, 24);
    }

    private static void BuildStrategicBriefSheet(XLWorkbook wb, IReadOnlyList<BriefDto> brief)
    {
        var ws = wb.Worksheets.Add("Strategic Brief");
        string[] headers = { "Section", "Headline", "Body", "Recommendation", "Tokens (in/out)", "Tool Calls" };
        WriteHeaderRow(ws, 1, headers);
        ws.SheetView.FreezeRows(1);

        var row = 2;
        foreach (var b in brief.OrderBy(b => SectionOrder(b.section)))
        {
            ws.Cell(row, 1).Value = b.section;
            ws.Cell(row, 2).Value = b.headline;
            ws.Cell(row, 3).Value = b.body;
            ws.Cell(row, 4).Value = b.recommendation ?? "";
            ws.Cell(row, 5).Value = $"{b.inputTokens:N0} / {b.outputTokens:N0}";
            ws.Cell(row, 6).Value = b.toolCalls;

            if (row % 2 == 0)
            {
                ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = XLColor.FromHtml("#F8FAFC");
            }

            ws.Range(row, 1, row, headers.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Range(row, 3, row, 4).Style.Alignment.WrapText = true;
            row++;
        }

        Finalize(ws, 1, headers.Length);
        ws.Column(2).Width = 48;
        ws.Column(3).Width = 80;
        ws.Column(4).Width = 65;
    }

    private static void BuildActionItemsSheet(XLWorkbook wb, IReadOnlyList<AlertDto> alerts)
    {
        var ws = wb.Worksheets.Add("Action Items");
        string[] headers = { "Section", "Severity", "Generated", "Subject", "Title", "Body", "Acked?", "Acked By" };
        WriteHeaderRow(ws, 1, headers);
        ws.SheetView.FreezeRows(1);

        var row = 2;
        foreach (var a in alerts
            .OrderBy(a => a.section)
            .ThenBy(a => SeverityOrder(a.severity))
            .ThenByDescending(a => a.generatedAt))
        {
            ws.Cell(row, 1).Value = a.section;
            ws.Cell(row, 2).Value = a.severity;
            ws.Cell(row, 3).Value = a.generatedAt;
            ws.Cell(row, 3).Style.DateFormat.Format = "yyyy-mm-dd hh:mm";
            ws.Cell(row, 4).Value = a.subject ?? "";
            ws.Cell(row, 5).Value = a.title;
            ws.Cell(row, 6).Value = a.body;
            ws.Cell(row, 7).Value = a.acknowledgedAt is null ? "No" : "Yes";
            ws.Cell(row, 8).Value = a.acknowledgedBy ?? "";

            var severityCell = ws.Cell(row, 2);
            severityCell.Style.Font.Bold = true;
            severityCell.Style.Font.FontColor = XLColor.White;
            severityCell.Style.Fill.BackgroundColor = SeverityColor(a.severity);
            ws.Range(row, 1, row, headers.Length).Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
            ws.Range(row, 5, row, 6).Style.Alignment.WrapText = true;
            row++;
        }

        Finalize(ws, 1, headers.Length);
        ws.Column(5).Width = 60;
        ws.Column(6).Width = 90;
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

    private static DateTime MostRecentMonday(DateTime date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.Date.AddDays(-daysSinceMonday);
    }

    private static int SectionOrder(string section) => section switch
    {
        "FinancialHealth" => 0,
        "PortfolioHealth" => 1,
        "ClientStrategy" => 2,
        "BdMarket" => 3,
        "OperationsTalent" => 4,
        "WatchItems" => 5,
        _ => 99,
    };

    private static int SeverityOrder(string severity) => severity.ToUpperInvariant() switch
    {
        "HIGH" => 0,
        "MEDIUM" => 1,
        "LOW" => 2,
        _ => 99,
    };

    private static XLColor SeverityColor(string severity) => severity.ToUpperInvariant() switch
    {
        "HIGH" => XLColor.FromHtml("#C62828"),
        "MEDIUM" => XLColor.FromHtml("#EF6C00"),
        "LOW" => XLColor.FromHtml("#1565C0"),
        _ => XLColor.FromHtml("#6B7280"),
    };
}
