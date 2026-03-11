using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Kor.Operations.Financials
{
    public partial class MetricDetailWindow : Window
    {
        private readonly FinancialsService _financialsService = new();

        public MetricDetailWindow(MetricDetailVm vm)
        {
            InitializeComponent();
            DataContext = vm ?? throw new ArgumentNullException(nameof(vm));
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void CopySummary_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            Clipboard.SetText(vm.ToClipboardText());
        }

        private void PreviousProjectPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousProjectPage();
        }

        private void NextProjectPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextProjectPage();
        }

        private void PreviousArPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousArPage();
        }

        private void NextArPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextArPage();
        }

        private void PreviousArInvoicePage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousArInvoicePage();
        }

        private void NextArInvoicePage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextArInvoicePage();
        }

        private void PreviousWipPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousWipPage();
        }

        private void NextWipPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextWipPage();
        }

        private void PreviousBacklogPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousBacklogPage();
        }

        private void NextBacklogPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextBacklogPage();
        }

        private void PreviousBillingsPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousBillingsPage();
        }

        private void NextBillingsPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextBillingsPage();
        }

        private void PreviousBudgetBurnPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousBudgetBurnPage();
        }

        private void NextBudgetBurnPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextBudgetBurnPage();
        }

        private void PreviousUtilizationPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousUtilizationPage();
        }

        private void NextUtilizationPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextUtilizationPage();
        }

        private void PreviousDeliveryRiskPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousDeliveryRiskPage();
        }

        private void NextDeliveryRiskPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextDeliveryRiskPage();
        }

        private void PreviousTrendPayerPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToPreviousTrendPayerPage();
        }

        private void NextTrendPayerPage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is not MetricDetailVm vm) return;
            vm.GoToNextTrendPayerPage();
        }

        private async void ProjectRowGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid grid)
                return;

            if (!IsDataGridRowDoubleClick(e))
                return;

            var wbs1 = GetSelectedWbs1(grid.SelectedItem);
            if (string.IsNullOrWhiteSpace(wbs1))
                return;

            try
            {
                var snapshot = await _financialsService.GetSnapshotAsync(forceRefresh: false, CancellationToken.None);
                var project = snapshot.Rows.FirstOrDefault(r => string.Equals((r.Wbs1 ?? string.Empty).Trim(), wbs1, StringComparison.OrdinalIgnoreCase));
                if (project == null)
                    return;

                var counts = BuildPortfolioHealthCounts(snapshot.Rows);
                var win = new ProjectFinancialDetailWindow(project, counts) { Owner = this };
                win.Show();
            }
            catch
            {
                // Ignore navigation failures; the metric modal remains usable.
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

        private static string GetSelectedWbs1(object? selectedItem)
        {
            if (selectedItem == null)
                return string.Empty;

            var prop = selectedItem.GetType().GetProperty("Wbs1");
            if (prop?.PropertyType != typeof(string))
                return string.Empty;

            return ((string?)prop.GetValue(selectedItem) ?? string.Empty).Trim();
        }

        private static CfoMetrics.PortfolioHealthCounts BuildPortfolioHealthCounts(IEnumerable<FinancialsProjectRow> rows)
        {
            var critical = 0;
            var watch = 0;
            var healthy = 0;

            foreach (var row in rows)
            {
                var util = UtilizationRow.FromProject(row);
                switch (util.ConfidenceLevel)
                {
                    case DeliveryConfidenceLevel.Critical:
                        critical++;
                        break;
                    case DeliveryConfidenceLevel.AtRisk:
                    case DeliveryConfidenceLevel.Stable:
                        watch++;
                        break;
                    default:
                        healthy++;
                        break;
                }
            }

            return new CfoMetrics.PortfolioHealthCounts(
                Healthy: healthy,
                Watch: watch,
                Critical: critical);
        }

    }

    public enum MetricKind
    {
        Kpi,
        Trend,
        Alert
    }

    public sealed class FactRow
    {
        public string Key { get; }
        public string Value { get; }
        public FactRow(string key, string value)
        {
            Key = key ?? string.Empty;
            Value = value ?? string.Empty;
        }
    }

    public sealed class MetricDetailVm : INotifyPropertyChanged
    {
        private const int ProjectPageSize = 12;
        private const int ArPageSize = 12;
        private const int ArInvoicePageSize = 12;
        private const int WipPageSize = 12;
        private const int BacklogPageSize = 12;
        private const int BillingsPageSize = 12;
        private const int BudgetBurnPageSize = 12;
        private const int UtilizationPageSize = 12;
        private const int DeliveryRiskPageSize = 12;
        private const int TrendPayerPageSize = 12;
        private readonly IReadOnlyList<KpiProjectDrilldownRow> _allProjectDrilldownRows;
        private readonly IReadOnlyList<KpiCashHistoryRow> _allCashHistoryRows;
        private readonly IReadOnlyList<KpiArOutstandingRow> _allArOutstandingRows;
        private readonly IReadOnlyList<KpiArInvoiceRow> _allArInvoiceRows;
        private readonly IReadOnlyList<KpiWipUnbilledRow> _allWipUnbilledRows;
        private readonly IReadOnlyList<KpiBacklogRow> _allBacklogRows;
        private readonly IReadOnlyList<KpiBillingsRow> _allBillingsRows;
        private readonly IReadOnlyList<KpiBudgetBurnRow> _allBudgetBurnRows;
        private readonly IReadOnlyList<KpiUtilizationRow> _allUtilizationRows;
        private readonly IReadOnlyList<KpiDeliveryRiskRow> _allDeliveryRiskRows;
        private readonly IReadOnlyList<TrendPayerRow> _allTrendPayerRows;
        private int _projectPageIndex;
        private int _arPageIndex;
        private int _arInvoicePageIndex;
        private int _wipPageIndex;
        private int _backlogPageIndex;
        private int _billingsPageIndex;
        private int _budgetBurnPageIndex;
        private int _utilizationPageIndex;
        private int _deliveryRiskPageIndex;
        private int _trendPayerPageIndex;

        public MetricKind Kind { get; }
        public string KindLabel { get; }
        public string Title { get; }
        public string ValueText { get; }
        public Visibility ValueVisibility { get; }
        public string Definition { get; }
        public Visibility DefinitionVisibility => string.IsNullOrWhiteSpace(Definition) ? Visibility.Collapsed : Visibility.Visible;
        public IReadOnlyList<string> BulletPoints { get; }
        public Visibility BulletPointsVisibility => BulletPoints.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public IReadOnlyList<FactRow> Facts { get; }
        public Visibility FactsVisibility => Facts.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string SubText { get; }
        public string StatusMessage { get; }
        public Visibility StatusVisibility { get; }

        public Visibility TrendVisibility { get; }
        public PointCollection TrendPoints { get; } = new();
        public string TrendHint { get; }
        public string TrendStats { get; }

        public string FooterHint { get; }

        public string TechnicalQueryText { get; }
        public Visibility TechnicalQueryVisibility { get; }

        public ObservableCollection<ProjectDrilldownRowVm> PagedProjectDrilldownRows { get; } = new();
        public Visibility ProjectDrilldownVisibility => _allProjectDrilldownRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string ProjectDrilldownCountText => $"{_allProjectDrilldownRows.Count:N0} projects";
        public bool CanGoToPreviousProjectPage => _projectPageIndex > 0;
        public bool CanGoToNextProjectPage => ((_projectPageIndex + 1) * ProjectPageSize) < _allProjectDrilldownRows.Count;
        public string ProjectPageText
        {
            get
            {
                if (_allProjectDrilldownRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allProjectDrilldownRows.Count / (double)ProjectPageSize);
                return $"Page {_projectPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public ObservableCollection<CashHistoryRowVm> PagedCashHistoryRows { get; } = new();
        public Visibility CashHistoryVisibility => _allCashHistoryRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string CashHistoryCountText => $"{_allCashHistoryRows.Count:N0} periods";
        public ObservableCollection<ArOutstandingRowVm> PagedArOutstandingRows { get; } = new();
        public Visibility ArOutstandingVisibility => _allArOutstandingRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string ArOutstandingCountText => $"{_allArOutstandingRows.Count:N0} projects";
        public bool CanGoToPreviousArPage => _arPageIndex > 0;
        public bool CanGoToNextArPage => ((_arPageIndex + 1) * ArPageSize) < _allArOutstandingRows.Count;
        public string ArPageText
        {
            get
            {
                if (_allArOutstandingRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allArOutstandingRows.Count / (double)ArPageSize);
                return $"Page {_arPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public string ArAgingSummaryText
        {
            get
            {
                if (_allArOutstandingRows.Count == 0) return string.Empty;
                var total = _allArOutstandingRows.Sum(r => r.Total);
                if (Math.Abs(total) < 0.005) return string.Empty;
                var current = _allArOutstandingRows.Sum(r => r.Current);
                var aged31 = _allArOutstandingRows.Sum(r => r.Aged31To60);
                var aged61 = _allArOutstandingRows.Sum(r => r.Aged61To90);
                var aged90 = _allArOutstandingRows.Sum(r => r.Aged90Plus);
                var over60 = aged61 + aged90;
                var pctOver60 = over60 / total;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Current {0:C0} | 31-60 {1:C0} | 61-90 {2:C0} | 90+ {3:C0} | 60+ {4:P1}",
                    current, aged31, aged61, aged90, pctOver60);
            }
        }
        public ObservableCollection<ArInvoiceRowVm> PagedArInvoiceRows { get; } = new();
        public Visibility ArInvoiceVisibility => _allArInvoiceRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string ArInvoiceCountText => $"{_allArInvoiceRows.Count:N0} invoice rows";
        public bool CanGoToPreviousArInvoicePage => _arInvoicePageIndex > 0;
        public bool CanGoToNextArInvoicePage => ((_arInvoicePageIndex + 1) * ArInvoicePageSize) < _allArInvoiceRows.Count;
        public string ArInvoicePageText
        {
            get
            {
                if (_allArInvoiceRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allArInvoiceRows.Count / (double)ArInvoicePageSize);
                return $"Page {_arInvoicePageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public ObservableCollection<WipUnbilledRowVm> PagedWipUnbilledRows { get; } = new();
        public Visibility WipUnbilledVisibility => _allWipUnbilledRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string WipUnbilledCountText => $"{_allWipUnbilledRows.Count:N0} projects";
        public bool CanGoToPreviousWipPage => _wipPageIndex > 0;
        public bool CanGoToNextWipPage => ((_wipPageIndex + 1) * WipPageSize) < _allWipUnbilledRows.Count;
        public string WipPageText
        {
            get
            {
                if (_allWipUnbilledRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allWipUnbilledRows.Count / (double)WipPageSize);
                return $"Page {_wipPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public string WipCompositionSummaryText
        {
            get
            {
                if (_allWipUnbilledRows.Count == 0) return string.Empty;
                var earned = _allWipUnbilledRows.Sum(r => r.Earned);
                var over = _allWipUnbilledRows.Sum(r => r.Overbilled);
                var net = _allWipUnbilledRows.Sum(r => r.Net);
                var overCount = _allWipUnbilledRows.Count(r => r.Overbilled > 0.004);
                var pctOver = _allWipUnbilledRows.Count == 0 ? 0.0 : overCount / (double)_allWipUnbilledRows.Count;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Earned {0:C0} | Overbilled {1:C0} | Net {2:C0} | Overbilled projects {3:N0} ({4:P1})",
                    earned, over, net, overCount, pctOver);
            }
        }
        public ObservableCollection<BacklogRowVm> PagedBacklogRows { get; } = new();
        public Visibility BacklogVisibility => _allBacklogRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string BacklogCountText => $"{_allBacklogRows.Count:N0} projects";
        public bool CanGoToPreviousBacklogPage => _backlogPageIndex > 0;
        public bool CanGoToNextBacklogPage => ((_backlogPageIndex + 1) * BacklogPageSize) < _allBacklogRows.Count;
        public string BacklogPageText
        {
            get
            {
                if (_allBacklogRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allBacklogRows.Count / (double)BacklogPageSize);
                return $"Page {_backlogPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public string BacklogSummaryText
        {
            get
            {
                if (_allBacklogRows.Count == 0) return string.Empty;
                var totalBacklog = _allBacklogRows.Sum(r => r.Backlog);
                var top5 = _allBacklogRows.OrderByDescending(r => r.Backlog).Take(5).Sum(r => r.Backlog);
                var top5Pct = totalBacklog <= 0.0 ? 0.0 : top5 / totalBacklog;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Total backlog {0:C0} | Top 5 concentration {1:P1}",
                    totalBacklog, top5Pct);
            }
        }
        public ObservableCollection<BillingsRowVm> PagedBillingsRows { get; } = new();
        public Visibility BillingsVisibility => _allBillingsRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string BillingsCountText => $"{_allBillingsRows.Count:N0} projects";
        public bool CanGoToPreviousBillingsPage => _billingsPageIndex > 0;
        public bool CanGoToNextBillingsPage => ((_billingsPageIndex + 1) * BillingsPageSize) < _allBillingsRows.Count;
        public string BillingsPageText
        {
            get
            {
                if (_allBillingsRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allBillingsRows.Count / (double)BillingsPageSize);
                return $"Page {_billingsPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public string BillingsSummaryText
        {
            get
            {
                if (_allBillingsRows.Count == 0) return string.Empty;
                var total = _allBillingsRows.Sum(r => r.FeeBilled);
                var top5 = _allBillingsRows.OrderByDescending(r => r.FeeBilled).Take(5).Sum(r => r.FeeBilled);
                var top5Pct = total <= 0.0 ? 0.0 : top5 / total;
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Total billed {0:C0} | Top 5 concentration {1:P1}",
                    total, top5Pct);
            }
        }
        public ObservableCollection<BudgetBurnRowVm> PagedBudgetBurnRows { get; } = new();
        public Visibility BudgetBurnVisibility => _allBudgetBurnRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string BudgetBurnCountText => $"{_allBudgetBurnRows.Count:N0} projects";
        public bool CanGoToPreviousBudgetBurnPage => _budgetBurnPageIndex > 0;
        public bool CanGoToNextBudgetBurnPage => ((_budgetBurnPageIndex + 1) * BudgetBurnPageSize) < _allBudgetBurnRows.Count;
        public string BudgetBurnPageText
        {
            get
            {
                if (_allBudgetBurnRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allBudgetBurnRows.Count / (double)BudgetBurnPageSize);
                return $"Page {_budgetBurnPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public string BudgetBurnSummaryText
        {
            get
            {
                if (_allBudgetBurnRows.Count == 0) return string.Empty;
                var weightedBurn = _allBudgetBurnRows.Sum(r => r.EngBudget) <= 0.0
                    ? 0.0
                    : (_allBudgetBurnRows.Sum(r => r.EngHours) / _allBudgetBurnRows.Sum(r => r.EngBudget));
                var overBudgetCount = _allBudgetBurnRows.Count(r => r.RemainingHours < 0.0);
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Portfolio burn {0:P1} | Over budget projects {1:N0}",
                    weightedBurn, overBudgetCount);
            }
        }
        public ObservableCollection<UtilizationRowVm> PagedUtilizationRows { get; } = new();
        public Visibility UtilizationVisibility => _allUtilizationRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string UtilizationCountText => $"{_allUtilizationRows.Count:N0} projects";
        public bool CanGoToPreviousUtilizationPage => _utilizationPageIndex > 0;
        public bool CanGoToNextUtilizationPage => ((_utilizationPageIndex + 1) * UtilizationPageSize) < _allUtilizationRows.Count;
        public string UtilizationPageText
        {
            get
            {
                if (_allUtilizationRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allUtilizationRows.Count / (double)UtilizationPageSize);
                return $"Page {_utilizationPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public string UtilizationSummaryText
        {
            get
            {
                if (_allUtilizationRows.Count == 0) return string.Empty;
                var billable = _allUtilizationRows.Sum(r => r.BillableHours);
                var nonBillable = _allUtilizationRows.Sum(r => r.NonBillableHours);
                var total = _allUtilizationRows.Sum(r => r.TotalHours);
                var pct = total <= 0.0 ? 0.0 : (billable / total);
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Billable {0:N1} hrs | Non-billable {1:N1} hrs | Portfolio utilization {2:P1}",
                    billable, nonBillable, pct);
            }
        }
        public ObservableCollection<DeliveryRiskRowVm> PagedDeliveryRiskRows { get; } = new();
        public Visibility DeliveryRiskVisibility => _allDeliveryRiskRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string DeliveryRiskCountText => $"{_allDeliveryRiskRows.Count:N0} projects";
        public bool CanGoToPreviousDeliveryRiskPage => _deliveryRiskPageIndex > 0;
        public bool CanGoToNextDeliveryRiskPage => ((_deliveryRiskPageIndex + 1) * DeliveryRiskPageSize) < _allDeliveryRiskRows.Count;
        public string DeliveryRiskPageText
        {
            get
            {
                if (_allDeliveryRiskRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allDeliveryRiskRows.Count / (double)DeliveryRiskPageSize);
                return $"Page {_deliveryRiskPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public string DeliveryRiskSummaryText
        {
            get
            {
                if (_allDeliveryRiskRows.Count == 0) return string.Empty;
                var critical = _allDeliveryRiskRows.Count(r => string.Equals(r.DeliveryRisk, "Critical", StringComparison.OrdinalIgnoreCase));
                var atRisk = _allDeliveryRiskRows.Count(r => string.Equals(r.DeliveryRisk, "At Risk", StringComparison.OrdinalIgnoreCase));
                var overBudget = _allDeliveryRiskRows.Count(r => r.RemainingHours < 0.0);
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Critical {0:N0} | At Risk {1:N0} | Over budget {2:N0}",
                    critical, atRisk, overBudget);
            }
        }
        public ObservableCollection<TrendPayerRowVm> PagedTrendPayerRows { get; } = new();
        public Visibility TrendPayerVisibility => _allTrendPayerRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        public string TrendPayerCountText => $"{_allTrendPayerRows.Count:N0} projects";
        public bool CanGoToPreviousTrendPayerPage => _trendPayerPageIndex > 0;
        public bool CanGoToNextTrendPayerPage => ((_trendPayerPageIndex + 1) * TrendPayerPageSize) < _allTrendPayerRows.Count;
        public string TrendPayerPageText
        {
            get
            {
                if (_allTrendPayerRows.Count == 0) return string.Empty;
                var totalPages = (int)Math.Ceiling(_allTrendPayerRows.Count / (double)TrendPayerPageSize);
                return $"Page {_trendPayerPageIndex + 1:N0} of {totalPages:N0}";
            }
        }
        public string TrendPayerSummaryText
        {
            get
            {
                if (_allTrendPayerRows.Count == 0) return string.Empty;
                if (string.Equals(Title, "Revenue (Earned) (30/90 day)", StringComparison.OrdinalIgnoreCase))
                {
                    var earned = _allTrendPayerRows.Sum(r => r.RevenueAmount);
                    var billed = _allTrendPayerRows.Sum(r => r.BilledAmount);
                    var gap = earned - billed;
                    var positiveGapCount = _allTrendPayerRows.Count(r => (r.RevenueAmount - r.BilledAmount) > 0.004);
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        "Earned {0:C0} | Invoiced {1:C0} | Unbilled gap {2:C0} | Positive gap projects {3:N0}",
                        earned,
                        billed,
                        gap,
                        positiveGapCount);
                }

                if (string.Equals(Title, "Billings (Invoiced) (30/90 day)", StringComparison.OrdinalIgnoreCase))
                {
                    var billed = _allTrendPayerRows.Sum(r => r.BilledAmount);
                    var ar = _allTrendPayerRows.Sum(r => r.ArOutstandingAmount);
                    var arPct = Math.Abs(billed) <= 0.004 ? 0.0 : (ar / billed);
                    var highExposure = _allTrendPayerRows.Count(r => r.BilledAmount > 0.004 && (r.ArOutstandingAmount / r.BilledAmount) >= 0.5);
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        "Invoiced {0:C0} | AR outstanding {1:C0} | AR/Invoiced {2:P1} | High exposure projects {3:N0}",
                        billed,
                        ar,
                        arPct,
                        highExposure);
                }

                if (string.Equals(Title, "AR Outstanding (Recent Months)", StringComparison.OrdinalIgnoreCase))
                {
                    var ar = _allTrendPayerRows.Sum(r => r.ArOutstandingAmount);
                    var billed = _allTrendPayerRows.Sum(r => r.BilledAmount);
                    var arPct = Math.Abs(billed) <= 0.004 ? 0.0 : (ar / billed);
                    return string.Format(
                        CultureInfo.CurrentCulture,
                        "Outstanding AR {0:C0} | AR/Invoiced {1:P1}",
                        ar,
                        arPct);
                }

                var total = _allTrendPayerRows.Sum(r => r.Amount);
                var top5 = _allTrendPayerRows.OrderByDescending(r => r.Amount).Take(5).Sum(r => r.Amount);
                var top5Pct = Math.Abs(total) <= 0.004 ? 0.0 : top5 / Math.Abs(total);
                return string.Format(CultureInfo.CurrentCulture, "Total {0:C0} | Top 5 concentration {1:P1}", total, top5Pct);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MetricDetailVm(
            MetricKind kind,
            string title,
            string valueText,
            string subText,
            string statusMessage,
            string? lastUpdatedDisplay = null,
            double[]? trendValues = null,
            IReadOnlyList<KpiProjectDrilldownRow>? projectDrilldownRows = null,
            IReadOnlyList<KpiCashHistoryRow>? cashHistoryRows = null,
            IReadOnlyList<KpiArOutstandingRow>? arOutstandingRows = null,
            IReadOnlyList<KpiArInvoiceRow>? arInvoiceRows = null,
            IReadOnlyList<KpiWipUnbilledRow>? wipUnbilledRows = null,
            IReadOnlyList<KpiBacklogRow>? backlogRows = null,
            IReadOnlyList<KpiBillingsRow>? billingsRows = null,
            IReadOnlyList<KpiBudgetBurnRow>? budgetBurnRows = null,
            IReadOnlyList<KpiDeliveryRiskRow>? deliveryRiskRows = null,
            IReadOnlyList<KpiUtilizationRow>? utilizationRows = null,
            IReadOnlyList<TrendPayerRow>? trendPayerRows = null)
        {
            _allProjectDrilldownRows = projectDrilldownRows ?? Array.Empty<KpiProjectDrilldownRow>();
            _projectPageIndex = 0;
            _allCashHistoryRows = cashHistoryRows ?? Array.Empty<KpiCashHistoryRow>();
            _allArOutstandingRows = arOutstandingRows ?? Array.Empty<KpiArOutstandingRow>();
            _arPageIndex = 0;
            _allArInvoiceRows = arInvoiceRows ?? Array.Empty<KpiArInvoiceRow>();
            _arInvoicePageIndex = 0;
            _allWipUnbilledRows = wipUnbilledRows ?? Array.Empty<KpiWipUnbilledRow>();
            _wipPageIndex = 0;
            _allBacklogRows = backlogRows ?? Array.Empty<KpiBacklogRow>();
            _backlogPageIndex = 0;
            _allBillingsRows = billingsRows ?? Array.Empty<KpiBillingsRow>();
            _billingsPageIndex = 0;
            _allBudgetBurnRows = budgetBurnRows ?? Array.Empty<KpiBudgetBurnRow>();
            _budgetBurnPageIndex = 0;
            _allUtilizationRows = utilizationRows ?? Array.Empty<KpiUtilizationRow>();
            _utilizationPageIndex = 0;
            _allDeliveryRiskRows = deliveryRiskRows ?? Array.Empty<KpiDeliveryRiskRow>();
            _deliveryRiskPageIndex = 0;
            _allTrendPayerRows = trendPayerRows ?? Array.Empty<TrendPayerRow>();
            _trendPayerPageIndex = 0;

            Kind = kind;
            KindLabel = kind switch
            {
                MetricKind.Kpi => "KPI",
                MetricKind.Trend => "Trend",
                MetricKind.Alert => "Alert",
                _ => "Metric"
            };

            Title = title ?? string.Empty;
            ValueText = valueText ?? string.Empty;
            ValueVisibility = string.IsNullOrWhiteSpace(ValueText) ? Visibility.Collapsed : Visibility.Visible;
            SubText = subText ?? string.Empty;
            StatusMessage = statusMessage ?? string.Empty;
            StatusVisibility = string.IsNullOrWhiteSpace(StatusMessage) ? Visibility.Collapsed : Visibility.Visible;

            var suppressNarrative = ShouldSuppressTrendNarrative(kind, Title);
            var defaultDefinition = kind switch
            {
                MetricKind.Kpi => "Snapshot of this KPI as of the last refresh. Click other cards to compare quickly.",
                MetricKind.Trend => "Recent movement of this metric over the points we already load. Use the sparkline to spot direction and volatility.",
                MetricKind.Alert => "A rule-based flag derived from existing loaded data. The message below explains what tripped the alert.",
                _ => "Metric details."
            };
            var defAndQuery = ResolveDefinitionAndQuery(kind, Title, defaultDefinition);
            Definition = suppressNarrative ? string.Empty : defAndQuery.DefinitionText;

            BulletPoints = suppressNarrative
                ? Array.Empty<string>()
                : BuildBullets(kind, SubText, StatusMessage, trendValues);

            if (trendValues != null && trendValues.Length >= 2)
            {
                TrendVisibility = Visibility.Visible;

                foreach (var p in SparklineBuilder.Build(trendValues, width: 200, height: 44))
                    TrendPoints.Add(p);

                // Simple, non-invasive hint derived from what we already have.
                var finite = trendValues.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();
                if (finite.Length >= 2)
                {
                    double min = finite.Min();
                    double max = finite.Max();
                    var latest = finite[finite.Length - 1];
                    TrendHint = string.Format(CultureInfo.CurrentCulture, "Min {0:N0} • Max {1:N0} • Points {2:N0}", min, max, trendValues.Length);
                    TrendStats = string.Format(CultureInfo.CurrentCulture, "Latest {0:N0}", latest);
                }
                else
                {
                    TrendHint = string.Format(CultureInfo.CurrentCulture, "Points {0:N0}", trendValues.Length);
                    TrendStats = string.Empty;
                }
            }
            else
            {
                TrendVisibility = Visibility.Collapsed;
                TrendHint = string.Empty;
                TrendStats = string.Empty;
            }

            Facts = suppressNarrative
                ? Array.Empty<FactRow>()
                : BuildFacts(kind, lastUpdatedDisplay, trendValues);

            TechnicalQueryText = defAndQuery.TechnicalQueryText;
            TechnicalQueryVisibility = string.IsNullOrWhiteSpace(TechnicalQueryText) ? Visibility.Collapsed : Visibility.Visible;

            FooterHint = "Tip: use Copy summary to paste into email/Teams.";

            RebuildProjectDrilldownPage();
            RebuildCashHistoryPage();
            RebuildArOutstandingPage();
            RebuildArInvoicePage();
            RebuildWipUnbilledPage();
            RebuildBacklogPage();
            RebuildBillingsPage();
            RebuildBudgetBurnPage();
            RebuildUtilizationPage();
            RebuildDeliveryRiskPage();
            RebuildTrendPayerPage();
        }

        public string ToClipboardText()
        {
            var lines = new List<string>(64);
            lines.Add($"{KindLabel}: {Title}".Trim());
            if (!string.IsNullOrWhiteSpace(ValueText))
                lines.Add(ValueText.Trim());
            lines.Add("");
            lines.Add("Definition:");
            lines.Add(Definition.Trim());
            lines.Add("");
            lines.Add("Key points:");
            foreach (var b in BulletPoints.Where(s => !string.IsNullOrWhiteSpace(s)))
                lines.Add($"- {b.Trim()}");
            lines.Add("");
            lines.Add("Data points:");
            foreach (var f in Facts)
                lines.Add($"- {f.Key}: {f.Value}");
            if (_allProjectDrilldownRows.Count > 0)
            {
                lines.Add("");
                lines.Add("Project breakdown:");
                foreach (var row in _allProjectDrilldownRows)
                {
                    lines.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        "- {0} {1} | PM {2} | Over by {3:N1} hrs | Eng used {4:P1} | Billed {5:P1}",
                        row.Wbs1 ?? "",
                        row.ProjectName ?? "",
                        row.Pm ?? "",
                        row.OverByHours,
                        row.PercentEngUsed,
                        row.PercentBilled));
                }
            }
            if (_allCashHistoryRows.Count > 0)
            {
                lines.Add("");
                lines.Add("Cash history:");
                foreach (var row in _allCashHistoryRows)
                {
                    lines.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        "- {0} | Total {1:C0} | CAD {2:C0} | USA {3:C0} | BCC {4:C0}",
                        FormatPeriod(row.Period),
                        row.Total,
                        row.Cad,
                        row.Usa,
                        row.Bcc));
                }
            }
            if (_allTrendPayerRows.Count > 0)
            {
                lines.Add("");
                lines.Add("Payer breakdown:");
                foreach (var row in _allTrendPayerRows.Take(25))
                {
                    lines.Add(string.Format(
                        CultureInfo.CurrentCulture,
                        "- {0} | {1} {2} | PM {3} | {4:C0}",
                        row.PayerName ?? "",
                        row.Wbs1 ?? "",
                        row.ProjectName ?? "",
                        row.Pm ?? "",
                        row.Amount));
                }
            }
            if (!string.IsNullOrWhiteSpace(TechnicalQueryText))
            {
                lines.Add("");
                lines.Add("Technical query:");
                lines.Add(TechnicalQueryText.TrimEnd());
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static IReadOnlyList<string> BuildBullets(MetricKind kind, string subText, string statusMessage, double[]? trendValues)
        {
            var bullets = new List<string>(12);

            void addFrom(string raw)
            {
                foreach (var s in SplitBullets(raw))
                {
                    if (!string.IsNullOrWhiteSpace(s))
                        bullets.Add(s.Trim());
                }
            }

            switch (kind)
            {
                case MetricKind.Alert:
                    addFrom(subText);
                    break;
                case MetricKind.Trend:
                    addFrom(statusMessage);
                    if (trendValues != null && trendValues.Length >= 2)
                        bullets.Add(string.Format(CultureInfo.CurrentCulture, "Points loaded: {0:N0}", trendValues.Length));
                    break;
                default:
                    addFrom(subText);
                    addFrom(statusMessage);
                    break;
            }

            if (bullets.Count == 0)
                bullets.Add("No additional notes.");

            return bullets;
        }

        private static IReadOnlyList<FactRow> BuildFacts(MetricKind kind, string? lastUpdatedDisplay, double[]? trendValues)
        {
            var facts = new List<FactRow>(12);

            if (trendValues != null && trendValues.Length >= 2)
            {
                var finite = trendValues.Where(v => !double.IsNaN(v) && !double.IsInfinity(v)).ToArray();
                facts.Add(new FactRow("Points", trendValues.Length.ToString("N0", CultureInfo.CurrentCulture)));
                if (finite.Length >= 2)
                {
                    double min = finite.Min();
                    double max = finite.Max();
                    double latest = finite[finite.Length - 1];
                    facts.Add(new FactRow("Min", min.ToString("N0", CultureInfo.CurrentCulture)));
                    facts.Add(new FactRow("Max", max.ToString("N0", CultureInfo.CurrentCulture)));
                    facts.Add(new FactRow("Latest", latest.ToString("N0", CultureInfo.CurrentCulture)));
                }
            }

            return facts;
        }

        private static bool ShouldSuppressTrendNarrative(MetricKind kind, string title)
        {
            if (kind != MetricKind.Trend || string.IsNullOrWhiteSpace(title))
                return false;

            return string.Equals(title, "Revenue (Earned) (30/90 day)", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(title, "Billings (Invoiced) (30/90 day)", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(title, "AR Outstanding (Recent Months)", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(title, "Delivery Risk (Critical Count)", StringComparison.OrdinalIgnoreCase);
        }

        private static (string DefinitionText, string TechnicalQueryText) ResolveDefinitionAndQuery(MetricKind kind, string title, string defaultDefinition)
        {
            if (string.IsNullOrWhiteSpace(title))
                return (defaultDefinition, string.Empty);

            var normalizedTitle = title.Trim();
            var key = (kind, normalizedTitle) switch
            {
                (MetricKind.Kpi, "Cash Position") => "Exec_CashPosition",
                (MetricKind.Kpi, "AR Outstanding") => "Exec_ArOutstanding",
                (MetricKind.Kpi, "AR > 60 Days") => "Exec_ArOver60",
                (MetricKind.Kpi, "WIP (Unbilled Earned)") => "Exec_WipUnbilled",
                (MetricKind.Kpi, "WIP (Draft Invoices)") => "Exec_WipPreInvoice",
                (MetricKind.Kpi, "Backlog") => "Exec_Backlog",
                (MetricKind.Kpi, "Billings To Date") => "Exec_BillingsToDate",
                (MetricKind.Kpi, "Budget Burn") => "Exec_BudgetBurn",
                (MetricKind.Kpi, "Portfolio Delivery Risk") => "PortfolioDeliveryHealth",
                (MetricKind.Kpi, "Projects Over Budget") => "Exec_ProjectsOverBudget",
                (MetricKind.Kpi, "Utilization") => "Exec_Utilization30",

                (MetricKind.Trend, "Revenue (Earned) (30/90 day)") => "Exec_Revenue3090",
                (MetricKind.Trend, "Billings (Invoiced) (30/90 day)") => "Exec_Billed3090",
                (MetricKind.Trend, "AR Outstanding (Recent Months)") => "Exec_ArOutstandingRecent",
                (MetricKind.Trend, "Delivery Risk (Critical Count)") => "Exec_DeliveryRiskCriticalCount",

                (MetricKind.Alert, "AR > 60 Days") => "Alert_ArOver60",
                (MetricKind.Alert, "Projects Over Budget") => "Alert_ProjectsOverBudget",
                (MetricKind.Alert, "Backlog Declining") => "Alert_BacklogDeclining",
                (MetricKind.Alert, "Billing Lagging Burn") => "Alert_BillingLaggingBurn",
                _ => string.Empty
            };

            if (string.IsNullOrWhiteSpace(key))
                return (defaultDefinition, string.Empty);

            if (!FinancialMetricDefinitions.Definitions.TryGetValue(key, out var def) || def == null)
                return (defaultDefinition, string.Empty);

            var definition = string.IsNullOrWhiteSpace(def.Description) ? defaultDefinition : def.Description.Trim();
            var query = string.IsNullOrWhiteSpace(def.Formula) ? string.Empty : def.Formula.Trim();
            return (definition, query);
        }

        public void GoToPreviousProjectPage()
        {
            if (!CanGoToPreviousProjectPage) return;
            _projectPageIndex--;
            RebuildProjectDrilldownPage();
        }

        public void GoToNextProjectPage()
        {
            if (!CanGoToNextProjectPage) return;
            _projectPageIndex++;
            RebuildProjectDrilldownPage();
        }

        public void GoToPreviousArPage()
        {
            if (!CanGoToPreviousArPage) return;
            _arPageIndex--;
            RebuildArOutstandingPage();
        }

        public void GoToNextArPage()
        {
            if (!CanGoToNextArPage) return;
            _arPageIndex++;
            RebuildArOutstandingPage();
        }

        public void GoToPreviousArInvoicePage()
        {
            if (!CanGoToPreviousArInvoicePage) return;
            _arInvoicePageIndex--;
            RebuildArInvoicePage();
        }

        public void GoToNextArInvoicePage()
        {
            if (!CanGoToNextArInvoicePage) return;
            _arInvoicePageIndex++;
            RebuildArInvoicePage();
        }

        public void GoToPreviousWipPage()
        {
            if (!CanGoToPreviousWipPage) return;
            _wipPageIndex--;
            RebuildWipUnbilledPage();
        }

        public void GoToNextWipPage()
        {
            if (!CanGoToNextWipPage) return;
            _wipPageIndex++;
            RebuildWipUnbilledPage();
        }

        public void GoToPreviousBacklogPage()
        {
            if (!CanGoToPreviousBacklogPage) return;
            _backlogPageIndex--;
            RebuildBacklogPage();
        }

        public void GoToNextBacklogPage()
        {
            if (!CanGoToNextBacklogPage) return;
            _backlogPageIndex++;
            RebuildBacklogPage();
        }

        public void GoToPreviousBillingsPage()
        {
            if (!CanGoToPreviousBillingsPage) return;
            _billingsPageIndex--;
            RebuildBillingsPage();
        }

        public void GoToNextBillingsPage()
        {
            if (!CanGoToNextBillingsPage) return;
            _billingsPageIndex++;
            RebuildBillingsPage();
        }

        public void GoToPreviousBudgetBurnPage()
        {
            if (!CanGoToPreviousBudgetBurnPage) return;
            _budgetBurnPageIndex--;
            RebuildBudgetBurnPage();
        }

        public void GoToNextBudgetBurnPage()
        {
            if (!CanGoToNextBudgetBurnPage) return;
            _budgetBurnPageIndex++;
            RebuildBudgetBurnPage();
        }

        public void GoToPreviousUtilizationPage()
        {
            if (!CanGoToPreviousUtilizationPage) return;
            _utilizationPageIndex--;
            RebuildUtilizationPage();
        }

        public void GoToNextUtilizationPage()
        {
            if (!CanGoToNextUtilizationPage) return;
            _utilizationPageIndex++;
            RebuildUtilizationPage();
        }

        public void GoToPreviousDeliveryRiskPage()
        {
            if (!CanGoToPreviousDeliveryRiskPage) return;
            _deliveryRiskPageIndex--;
            RebuildDeliveryRiskPage();
        }

        public void GoToNextDeliveryRiskPage()
        {
            if (!CanGoToNextDeliveryRiskPage) return;
            _deliveryRiskPageIndex++;
            RebuildDeliveryRiskPage();
        }

        public void GoToPreviousTrendPayerPage()
        {
            if (!CanGoToPreviousTrendPayerPage) return;
            _trendPayerPageIndex--;
            RebuildTrendPayerPage();
        }

        public void GoToNextTrendPayerPage()
        {
            if (!CanGoToNextTrendPayerPage) return;
            _trendPayerPageIndex++;
            RebuildTrendPayerPage();
        }

        private void RebuildProjectDrilldownPage()
        {
            PagedProjectDrilldownRows.Clear();

            if (_allProjectDrilldownRows.Count > 0)
            {
                var pageRows = _allProjectDrilldownRows
                    .Skip(_projectPageIndex * ProjectPageSize)
                    .Take(ProjectPageSize)
                    .Select(r => new ProjectDrilldownRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.OverByHours,
                        r.PercentEngUsed,
                        r.PercentBilled));

                foreach (var row in pageRows)
                    PagedProjectDrilldownRows.Add(row);
            }

            OnPropertyChanged(nameof(ProjectDrilldownVisibility));
            OnPropertyChanged(nameof(ProjectDrilldownCountText));
            OnPropertyChanged(nameof(CanGoToPreviousProjectPage));
            OnPropertyChanged(nameof(CanGoToNextProjectPage));
            OnPropertyChanged(nameof(ProjectPageText));
        }

        private void RebuildCashHistoryPage()
        {
            PagedCashHistoryRows.Clear();

            if (_allCashHistoryRows.Count > 0)
            {
                var pageRows = _allCashHistoryRows
                    .OrderByDescending(r => r.Period, StringComparer.Ordinal)
                    .Select(r => new CashHistoryRowVm(r.Period, r.Total, r.Cad, r.Usa, r.Bcc));

                foreach (var row in pageRows)
                    PagedCashHistoryRows.Add(row);
            }

            OnPropertyChanged(nameof(CashHistoryVisibility));
            OnPropertyChanged(nameof(CashHistoryCountText));
        }

        private void RebuildArOutstandingPage()
        {
            PagedArOutstandingRows.Clear();

            if (_allArOutstandingRows.Count > 0)
            {
                var pageRows = _allArOutstandingRows
                    .OrderByDescending(r => r.Aged90Plus)
                    .ThenByDescending(r => r.Total)
                    .Skip(_arPageIndex * ArPageSize)
                    .Take(ArPageSize)
                    .Select(r => new ArOutstandingRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.Total,
                        r.Current,
                        r.Aged31To60,
                        r.Aged61To90,
                        r.Aged90Plus,
                        r.OldestInvoiceDate));

                foreach (var row in pageRows)
                    PagedArOutstandingRows.Add(row);
            }

            OnPropertyChanged(nameof(ArOutstandingVisibility));
            OnPropertyChanged(nameof(ArOutstandingCountText));
            OnPropertyChanged(nameof(CanGoToPreviousArPage));
            OnPropertyChanged(nameof(CanGoToNextArPage));
            OnPropertyChanged(nameof(ArPageText));
            OnPropertyChanged(nameof(ArAgingSummaryText));
        }

        private void RebuildArInvoicePage()
        {
            PagedArInvoiceRows.Clear();

            if (_allArInvoiceRows.Count > 0)
            {
                var pageRows = _allArInvoiceRows
                    .OrderByDescending(r => r.DaysPastDue)
                    .ThenByDescending(r => r.Balance)
                    .Skip(_arInvoicePageIndex * ArInvoicePageSize)
                    .Take(ArInvoicePageSize)
                    .Select(r => new ArInvoiceRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.InvoiceDate,
                        r.DueDate,
                        r.DaysPastDue,
                        r.Balance));

                foreach (var row in pageRows)
                    PagedArInvoiceRows.Add(row);
            }

            OnPropertyChanged(nameof(ArInvoiceVisibility));
            OnPropertyChanged(nameof(ArInvoiceCountText));
            OnPropertyChanged(nameof(CanGoToPreviousArInvoicePage));
            OnPropertyChanged(nameof(CanGoToNextArInvoicePage));
            OnPropertyChanged(nameof(ArInvoicePageText));
        }

        private void RebuildWipUnbilledPage()
        {
            PagedWipUnbilledRows.Clear();

            if (_allWipUnbilledRows.Count > 0)
            {
                var pageRows = _allWipUnbilledRows
                    .OrderByDescending(r => r.Overbilled)
                    .ThenByDescending(r => r.Earned)
                    .Skip(_wipPageIndex * WipPageSize)
                    .Take(WipPageSize)
                    .Select(r => new WipUnbilledRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.Earned,
                        r.Overbilled,
                        r.Net,
                        r.NetAsPercentOfFee,
                        r.Period));

                foreach (var row in pageRows)
                    PagedWipUnbilledRows.Add(row);
            }

            OnPropertyChanged(nameof(WipUnbilledVisibility));
            OnPropertyChanged(nameof(WipUnbilledCountText));
            OnPropertyChanged(nameof(CanGoToPreviousWipPage));
            OnPropertyChanged(nameof(CanGoToNextWipPage));
            OnPropertyChanged(nameof(WipPageText));
            OnPropertyChanged(nameof(WipCompositionSummaryText));
        }

        private void RebuildBacklogPage()
        {
            PagedBacklogRows.Clear();

            if (_allBacklogRows.Count > 0)
            {
                var pageRows = _allBacklogRows
                    .OrderByDescending(r => r.Backlog)
                    .Skip(_backlogPageIndex * BacklogPageSize)
                    .Take(BacklogPageSize)
                    .Select(r => new BacklogRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.Fee,
                        r.FeeBilled,
                        r.Backlog,
                        r.PercentBilled));

                foreach (var row in pageRows)
                    PagedBacklogRows.Add(row);
            }

            OnPropertyChanged(nameof(BacklogVisibility));
            OnPropertyChanged(nameof(BacklogCountText));
            OnPropertyChanged(nameof(CanGoToPreviousBacklogPage));
            OnPropertyChanged(nameof(CanGoToNextBacklogPage));
            OnPropertyChanged(nameof(BacklogPageText));
            OnPropertyChanged(nameof(BacklogSummaryText));
        }

        private void RebuildBillingsPage()
        {
            PagedBillingsRows.Clear();

            if (_allBillingsRows.Count > 0)
            {
                var pageRows = _allBillingsRows
                    .OrderByDescending(r => r.FeeBilled)
                    .Skip(_billingsPageIndex * BillingsPageSize)
                    .Take(BillingsPageSize)
                    .Select(r => new BillingsRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.FeeBilled,
                        r.Fee,
                        r.PercentBilled,
                        r.ContributionPercent));

                foreach (var row in pageRows)
                    PagedBillingsRows.Add(row);
            }

            OnPropertyChanged(nameof(BillingsVisibility));
            OnPropertyChanged(nameof(BillingsCountText));
            OnPropertyChanged(nameof(CanGoToPreviousBillingsPage));
            OnPropertyChanged(nameof(CanGoToNextBillingsPage));
            OnPropertyChanged(nameof(BillingsPageText));
            OnPropertyChanged(nameof(BillingsSummaryText));
        }

        private void RebuildBudgetBurnPage()
        {
            PagedBudgetBurnRows.Clear();

            if (_allBudgetBurnRows.Count > 0)
            {
                var pageRows = _allBudgetBurnRows
                    .OrderByDescending(r => r.PercentUsed)
                    .Skip(_budgetBurnPageIndex * BudgetBurnPageSize)
                    .Take(BudgetBurnPageSize)
                    .Select(r => new BudgetBurnRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.EngHours,
                        r.EngBudget,
                        r.PercentUsed,
                        r.RemainingHours));

                foreach (var row in pageRows)
                    PagedBudgetBurnRows.Add(row);
            }

            OnPropertyChanged(nameof(BudgetBurnVisibility));
            OnPropertyChanged(nameof(BudgetBurnCountText));
            OnPropertyChanged(nameof(CanGoToPreviousBudgetBurnPage));
            OnPropertyChanged(nameof(CanGoToNextBudgetBurnPage));
            OnPropertyChanged(nameof(BudgetBurnPageText));
            OnPropertyChanged(nameof(BudgetBurnSummaryText));
        }

        private void RebuildUtilizationPage()
        {
            PagedUtilizationRows.Clear();

            if (_allUtilizationRows.Count > 0)
            {
                var pageRows = _allUtilizationRows
                    .OrderByDescending(r => r.UtilizationPct)
                    .ThenByDescending(r => r.TotalHours)
                    .Skip(_utilizationPageIndex * UtilizationPageSize)
                    .Take(UtilizationPageSize)
                    .Select(r => new UtilizationRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.BillableHours,
                        r.NonBillableHours,
                        r.TotalHours,
                        r.UtilizationPct));

                foreach (var row in pageRows)
                    PagedUtilizationRows.Add(row);
            }

            OnPropertyChanged(nameof(UtilizationVisibility));
            OnPropertyChanged(nameof(UtilizationCountText));
            OnPropertyChanged(nameof(CanGoToPreviousUtilizationPage));
            OnPropertyChanged(nameof(CanGoToNextUtilizationPage));
            OnPropertyChanged(nameof(UtilizationPageText));
            OnPropertyChanged(nameof(UtilizationSummaryText));
        }

        private void RebuildDeliveryRiskPage()
        {
            PagedDeliveryRiskRows.Clear();

            if (_allDeliveryRiskRows.Count > 0)
            {
                var pageRows = _allDeliveryRiskRows
                    .OrderByDescending(r => string.Equals(r.DeliveryRisk, "Critical", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenBy(r => r.RemainingHours)
                    .Skip(_deliveryRiskPageIndex * DeliveryRiskPageSize)
                    .Take(DeliveryRiskPageSize)
                    .Select(r => new DeliveryRiskRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.DeliveryRisk,
                        r.BudgetStatus,
                        r.PercentEngUsed,
                        r.RemainingHours));

                foreach (var row in pageRows)
                    PagedDeliveryRiskRows.Add(row);
            }

            OnPropertyChanged(nameof(DeliveryRiskVisibility));
            OnPropertyChanged(nameof(DeliveryRiskCountText));
            OnPropertyChanged(nameof(CanGoToPreviousDeliveryRiskPage));
            OnPropertyChanged(nameof(CanGoToNextDeliveryRiskPage));
            OnPropertyChanged(nameof(DeliveryRiskPageText));
            OnPropertyChanged(nameof(DeliveryRiskSummaryText));
        }

        private void RebuildTrendPayerPage()
        {
            PagedTrendPayerRows.Clear();

            if (_allTrendPayerRows.Count > 0)
            {
                IEnumerable<TrendPayerRow> ordered;
                if (string.Equals(Title, "Revenue (Earned) (30/90 day)", StringComparison.OrdinalIgnoreCase))
                {
                    ordered = _allTrendPayerRows
                        .OrderByDescending(r => (r.RevenueAmount - r.BilledAmount))
                        .ThenByDescending(r => r.RevenueAmount);
                }
                else if (string.Equals(Title, "Billings (Invoiced) (30/90 day)", StringComparison.OrdinalIgnoreCase))
                {
                    ordered = _allTrendPayerRows
                        .OrderByDescending(r => r.BilledAmount > 0.004 ? (r.ArOutstandingAmount / r.BilledAmount) : 0.0)
                        .ThenByDescending(r => r.BilledAmount);
                }
                else if (string.Equals(Title, "AR Outstanding (Recent Months)", StringComparison.OrdinalIgnoreCase))
                {
                    ordered = _allTrendPayerRows
                        .OrderByDescending(r => r.ArOutstandingAmount)
                        .ThenByDescending(r => r.BilledAmount);
                }
                else
                {
                    ordered = _allTrendPayerRows
                        .OrderByDescending(r => r.Amount)
                        .ThenBy(r => r.PayerName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
                }

                var pageRows = ordered
                    .Skip(_trendPayerPageIndex * TrendPayerPageSize)
                    .Take(TrendPayerPageSize)
                    .Select(r => new TrendPayerRowVm(
                        r.Wbs1,
                        r.ProjectName,
                        r.Pm,
                        r.PayerName,
                        r.Amount,
                        r.RevenueAmount,
                        r.BilledAmount,
                        r.ArOutstandingAmount));

                foreach (var row in pageRows)
                    PagedTrendPayerRows.Add(row);
            }

            OnPropertyChanged(nameof(TrendPayerVisibility));
            OnPropertyChanged(nameof(TrendPayerCountText));
            OnPropertyChanged(nameof(CanGoToPreviousTrendPayerPage));
            OnPropertyChanged(nameof(CanGoToNextTrendPayerPage));
            OnPropertyChanged(nameof(TrendPayerPageText));
            OnPropertyChanged(nameof(TrendPayerSummaryText));
        }

        private static string FormatPeriod(string period)
        {
            if (string.IsNullOrWhiteSpace(period)) return string.Empty;
            var p = period.Trim();
            if (p.Length == 6 &&
                int.TryParse(p.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) &&
                int.TryParse(p.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
                m >= 1 && m <= 12)
            {
                return new DateTime(y, m, 1).ToString("MMM yyyy", CultureInfo.CurrentCulture);
            }
            return p;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        private static IEnumerable<string> SplitBullets(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                yield break;

            // Prefer explicit separators; avoid over-splitting sentences.
            var text = raw.Replace("\r\n", "\n").Trim();

            if (text.Contains('\n'))
            {
                foreach (var s in text.Split('\n'))
                {
                    var t = s.Trim().TrimStart('•', '-', '*').Trim();
                    if (!string.IsNullOrWhiteSpace(t))
                        yield return t;
                }
                yield break;
            }

            if (text.Contains('•'))
            {
                foreach (var s in text.Split('•'))
                {
                    var t = s.Trim().TrimStart('-', '*').Trim();
                    if (!string.IsNullOrWhiteSpace(t))
                        yield return t;
                }
                yield break;
            }

            yield return text;
        }
    }

    public sealed class ProjectDrilldownRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string OverByHoursText { get; }
        public string PercentEngUsedText { get; }
        public string PercentBilledText { get; }

        public ProjectDrilldownRowVm(
            string wbs1,
            string projectName,
            string pm,
            double overByHours,
            double percentEngUsed,
            double percentBilled)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            OverByHoursText = string.Format(CultureInfo.CurrentCulture, "{0:N1} hrs", overByHours);
            PercentEngUsedText = percentEngUsed.ToString("P1", CultureInfo.CurrentCulture);
            PercentBilledText = percentBilled.ToString("P1", CultureInfo.CurrentCulture);
        }
    }

    public sealed class CashHistoryRowVm
    {
        public string PeriodText { get; }
        public string TotalText { get; }
        public string CadText { get; }
        public string UsaText { get; }
        public string BccText { get; }

        public CashHistoryRowVm(string period, double total, double cad, double usa, double bcc)
        {
            PeriodText = FormatPeriod(period);
            TotalText = total.ToString("C0", CultureInfo.CurrentCulture);
            CadText = cad.ToString("C0", CultureInfo.CurrentCulture);
            UsaText = usa.ToString("C0", CultureInfo.CurrentCulture);
            BccText = bcc.ToString("C0", CultureInfo.CurrentCulture);
        }

        private static string FormatPeriod(string period)
        {
            if (string.IsNullOrWhiteSpace(period)) return string.Empty;
            var p = period.Trim();
            if (p.Length == 6 &&
                int.TryParse(p.Substring(0, 4), NumberStyles.None, CultureInfo.InvariantCulture, out var y) &&
                int.TryParse(p.Substring(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m) &&
                m >= 1 && m <= 12)
            {
                return new DateTime(y, m, 1).ToString("MMM yyyy", CultureInfo.CurrentCulture);
            }
            return p;
        }
    }

    public sealed class ArOutstandingRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string TotalText { get; }
        public string CurrentText { get; }
        public string Aged31To60Text { get; }
        public string Aged61To90Text { get; }
        public string Aged90PlusText { get; }
        public string OldestDateText { get; }

        public ArOutstandingRowVm(
            string wbs1,
            string projectName,
            string pm,
            double total,
            double current,
            double aged31To60,
            double aged61To90,
            double aged90Plus,
            DateTime? oldestInvoiceDate)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            TotalText = total.ToString("C0", CultureInfo.CurrentCulture);
            CurrentText = current.ToString("C0", CultureInfo.CurrentCulture);
            Aged31To60Text = aged31To60.ToString("C0", CultureInfo.CurrentCulture);
            Aged61To90Text = aged61To90.ToString("C0", CultureInfo.CurrentCulture);
            Aged90PlusText = aged90Plus.ToString("C0", CultureInfo.CurrentCulture);
            OldestDateText = oldestInvoiceDate.HasValue
                ? oldestInvoiceDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture)
                : string.Empty;
        }
    }

    public sealed class ArInvoiceRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string InvoiceDateText { get; }
        public string DueDateText { get; }
        public string DaysPastDueText { get; }
        public string BalanceText { get; }

        public ArInvoiceRowVm(
            string wbs1,
            string projectName,
            string pm,
            DateTime? invoiceDate,
            DateTime? dueDate,
            int daysPastDue,
            double balance)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            InvoiceDateText = invoiceDate.HasValue ? invoiceDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) : string.Empty;
            DueDateText = dueDate.HasValue ? dueDate.Value.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) : string.Empty;
            DaysPastDueText = daysPastDue.ToString("N0", CultureInfo.CurrentCulture);
            BalanceText = balance.ToString("C0", CultureInfo.CurrentCulture);
        }
    }

    public sealed class WipUnbilledRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string EarnedText { get; }
        public string OverbilledText { get; }
        public string NetText { get; }
        public string NetPctFeeText { get; }
        public string PeriodText { get; }

        public WipUnbilledRowVm(
            string wbs1,
            string projectName,
            string pm,
            double earned,
            double overbilled,
            double net,
            double netAsPercentOfFee,
            string period)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            EarnedText = earned.ToString("C0", CultureInfo.CurrentCulture);
            OverbilledText = overbilled.ToString("C0", CultureInfo.CurrentCulture);
            NetText = net.ToString("C0", CultureInfo.CurrentCulture);
            NetPctFeeText = netAsPercentOfFee.ToString("P1", CultureInfo.CurrentCulture);
            PeriodText = string.IsNullOrWhiteSpace(period) ? string.Empty : period.Trim();
        }
    }

    public sealed class BacklogRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string FeeText { get; }
        public string BilledText { get; }
        public string BacklogText { get; }
        public string PercentBilledText { get; }

        public BacklogRowVm(
            string wbs1,
            string projectName,
            string pm,
            double fee,
            double billed,
            double backlog,
            double percentBilled)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            FeeText = fee.ToString("C0", CultureInfo.CurrentCulture);
            BilledText = billed.ToString("C0", CultureInfo.CurrentCulture);
            BacklogText = backlog.ToString("C0", CultureInfo.CurrentCulture);
            PercentBilledText = percentBilled.ToString("P1", CultureInfo.CurrentCulture);
        }
    }

    public sealed class BillingsRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string FeeBilledText { get; }
        public string FeeText { get; }
        public string PercentBilledText { get; }
        public string ContributionText { get; }

        public BillingsRowVm(
            string wbs1,
            string projectName,
            string pm,
            double feeBilled,
            double fee,
            double percentBilled,
            double contributionPercent)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            FeeBilledText = feeBilled.ToString("C0", CultureInfo.CurrentCulture);
            FeeText = fee.ToString("C0", CultureInfo.CurrentCulture);
            PercentBilledText = percentBilled.ToString("P1", CultureInfo.CurrentCulture);
            ContributionText = contributionPercent.ToString("P1", CultureInfo.CurrentCulture);
        }
    }

    public sealed class BudgetBurnRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string EngHoursText { get; }
        public string EngBudgetText { get; }
        public string PercentUsedText { get; }
        public string RemainingHoursText { get; }

        public BudgetBurnRowVm(
            string wbs1,
            string projectName,
            string pm,
            double engHours,
            double engBudget,
            double percentUsed,
            double remainingHours)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            EngHoursText = engHours.ToString("N1", CultureInfo.CurrentCulture);
            EngBudgetText = engBudget.ToString("N1", CultureInfo.CurrentCulture);
            PercentUsedText = percentUsed.ToString("P1", CultureInfo.CurrentCulture);
            RemainingHoursText = remainingHours.ToString("N1", CultureInfo.CurrentCulture);
        }
    }

    public sealed class UtilizationRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string BillableHoursText { get; }
        public string NonBillableHoursText { get; }
        public string TotalHoursText { get; }
        public string UtilizationPctText { get; }

        public UtilizationRowVm(
            string wbs1,
            string projectName,
            string pm,
            double billableHours,
            double nonBillableHours,
            double totalHours,
            double utilizationPct)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            BillableHoursText = billableHours.ToString("N1", CultureInfo.CurrentCulture);
            NonBillableHoursText = nonBillableHours.ToString("N1", CultureInfo.CurrentCulture);
            TotalHoursText = totalHours.ToString("N1", CultureInfo.CurrentCulture);
            UtilizationPctText = utilizationPct.ToString("P1", CultureInfo.CurrentCulture);
        }
    }

    public sealed class DeliveryRiskRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string DeliveryRiskText { get; }
        public string BudgetStatusText { get; }
        public string PercentUsedText { get; }
        public string RemainingHoursText { get; }

        public DeliveryRiskRowVm(
            string wbs1,
            string projectName,
            string pm,
            string deliveryRisk,
            string budgetStatus,
            double percentUsed,
            double remainingHours)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            DeliveryRiskText = deliveryRisk ?? string.Empty;
            BudgetStatusText = budgetStatus ?? string.Empty;
            PercentUsedText = percentUsed.ToString("P1", CultureInfo.CurrentCulture);
            RemainingHoursText = remainingHours.ToString("N1", CultureInfo.CurrentCulture);
        }
    }

    public sealed class TrendPayerRowVm
    {
        public string Wbs1 { get; }
        public string ProjectName { get; }
        public string Pm { get; }
        public string PayerName { get; }
        public string AmountText { get; }
        public string RevenueText { get; }
        public string BilledText { get; }
        public string UnbilledText { get; }
        public string ArOutstandingText { get; }
        public string BilledVsRevenueText { get; }
        public string ArVsBilledText { get; }

        public TrendPayerRowVm(
            string wbs1,
            string projectName,
            string pm,
            string payerName,
            double amount,
            double revenueAmount,
            double billedAmount,
            double arOutstandingAmount)
        {
            Wbs1 = wbs1 ?? string.Empty;
            ProjectName = projectName ?? string.Empty;
            Pm = pm ?? string.Empty;
            PayerName = payerName ?? string.Empty;
            AmountText = amount.ToString("C0", CultureInfo.CurrentCulture);
            RevenueText = revenueAmount.ToString("C0", CultureInfo.CurrentCulture);
            BilledText = billedAmount.ToString("C0", CultureInfo.CurrentCulture);
            UnbilledText = (revenueAmount - billedAmount).ToString("C0", CultureInfo.CurrentCulture);
            ArOutstandingText = arOutstandingAmount.ToString("C0", CultureInfo.CurrentCulture);
            var billedVsRevenue = Math.Abs(revenueAmount) <= 0.004 ? 0.0 : (billedAmount / revenueAmount);
            BilledVsRevenueText = billedVsRevenue.ToString("P1", CultureInfo.CurrentCulture);
            var arVsBilled = Math.Abs(billedAmount) <= 0.004 ? 0.0 : (arOutstandingAmount / billedAmount);
            ArVsBilledText = arVsBilled.ToString("P1", CultureInfo.CurrentCulture);
        }
    }
}
