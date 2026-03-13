#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ClosedXML.Excel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;

namespace Kor.Operations.Financials
{
    public partial class GlProfitLossView : UserControl
    {
        private readonly GlProfitLossService _glProfitLossService;
        private readonly GlProfitLossViewModel _vm = new();
        private CancellationTokenSource? _cts;

        private GlProfitLossService.BuildResult? _lastResult;
        private decimal[] _lastNetTrend = Array.Empty<decimal>();
        private decimal[] _lastRevenueTrend = Array.Empty<decimal>();
        private decimal[] _lastExpenseTrend = Array.Empty<decimal>();
        private string[] _lastTrendLabels = Array.Empty<string>();

        public GlProfitLossView()
            : this(((App)Application.Current).Services.GetRequiredService<GlProfitLossService>())
        {
        }

        public GlProfitLossView(GlProfitLossService glProfitLossService)
        {
            _glProfitLossService = glProfitLossService ?? throw new ArgumentNullException(nameof(glProfitLossService));
            InitializeComponent();
            DataContext = _vm;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // Default: last 12 full months through the end of the prior month.
            var today = DateTime.Today;
            var endOfPrevMonth = new DateTime(today.Year, today.Month, 1).AddDays(-1);
            var startOfRange = new DateTime(endOfPrevMonth.Year, endOfPrevMonth.Month, 1).AddMonths(-11);

            _vm.FromDate = startOfRange;
            _vm.ToDate = endOfPrevMonth;

            _vm.FlipSign = _vm.ReadFlipSignDefault();

            await LoadTablesAsync().ConfigureAwait(true);
            await RefreshAsync(forceRefresh: false).ConfigureAwait(true);
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshAsync(forceRefresh: true).ConfigureAwait(true);
        }

        private void MetricDictionaryBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new FinancialMetricDictionaryWindow { Owner = Window.GetWindow(this) };
                win.ShowDialog();
            }
            catch
            {
                // Non-critical: ignore if window cannot be created for any reason.
            }
        }

        private async Task LoadTablesAsync()
        {
            try
            {
                var tables = await _glProfitLossService.GetTablesAsync(CancellationToken.None).ConfigureAwait(true);

                _vm.Tables.Clear();
                foreach (var t in tables)
                    _vm.Tables.Add(t);

                _vm.SelectedTable ??= PickBestDefaultTable(_vm.Tables);
            }
            catch (Exception ex)
            {
                _vm.SetError($"Unable to load GL table definitions.\n{ex.Message}");
            }
        }

        private static GlTableInfo? PickBestDefaultTable(ObservableCollection<GlTableInfo> tables)
        {
            if (tables.Count == 0)
                return null;

            static int Score(string? name)
            {
                if (string.IsNullOrWhiteSpace(name))
                    return 0;

                var n = name.Trim().ToLowerInvariant();
                var score = 0;

                // Strong signals.
                if (n.Contains("income statement")) score += 200;
                if (n.Contains("profit") && n.Contains("loss")) score += 200;
                if (n.Contains("p&l") || n.Contains("pnl")) score += 180;

                // Medium signals.
                if (n.Contains("income")) score += 60;
                if (n.Contains("statement")) score += 20;
                if (n.Contains("operating")) score += 20;
                if (n.Contains("grouped") && n.Contains("expense")) score += 50;

                // Negative signals for common non-P&L tables.
                if (n.Contains("balance sheet")) score -= 200;
                if (n.Contains("cash flow") || n.Contains("cashflow")) score -= 200;
                if (n.Contains("ar ") || n.Contains(" a/r") || n.Contains("accounts receivable")) score -= 60;
                if (n.Contains("ap ") || n.Contains(" a/p") || n.Contains("accounts payable")) score -= 60;

                return score;
            }

            var best = tables
                .Select(t => new { Table = t, Score = Score(t.TableName) })
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            // If nothing looks like a P&L, keep the first to avoid blocking the user.
            if (best == null || best.Score <= 0)
                return tables[0];

            return best.Table;
        }

        private async Task RefreshAsync(bool forceRefresh)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _vm.ClearMessages();
            _vm.CanRefresh = false;
            _vm.CanExport = false;
            _lastResult = null;

            try
            {
                var from = (_vm.FromDate ?? DateTime.Today).Date;
                var to = (_vm.ToDate ?? DateTime.Today).Date;
                if (to < from)
                {
                    _vm.SetError("Invalid date range: 'To' must be on or after 'From'.");
                    return;
                }

                if (_vm.SelectedTable == null)
                {
                    _vm.SetError("No GL table selected.");
                    return;
                }

                var result = await _glProfitLossService.BuildProfitLossAsync(
                    tableNo: _vm.SelectedTable.TableNo,
                    fromDate: from,
                    toDate: to,
                    orgFilter: _vm.OrgFilter,
                    flipSign: _vm.FlipSign,
                    forceRefresh: forceRefresh,
                    cancelToken: _cts.Token).ConfigureAwait(true);

                _vm.ApplySummary(result);
                BindGrid(result);
                _lastResult = result;
                _vm.CanExport = true;
            }
            catch (OperationCanceledException)
            {
                // ignore
            }
            catch (Exception ex)
            {
                _vm.SetError($"Unable to load GL P&L.\n{ex.Message}");
            }
            finally
            {
                _vm.CanRefresh = true;
            }
        }

        private void BindGrid(GlProfitLossService.BuildResult result)
        {
            var table = result.Table;
            var periodColumns = result.PeriodColumnNames;

            PnLGrid.Columns.Clear();
            PnLGrid.ItemsSource = null;

            // Optional filtering: hide rows that are zero across all visible numeric columns.
            table.DefaultView.RowFilter = _vm.HideZeroRows ? "IsAllZero = 0" : "";

            var cvs = new CollectionViewSource { Source = table.DefaultView };
            cvs.GroupDescriptions.Clear();
            cvs.GroupDescriptions.Add(new PropertyGroupDescription("Section"));
            cvs.SortDescriptions.Clear();
            cvs.SortDescriptions.Add(new SortDescription("SectionSort", ListSortDirection.Ascending));
            cvs.SortDescriptions.Add(new SortDescription("LineSort", ListSortDirection.Ascending));
            PnLGrid.ItemsSource = cvs.View;

            PnLGrid.Columns.Add(MakeLineItemColumn());

            foreach (var p in periodColumns)
            {
                PnLGrid.Columns.Add(MakeNumericTemplateColumn(
                    header: CreateHeaderWithInfo(p, "GlPnL_PeriodColumn"),
                    bindingPath: $"[{p}]",
                    format: "{0:C0}",
                    width: 120));
            }

            // Standard executive columns.
            PnLGrid.Columns.Add(MakeNumericTemplateColumn(CreateHeaderWithInfo("Current", "GlPnL_Current"), "[Current]", "{0:C0}", 120));
            PnLGrid.Columns.Add(MakeNumericTemplateColumn(CreateHeaderWithInfo("Prior", "GlPnL_Prior"), "[Prior]", "{0:C0}", 120));
            PnLGrid.Columns.Add(MakeNumericTemplateColumn(CreateHeaderWithInfo("MoM", "GlPnL_MoM"), "[MoM]", "{0:C0}", 120));
            PnLGrid.Columns.Add(MakeNumericTemplateColumn(CreateHeaderWithInfo("MoM %", "GlPnL_MoMPct"), "[MoMPct]", "{0:P1}", 90));
            PnLGrid.Columns.Add(MakeNumericTemplateColumn(CreateHeaderWithInfo("YTD", "GlPnL_YTD"), "[YTD]", "{0:C0}", 120));
            PnLGrid.Columns.Add(MakeNumericTemplateColumn(CreateHeaderWithInfo("TTM", "GlPnL_TTM"), "[TTM]", "{0:C0}", 120));
            PnLGrid.Columns.Add(MakeNumericTemplateColumn(CreateHeaderWithInfo("% Rev", "GlPnL_PctOfRevenue"), "[PctOfRevenue]", "{0:P1}", 90));

            // Update the trend polyline points explicitly (binding to PointCollection is fragile under some themes).
            _lastNetTrend = result.NetIncomeTrendValues ?? Array.Empty<decimal>();
            _lastRevenueTrend = result.RevenueTrendValues ?? Array.Empty<decimal>();
            _lastExpenseTrend = result.ExpenseTrendValues ?? Array.Empty<decimal>();
            _lastTrendLabels = result.TrendLabels ?? Array.Empty<string>();
            RenderCharts();
        }

        private void RenderCharts()
        {
            RenderNetTrend(_lastNetTrend);
            RenderRevExpTrend(_lastRevenueTrend, _lastExpenseTrend);
            RenderLabelGrid(NetTrendLabelGrid, _lastTrendLabels);
            RenderLabelGrid(RevExpLabelGrid, _lastTrendLabels);
        }

        private void NetTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderCharts();
        private void RevExpCanvas_SizeChanged(object sender, SizeChangedEventArgs e) => RenderCharts();

        private void RenderNetTrend(decimal[] values)
        {
            NetTrendLine.Points = BuildSparklinePoints(NetTrendCanvas, values);
            RenderBarsBehindLine(NetTrendCanvas, NetTrendLine, values, pos: "#93C5FD", neg: "#FCA5A5");
        }

        private void RenderRevExpTrend(decimal[] revenue, decimal[] expense)
        {
            // Stacked bars: Revenue (top) and Expenses (bottom) as magnitudes for exec scanning.
            if (revenue == null || expense == null)
                return;

            var n = Math.Min(revenue.Length, expense.Length);
            if (n <= 0)
                return;

            // Clear (keep nothing; this canvas has only bars).
            RevExpCanvas.Children.Clear();

            var w = RevExpCanvas.ActualWidth;
            if (w <= 10) w = 320;
            var h = RevExpCanvas.Height;
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
            var padTop = 2.0;
            var innerH = Math.Max(1.0, h - (padTop * 2));

            var revBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#22C55E"));   // green
            var expBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F97316"));   // orange
            revBrush.Freeze();
            expBrush.Freeze();

            for (var i = 0; i < n; i++)
            {
                var stack = revAbs[i] + expAbs[i];
                var x = i * (barW + gap);

                var expH = (expAbs[i] / max) * innerH;
                var revH = (revAbs[i] / max) * innerH;

                // Expenses at bottom.
                var expRect = new Rectangle
                {
                    Width = barW,
                    Height = Math.Max(1.0, expH),
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = expBrush,
                    Opacity = 0.70
                };
                Canvas.SetLeft(expRect, x);
                Canvas.SetTop(expRect, padTop + (innerH - expH));
                RevExpCanvas.Children.Add(expRect);

                // Revenue stacked above.
                var revRect = new Rectangle
                {
                    Width = barW,
                    Height = Math.Max(1.0, revH),
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = revBrush,
                    Opacity = 0.75
                };
                Canvas.SetLeft(revRect, x);
                Canvas.SetTop(revRect, padTop + (innerH - expH - revH));
                RevExpCanvas.Children.Add(revRect);
            }
        }

        private static void RenderLabelGrid(UniformGrid grid, string[] labels)
        {
            if (grid == null)
                return;

            grid.Children.Clear();
            if (labels == null || labels.Length == 0)
                return;

            grid.Columns = labels.Length;
            foreach (var l in labels)
            {
                grid.Children.Add(new TextBlock
                {
                    Text = l ?? "",
                    FontSize = 10,
                    Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6B7280")),
                    TextAlignment = TextAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 0)
                });
            }
        }

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            var result = _lastResult;
            if (result == null || !_vm.CanExport)
                return;

            var tableNo = _vm.SelectedTable?.TableNo ?? (short)0;
            var from = (_vm.FromDate ?? DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var to = (_vm.ToDate ?? DateTime.Today).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var fromDisplay = (_vm.FromDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var toDisplay = (_vm.ToDate ?? DateTime.Today).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var tableDisplay = _vm.SelectedTable?.Display ?? "";
            var org = _vm.OrgFilter ?? "";
            var hideZeros = _vm.HideZeroRows;

            // Snapshot the export data on the UI thread to avoid cross-thread access to DataTable/DataView.
            var export = BuildExportSnapshot(result, hideZeros);

            var sfd = new SaveFileDialog
            {
                Title = "Export GL P&L",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"PnL_GL_{tableNo}_{from}_{to}.xlsx",
                AddExtension = true,
                OverwritePrompt = true
            };

            var owner = Window.GetWindow(this);
            if (sfd.ShowDialog(owner) != true)
                return;

            _vm.SetExporting(true);
            try
            {
                var path = sfd.FileName;
                await Task.Run(() => ExportToExcel(path, export, fromDisplay, toDisplay, tableDisplay, org)).ConfigureAwait(true);
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
                _vm.SetExporting(false);
            }
        }

        private void PnLGrid_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PnLGrid.SelectedItem is not DataRowView rowView)
                return;

            if (!IsDataGridRowDoubleClick(e))
                return;

            var row = rowView.Row;
            var rowKind = Convert.ToString(row["RowKind"]) ?? string.Empty;
            var lineItem = Convert.ToString(row["LineItem"]) ?? string.Empty;
            var section = Convert.ToString(row["Section"]) ?? string.Empty;

            if (string.Equals(rowKind, "SectionTotal", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(rowKind, "GrandTotal", StringComparison.OrdinalIgnoreCase))
            {
                var contributors = BuildContributorRows(rowKind, lineItem, section);
                if (contributors.Count == 0)
                    return;

                var title = $"{lineItem} Breakdown";
                var subtitle = $"{contributors.Count:N0} contributing line items";
                ShowDrilldownWindow(title, subtitle, contributors);
                return;
            }

            if (string.Equals(rowKind, "Detail", StringComparison.OrdinalIgnoreCase))
            {
                var periods = BuildPeriodRows(row);
                if (periods.Count == 0)
                    return;

                var current = ReadDecimal(row, "Current");
                var ytd = ReadDecimal(row, "YTD");
                var title = $"{lineItem} Period Breakdown";
                var subtitle = $"Current {current:C0} | YTD {ytd:C0} | Double-click a month to drill into supporting entries";
                ShowDrilldownWindow(
                    title,
                    subtitle,
                    periods,
                    onRowDoubleClick: periodRow => _ = OpenPeriodDetailDrilldownAsync(periodRow));
            }
        }

        private static bool IsDataGridRowDoubleClick(System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d)
                return false;

            DependencyObject? cur = d;
            while (cur != null && cur is not DataGridRow)
                cur = VisualTreeHelper.GetParent(cur);
            return cur is DataGridRow;
        }

        private System.Collections.Generic.List<PnLContributorRow> BuildContributorRows(string rowKind, string lineItem, string section)
        {
            var result = _lastResult;
            if (result == null)
                return new System.Collections.Generic.List<PnLContributorRow>();

            var detailRows = result.Table.Rows.Cast<DataRow>()
                .Where(r => string.Equals(Convert.ToString(r["RowKind"]), "Detail", StringComparison.OrdinalIgnoreCase))
                .ToList();

            bool IsExpenseSection(string s) => s.IndexOf("expense", StringComparison.OrdinalIgnoreCase) >= 0;
            bool IsIncomeSection(string s)
                => s.IndexOf("revenue", StringComparison.OrdinalIgnoreCase) >= 0
                   || (s.IndexOf("income", StringComparison.OrdinalIgnoreCase) >= 0 && s.IndexOf("expense", StringComparison.OrdinalIgnoreCase) < 0);

            System.Collections.Generic.IEnumerable<DataRow> src = detailRows;
            if (string.Equals(rowKind, "SectionTotal", StringComparison.OrdinalIgnoreCase))
            {
                src = detailRows.Where(r => string.Equals(Convert.ToString(r["Section"]) ?? string.Empty, section, StringComparison.OrdinalIgnoreCase));
            }
            else if (string.Equals(rowKind, "GrandTotal", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(lineItem, "Total Revenue", StringComparison.OrdinalIgnoreCase))
                    src = detailRows.Where(r => IsIncomeSection(Convert.ToString(r["Section"]) ?? string.Empty));
                else if (string.Equals(lineItem, "Total Expenses", StringComparison.OrdinalIgnoreCase))
                    src = detailRows.Where(r => IsExpenseSection(Convert.ToString(r["Section"]) ?? string.Empty));
                else
                    src = detailRows;
            }

            return src
                .Select(r => new PnLContributorRow(
                    lineItem: Convert.ToString(r["LineItem"]) ?? string.Empty,
                    current: ReadDecimal(r, "Current"),
                    prior: ReadDecimal(r, "Prior"),
                    ytd: ReadDecimal(r, "YTD"),
                    ttm: ReadDecimal(r, "TTM"),
                    pctRev: ReadDecimal(r, "PctOfRevenue")))
                .OrderByDescending(r => Math.Abs(r.CurrentRaw))
                .ToList();
        }

        private System.Collections.Generic.List<PnLPeriodAmountRow> BuildPeriodRows(DataRow row)
        {
            var result = _lastResult;
            if (result == null || result.PeriodColumnNames == null || result.PeriodColumnNames.Length == 0)
                return new System.Collections.Generic.List<PnLPeriodAmountRow>();

            var rows = new System.Collections.Generic.List<PnLPeriodAmountRow>(result.PeriodColumnNames.Length);
            var lineItem = Convert.ToString(row["LineItem"]) ?? string.Empty;
            var lineGroupCode = row.Table.Columns.Contains("LineGroupCode") && row["LineGroupCode"] is short s ? (short?)s : null;
            for (var i = 0; i < result.PeriodColumnNames.Length; i++)
            {
                var periodCol = result.PeriodColumnNames[i];
                var period = i < result.Periods.Length ? result.Periods[i] : 0;
                var amount = ReadDecimal(row, periodCol);
                rows.Add(new PnLPeriodAmountRow(lineItem, periodCol, period, lineGroupCode, amount));
            }
            return rows;
        }

        private static decimal ReadDecimal(DataRow row, string col)
            => row.Table.Columns.Contains(col) && row[col] is decimal d ? d : 0m;

        private void ShowDrilldownWindow<T>(string title, string subtitle, System.Collections.Generic.IReadOnlyList<T> rows, Action<T>? onRowDoubleClick = null)
        {
            var owner = Window.GetWindow(this);
            var grid = new DataGrid
            {
                AutoGenerateColumns = true,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                Margin = new Thickness(0, 8, 0, 0),
                ItemsSource = rows
            };
            if (onRowDoubleClick != null)
            {
                grid.MouseDoubleClick += (_, e) =>
                {
                    if (!IsDataGridRowDoubleClick(e))
                        return;
                    if (grid.SelectedItem is T item)
                        onRowDoubleClick(item);
                };
            }

            var panel = new Grid { Margin = new Thickness(12) };
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold
            });

            var subtitleBlock = new TextBlock
            {
                Text = subtitle,
                Margin = new Thickness(0, 4, 0, 0),
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#64748B"))
            };
            Grid.SetRow(subtitleBlock, 1);
            panel.Children.Add(subtitleBlock);

            Grid.SetRow(grid, 2);
            panel.Children.Add(grid);

            var closeBtn = new Button
            {
                Content = "Close",
                MinWidth = 90,
                Margin = new Thickness(0, 10, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right,
                IsDefault = true
            };
            Grid.SetRow(closeBtn, 3);
            panel.Children.Add(closeBtn);

            var win = new Window
            {
                Title = "Line Item Details",
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Width = 900,
                Height = 620,
                MinWidth = 760,
                MinHeight = 460,
                Content = panel,
                Background = Application.Current?.TryFindResource("App.Background") as Brush ?? Brushes.White
            };
            closeBtn.Click += (_, __) => win.Close();
            win.ShowDialog();
        }

        private async Task OpenPeriodDetailDrilldownAsync(PnLPeriodAmountRow row)
        {
            if (row == null || row.LineGroupCode == null || row.Period <= 0 || _vm.SelectedTable == null)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var detail = await _glProfitLossService.LoadLineItemTransactionsAsync(
                    tableNo: _vm.SelectedTable.TableNo,
                    glGroup: row.LineGroupCode.Value,
                    period: row.Period,
                    orgFilter: _vm.OrgFilter,
                    flipSign: _vm.FlipSign,
                    cancelToken: CancellationToken.None).ConfigureAwait(true);

                if (detail == null || detail.Count == 0)
                {
                    MessageBox.Show(
                        Window.GetWindow(this) ?? Application.Current?.MainWindow,
                        "No supporting transaction rows were found for this line item and period.",
                        "P&L Drilldown",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var total = detail.Sum(d => d.Amount);
                var title = $"{row.LineItem} - {row.PeriodLabel}";
                var subtitle = $"{detail.Count:N0} grouped entries | Total {total:C0}";
                ShowDrilldownWindow(title, subtitle, detail);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this) ?? Application.Current?.MainWindow,
                    $"Unable to load supporting transaction rows.\n{ex.Message}",
                    "P&L Drilldown",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private sealed class ExportSnapshot
        {
            public string[] PeriodHeaders { get; init; } = Array.Empty<string>();
            public ExportRow[] Rows { get; init; } = Array.Empty<ExportRow>();
        }

        private sealed class PnLContributorRow
        {
            public string LineItem { get; }
            public string Current { get; }
            public string Prior { get; }
            public string YTD { get; }
            public string TTM { get; }
            public string PercentOfRevenue { get; }

            [System.ComponentModel.Browsable(false)]
            public decimal CurrentRaw { get; }

            public PnLContributorRow(string lineItem, decimal current, decimal prior, decimal ytd, decimal ttm, decimal pctRev)
            {
                LineItem = lineItem ?? string.Empty;
                CurrentRaw = current;
                Current = current.ToString("C0", CultureInfo.CurrentCulture);
                Prior = prior.ToString("C0", CultureInfo.CurrentCulture);
                YTD = ytd.ToString("C0", CultureInfo.CurrentCulture);
                TTM = ttm.ToString("C0", CultureInfo.CurrentCulture);
                PercentOfRevenue = pctRev.ToString("P1", CultureInfo.CurrentCulture);
            }
        }

        private sealed class PnLPeriodAmountRow
        {
            [System.ComponentModel.Browsable(false)]
            public string LineItem { get; }
            public string PeriodLabel { get; }
            public string Amount { get; }
            [System.ComponentModel.Browsable(false)]
            public int Period { get; }
            [System.ComponentModel.Browsable(false)]
            public short? LineGroupCode { get; }

            public PnLPeriodAmountRow(string lineItem, string periodLabel, int period, short? lineGroupCode, decimal amount)
            {
                LineItem = lineItem ?? string.Empty;
                PeriodLabel = periodLabel ?? string.Empty;
                Period = period;
                LineGroupCode = lineGroupCode;
                Amount = amount.ToString("C0", CultureInfo.CurrentCulture);
            }
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

        private static ExportSnapshot BuildExportSnapshot(GlProfitLossService.BuildResult result, bool hideZeros)
        {
            var periodHeaders = result.PeriodColumnNames ?? Array.Empty<string>();
            var rows = new System.Collections.Generic.List<ExportRow>(capacity: Math.Max(32, result.Table.Rows.Count));

            foreach (DataRow r in result.Table.Rows)
            {
                if (hideZeros && r.Table.Columns.Contains("IsAllZero") && r["IsAllZero"] is bool b && b)
                    continue;

                var periodValues = new decimal[periodHeaders.Length];
                for (var i = 0; i < periodHeaders.Length; i++)
                    periodValues[i] = r[periodHeaders[i]] is decimal d ? d : 0m;

                rows.Add(new ExportRow
                {
                    Section = Convert.ToString(r["Section"]) ?? "",
                    LineItem = Convert.ToString(r["LineItem"]) ?? "",
                    RowKind = Convert.ToString(r["RowKind"]) ?? "",
                    PeriodValues = periodValues,
                    Current = r["Current"] is decimal cur ? cur : 0m,
                    Prior = r["Prior"] is decimal prior ? prior : 0m,
                    MoM = r["MoM"] is decimal mom ? mom : 0m,
                    MoMPct = r["MoMPct"] is decimal mp ? mp : 0m,
                    YTD = r["YTD"] is decimal ytd ? ytd : 0m,
                    TTM = r["TTM"] is decimal ttm ? ttm : 0m,
                    PctOfRevenue = r["PctOfRevenue"] is decimal pr ? pr : 0m,
                });
            }

            return new ExportSnapshot
            {
                PeriodHeaders = periodHeaders,
                Rows = rows.ToArray()
            };
        }

        private static void ExportToExcel(string path, ExportSnapshot snap, string fromDisplay, string toDisplay, string tableDisplay, string org)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("P&L (GL)");

            var row = 1;
            ws.Cell(row, 1).Value = "Profit & Loss (GL)";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontSize = 16;
            row++;

            // Metadata row
            ws.Cell(row, 1).Value = "From";
            ws.Cell(row, 2).Value = fromDisplay;
            ws.Cell(row, 3).Value = "To";
            ws.Cell(row, 4).Value = toDisplay;
            ws.Cell(row, 5).Value = "Table";
            ws.Cell(row, 6).Value = tableDisplay;
            ws.Cell(row, 7).Value = "Org";
            ws.Cell(row, 8).Value = org;
            row += 2;

            // Columns: Section, LineItem, period columns, executive columns.
            var headers = new System.Collections.Generic.List<string> { "Section", "Line Item" };
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
                {
                    ws.Cell(row, colIndex).Value = r.PeriodValues[i];
                    colIndex++;
                }

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
                // Number formats.
                var firstMoneyCol = 3;
                var lastMoneyCol = 2 + snap.PeriodHeaders.Length + 3; // through MoM (currency)
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
                    .Select(i => new Point((values.Length == 1 ? w : (w * i / (values.Length - 1))), y)));
            }

            var pad = 2.0;
            var innerH = Math.Max(1.0, h - (pad * 2));
            var pts = new PointCollection(values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                var x = (values.Length == 1) ? w : (w * i / (values.Length - 1));
                var t = (double)((values[i] - min) / (max - min)); // 0..1
                var y = pad + (1.0 - t) * innerH;
                pts.Add(new Point(x, y));
            }

            return pts;
        }

        private static void RenderBarsBehindLine(Canvas canvas, Polyline line, decimal[] values, string pos, string neg)
        {
            if (values == null || values.Length == 0)
                return;

            // Remove previously added bars (keep the polyline).
            for (var i = canvas.Children.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(canvas.Children[i], line))
                    canvas.Children.RemoveAt(i);
            }

            var w = canvas.ActualWidth;
            if (w <= 10) w = 240;
            var h = canvas.Height;
            if (h <= 10) h = 44;

            var min = values.Min();
            var max = values.Max();
            var maxAbs = (double)Math.Max(Math.Abs(min), Math.Abs(max));
            if (maxAbs <= 0.000001)
                return;

            var barCount = values.Length;
            var gap = 2.0;
            var barW = Math.Max(1.0, (w - (gap * (barCount - 1))) / barCount);
            var midY = h / 2.0;

            var posBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(pos)) { Opacity = 0.55 };
            var negBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(neg)) { Opacity = 0.55 };
            posBrush.Freeze();
            negBrush.Freeze();

            for (var i = 0; i < barCount; i++)
            {
                var v = (double)values[i];
                var mag = Math.Min(1.0, Math.Abs(v) / maxAbs);
                var bh = mag * (h * 0.45);
                var x = i * (barW + gap);
                var y = v >= 0 ? (midY - bh) : midY;

                var rect = new Rectangle
                {
                    Width = barW,
                    Height = Math.Max(1.0, bh),
                    RadiusX = 1,
                    RadiusY = 1,
                    Fill = v >= 0 ? posBrush : negBrush
                };

                Canvas.SetLeft(rect, x);
                Canvas.SetTop(rect, y);
                Panel.SetZIndex(rect, 0);
                canvas.Children.Add(rect);
            }
        }

        private static readonly SignToBrushConverter _signToBrush = new();
        private static readonly LineIndentConverter _lineIndent = new();

        private DataGridTemplateColumn MakeLineItemColumn()
        {
            var root = new FrameworkElementFactory(typeof(StackPanel));
            root.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
            root.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);

            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            text.SetBinding(TextBlock.TextProperty, new Binding("[LineItem]"));
            text.SetBinding(TextBlock.MarginProperty, new Binding("[RowKind]") { Converter = _lineIndent });
            root.AppendChild(text);

            var template = new DataTemplate { VisualTree = root };

            return new DataGridTemplateColumn
            {
                Header = CreateHeaderWithInfo("Line Item", "GlPnL_LineItem"),
                CellTemplate = template,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            };
        }

        private object CreateHeaderWithInfo(string text, string tooltipKey)
        {
            var tip = FinancialMetricDefinitions.TryGetTooltipText(tooltipKey);
            if (string.IsNullOrWhiteSpace(tip))
                return text;

            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = text });

            var b = new Border { Margin = new Thickness(6, 0, 0, 0) };
            if (TryFindResource("InfoIconBorder") is Style bs) b.Style = bs;
            b.ToolTip = tip;
            var t = new TextBlock();
            if (TryFindResource("InfoIconText") is Style ts) t.Style = ts;
            b.Child = t;
            sp.Children.Add(b);

            sp.ToolTip = tip;
            return sp;
        }

        private static DataGridTemplateColumn MakeNumericTemplateColumn(object header, string bindingPath, string format, double width)
        {
            var text = new FrameworkElementFactory(typeof(TextBlock));
            text.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
            text.SetValue(TextBlock.MarginProperty, new Thickness(0, 0, 6, 0));

            var bText = new Binding(bindingPath) { StringFormat = format };
            text.SetBinding(TextBlock.TextProperty, bText);

            var bBrush = new Binding(bindingPath) { Converter = _signToBrush };
            text.SetBinding(TextBlock.ForegroundProperty, bBrush);

            var template = new DataTemplate { VisualTree = text };
            return new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = template,
                Width = width
            };
        }

        private sealed class SignToBrushConverter : IValueConverter
        {
            private static readonly Brush Pos = FrozenBrush("#166534"); // green-800
            private static readonly Brush Neg = FrozenBrush("#B91C1C"); // red-700
            private static readonly Brush Zero = FrozenBrush("#6B7280"); // gray-500

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value == null || value == DBNull.Value)
                    return Zero;

                try
                {
                    var d = System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
                    if (d > 0m) return Pos;
                    if (d < 0m) return Neg;
                    return Zero;
                }
                catch
                {
                    return Zero;
                }
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => Binding.DoNothing;

            private static Brush FrozenBrush(string hex)
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            }
        }

        private sealed class LineIndentConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                var kind = value?.ToString() ?? "";
                // Detail rows indent; totals sit flush-left.
                var left = string.Equals(kind, "Detail", StringComparison.OrdinalIgnoreCase) ? 14.0 : 0.0;
                return new Thickness(left, 0, 8, 0);
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
                => Binding.DoNothing;
        }
    }

}
