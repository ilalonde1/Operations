#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ClosedXML.Excel;
using Kor.Operations.App.Options;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Microsoft.Win32;

namespace Kor.Operations.Financials
{
    internal sealed class BilledFinancialsPresenter
    {
        private static readonly SignToBrushConverter SignToBrush = new();
        private static readonly LineIndentConverter LineIndent = new();

        private readonly BilledFinancialsService _service;
        private readonly FinancialsOptions _financialsOptions;
        private readonly DataGrid _pnlGrid;
        private readonly Canvas _netTrendCanvas;
        private readonly Polyline _netTrendLine;
        private readonly Canvas _revExpCanvas;
        private readonly UniformGrid _netTrendLabelGrid;
        private readonly UniformGrid _revExpLabelGrid;

        private CancellationTokenSource? _cts;
        private BilledFinancialsResult? _lastResult;
        private decimal[] _lastNetTrend = Array.Empty<decimal>();
        private decimal[] _lastRevenueTrend = Array.Empty<decimal>();
        private decimal[] _lastExpenseTrend = Array.Empty<decimal>();
        private string[] _lastTrendLabels = Array.Empty<string>();

        public BilledFinancialsPresenter(
            BilledFinancialsService service,
            FinancialsOptions financialsOptions,
            DataGrid pnlGrid,
            Canvas netTrendCanvas,
            Polyline netTrendLine,
            Canvas revExpCanvas,
            UniformGrid netTrendLabelGrid,
            UniformGrid revExpLabelGrid)
        {
            _service = service ?? throw new ArgumentNullException(nameof(service));
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
            _pnlGrid = pnlGrid ?? throw new ArgumentNullException(nameof(pnlGrid));
            _netTrendCanvas = netTrendCanvas ?? throw new ArgumentNullException(nameof(netTrendCanvas));
            _netTrendLine = netTrendLine ?? throw new ArgumentNullException(nameof(netTrendLine));
            _revExpCanvas = revExpCanvas ?? throw new ArgumentNullException(nameof(revExpCanvas));
            _netTrendLabelGrid = netTrendLabelGrid ?? throw new ArgumentNullException(nameof(netTrendLabelGrid));
            _revExpLabelGrid = revExpLabelGrid ?? throw new ArgumentNullException(nameof(revExpLabelGrid));
            ViewModel = new BilledFinancialsViewModel(_financialsOptions);
        }

        public BilledFinancialsViewModel ViewModel { get; }

        public BilledFinancialsResult? CurrentResult => _lastResult;

        public async Task InitializeAsync()
        {
            var today = DateTime.Today;
            var endOfPrevMonth = new DateTime(today.Year, today.Month, 1).AddDays(-1);
            var fyStartMonth = Math.Clamp(ReadInt(_financialsOptions.FiscalYearStartMonth, 4), 1, 12);
            var startYear = endOfPrevMonth.Month >= fyStartMonth ? endOfPrevMonth.Year : endOfPrevMonth.Year - 1;

            ViewModel.FromDate = new DateTime(startYear, fyStartMonth, 1);
            ViewModel.ToDate = endOfPrevMonth;
            ViewModel.OrgFilter = (_financialsOptions.BilledDefaultOrg ?? "").Trim();
            await RefreshAsync(forceRefresh: false).ConfigureAwait(true);
        }

        public async Task RefreshAsync(bool forceRefresh)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            ViewModel.ClearMessages();
            ViewModel.CanRefresh = false;
            ViewModel.CanExport = false;
            _lastResult = null;

            try
            {
                var from = (ViewModel.FromDate ?? DateTime.Today).Date;
                var to = (ViewModel.ToDate ?? DateTime.Today).Date;
                if (to < from)
                {
                    ViewModel.SetError("Invalid date range: 'To' must be on or after 'From'.");
                    return;
                }

                var org = string.IsNullOrWhiteSpace(ViewModel.OrgFilter) ? null : ViewModel.OrgFilter.Trim();
                var result = await _service.BuildAsync(
                    fromDate: from,
                    toDate: to,
                    orgFilter: org,
                    forceRefresh: forceRefresh,
                    ct: _cts.Token).ConfigureAwait(true);

                ViewModel.ApplySummary(result);
                BindGrid(result);
                _lastResult = result;
                ViewModel.CanExport = true;
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                ViewModel.SetError($"Unable to load Billed P&L.\n{ex.Message}");
            }
            finally
            {
                ViewModel.CanRefresh = true;
            }
        }

        public void RenderCharts()
        {
            RenderNetTrend(_lastNetTrend);
            RenderRevExpTrend(_lastRevenueTrend, _lastExpenseTrend);
            RenderLabelGrid(_netTrendLabelGrid, _lastTrendLabels);
            RenderLabelGrid(_revExpLabelGrid, _lastTrendLabels);
        }

        public async Task ExportAsync(Window? owner)
        {
            var result = _lastResult;
            if (result == null || !ViewModel.CanExport)
                return;

            var from = (ViewModel.FromDate ?? DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var to = (ViewModel.ToDate ?? DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var fromDisplay = (ViewModel.FromDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var toDisplay = (ViewModel.ToDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var org = ViewModel.OrgFilter ?? "";
            var export = BuildExportSnapshot(BuildDataTable(result), result.PeriodColumnNames, ViewModel.HideZeroRows);

            var sfd = new SaveFileDialog
            {
                Title = "Export Billed P&L",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"PnL_Billed_{from}_{to}.xlsx",
                AddExtension = true,
                OverwritePrompt = true
            };

            var accepted = owner != null ? sfd.ShowDialog(owner) : sfd.ShowDialog();
            if (accepted != true)
                return;

            ViewModel.CanExport = false;
            try
            {
                var path = sfd.FileName;
                await Task.Run(() => ExportToExcel(path, export, fromDisplay, toDisplay, org)).ConfigureAwait(true);
                if (owner != null)
                    MessageBox.Show(owner, "Export completed.", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Information);
                else
                    MessageBox.Show("Export completed.", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                if (owner != null)
                    MessageBox.Show(owner, $"Export failed:\n{ex.Message}", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                else
                    MessageBox.Show($"Export failed:\n{ex.Message}", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ViewModel.CanExport = true;
            }
        }

        private sealed class ExportSnapshot
        {
            public string[] PeriodHeaders { get; init; } = Array.Empty<string>();
            public ExportRow[] Rows { get; init; } = Array.Empty<ExportRow>();
        }

        private sealed class ExportRow
        {
            public string Section { get; init; } = "";
            public string LineItem { get; init; } = "";
            public string RowKind { get; init; } = "";
            public decimal[] PeriodValues { get; init; } = Array.Empty<decimal>();
            public decimal Current { get; init; }
            public decimal Prior { get; init; }
            public decimal MoM { get; init; }
            public decimal MoMPct { get; init; }
            public decimal YTD { get; init; }
            public decimal TTM { get; init; }
            public decimal PctOfRevenue { get; init; }
        }

        private static ExportSnapshot BuildExportSnapshot(DataTable table, string[] periodHeaders, bool hideZeros)
        {
            var rows = new List<ExportRow>(Math.Max(32, table.Rows.Count));
            foreach (DataRow r in table.Rows)
            {
                if (hideZeros && r.Table.Columns.Contains("IsAllZero") && r["IsAllZero"] is bool b && b)
                    continue;

                var periodValues = new decimal[periodHeaders.Length];
                for (var i = 0; i < periodHeaders.Length; i++)
                    periodValues[i] = r[periodHeaders[i]] is decimal d ? d : 0m;

                rows.Add(new ExportRow
                {
                    Section = Convert.ToString(r["Section"], CultureInfo.InvariantCulture) ?? "",
                    LineItem = Convert.ToString(r["LineItem"], CultureInfo.InvariantCulture) ?? "",
                    RowKind = Convert.ToString(r["RowKind"], CultureInfo.InvariantCulture) ?? "",
                    PeriodValues = periodValues,
                    Current = r["Current"] is decimal cur ? cur : 0m,
                    Prior = r["Prior"] is decimal prior ? prior : 0m,
                    MoM = r["MoM"] is decimal mom ? mom : 0m,
                    MoMPct = r["MoMPct"] is decimal mp ? mp : 0m,
                    YTD = r["YTD"] is decimal ytd ? ytd : 0m,
                    TTM = r["TTM"] is decimal ttm ? ttm : 0m,
                    PctOfRevenue = r["PctOfRevenue"] is decimal pr ? pr : 0m
                });
            }

            return new ExportSnapshot { PeriodHeaders = periodHeaders, Rows = rows.ToArray() };
        }

        private static void ExportToExcel(string path, ExportSnapshot snap, string fromDisplay, string toDisplay, string org)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("P&L (Billed)");

            var row = 1;
            ws.Cell(row, 1).Value = "Profit & Loss (Billed)";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 16;
            row++;

            ws.Cell(row, 1).Value = "From";
            ws.Cell(row, 2).Value = fromDisplay;
            ws.Cell(row, 3).Value = "To";
            ws.Cell(row, 4).Value = toDisplay;
            ws.Cell(row, 5).Value = "Org";
            ws.Cell(row, 6).Value = org;
            row += 2;

            var headers = new List<string> { "Section", "Line Item" };
            headers.AddRange(snap.PeriodHeaders);
            headers.AddRange(new[] { "Current", "Prior", "MoM", "MoM %", "YTD", "TTM", "% Rev" });

            for (var c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#E5E7EB");
            }

            ws.SheetView.FreezeRows(row);

            var dataStart = row + 1;
            row = dataStart;
            foreach (var r in snap.Rows)
            {
                ws.Cell(row, 1).Value = r.Section;
                ws.Cell(row, 2).Value = r.LineItem;

                var colIndex = 3;
                for (var i = 0; i < r.PeriodValues.Length; i++)
                    ws.Cell(row, colIndex++).Value = r.PeriodValues[i];

                ws.Cell(row, colIndex++).Value = r.Current;
                ws.Cell(row, colIndex++).Value = r.Prior;
                ws.Cell(row, colIndex++).Value = r.MoM;
                ws.Cell(row, colIndex++).Value = r.MoMPct;
                ws.Cell(row, colIndex++).Value = r.YTD;
                ws.Cell(row, colIndex++).Value = r.TTM;
                ws.Cell(row, colIndex++).Value = r.PctOfRevenue;

                if (string.Equals(r.RowKind, "GrandTotal", StringComparison.OrdinalIgnoreCase))
                {
                    ws.Range(row, 1, row, headers.Count).Style.Font.Bold = true;
                    ws.Range(row, 1, row, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#E5E7EB");
                }
                else if (string.Equals(r.RowKind, "SectionTotal", StringComparison.OrdinalIgnoreCase))
                {
                    ws.Range(row, 1, row, headers.Count).Style.Font.Bold = true;
                    ws.Range(row, 1, row, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#F9FAFB");
                }

                row++;
            }

            var lastRow = row - 1;
            if (lastRow >= dataStart)
            {
                var firstMoneyCol = 3;
                var lastMoneyCol = 2 + snap.PeriodHeaders.Length + 3;
                for (var c = firstMoneyCol; c <= lastMoneyCol; c++)
                    ws.Column(c).Style.NumberFormat.Format = "$#,##0;-$#,##0;0";

                var momPctCol = 2 + snap.PeriodHeaders.Length + 4;
                ws.Column(momPctCol).Style.NumberFormat.Format = "0.0%";

                var ytdCol = 2 + snap.PeriodHeaders.Length + 5;
                var ttmCol = 2 + snap.PeriodHeaders.Length + 6;
                ws.Column(ytdCol).Style.NumberFormat.Format = "$#,##0;-$#,##0;0";
                ws.Column(ttmCol).Style.NumberFormat.Format = "$#,##0;-$#,##0;0";

                var pctRevCol = 2 + snap.PeriodHeaders.Length + 7;
                ws.Column(pctRevCol).Style.NumberFormat.Format = "0.0%";

                ws.Columns(1, 2).AdjustToContents();
                ws.Columns(3, headers.Count).Width = 14;

                var rng = ws.Range(dataStart - 1, 1, lastRow, headers.Count);
                var tbl = rng.CreateTable();
                tbl.Theme = XLTableTheme.TableStyleLight9;
            }

            wb.SaveAs(path);
        }

        private void BindGrid(BilledFinancialsResult result)
        {
            var table = BuildDataTable(result);
            _pnlGrid.Columns.Clear();
            _pnlGrid.ItemsSource = null;

            table.DefaultView.RowFilter = ViewModel.HideZeroRows ? "IsAllZero = 0" : "";

            var cvs = new CollectionViewSource { Source = table.DefaultView };
            cvs.GroupDescriptions.Clear();
            cvs.GroupDescriptions.Add(new PropertyGroupDescription("Section"));
            cvs.SortDescriptions.Clear();
            cvs.SortDescriptions.Add(new SortDescription("SectionSort", ListSortDirection.Ascending));
            cvs.SortDescriptions.Add(new SortDescription("LineSort", ListSortDirection.Ascending));
            _pnlGrid.ItemsSource = cvs.View;

            _pnlGrid.Columns.Add(MakeLineItemColumn());
            foreach (var p in result.PeriodColumnNames)
                _pnlGrid.Columns.Add(MakeNumericTemplateColumn(p, $"[{p}]", "{0:C0}", 120));

            _pnlGrid.Columns.Add(MakeNumericTemplateColumn("Current", "[Current]", "{0:C0}", 120));
            _pnlGrid.Columns.Add(MakeNumericTemplateColumn("Prior", "[Prior]", "{0:C0}", 120));
            _pnlGrid.Columns.Add(MakeNumericTemplateColumn("MoM", "[MoM]", "{0:C0}", 120));
            _pnlGrid.Columns.Add(MakeNumericTemplateColumn("MoM %", "[MoMPct]", "{0:P1}", 90));
            _pnlGrid.Columns.Add(MakeNumericTemplateColumn("YTD", "[YTD]", "{0:C0}", 120));
            _pnlGrid.Columns.Add(MakeNumericTemplateColumn("TTM", "[TTM]", "{0:C0}", 120));
            _pnlGrid.Columns.Add(MakeNumericTemplateColumn("% Rev", "[PctOfRevenue]", "{0:P1}", 90));

            _lastNetTrend = result.NetIncomeTrendValues ?? Array.Empty<decimal>();
            _lastRevenueTrend = result.RevenueTrendValues ?? Array.Empty<decimal>();
            _lastExpenseTrend = result.ExpenseTrendValues ?? Array.Empty<decimal>();
            _lastTrendLabels = result.TrendLabels ?? Array.Empty<string>();
            RenderCharts();
        }

        private DataTable BuildDataTable(BilledFinancialsResult result)
        {
            var dt = new DataTable("BilledPnL");
            dt.Columns.Add("Section", typeof(string));
            dt.Columns.Add("LineItem", typeof(string));
            dt.Columns.Add("RowKind", typeof(string));
            dt.Columns.Add("SectionSort", typeof(int));
            dt.Columns.Add("LineSort", typeof(int));
            dt.Columns.Add("IsAllZero", typeof(bool));

            foreach (var col in result.PeriodColumnNames)
                dt.Columns.Add(col, typeof(decimal));

            dt.Columns.Add("Current", typeof(decimal));
            dt.Columns.Add("Prior", typeof(decimal));
            dt.Columns.Add("MoM", typeof(decimal));
            dt.Columns.Add("MoMPct", typeof(decimal));
            dt.Columns.Add("YTD", typeof(decimal));
            dt.Columns.Add("TTM", typeof(decimal));
            dt.Columns.Add("PctOfRevenue", typeof(decimal));

            foreach (var line in result.Lines)
            {
                var row = dt.NewRow();
                row["Section"] = line.Section;
                row["LineItem"] = line.LineItem;
                row["RowKind"] = line.RowKind;
                row["SectionSort"] = line.SectionSort;
                row["LineSort"] = line.LineSort;
                for (var i = 0; i < result.Periods.Length; i++)
                {
                    var period = result.Periods[i];
                    row[result.PeriodColumnNames[i]] = line.AmountsByPeriod.TryGetValue(period, out var amount) ? amount : 0m;
                }

                dt.Rows.Add(row);
            }

            ComputeExecutiveColumns(dt, result);
            return dt;
        }

        private void ComputeExecutiveColumns(DataTable dt, BilledFinancialsResult result)
        {
            if (result.Periods.Length == 0)
                return;

            var curIdx = result.Periods.Length - 1;
            if (result.MaxBilledPeriod.HasValue)
            {
                var clamped = Array.FindLastIndex(result.Periods, p => p <= result.MaxBilledPeriod.Value);
                if (clamped >= 0)
                    curIdx = clamped;
            }

            var priorIdx = curIdx >= 1 ? curIdx - 1 : -1;
            var curCol = result.PeriodColumnNames[curIdx];
            var priorCol = priorIdx >= 0 ? result.PeriodColumnNames[priorIdx] : null;
            var revenueCurrent = FindGrandValue(dt, "Total Revenue", curCol);
            var fyStartMonth = Math.Clamp(ReadInt(_financialsOptions.FiscalYearStartMonth, 4), 1, 12);
            var ytdStart = FiscalYearStartPeriod(result.Periods[curIdx], fyStartMonth);
            var ytdIndexes = result.Periods
                .Select((p, idx) => new { p, idx })
                .Where(x => x.p >= ytdStart && x.idx <= curIdx)
                .Select(x => x.idx)
                .ToList();
            var ttmStart = Math.Max(0, result.Periods.Length - 12);
            var ttmIndexes = Enumerable.Range(ttmStart, result.Periods.Length - ttmStart).ToList();

            foreach (DataRow row in dt.Rows)
            {
                var cur = ReadDecimal(row, curCol);
                var prior = priorCol == null ? 0m : ReadDecimal(row, priorCol);
                var mom = cur - prior;
                row["Current"] = cur;
                row["Prior"] = prior;
                row["MoM"] = mom;
                if (prior != 0m)
                    row["MoMPct"] = mom / prior;
                row["YTD"] = Sum(row, ytdIndexes, result.PeriodColumnNames);
                row["TTM"] = Sum(row, ttmIndexes, result.PeriodColumnNames);
                if (revenueCurrent != 0m)
                    row["PctOfRevenue"] = cur / revenueCurrent;
                row["IsAllZero"] = result.PeriodColumnNames.All(c => ReadDecimal(row, c) == 0m);
            }
        }

        private static decimal FindGrandValue(DataTable dt, string lineItem, string column)
        {
            foreach (DataRow row in dt.Rows)
            {
                if (!string.Equals(Convert.ToString(row["LineItem"]), lineItem, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!string.Equals(Convert.ToString(row["RowKind"]), "GrandTotal", StringComparison.OrdinalIgnoreCase))
                    continue;
                return ReadDecimal(row, column);
            }

            return 0m;
        }

        private static decimal Sum(DataRow row, IReadOnlyList<int> indexes, IReadOnlyList<string> columns)
            => indexes.Sum(i => ReadDecimal(row, columns[i]));

        private static decimal ReadDecimal(DataRow row, string column)
            => row.Table.Columns.Contains(column) && row[column] is decimal value ? value : 0m;

        private static int FiscalYearStartPeriod(int period, int fyStartMonth)
        {
            var year = period / 100;
            var month = period % 100;
            var startYear = month >= fyStartMonth ? year : year - 1;
            return (startYear * 100) + fyStartMonth;
        }

        private void RenderNetTrend(decimal[] values)
        {
            _netTrendLine.Points = BuildSparklinePoints(_netTrendCanvas, values);
            RenderBarsBehindLine(_netTrendCanvas, _netTrendLine, values, pos: "#93C5FD", neg: "#FCA5A5");
        }

        private void RenderRevExpTrend(decimal[] revenue, decimal[] expense)
        {
            if (revenue == null || expense == null)
                return;

            var n = Math.Min(revenue.Length, expense.Length);
            if (n <= 0)
                return;

            _revExpCanvas.Children.Clear();
            var w = _revExpCanvas.ActualWidth;
            if (w <= 10) w = 320;
            var h = _revExpCanvas.Height;
            if (h <= 10) h = 56;

            var revAbs = revenue.Take(n).Select(v => (double)Math.Abs(v)).ToArray();
            var expAbs = expense.Take(n).Select(v => (double)Math.Abs(v)).ToArray();
            var max = 0d;
            for (var i = 0; i < n; i++)
                max = Math.Max(max, revAbs[i] + expAbs[i]);
            if (max <= 0.000001)
                return;

            var gap = 2.0;
            var barW = Math.Max(2.0, (w - (gap * (n - 1))) / n);
            var innerH = Math.Max(1.0, h - 4.0);
            var revBrush = FrozenBrush("#22C55E");
            var expBrush = FrozenBrush("#F97316");

            for (var i = 0; i < n; i++)
            {
                var x = i * (barW + gap);
                var expH = (expAbs[i] / max) * innerH;
                var revH = (revAbs[i] / max) * innerH;

                var expRect = new Rectangle { Width = barW, Height = Math.Max(1.0, expH), RadiusX = 2, RadiusY = 2, Fill = expBrush, Opacity = 0.70 };
                Canvas.SetLeft(expRect, x);
                Canvas.SetTop(expRect, 2 + (innerH - expH));
                _revExpCanvas.Children.Add(expRect);

                var revRect = new Rectangle { Width = barW, Height = Math.Max(1.0, revH), RadiusX = 2, RadiusY = 2, Fill = revBrush, Opacity = 0.75 };
                Canvas.SetLeft(revRect, x);
                Canvas.SetTop(revRect, 2 + (innerH - expH - revH));
                _revExpCanvas.Children.Add(revRect);
            }
        }

        private static void RenderLabelGrid(UniformGrid grid, string[] labels)
        {
            grid.Children.Clear();
            if (labels == null || labels.Length == 0)
                return;

            grid.Columns = labels.Length;
            foreach (var label in labels)
            {
                grid.Children.Add(new TextBlock
                {
                    Text = label ?? "",
                    FontSize = 10,
                    Foreground = FrozenBrush("#6B7280"),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0)
                });
            }
        }

        private static PointCollection BuildSparklinePoints(Canvas canvas, decimal[] values)
        {
            if (values == null || values.Length == 0)
                return new PointCollection();

            var w = canvas.ActualWidth;
            if (w <= 10) w = 240;
            var h = canvas.Height;
            if (h <= 10) h = 44;
            var min = values.Min();
            var max = values.Max();
            if (min == max)
            {
                var y = h / 2.0;
                return new PointCollection(Enumerable.Range(0, values.Length)
                    .Select(i => new Point(values.Length == 1 ? w : (w * i / (values.Length - 1)), y)));
            }

            var innerH = Math.Max(1.0, h - 4.0);
            var points = new PointCollection(values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                var x = values.Length == 1 ? w : (w * i / (values.Length - 1));
                var t = (double)((values[i] - min) / (max - min));
                points.Add(new Point(x, 2.0 + (1.0 - t) * innerH));
            }

            return points;
        }

        private static void RenderBarsBehindLine(Canvas canvas, Polyline line, decimal[] values, string pos, string neg)
        {
            if (values == null || values.Length == 0)
                return;

            for (var i = canvas.Children.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(canvas.Children[i], line))
                    canvas.Children.RemoveAt(i);
            }

            var w = canvas.ActualWidth;
            if (w <= 10) w = 240;
            var h = canvas.Height;
            if (h <= 10) h = 44;
            var maxAbs = (double)Math.Max(Math.Abs(values.Min()), Math.Abs(values.Max()));
            if (maxAbs <= 0.000001)
                return;

            var gap = 2.0;
            var barW = Math.Max(1.0, (w - (gap * (values.Length - 1))) / values.Length);
            var midY = h / 2.0;
            var posBrush = FrozenBrush(pos);
            var negBrush = FrozenBrush(neg);

            for (var i = 0; i < values.Length; i++)
            {
                var value = (double)values[i];
                var height = Math.Min(1.0, Math.Abs(value) / maxAbs) * (h * 0.45);
                var rect = new Rectangle
                {
                    Width = barW,
                    Height = Math.Max(1.0, height),
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = value >= 0 ? posBrush : negBrush
                };
                Canvas.SetLeft(rect, i * (barW + gap));
                Canvas.SetTop(rect, value >= 0 ? midY - height : midY);
                Panel.SetZIndex(rect, 0);
                canvas.Children.Add(rect);
            }
        }

        private DataGridTemplateColumn MakeLineItemColumn()
        {
            var root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            root.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetBinding(TextBlock.TextProperty, new Binding("[LineItem]"));
            text.SetBinding(TextBlock.MarginProperty, new Binding("[RowKind]") { Converter = LineIndent });
            root.AppendChild(text);

            return new DataGridTemplateColumn
            {
                Header = "Line Item",
                CellTemplate = new DataTemplate { VisualTree = root },
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            };
        }

        private static DataGridTemplateColumn MakeNumericTemplateColumn(object header, string bindingPath, string format, double width)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            text.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));
            text.SetBinding(TextBlock.TextProperty, new Binding(bindingPath) { StringFormat = format });
            text.SetBinding(TextBlock.ForegroundProperty, new Binding(bindingPath) { Converter = SignToBrush });

            return new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = new DataTemplate { VisualTree = text },
                Width = width
            };
        }

        private static int ReadInt(string raw, int fallback)
        {
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
                return value;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.CurrentCulture, out value))
                return value;
            return fallback;
        }

        private static Brush FrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }

        private sealed class SignToBrushConverter : IValueConverter
        {
            private static readonly Brush Pos = FrozenBrush("#166534");
            private static readonly Brush Neg = FrozenBrush("#B91C1C");
            private static readonly Brush Zero = FrozenBrush("#6B7280");

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value == null || value == DBNull.Value)
                    return Zero;
                try
                {
                    var decimalValue = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    if (decimalValue > 0m) return Pos;
                    if (decimalValue < 0m) return Neg;
                    return Zero;
                }
                catch
                {
                    return Zero;
                }
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => Binding.DoNothing;
        }

        private sealed class LineIndentConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var kind = value?.ToString() ?? "";
                return new Thickness(string.Equals(kind, "Detail", StringComparison.OrdinalIgnoreCase) ? 14.0 : 0.0, 0, 8, 0);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => Binding.DoNothing;
        }
    }

    internal sealed class BilledFinancialsViewModel : ObservableObject, IAiContextProvider
    {
        private readonly FinancialsOptions _financialsOptions;
        private DateTime? _fromDate;
        private DateTime? _toDate;
        private string _orgFilter = "";
        private bool _hideZeroRows = true;
        private bool _canRefresh = true;
        private bool _canExport;
        private string _errorMessage = "";
        private Visibility _errorVisibility = Visibility.Collapsed;
        private string _summaryRevenue = "";
        private string _summaryExpenses = "";
        private string _summaryNet = "";
        private string _summaryMargin = "";
        private string _reconciliationBanner = "";
        private Brush _reconciliationBrush = FrozenBrush("#FF065F46");
        private BilledFinancialsResult? _lastResult;

        public BilledFinancialsViewModel(FinancialsOptions financialsOptions)
        {
            _financialsOptions = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));
        }

        public DateTime? FromDate
        {
            get => _fromDate;
            set { _fromDate = value; OnPropertyChanged(); }
        }

        public DateTime? ToDate
        {
            get => _toDate;
            set { _toDate = value; OnPropertyChanged(); }
        }

        public string OrgFilter
        {
            get => _orgFilter;
            set { _orgFilter = value ?? ""; OnPropertyChanged(); }
        }

        public bool HideZeroRows
        {
            get => _hideZeroRows;
            set { _hideZeroRows = value; OnPropertyChanged(); }
        }

        public bool CanRefresh
        {
            get => _canRefresh;
            set { _canRefresh = value; OnPropertyChanged(); }
        }

        public bool CanExport
        {
            get => _canExport;
            set { _canExport = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { _errorMessage = value ?? ""; OnPropertyChanged(); }
        }

        public Visibility ErrorVisibility
        {
            get => _errorVisibility;
            private set { _errorVisibility = value; OnPropertyChanged(); }
        }

        public string SummaryRevenue
        {
            get => _summaryRevenue;
            private set { _summaryRevenue = value ?? ""; OnPropertyChanged(); }
        }

        public string SummaryExpenses
        {
            get => _summaryExpenses;
            private set { _summaryExpenses = value ?? ""; OnPropertyChanged(); }
        }

        public string SummaryNet
        {
            get => _summaryNet;
            private set { _summaryNet = value ?? ""; OnPropertyChanged(); }
        }

        public string SummaryMargin
        {
            get => _summaryMargin;
            private set { _summaryMargin = value ?? ""; OnPropertyChanged(); }
        }

        public string ReconciliationBanner
        {
            get => _reconciliationBanner;
            private set { _reconciliationBanner = value ?? ""; OnPropertyChanged(); OnPropertyChanged(nameof(ReconciliationVisibility)); }
        }

        public Brush ReconciliationBrush
        {
            get => _reconciliationBrush;
            private set { _reconciliationBrush = value; OnPropertyChanged(); }
        }

        public Visibility ReconciliationVisibility => string.IsNullOrWhiteSpace(ReconciliationBanner) ? Visibility.Collapsed : Visibility.Visible;

        public string ReconciliationTooltip =>
            "Billed = invoices issued (LedgerAR). Posted = transactions GL-posted by accounting (GLSummary). The lag is normal; Billed matches Daler's source-of-truth Crystal report.";

        public void ClearMessages()
        {
            ErrorMessage = "";
            ErrorVisibility = Visibility.Collapsed;
        }

        public void SetError(string message)
        {
            ErrorMessage = message ?? "";
            ErrorVisibility = string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void ApplySummary(BilledFinancialsResult result)
        {
            _lastResult = result;
            var currentPeriod = result.MaxBilledPeriod.HasValue
                ? result.Periods.LastOrDefault(p => p <= result.MaxBilledPeriod.Value)
                : result.Periods.LastOrDefault();

            var revenue = GetLineValue(result, "Total Revenue", currentPeriod);
            var expenses = GetLineValue(result, "Total Expenses", currentPeriod);
            var net = GetLineValue(result, "Net Income", currentPeriod);
            var margin = revenue == 0m ? (decimal?)null : net / Math.Abs(revenue);

            SummaryRevenue = Math.Abs(revenue).ToString("C0", CultureInfo.CurrentCulture);
            SummaryExpenses = Math.Abs(expenses).ToString("C0", CultureInfo.CurrentCulture);
            SummaryNet = net.ToString("C0", CultureInfo.CurrentCulture);
            SummaryMargin = margin.HasValue ? margin.Value.ToString("P1", CultureInfo.CurrentCulture) : "";

            var rec = result.Reconciliation;
            ReconciliationBanner = $"Billed YTD: {rec.BilledYtdCad:C0} | Posted to GL: {rec.PostedYtdCad:C0} | Δ {rec.DeltaCad:C0} (GL post lags; Daler's report uses Billed)";
            ReconciliationBrush = rec.PostedYtdCad > rec.BilledYtdCad ? FrozenBrush("#FFB91C1C") : FrozenBrush("#FF065F46");
        }

        string IAiContextProvider.ProviderName => "Financials (Billed P&L)";

        bool IAiContextProvider.HasData => _lastResult != null;

        string IAiContextProvider.BuildContext()
        {
            var result = _lastResult;
            if (result == null)
                return "";

            var sb = new StringBuilder();
            sb.AppendLine("Billed P&L source: LedgerAR invoices for revenue; LedgerAP/LedgerEX/LedgerMisc for expenses.");
            sb.AppendLine(string.IsNullOrWhiteSpace(OrgFilter)
                ? $"Org filter: all orgs, USA converted to CAD at {_financialsOptions.BilledUsdToCadRate.NullIfWhiteSpace() ?? "1.36"}."
                : $"Org filter: {OrgFilter}.");
            sb.AppendLine($"Range: {FromDate:yyyy-MM-dd} to {ToDate:yyyy-MM-dd}.");
            sb.AppendLine($"Revenue period: {SummaryRevenue}; Expenses period: {SummaryExpenses}; Net period: {SummaryNet}; Margin: {SummaryMargin}.");
            sb.AppendLine($"Reconciliation: Billed YTD {result.Reconciliation.BilledYtdCad:C0}; Posted GL YTD {result.Reconciliation.PostedYtdCad:C0}; Delta {result.Reconciliation.DeltaCad:C0}.");
            if (result.MaxBilledPeriod.HasValue)
                sb.AppendLine($"Max billed period: {result.MaxBilledPeriod.Value}.");
            if (result.Reconciliation.MaxPostedPeriod.HasValue)
                sb.AppendLine($"Max posted GL period: {result.Reconciliation.MaxPostedPeriod.Value}.");
            return sb.ToString();
        }

        string IAiContextProvider.BuildLocalContext() => ((IAiContextProvider)this).BuildContext();

        private static decimal GetLineValue(BilledFinancialsResult result, string lineItem, int period)
        {
            var line = result.Lines.FirstOrDefault(l => string.Equals(l.LineItem, lineItem, StringComparison.OrdinalIgnoreCase));
            if (line == null || period <= 0)
                return 0m;
            return line.AmountsByPeriod.TryGetValue(period, out var value) ? value : 0m;
        }

        private static Brush FrozenBrush(string hex)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            return brush;
        }
    }

    internal static class StringExtensionsForFinancials
    {
        public static string? NullIfWhiteSpace(this string value)
            => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
