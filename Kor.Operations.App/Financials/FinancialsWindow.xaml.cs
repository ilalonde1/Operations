#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Win32;
using ClosedXML.Excel;
using Kor.Operations.Data;
namespace Kor.Operations.Financials
{
    public partial class FinancialsWindow : Window
    {
        private readonly FinancialsViewModel _vm = new();
        private CancellationTokenSource? _cts;

        public FinancialsWindow()
        {
            InitializeComponent();
            DataContext = _vm;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = ApplyHeaderAsync();

            // Respect "prefer cached display"; only load on open if nothing is loaded yet.
            if (_vm.HasData)
                return;

            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: false, _cts.Token);
        }

        private async Task ApplyHeaderAsync()
        {
            try { await global::Kor.Operations.HeaderLoader.ApplyAsync(HeaderBar); } catch { /* non-fatal */ }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
        }

        private void MetricDictionaryBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new FinancialMetricDictionaryWindow { Owner = this };
            win.Show();
        }

        private void PnLReportBtn_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 2;
        }

        private async void DumpDeltekSchemaBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Run off UI thread; this can take a while and may require VPN/Deltek connectivity.
                var result = await DeltekSchemaDumper.DumpAsync(CancellationToken.None).ConfigureAwait(true);

                MessageBox.Show(
                    this,
                    $"Deltek schema dump completed.\n\nFolder:\n{result.OutputDirectory}\n\nTables: {result.TableCount:N0}\nColumns: {result.ColumnCount:N0}",
                    "Deltek Schema Dump",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    this,
                    $"Deltek schema dump failed.\n\n{ex.Message}",
                    "Deltek Schema Dump",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private void ShowCommandCenter_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 0;
        }

        private void ShowExecutiveSummary_Click(object sender, RoutedEventArgs e)
        {
            _vm.SectionIndex = 1;
            _ = _vm.ExecutiveSummary.RefreshAsync(
                forceRefresh: false,
                existingSnapshot: null,
                existingTrend: null,
                existingUtilRows: null,
                ct: CancellationToken.None);
        }

        private void UtilizationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UtilizationGrid.SelectedItem is not UtilizationRow row)
                return;

            if (e.OriginalSource is DependencyObject d)
            {
                DependencyObject? cur = d;
                while (cur != null && cur is not DataGridRow)
                    cur = VisualTreeHelper.GetParent(cur);
                if (cur is not DataGridRow)
                    return;
            }

            var counts = new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: _vm.PortfolioStableCount + _vm.PortfolioAtRiskCount,
                Critical: _vm.PortfolioCriticalCount);

            var win = new ProjectFinancialDetailWindow(row.Project, counts) { Owner = this };
            win.Show();
        }

        private void DraftUtilizationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DraftUtilizationGrid.SelectedItem is not DraftUtilizationRow row)
                return;

            if (e.OriginalSource is DependencyObject d)
            {
                DependencyObject? cur = d;
                while (cur != null && cur is not DataGridRow)
                    cur = VisualTreeHelper.GetParent(cur);
                if (cur is not DataGridRow)
                    return;
            }

            var counts = new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: _vm.PortfolioStableCount + _vm.PortfolioAtRiskCount,
                Critical: _vm.PortfolioCriticalCount);

            var win = new ProjectFinancialDetailWindow(row.Project, counts) { Owner = this };
            win.Show();
        }

        private void ShowEngineeringCapacityRisk_Click(object sender, RoutedEventArgs e)
        {
            _vm.CapacityRiskViewIndex = 0;
        }

        private void ShowDraftingCapacityRisk_Click(object sender, RoutedEventArgs e)
        {
            _vm.CapacityRiskViewIndex = 1;
        }

        private async void ExportUtilizationBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanExportUtilization)
                return;

            var sfd = new SaveFileDialog
            {
                Title = "Export Utilization",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"UtilizationReport_{DateTime.Now:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true
            };

            if (sfd.ShowDialog(this) != true)
                return;

            var exportEngineering = _vm.IsEngineeringCapacitySelected;

            _vm.SetExporting(true);
            try
            {
                var path = sfd.FileName;
                await Task.Run(() =>
                {
                    using var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add("Utilization");

                    var headers = exportEngineering
                        ? new[]
                        {
                            "Project",
                            DisplayTerms.ProjectNumber,
                            "PM",
                            "Phase",
                            "Eng Budget",
                            "Eng Hours",
                            "Remaining Eng Hours",
                            "% Eng Used",
                            "Fee",
                            "% Billed",
                            "Risk",
                            "SeverityScore"
                        }
                        : new[]
                        {
                            "Project",
                            DisplayTerms.ProjectNumber,
                            "PM",
                            "Phase",
                            "Draft Budget",
                            "Draft Hours",
                            "Remaining Draft Hours",
                            "% Draft Used",
                            "Fee",
                            "% Billed",
                            "Risk",
                            "SeverityScore"
                        };

                    for (var c = 0; c < headers.Length; c++)
                    {
                        var cell = ws.Cell(1, c + 1);
                        cell.Value = headers[c];
                        cell.Style.Font.Bold = true;
                    }

                    ws.SheetView.FreezeRows(1);

                    var xlRed = XLColor.FromHtml("#FEE2E2");
                    var xlYellow = XLColor.FromHtml("#FEF3C7");
                    var xlGreen = XLColor.FromHtml("#DCFCE7");

                    if (exportEngineering)
                    {
                        var rows = _vm.UtilizationView.Cast<UtilizationRow>().ToList();
                        var rowIndex = 2;
                        foreach (var r in rows)
                        {
                            ws.Cell(rowIndex, 1).Value = r.ProjectName;
                            ws.Cell(rowIndex, 2).Value = r.Wbs1;
                            ws.Cell(rowIndex, 3).Value = r.Pm;
                            ws.Cell(rowIndex, 4).Value = r.Phase;

                            ws.Cell(rowIndex, 5).Value = r.EngBudget;
                            ws.Cell(rowIndex, 5).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 6).Value = r.EngHours;
                            ws.Cell(rowIndex, 6).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 7).Value = r.RemainingEngHours;
                            ws.Cell(rowIndex, 7).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 8).Value = r.PercentEngUsed;
                            ws.Cell(rowIndex, 8).Style.NumberFormat.Format = "0.0%";

                            ws.Cell(rowIndex, 9).Value = r.Fee;
                            ws.Cell(rowIndex, 9).Style.NumberFormat.Format = "$#,##0";

                            ws.Cell(rowIndex, 10).Value = r.PercentBilled;
                            ws.Cell(rowIndex, 10).Style.NumberFormat.Format = "0.0%";

                            ws.Cell(rowIndex, 11).Value = r.RiskStatus;
                            ws.Cell(rowIndex, 12).Value = (int)r.ConfidenceLevel;

                            rowIndex++;
                        }
                    }
                    else
                    {
                        var rows = _vm.DraftUtilizationView.Cast<DraftUtilizationRow>().ToList();
                        var rowIndex = 2;
                        foreach (var r in rows)
                        {
                            ws.Cell(rowIndex, 1).Value = r.ProjectName;
                            ws.Cell(rowIndex, 2).Value = r.Wbs1;
                            ws.Cell(rowIndex, 3).Value = r.Pm;
                            ws.Cell(rowIndex, 4).Value = r.Phase;

                            ws.Cell(rowIndex, 5).Value = r.DraftBudget;
                            ws.Cell(rowIndex, 5).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 6).Value = r.DraftHours;
                            ws.Cell(rowIndex, 6).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 7).Value = r.RemainingDraftHours;
                            ws.Cell(rowIndex, 7).Style.NumberFormat.Format = "0.0";

                            ws.Cell(rowIndex, 8).Value = r.PercentDraftUsed;
                            ws.Cell(rowIndex, 8).Style.NumberFormat.Format = "0.0%";

                            ws.Cell(rowIndex, 9).Value = r.Fee;
                            ws.Cell(rowIndex, 9).Style.NumberFormat.Format = "$#,##0";

                            ws.Cell(rowIndex, 10).Value = r.PercentBilled;
                            ws.Cell(rowIndex, 10).Style.NumberFormat.Format = "0.0%";

                            ws.Cell(rowIndex, 11).Value = r.RiskStatus;
                            ws.Cell(rowIndex, 12).Value = (int)r.ConfidenceLevel;

                            rowIndex++;
                        }
                    }

                    var used = ws.RangeUsed();
                    if (used != null)
                    {
                        used.CreateTable().Theme = XLTableTheme.TableStyleLight9;
                    }

                    var rowCount = ws.LastRowUsed()?.RowNumber() ?? 1;
                    if (rowCount > 1)
                    {
                        var severityCol = headers.Length; // SeverityScore
                        var dataColsEnd = headers.Length - 1; // exclude SeverityScore from visible range
                        var severityLetter = XLHelper.GetColumnLetterFromNumber(severityCol);
                        var visibleRange = ws.Range(2, 1, rowCount, dataColsEnd);

                        visibleRange.AddConditionalFormat()
                            .WhenIsTrue($"=${severityLetter}2=0")
                            .Fill.SetBackgroundColor(xlRed);

                        visibleRange.AddConditionalFormat()
                            .WhenIsTrue($"=${severityLetter}2=1")
                            .Fill.SetBackgroundColor(xlYellow);

                        visibleRange.AddConditionalFormat()
                            .WhenIsTrue($"=${severityLetter}2=3")
                            .Fill.SetBackgroundColor(xlGreen);

                        ws.Column(severityCol).Hide();
                    }

                    ws.Columns(1, headers.Length).AdjustToContents();
                    wb.SaveAs(path);
                }).ConfigureAwait(true);

                MessageBox.Show(this, "Export completed.", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _vm.SetExporting(false);
            }
        }

        private static string Csv(string? s)
        {
            s ??= "";
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }

        private void FinancialsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FinancialsGrid.SelectedItem is not FinancialsProjectRow row)
                return;

            if (e.OriginalSource is DependencyObject d)
            {
                DependencyObject? cur = d;
                while (cur != null && cur is not DataGridRow)
                    cur = VisualTreeHelper.GetParent(cur);
                if (cur is not DataGridRow)
                    return;
            }

            var counts = new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: _vm.PortfolioStableCount + _vm.PortfolioAtRiskCount,
                Critical: _vm.PortfolioCriticalCount);

            var win = new ProjectFinancialDetailWindow(row, counts) { Owner = this };
            win.Show();
        }

        private void Window_Closing(object? sender, CancelEventArgs e)
        {
            _cts?.Cancel();
        }
    }

    internal sealed class FinancialsViewModel : INotifyPropertyChanged
    {
        private readonly FinancialsService _svc = new();
        private readonly SqlFinancialPortfolioSnapshotStore _portfolioStore = new();
        public ExecutiveSummaryViewModel ExecutiveSummary { get; } = new();
        private bool _isLoading;
        private bool _isExporting;
        private string _errorMessage = "";
        private DateTimeOffset? _lastRefreshed;
        private FinancialsHeadlineKpis _headline = new();
        private int _sectionIndex;
        private string _selectedUtilizationPm = "All";
        private string _selectedUtilizationRisk = "All";
        private string _utilizationSearchText = "";
        private int _capacityRiskViewIndex;

        public ObservableCollection<FinancialsProjectRow> Rows { get; } = new();
        public ObservableCollection<UtilizationRow> UtilizationRows { get; } = new();
        public ObservableCollection<DraftUtilizationRow> DraftUtilizationRows { get; } = new();
        public ObservableCollection<PortfolioTrendPoint> PortfolioTrend { get; } = new();
        public ObservableCollection<string> UtilizationPmOptions { get; } = new();
        public ObservableCollection<string> UtilizationRiskOptions { get; } = new() { "All", "Over budget", "At risk", "Healthy" };
        public ICollectionView UtilizationView { get; }
        public ICollectionView DraftUtilizationView { get; }

        public FinancialsHeadlineKpis Headline
        {
            get => _headline;
            private set { _headline = value; OnPropertyChanged(); }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                _errorMessage = value ?? "";
                OnPropertyChanged();
                OnPropertyChanged(nameof(ErrorVisibility));
            }
        }

        public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

        public bool HasData => Rows.Count > 0;

        public int SectionIndex
        {
            get => _sectionIndex;
            set
            {
                var v = value < 0 ? 0 : (value > 2 ? 2 : value);
                if (_sectionIndex == v) return;
                _sectionIndex = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CommandCenterVisibility));
                OnPropertyChanged(nameof(ExecutiveSummaryVisibility));
                OnPropertyChanged(nameof(ProfitLossVisibility));
                OnPropertyChanged(nameof(IsCommandCenterSelected));
                OnPropertyChanged(nameof(IsExecutiveSummarySelected));
                OnPropertyChanged(nameof(IsProfitLossSelected));
            }
        }

        public bool IsCommandCenterSelected => SectionIndex == 0;
        public bool IsExecutiveSummarySelected => SectionIndex == 1;
        public bool IsProfitLossSelected => SectionIndex == 2;
        public Visibility CommandCenterVisibility => SectionIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ExecutiveSummaryVisibility => SectionIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ProfitLossVisibility => SectionIndex == 2 ? Visibility.Visible : Visibility.Collapsed;

        public bool CanRefresh => !_isLoading;

        public bool CanExportUtilization =>
            !_isLoading &&
            !_isExporting &&
            (IsEngineeringCapacitySelected ? UtilizationRows.Count > 0 : DraftUtilizationRows.Count > 0);

        public int CapacityRiskViewIndex
        {
            get => _capacityRiskViewIndex;
            set
            {
                var v = value < 0 ? 0 : (value > 1 ? 1 : value);
                if (_capacityRiskViewIndex == v) return;
                _capacityRiskViewIndex = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsEngineeringCapacitySelected));
                OnPropertyChanged(nameof(IsDraftingCapacitySelected));
                OnPropertyChanged(nameof(CapacityRiskTitle));
                OnPropertyChanged(nameof(CapacityRiskSubtitle));
                OnPropertyChanged(nameof(CanExportUtilization));
            }
        }

        public bool IsEngineeringCapacitySelected => CapacityRiskViewIndex == 0;
        public bool IsDraftingCapacitySelected => CapacityRiskViewIndex == 1;
        public string CapacityRiskTitle => IsEngineeringCapacitySelected ? "Engineering Capacity Risk" : "Drafting Capacity Risk";
        public string CapacityRiskSubtitle =>
            IsEngineeringCapacitySelected
                ? "Highlights projects consuming engineering hours faster than planned."
                : "Highlights projects consuming drafting hours faster than planned.";

        public string SelectedUtilizationPm
        {
            get => _selectedUtilizationPm;
            set
            {
                _selectedUtilizationPm = value ?? "All";
                OnPropertyChanged();
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();
            }
        }

        public string SelectedUtilizationRisk
        {
            get => _selectedUtilizationRisk;
            set
            {
                _selectedUtilizationRisk = value ?? "All";
                OnPropertyChanged();
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();
            }
        }

        public string UtilizationSearchText
        {
            get => _utilizationSearchText;
            set
            {
                _utilizationSearchText = value ?? "";
                OnPropertyChanged();
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();
            }
        }

        public string LastRefreshedDisplay =>
            _lastRefreshed.HasValue
                ? _lastRefreshed.Value.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                : "Not yet";

        public string StatusHint => _isLoading ? $"Loading... Refresh may take time ({DisplayTerms.Hours}/{DisplayTerms.FeeBilled})." : "";

        public Visibility PortfolioVisibility => UtilizationRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public int PortfolioCriticalCount { get; private set; }
        public int PortfolioAtRiskCount { get; private set; }
        public int PortfolioStableCount { get; private set; }
        public int PortfolioHighConfidenceCount { get; private set; }

        public double PortfolioCriticalPct { get; private set; }
        public double PortfolioAtRiskPct { get; private set; }
        public double PortfolioStablePct { get; private set; }
        public double PortfolioHighConfidencePct { get; private set; }

        public double PortfolioRiskExposureFee { get; private set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        public FinancialsViewModel()
        {
            UtilizationView = CollectionViewSource.GetDefaultView(UtilizationRows);
            UtilizationView.Filter = UtilizationFilter;
            DraftUtilizationView = CollectionViewSource.GetDefaultView(DraftUtilizationRows);
            DraftUtilizationView.Filter = DraftUtilizationFilter;
            UtilizationPmOptions.Add("All");
        }

        internal void SetExporting(bool exporting)
        {
            _isExporting = exporting;
            OnPropertyChanged(nameof(CanExportUtilization));
        }

        public async Task RefreshAsync(bool forceRefresh, CancellationToken ct)
        {
            if (_isLoading)
                return;

            _isLoading = true;
            ErrorMessage = "";
            OnPropertyChanged(nameof(CanRefresh));
            OnPropertyChanged(nameof(StatusHint));
            OnPropertyChanged(nameof(CanExportUtilization));

            try
            {
                var snap = await _svc.GetSnapshotAsync(forceRefresh, ct);

                Rows.Clear();
                foreach (var r in snap.Rows)
                    Rows.Add(r);

                Headline = snap.Headline;
                _lastRefreshed = snap.RefreshedAt;

                UtilizationRows.Clear();
                foreach (var r in snap.Rows)
                    UtilizationRows.Add(UtilizationRow.FromProject(r));
                DraftUtilizationRows.Clear();
                foreach (var r in snap.Rows)
                    DraftUtilizationRows.Add(DraftUtilizationRow.FromProject(r));

                RecalcPortfolio();
                await PersistSnapshotAndLoadTrendAsync(ct);
                BuildUtilizationPmOptions(snap.Rows);
                UtilizationView.Refresh();
                DraftUtilizationView.Refresh();
                OnPropertyChanged(nameof(CanExportUtilization));

                await ExecutiveSummary.RefreshAsync(
                    forceRefresh,
                    existingSnapshot: snap,
                    existingTrend: PortfolioTrend.ToArray(),
                    existingUtilRows: UtilizationRows.ToArray(),
                    ct);

                OnPropertyChanged(nameof(LastRefreshedDisplay));
                OnPropertyChanged(nameof(HasData));
                OnPropertyChanged(nameof(PortfolioVisibility));
            }
            catch (OperationCanceledException)
            {
                // No-op: keep existing snapshot displayed.
            }
            catch (Exception ex)
            {
                ErrorMessage =
                    "Unable to load financial data from Deltek. " +
                    "The window remains responsive; try Refresh.\n\n" +
                    $"{ex.GetType().Name}: {ex.Message}";

                // Executive Summary should still render per-card "Data unavailable" instead of hard-failing.
                try
                {
                    await ExecutiveSummary.RefreshAsync(
                        forceRefresh: false,
                        existingSnapshot: null,
                        existingTrend: PortfolioTrend.ToArray(),
                        existingUtilRows: UtilizationRows.ToArray(),
                        ct);
                }
                catch
                {
                    // ignore
                }
            }
            finally
            {
                _isLoading = false;
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(StatusHint));
                OnPropertyChanged(nameof(CanExportUtilization));
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void RecalcPortfolio()
        {
            var total = UtilizationRows.Count;
            if (total <= 0)
            {
                PortfolioCriticalCount = 0;
                PortfolioAtRiskCount = 0;
                PortfolioStableCount = 0;
                PortfolioHighConfidenceCount = 0;

                PortfolioCriticalPct = 0.0;
                PortfolioAtRiskPct = 0.0;
                PortfolioStablePct = 0.0;
                PortfolioHighConfidencePct = 0.0;
                PortfolioRiskExposureFee = 0.0;

                OnPropertyChanged(nameof(PortfolioCriticalCount));
                OnPropertyChanged(nameof(PortfolioAtRiskCount));
                OnPropertyChanged(nameof(PortfolioStableCount));
                OnPropertyChanged(nameof(PortfolioHighConfidenceCount));
                OnPropertyChanged(nameof(PortfolioCriticalPct));
                OnPropertyChanged(nameof(PortfolioAtRiskPct));
                OnPropertyChanged(nameof(PortfolioStablePct));
                OnPropertyChanged(nameof(PortfolioHighConfidencePct));
                OnPropertyChanged(nameof(PortfolioRiskExposureFee));
                return;
            }

            var critical = 0;
            var atRisk = 0;
            var stable = 0;
            var high = 0;
            var riskExposureFee = 0.0;

            foreach (var r in UtilizationRows)
            {
                switch (r.ConfidenceLevel)
                {
                    case DeliveryConfidenceLevel.Critical:
                        critical++;
                        riskExposureFee += r.Fee;
                        break;
                    case DeliveryConfidenceLevel.AtRisk:
                        atRisk++;
                        break;
                    case DeliveryConfidenceLevel.Stable:
                        stable++;
                        break;
                    default:
                        high++;
                        break;
                }
            }

            PortfolioCriticalCount = critical;
            PortfolioAtRiskCount = atRisk;
            PortfolioStableCount = stable;
            PortfolioHighConfidenceCount = high;

            PortfolioCriticalPct = (double)critical / total;
            PortfolioAtRiskPct = (double)atRisk / total;
            PortfolioStablePct = (double)stable / total;
            PortfolioHighConfidencePct = (double)high / total;
            PortfolioRiskExposureFee = riskExposureFee;

            OnPropertyChanged(nameof(PortfolioCriticalCount));
            OnPropertyChanged(nameof(PortfolioAtRiskCount));
            OnPropertyChanged(nameof(PortfolioStableCount));
            OnPropertyChanged(nameof(PortfolioHighConfidenceCount));
            OnPropertyChanged(nameof(PortfolioCriticalPct));
            OnPropertyChanged(nameof(PortfolioAtRiskPct));
            OnPropertyChanged(nameof(PortfolioStablePct));
            OnPropertyChanged(nameof(PortfolioHighConfidencePct));
            OnPropertyChanged(nameof(PortfolioRiskExposureFee));
        }

        private async Task PersistSnapshotAndLoadTrendAsync(CancellationToken ct)
        {
            if (UtilizationRows.Count <= 0)
            {
                PortfolioTrend.Clear();
                return;
            }

            try
            {
                await _portfolioStore.EnsureSchemaAsync(ct);

                var total = UtilizationRows.Count;
                var critical = PortfolioCriticalCount;

                // Trend buckets are 3-wide. Match the on-screen 4-bucket portfolio health:
                // Healthy = High Confidence + Stable, Watch = At Risk, Critical = Critical.
                var healthy = PortfolioHighConfidenceCount + PortfolioStableCount;
                var watch = PortfolioAtRiskCount;

                await _portfolioStore.TryInsertSnapshotAsync(DateTime.Today, healthy, watch, critical, total, ct);

                var start = DateTime.Today.AddDays(-7 * 12);
                var rows = await _portfolioStore.LoadSnapshotsAsync(start, ct);

                // Weekly rollup: show last snapshot per week for the last 12 weeks.
                var weekly = rows
                    .GroupBy(s =>
                    {
                        var d = s.SnapshotDate.Date;
                        var weekStart = d.AddDays(-(((int)d.DayOfWeek + 6) % 7)); // Monday
                        return weekStart;
                    })
                    .OrderBy(g => g.Key)
                    .Select(g => g.OrderBy(x => x.SnapshotDate).Last())
                    .TakeLast(12)
                    .ToList();

                PortfolioTrend.Clear();
                foreach (var s in weekly)
                {
                    PortfolioTrend.Add(new PortfolioTrendPoint(
                        s.SnapshotDate,
                        s.HealthyCount,
                        s.WatchCount,
                        s.CriticalCount,
                        s.TotalProjects));
                }
            }
            catch
            {
                // Non-fatal: Financials data can still be displayed without persistence/trend.
                PortfolioTrend.Clear();
            }
        }

        private bool UtilizationFilter(object obj)
        {
            if (obj is not UtilizationRow r) return false;
            return MatchesUtilizationFilters(r.Pm, r.RiskStatus, r.ProjectName, r.Wbs1);
        }

        private bool DraftUtilizationFilter(object obj)
        {
            if (obj is not DraftUtilizationRow r) return false;
            return MatchesUtilizationFilters(r.Pm, r.RiskStatus, r.ProjectName, r.Wbs1);
        }

        private bool MatchesUtilizationFilters(string pmValue, string riskValue, string projectName, string wbs1)
        {
            var pm = SelectedUtilizationPm;
            if (!string.IsNullOrWhiteSpace(pm) && !pm.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(pmValue ?? "", pm, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            var risk = SelectedUtilizationRisk;
            if (!string.IsNullOrWhiteSpace(risk) && !risk.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.Equals(riskValue ?? "", risk, StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            var q = (UtilizationSearchText ?? "").Trim();
            if (q.Length > 0)
            {
                if ((projectName ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (wbs1 ?? "").IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            return true;
        }

        private void BuildUtilizationPmOptions(List<FinancialsProjectRow> rows)
        {
            var keep = SelectedUtilizationPm;

            var pms = rows
                .Select(r => (r.Pm ?? "").Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();

            UtilizationPmOptions.Clear();
            UtilizationPmOptions.Add("All");
            foreach (var pm in pms)
                UtilizationPmOptions.Add(pm);

            if (string.IsNullOrWhiteSpace(keep) || !UtilizationPmOptions.Contains(keep))
                SelectedUtilizationPm = "All";
            else
                SelectedUtilizationPm = keep;
        }

    }

    public sealed class UtilizationRow
    {
        public FinancialsProjectRow Project { get; private set; } = new();
        public string Wbs1 { get; private set; } = "";
        public string ProjectName { get; private set; } = "";
        public string Pm { get; private set; } = "";
        public string Phase { get; private set; } = "";
        public double EngBudget { get; private set; }
        public double EngHours { get; private set; }
        public double RemainingEngHours { get; private set; }
        public double PercentEngUsed { get; private set; }
        public double Fee { get; private set; }
        public double PercentBilled { get; private set; }
        public string RiskStatus { get; private set; } = "Healthy";
        public string RiskColorName { get; private set; } = "Green";
        public string DeliveryConfidence { get; private set; } = "High Confidence";
        public string DeliveryConfidenceColorName { get; private set; } = "Green";
        public string DeliveryConfidenceSummary { get; private set; } = "";
        public string DeliveryConfidenceTooltip { get; private set; } = "";
        public DeliveryConfidenceLevel ConfidenceLevel { get; private set; } = DeliveryConfidenceLevel.HighConfidence;
        public string ConfidenceDisplay { get; private set; } = "High Confidence";

        public static UtilizationRow FromProject(FinancialsProjectRow p)
        {
            var budget = p?.EngBudget ?? 0.0;
            var hrs = p?.EngHrs ?? 0.0;
            var remaining = budget - hrs;

            var status = remaining < 0 ? "Over budget" : (remaining < 50 ? "At risk" : "Healthy");
            var color = status == "Over budget" ? "Red" : (status == "At risk" ? "Amber" : "Green");

            var dc = DeliveryConfidenceCalculator.Compute(p);
            var level =
                dc.Status == "Critical" ? DeliveryConfidenceLevel.Critical :
                dc.Status == "At Risk" ? DeliveryConfidenceLevel.AtRisk :
                dc.Status == "Watch" ? DeliveryConfidenceLevel.Stable :
                DeliveryConfidenceLevel.HighConfidence;

            return new UtilizationRow
            {
                Project = p ?? new FinancialsProjectRow(),
                Wbs1 = (p?.Wbs1 ?? "").Trim(),
                ProjectName = (p?.Name ?? "").Trim(),
                Pm = (p?.Pm ?? "").Trim(),
                Phase = (p?.Phase ?? "").Trim(),
                EngBudget = budget,
                EngHours = hrs,
                RemainingEngHours = remaining,
                PercentEngUsed = budget == 0.0 ? 0.0 : (hrs / budget),
                Fee = p?.Fee ?? 0.0,
                PercentBilled = p?.PercentBilled ?? 0.0,
                RiskStatus = status,
                RiskColorName = color,
                DeliveryConfidence = dc.Status,
                DeliveryConfidenceColorName = dc.ColorName,
                DeliveryConfidenceSummary = dc.Summary,
                DeliveryConfidenceTooltip = dc.Tooltip,
                ConfidenceLevel = level,
                ConfidenceDisplay = dc.Status
            };
        }
    }

    internal sealed class DraftUtilizationRow
    {
        public FinancialsProjectRow Project { get; private set; } = new();
        public string Wbs1 { get; private set; } = "";
        public string ProjectName { get; private set; } = "";
        public string Pm { get; private set; } = "";
        public string Phase { get; private set; } = "";
        public double DraftBudget { get; private set; }
        public double DraftHours { get; private set; }
        public double RemainingDraftHours { get; private set; }
        public double PercentDraftUsed { get; private set; }
        public double Fee { get; private set; }
        public double PercentBilled { get; private set; }
        public string RiskStatus { get; private set; } = "Healthy";
        public string RiskColorName { get; private set; } = "Green";
        public string DeliveryConfidence { get; private set; } = "High Confidence";
        public string DeliveryConfidenceColorName { get; private set; } = "Green";
        public string DeliveryConfidenceSummary { get; private set; } = "";
        public string DeliveryConfidenceTooltip { get; private set; } = "";
        public DeliveryConfidenceLevel ConfidenceLevel { get; private set; } = DeliveryConfidenceLevel.HighConfidence;
        public string ConfidenceDisplay { get; private set; } = "High Confidence";

        public static DraftUtilizationRow FromProject(FinancialsProjectRow p)
        {
            var budget = p?.DraftBudget ?? 0.0;
            var hrs = p?.DraftHrs ?? 0.0;
            var remaining = budget - hrs;

            var status = remaining < 0 ? "Over budget" : (remaining < 50 ? "At risk" : "Healthy");
            var color = status == "Over budget" ? "Red" : (status == "At risk" ? "Amber" : "Green");

            var dc = DeliveryConfidenceCalculator.Compute(p);
            var level =
                dc.Status == "Critical" ? DeliveryConfidenceLevel.Critical :
                dc.Status == "At Risk" ? DeliveryConfidenceLevel.AtRisk :
                dc.Status == "Watch" ? DeliveryConfidenceLevel.Stable :
                DeliveryConfidenceLevel.HighConfidence;

            return new DraftUtilizationRow
            {
                Project = p ?? new FinancialsProjectRow(),
                Wbs1 = (p?.Wbs1 ?? "").Trim(),
                ProjectName = (p?.Name ?? "").Trim(),
                Pm = (p?.Pm ?? "").Trim(),
                Phase = (p?.Phase ?? "").Trim(),
                DraftBudget = budget,
                DraftHours = hrs,
                RemainingDraftHours = remaining,
                PercentDraftUsed = budget == 0.0 ? 0.0 : (hrs / budget),
                Fee = p?.Fee ?? 0.0,
                PercentBilled = p?.PercentBilled ?? 0.0,
                RiskStatus = status,
                RiskColorName = color,
                DeliveryConfidence = dc.Status,
                DeliveryConfidenceColorName = dc.ColorName,
                DeliveryConfidenceSummary = dc.Summary,
                DeliveryConfidenceTooltip = dc.Tooltip,
                ConfidenceLevel = level,
                ConfidenceDisplay = dc.Status
            };
        }
    }

    public sealed class PortfolioTrendPoint
    {
        public DateTime SnapshotDate { get; }
        public string DateLabel { get; }
        public int HealthyCount { get; }
        public int WatchCount { get; }
        public int CriticalCount { get; }
        public int TotalProjects { get; }

        public string HealthyTooltip => $"{HealthyCount} projects - Healthy";
        public string WatchTooltip => $"{WatchCount} projects - Watch";
        public string CriticalTooltip => $"{CriticalCount} projects - Critical";

        public PortfolioTrendPoint(DateTime snapshotDate, int healthy, int watch, int critical, int total)
        {
            SnapshotDate = snapshotDate.Date;
            DateLabel = snapshotDate.ToString("MM-dd");
            HealthyCount = healthy;
            WatchCount = watch;
            CriticalCount = critical;
            TotalProjects = total;
        }
    }
}
