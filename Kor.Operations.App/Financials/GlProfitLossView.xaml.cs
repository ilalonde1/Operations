#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Kor.Operations.App.Options;
using Kor.Operations.Services;
using Serilog;

namespace Kor.Operations.Financials
{
    public partial class GlProfitLossView : UserControl
    {
        private readonly BilledFinancialsPresenter _billedPresenter;
        private readonly GlProfitLossPresenter _presenter;
        private readonly GlProfitLossPresenter _sideBySidePostedPresenter;
        private bool _initialized;
        private bool _billedInitialized;
        private bool _postedInitialized;
        private bool _sideBySideInitialized;
        private PnlViewMode _currentMode = PnlViewMode.Billed;

        public GlProfitLossView()
            : this(
                Kor.Operations.Services.AppServices.Get<GlProfitLossService>(),
                Kor.Operations.Services.AppServices.Get<BilledFinancialsService>(),
                Kor.Operations.Services.AppServices.Get<FinancialsOptions>())
        {
        }

        public GlProfitLossView(
            GlProfitLossService glProfitLossService,
            BilledFinancialsService billedFinancialsService,
            FinancialsOptions financialsOptions)
        {
            InitializeComponent();
            var options = financialsOptions ?? throw new ArgumentNullException(nameof(financialsOptions));

            _billedPresenter = new BilledFinancialsPresenter(
                billedFinancialsService ?? throw new ArgumentNullException(nameof(billedFinancialsService)),
                options,
                PnLGrid,
                NetTrendCanvas,
                NetTrendLine,
                RevExpCanvas,
                NetTrendLabelGrid,
                RevExpLabelGrid);

            _presenter = new GlProfitLossPresenter(
                this,
                glProfitLossService ?? throw new ArgumentNullException(nameof(glProfitLossService)),
                options,
                PnLGrid,
                NetTrendCanvas,
                NetTrendLine,
                RevExpCanvas,
                NetTrendLabelGrid,
                RevExpLabelGrid);

            _sideBySidePostedPresenter = new GlProfitLossPresenter(
                this,
                glProfitLossService,
                options,
                PostedPnLGrid,
                NetTrendCanvas,
                NetTrendLine,
                RevExpCanvas,
                NetTrendLabelGrid,
                RevExpLabelGrid);

            DataContext = _billedPresenter.ViewModel;

            AppServices.GetOptional<AppAiContextBuilder>()?.Register(_billedPresenter.ViewModel);
            AppServices.GetOptional<AppAiContextBuilder>()?.Register(_presenter.ViewModel);
            Unloaded += (_, _) =>
            {
                var builder = AppServices.GetOptional<AppAiContextBuilder>();
                builder?.Unregister(_billedPresenter.ViewModel);
                builder?.Unregister(_presenter.ViewModel);
            };
        }

        private bool IsPostedMode => PostedViewRadio.IsChecked == true;

        private bool IsSideBySideMode => SideBySideViewRadio.IsChecked == true;

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            await _billedPresenter.InitializeAsync().ConfigureAwait(true);
            _billedInitialized = true;
            _initialized = true;
            BilledViewRadio.IsChecked = true;
            _currentMode = PnlViewMode.Billed;
            ApplyViewMode();
        }

        private async void ViewMode_Checked(object sender, RoutedEventArgs e)
        {
            if (!_initialized)
                return;

            var nextMode = GetSelectedMode();
            await EnsureModeInitializedAsync(nextMode).ConfigureAwait(true);
            CopyFilters(_currentMode, nextMode);
            _currentMode = nextMode;
            ApplyViewMode();
            await RefreshActiveAsync(forceRefresh: false).ConfigureAwait(true);
        }

        private void ApplyViewMode()
        {
            if (IsPostedMode)
            {
                DataContext = _presenter.ViewModel;
                BilledExpander.Header = "Posted GL P&L (GLSummary)";
                BilledExpander.IsExpanded = true;
                BilledExpander.Visibility = Visibility.Visible;
                PostedExpander.Visibility = Visibility.Collapsed;
                // TableFilterPanel stays hidden — the presenter auto-picks the best Income
                // Statement table; users shouldn't see GLTable internals in the toolbar.
                FlipSignCheckBox.Visibility = Visibility.Visible;
                ReconciliationPanel.Visibility = Visibility.Collapsed;
                PostedLagPanel.Visibility = _presenter.ViewModel.PostingLagVisibility;
                PnLGrid.MouseDoubleClick -= BilledPnLGrid_MouseDoubleClick;
                PnLGrid.MouseDoubleClick -= PnLGrid_MouseDoubleClick;
                PnLGrid.MouseDoubleClick += PnLGrid_MouseDoubleClick;
                _presenter.RenderCharts();
                return;
            }

            DataContext = _billedPresenter.ViewModel;
            BilledExpander.Header = "Billed P&L (LedgerAR)";
            BilledExpander.Visibility = Visibility.Visible;
            BilledExpander.IsExpanded = !IsSideBySideMode;
            PostedExpander.Visibility = IsSideBySideMode ? Visibility.Visible : Visibility.Collapsed;
            PostedExpander.IsExpanded = false;
            FlipSignCheckBox.Visibility = Visibility.Collapsed;
            ReconciliationPanel.Visibility = _billedPresenter.ViewModel.ReconciliationVisibility;
            PostedLagPanel.Visibility = Visibility.Collapsed;
            PnLGrid.MouseDoubleClick -= PnLGrid_MouseDoubleClick;
            PnLGrid.MouseDoubleClick -= BilledPnLGrid_MouseDoubleClick;
            PnLGrid.MouseDoubleClick += BilledPnLGrid_MouseDoubleClick;
            _billedPresenter.RenderCharts();
        }

        private void SyncFiltersFromActive()
        {
            if (IsPostedMode)
            {
                _billedPresenter.ViewModel.FromDate = _presenter.ViewModel.FromDate;
                _billedPresenter.ViewModel.ToDate = _presenter.ViewModel.ToDate;
                _billedPresenter.ViewModel.OrgFilter = _presenter.ViewModel.OrgFilter;
                _billedPresenter.ViewModel.HideZeroRows = _presenter.ViewModel.HideZeroRows;
                _sideBySidePostedPresenter.ViewModel.FromDate = _presenter.ViewModel.FromDate;
                _sideBySidePostedPresenter.ViewModel.ToDate = _presenter.ViewModel.ToDate;
                _sideBySidePostedPresenter.ViewModel.OrgFilter = _presenter.ViewModel.OrgFilter;
                _sideBySidePostedPresenter.ViewModel.HideZeroRows = _presenter.ViewModel.HideZeroRows;
                return;
            }

            _presenter.ViewModel.FromDate = _billedPresenter.ViewModel.FromDate;
            _presenter.ViewModel.ToDate = _billedPresenter.ViewModel.ToDate;
            _presenter.ViewModel.OrgFilter = _billedPresenter.ViewModel.OrgFilter;
            _presenter.ViewModel.HideZeroRows = _billedPresenter.ViewModel.HideZeroRows;
            _sideBySidePostedPresenter.ViewModel.FromDate = _billedPresenter.ViewModel.FromDate;
            _sideBySidePostedPresenter.ViewModel.ToDate = _billedPresenter.ViewModel.ToDate;
            _sideBySidePostedPresenter.ViewModel.OrgFilter = _billedPresenter.ViewModel.OrgFilter;
            _sideBySidePostedPresenter.ViewModel.HideZeroRows = _billedPresenter.ViewModel.HideZeroRows;
        }

        private void CopyFilters(PnlViewMode fromMode, PnlViewMode toMode)
        {
            if (fromMode == toMode)
                return;

            if (fromMode == PnlViewMode.Posted && toMode != PnlViewMode.Posted)
            {
                _billedPresenter.ViewModel.FromDate = _presenter.ViewModel.FromDate;
                _billedPresenter.ViewModel.ToDate = _presenter.ViewModel.ToDate;
                _billedPresenter.ViewModel.OrgFilter = _presenter.ViewModel.OrgFilter;
                _billedPresenter.ViewModel.HideZeroRows = _presenter.ViewModel.HideZeroRows;
                return;
            }

            if (fromMode != PnlViewMode.Posted && toMode == PnlViewMode.Posted)
            {
                _presenter.ViewModel.FromDate = _billedPresenter.ViewModel.FromDate;
                _presenter.ViewModel.ToDate = _billedPresenter.ViewModel.ToDate;
                _presenter.ViewModel.OrgFilter = _billedPresenter.ViewModel.OrgFilter;
                _presenter.ViewModel.HideZeroRows = _billedPresenter.ViewModel.HideZeroRows;
            }
        }

        private PnlViewMode GetSelectedMode()
        {
            if (PostedViewRadio.IsChecked == true)
                return PnlViewMode.Posted;
            if (SideBySideViewRadio.IsChecked == true)
                return PnlViewMode.SideBySide;
            return PnlViewMode.Billed;
        }

        private async Task RefreshActiveAsync(bool forceRefresh)
        {
            await EnsureModeInitializedAsync(_currentMode).ConfigureAwait(true);
            SyncFiltersFromActive();
            if (IsPostedMode)
            {
                await _presenter.RefreshAsync(forceRefresh).ConfigureAwait(true);
            }
            else if (IsSideBySideMode)
            {
                await _billedPresenter.RefreshAsync(forceRefresh).ConfigureAwait(true);
                await _sideBySidePostedPresenter.RefreshAsync(forceRefresh).ConfigureAwait(true);
            }
            else
            {
                await _billedPresenter.RefreshAsync(forceRefresh).ConfigureAwait(true);
            }

            ApplyViewMode();
        }

        private async Task EnsureModeInitializedAsync(PnlViewMode mode)
        {
            if (!_billedInitialized)
            {
                await _billedPresenter.InitializeAsync().ConfigureAwait(true);
                _billedInitialized = true;
            }

            if (mode == PnlViewMode.Posted && !_postedInitialized)
            {
                await _presenter.InitializeAsync().ConfigureAwait(true);
                _postedInitialized = true;
            }

            if (mode == PnlViewMode.SideBySide && !_sideBySideInitialized)
            {
                await _sideBySidePostedPresenter.InitializeAsync().ConfigureAwait(true);
                _sideBySideInitialized = true;
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            await RefreshActiveAsync(forceRefresh: true).ConfigureAwait(true);
        }

        private void MetricDictionaryBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var win = new FinancialMetricDictionaryWindow { Owner = Window.GetWindow(this) };
                win.ShowDialog();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to open Financial metric dictionary.");
            }
        }

        private void NetTrendCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsPostedMode)
                _presenter.RenderCharts();
            else
                _billedPresenter.RenderCharts();
        }

        private void RevExpCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsPostedMode)
                _presenter.RenderCharts();
            else
                _billedPresenter.RenderCharts();
        }

        private async void ExportBtn_Click(object sender, RoutedEventArgs e)
        {
            if (IsPostedMode)
            {
                await _presenter.ExportAsync(Window.GetWindow(this)).ConfigureAwait(true);
                return;
            }

            if (IsSideBySideMode)
            {
                await _sideBySidePostedPresenter.ExportAsync(Window.GetWindow(this)).ConfigureAwait(true);
                return;
            }

            await _billedPresenter.ExportAsync(Window.GetWindow(this)).ConfigureAwait(true);
        }

        private enum PnlViewMode
        {
            Billed,
            Posted,
            SideBySide
        }

        private void BilledPnLGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PnLGrid.SelectedItem is not DataRowView rowView)
                return;
            if (!IsDataGridRowDoubleClick(e))
                return;

            var row = rowView.Row;
            var rowKind = Convert.ToString(row["RowKind"]) ?? string.Empty;
            if (!string.Equals(rowKind, "Detail", StringComparison.OrdinalIgnoreCase))
                return;

            var account = row.Table.Columns.Contains("Account") && row["Account"] is string a
                ? a
                : string.Empty;
            if (string.IsNullOrWhiteSpace(account))
                return;

            var lineItem = Convert.ToString(row["LineItem"]) ?? string.Empty;
            // Section ("Revenue" / "Expenses" / "Other Income") restricts the drilldown
            // to the same ledger sources the section's total uses, so Σ rows ties out.
            var section = row.Table.Columns.Contains("Section")
                ? Convert.ToString(row["Section"]) ?? string.Empty
                : string.Empty;
            var result = _billedPresenter.CurrentResult;
            if (result == null || result.PeriodColumnNames == null || result.PeriodColumnNames.Length == 0)
                return;

            var periods = new List<BilledPeriodRow>(result.PeriodColumnNames.Length);
            for (var i = 0; i < result.PeriodColumnNames.Length; i++)
            {
                var col = result.PeriodColumnNames[i];
                var period = i < result.Periods.Length ? result.Periods[i] : 0;
                var amount = ReadDecimal(row, col);
                periods.Add(new BilledPeriodRow(account, col, period, amount));
            }

            var rangeTotal = periods.Sum(p => p.AmountRaw);
            var title = $"{lineItem} - Period Breakdown";
            var subtitle = $"Range total {rangeTotal:C0} | Double-click a month to see invoices/transactions";
            ShowDrilldownWindow(
                title,
                subtitle,
                periods,
                onRowDoubleClick: pr => _ = ExecuteBilledDrilldownAsync(lineItem, section, pr));
        }

        private async Task ExecuteBilledDrilldownAsync(string lineItem, string section, BilledPeriodRow periodRow)
        {
            if (periodRow.PeriodInt <= 0 || string.IsNullOrWhiteSpace(periodRow.Account))
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var service = AppServices.Get<BilledFinancialsService>();
                var orgFilter = string.IsNullOrWhiteSpace(_billedPresenter.ViewModel.OrgFilter)
                    ? null
                    : _billedPresenter.ViewModel.OrgFilter.Trim();
                var detail = await service.LoadLedgerTransactionsAsync(
                    periodRow.Account,
                    periodRow.PeriodInt,
                    orgFilter,
                    section,
                    CancellationToken.None).ConfigureAwait(true);

                if (detail == null || detail.Count == 0)
                {
                    MessageBox.Show(
                        Window.GetWindow(this) ?? Application.Current?.MainWindow,
                        "No supporting transactions were found for this account and period.",
                        "Billed P&L Drilldown",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    return;
                }

                var rows = detail
                    .Select(d => new BilledLedgerDisplayRow(d))
                    .ToList();
                var total = detail.Sum(d => d.Amount);
                var title = $"{lineItem} - {periodRow.PeriodLabel}";
                var subtitle = $"{detail.Count:N0} grouped entries | Total {total:C0}";
                ShowDrilldownWindow(title, subtitle, rows);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    Window.GetWindow(this) ?? Application.Current?.MainWindow,
                    $"Unable to load supporting transactions.\n{ex.Message}",
                    "Billed P&L Drilldown",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        private sealed class BilledPeriodRow
        {
            [Browsable(false)]
            public string Account { get; }
            public string Period { get; }
            public string Amount { get; }
            [Browsable(false)]
            public string PeriodLabel { get; }
            [Browsable(false)]
            public int PeriodInt { get; }
            [Browsable(false)]
            public decimal AmountRaw { get; }

            public BilledPeriodRow(string account, string periodLabel, int period, decimal amount)
            {
                Account = account ?? string.Empty;
                PeriodLabel = periodLabel ?? string.Empty;
                PeriodInt = period;
                AmountRaw = amount;
                Period = periodLabel ?? string.Empty;
                Amount = amount.ToString("C0", CultureInfo.CurrentCulture);
            }
        }

        private sealed class BilledLedgerDisplayRow
        {
            public string Source { get; }
            public string Date { get; }
            public string DocumentNo { get; }
            public string Counterparty { get; }
            public string Account { get; }
            public string Description { get; }
            public string Amount { get; }
            public int Entries { get; }

            public BilledLedgerDisplayRow(BilledLedgerTransaction t)
            {
                Source = t.Source ?? "";
                Date = t.TransDate.HasValue
                    ? t.TransDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)
                    : "";
                DocumentNo = t.DocumentNo ?? "";
                Counterparty = t.Counterparty ?? "";
                Account = t.Account ?? "";
                Description = t.Description ?? "";
                Amount = t.Amount.ToString("C0", CultureInfo.CurrentCulture);
                Entries = t.EntryCount;
            }
        }

        private void PnLGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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
                    onRowDoubleClick: periodRow => _ = ExecuteDrilldownAsync(periodRow));
            }
        }

        private static bool IsDataGridRowDoubleClick(MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d)
                return false;

            DependencyObject? cur = d;
            while (cur != null && cur is not DataGridRow)
                cur = VisualTreeHelper.GetParent(cur);
            return cur is DataGridRow;
        }

        private List<PnLContributorRow> BuildContributorRows(string rowKind, string lineItem, string section)
        {
            var result = _presenter.CurrentResult;
            if (result == null)
                return new List<PnLContributorRow>();

            var detailRows = result.Table.Rows.Cast<DataRow>()
                .Where(r => string.Equals(Convert.ToString(r["RowKind"]), "Detail", StringComparison.OrdinalIgnoreCase))
                .ToList();

            static bool IsExpenseSection(string s) => s.IndexOf("expense", StringComparison.OrdinalIgnoreCase) >= 0;
            static bool IsIncomeSection(string s)
                => s.IndexOf("revenue", StringComparison.OrdinalIgnoreCase) >= 0
                   || (s.IndexOf("income", StringComparison.OrdinalIgnoreCase) >= 0 && s.IndexOf("expense", StringComparison.OrdinalIgnoreCase) < 0);

            IEnumerable<DataRow> src = detailRows;
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

        private List<PnLPeriodAmountRow> BuildPeriodRows(DataRow row)
        {
            var result = _presenter.CurrentResult;
            if (result == null || result.PeriodColumnNames == null || result.PeriodColumnNames.Length == 0)
                return new List<PnLPeriodAmountRow>();

            var rows = new List<PnLPeriodAmountRow>(result.PeriodColumnNames.Length);
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

        private void ShowDrilldownWindow<T>(string title, string subtitle, IReadOnlyList<T> rows, Action<T>? onRowDoubleClick = null)
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

        private async Task ExecuteDrilldownAsync(PnLPeriodAmountRow periodRow)
        {
            try
            {
                await OpenPeriodDetailDrilldownAsync(periodRow);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open period detail drilldown");
            }
        }

        private async Task OpenPeriodDetailDrilldownAsync(PnLPeriodAmountRow row)
        {
            if (row == null || row.LineGroupCode == null || row.Period <= 0 || _presenter.ViewModel.SelectedTable == null)
                return;

            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                var detail = await _presenter.LoadLineItemTransactionsAsync(
                    tableNo: _presenter.ViewModel.SelectedTable.TableNo,
                    glGroup: row.LineGroupCode.Value,
                    period: row.Period,
                    orgFilter: _presenter.ViewModel.OrgFilter,
                    flipSign: _presenter.ViewModel.FlipSign,
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

        private sealed class PnLContributorRow
        {
            public string LineItem { get; }
            public string Current { get; }
            public string Prior { get; }
            public string YTD { get; }
            public string TTM { get; }
            public string PercentOfRevenue { get; }

            [Browsable(false)]
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
            [Browsable(false)]
            public string LineItem { get; }
            public string PeriodLabel { get; }
            public string Amount { get; }
            [Browsable(false)]
            public int Period { get; }
            [Browsable(false)]
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
    }
}
