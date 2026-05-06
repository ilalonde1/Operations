#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Kor.Operations.Core;

namespace Kor.Operations.Financials;

public enum MetricKind
{
    Kpi,
    Trend,
    Alert
}

public sealed class MetricDetailVm : ObservableObject
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
    private readonly PaginatedGrid<KpiProjectDrilldownRow, ProjectDrilldownRowVm> _projectGrid;
    private readonly UnpagedGrid<KpiCashHistoryRow, CashHistoryRowVm> _cashHistoryGrid;
    private readonly UnpagedGrid<KpiCashAccountRow, CashAccountRowVm> _cashAccountsGrid;
    private readonly PaginatedGrid<KpiArOutstandingRow, ArOutstandingRowVm> _arOutstandingGrid;
    private readonly PaginatedGrid<KpiArInvoiceRow, ArInvoiceRowVm> _arInvoiceGrid;
    private readonly PaginatedGrid<KpiWipUnbilledRow, WipUnbilledRowVm> _wipGrid;
    private readonly PaginatedGrid<KpiBacklogRow, BacklogRowVm> _backlogGrid;
    private readonly PaginatedGrid<KpiBillingsRow, BillingsRowVm> _billingsGrid;
    private readonly PaginatedGrid<KpiBudgetBurnRow, BudgetBurnRowVm> _budgetBurnGrid;
    private readonly PaginatedGrid<KpiUtilizationRow, UtilizationRowVm> _utilizationGrid;
    private readonly PaginatedGrid<KpiDeliveryRiskRow, DeliveryRiskRowVm> _deliveryRiskGrid;
    private readonly PaginatedGrid<TrendPayerRow, TrendPayerRowVm> _trendPayerGrid;

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


    public string TechnicalQueryText { get; }
    public Visibility TechnicalQueryVisibility { get; }

    public ObservableCollection<ProjectDrilldownRowVm> PagedProjectDrilldownRows => _projectGrid.PagedRows;
    public Visibility ProjectDrilldownVisibility => _projectGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string ProjectDrilldownCountText => $"{_projectGrid.RowCount:N0} projects";
    public bool CanGoToPreviousProjectPage => _projectGrid.CanGoPrev;
    public bool CanGoToNextProjectPage => _projectGrid.CanGoNext;
    public string ProjectPageText => _projectGrid.PageText;
    public ObservableCollection<CashHistoryRowVm> PagedCashHistoryRows => _cashHistoryGrid.PagedRows;
    public Visibility CashHistoryVisibility => _cashHistoryGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string CashHistoryCountText => $"{_cashHistoryGrid.RowCount:N0} periods";
    public ObservableCollection<CashAccountRowVm> PagedCashAccountRows => _cashAccountsGrid.PagedRows;
    public Visibility CashAccountsVisibility => _cashAccountsGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string CashAccountsCountText => $"{_cashAccountsGrid.RowCount:N0} accounts";
    public ObservableCollection<ArOutstandingRowVm> PagedArOutstandingRows => _arOutstandingGrid.PagedRows;
    public Visibility ArOutstandingVisibility => _arOutstandingGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string ArOutstandingCountText => $"{_arOutstandingGrid.RowCount:N0} projects";
    public bool CanGoToPreviousArPage => _arOutstandingGrid.CanGoPrev;
    public bool CanGoToNextArPage => _arOutstandingGrid.CanGoNext;
    public string ArPageText => _arOutstandingGrid.PageText;
    public string ArAgingSummaryText
    {
        get
        {
            if (_arOutstandingGrid.RowCount == 0) return string.Empty;
            var total = _arOutstandingGrid.AllRows.Sum(r => r.Total);
            if (Math.Abs(total) < 0.005) return string.Empty;
            var current = _arOutstandingGrid.AllRows.Sum(r => r.Current);
            var aged31 = _arOutstandingGrid.AllRows.Sum(r => r.Aged31To60);
            var aged61 = _arOutstandingGrid.AllRows.Sum(r => r.Aged61To90);
            var aged90 = _arOutstandingGrid.AllRows.Sum(r => r.Aged90Plus);
            var over60 = aged61 + aged90;
            var pctOver60 = over60 / total;
            return string.Format(
                CultureInfo.CurrentCulture,
                "Current {0:C0} | 31-60 {1:C0} | 61-90 {2:C0} | 90+ {3:C0} | 60+ {4:P1}",
                current, aged31, aged61, aged90, pctOver60);
        }
    }
    public ObservableCollection<ArInvoiceRowVm> PagedArInvoiceRows => _arInvoiceGrid.PagedRows;
    public Visibility ArInvoiceVisibility => _arInvoiceGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string ArInvoiceCountText => $"{_arInvoiceGrid.RowCount:N0} invoice rows";
    public bool CanGoToPreviousArInvoicePage => _arInvoiceGrid.CanGoPrev;
    public bool CanGoToNextArInvoicePage => _arInvoiceGrid.CanGoNext;
    public string ArInvoicePageText => _arInvoiceGrid.PageText;
    public ObservableCollection<WipUnbilledRowVm> PagedWipUnbilledRows => _wipGrid.PagedRows;
    public Visibility WipUnbilledVisibility => _wipGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string WipUnbilledCountText => $"{_wipGrid.RowCount:N0} projects";
    public bool CanGoToPreviousWipPage => _wipGrid.CanGoPrev;
    public bool CanGoToNextWipPage => _wipGrid.CanGoNext;
    public string WipPageText => _wipGrid.PageText;
    public string WipCompositionSummaryText
    {
        get
        {
            if (_wipGrid.RowCount == 0) return string.Empty;
            var earned = _wipGrid.AllRows.Sum(r => r.Earned);
            var over = _wipGrid.AllRows.Sum(r => r.Overbilled);
            var net = _wipGrid.AllRows.Sum(r => r.Net);
            var overCount = _wipGrid.AllRows.Count(r => r.Overbilled > 0.004);
            var pctOver = _wipGrid.RowCount == 0 ? 0.0 : overCount / (double)_wipGrid.RowCount;
            return string.Format(
                CultureInfo.CurrentCulture,
                "Earned {0:C0} | Overbilled {1:C0} | Net {2:C0} | Overbilled projects {3:N0} ({4:P1})",
                earned, over, net, overCount, pctOver);
        }
    }
    public ObservableCollection<BacklogRowVm> PagedBacklogRows => _backlogGrid.PagedRows;
    public Visibility BacklogVisibility => _backlogGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string BacklogCountText => $"{_backlogGrid.RowCount:N0} projects";
    public bool CanGoToPreviousBacklogPage => _backlogGrid.CanGoPrev;
    public bool CanGoToNextBacklogPage => _backlogGrid.CanGoNext;
    public string BacklogPageText => _backlogGrid.PageText;
    public string BacklogSummaryText
    {
        get
        {
            if (_backlogGrid.RowCount == 0) return string.Empty;
            var totalBacklog = _backlogGrid.AllRows.Sum(r => r.Backlog);
            var top5 = _backlogGrid.AllRows.OrderByDescending(r => r.Backlog).Take(5).Sum(r => r.Backlog);
            var top5Pct = totalBacklog <= 0.0 ? 0.0 : top5 / totalBacklog;
            return string.Format(
                CultureInfo.CurrentCulture,
                "Total backlog {0:C0} | Top 5 concentration {1:P1}",
                totalBacklog, top5Pct);
        }
    }
    public ObservableCollection<BillingsRowVm> PagedBillingsRows => _billingsGrid.PagedRows;
    public Visibility BillingsVisibility => _billingsGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string BillingsCountText => $"{_billingsGrid.RowCount:N0} projects";
    public bool CanGoToPreviousBillingsPage => _billingsGrid.CanGoPrev;
    public bool CanGoToNextBillingsPage => _billingsGrid.CanGoNext;
    public string BillingsPageText => _billingsGrid.PageText;
    public string BillingsSummaryText
    {
        get
        {
            if (_billingsGrid.RowCount == 0) return string.Empty;
            var total = _billingsGrid.AllRows.Sum(r => r.FeeBilled);
            var top5 = _billingsGrid.AllRows.OrderByDescending(r => r.FeeBilled).Take(5).Sum(r => r.FeeBilled);
            var top5Pct = total <= 0.0 ? 0.0 : top5 / total;
            return string.Format(
                CultureInfo.CurrentCulture,
                "Total billed {0:C0} | Top 5 concentration {1:P1}",
                total, top5Pct);
        }
    }
    public ObservableCollection<BudgetBurnRowVm> PagedBudgetBurnRows => _budgetBurnGrid.PagedRows;
    public Visibility BudgetBurnVisibility => _budgetBurnGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string BudgetBurnCountText => $"{_budgetBurnGrid.RowCount:N0} projects";
    public bool CanGoToPreviousBudgetBurnPage => _budgetBurnGrid.CanGoPrev;
    public bool CanGoToNextBudgetBurnPage => _budgetBurnGrid.CanGoNext;
    public string BudgetBurnPageText => _budgetBurnGrid.PageText;
    public string BudgetBurnSummaryText
    {
        get
        {
            if (_budgetBurnGrid.RowCount == 0) return string.Empty;
            var weightedBurn = _budgetBurnGrid.AllRows.Sum(r => r.EngBudget) <= 0.0
                ? 0.0
                : (_budgetBurnGrid.AllRows.Sum(r => r.EngHours) / _budgetBurnGrid.AllRows.Sum(r => r.EngBudget));
            var overBudgetCount = _budgetBurnGrid.AllRows.Count(r => r.RemainingHours < 0.0);
            return string.Format(
                CultureInfo.CurrentCulture,
                "Portfolio burn {0:P1} | Over budget projects {1:N0}",
                weightedBurn, overBudgetCount);
        }
    }
    public ObservableCollection<UtilizationRowVm> PagedUtilizationRows => _utilizationGrid.PagedRows;
    public Visibility UtilizationVisibility => _utilizationGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string UtilizationCountText => $"{_utilizationGrid.RowCount:N0} projects";
    public bool CanGoToPreviousUtilizationPage => _utilizationGrid.CanGoPrev;
    public bool CanGoToNextUtilizationPage => _utilizationGrid.CanGoNext;
    public string UtilizationPageText => _utilizationGrid.PageText;
    public string UtilizationSummaryText
    {
        get
        {
            if (_utilizationGrid.RowCount == 0) return string.Empty;
            var billable = _utilizationGrid.AllRows.Sum(r => r.BillableHours);
            var nonBillable = _utilizationGrid.AllRows.Sum(r => r.NonBillableHours);
            var total = _utilizationGrid.AllRows.Sum(r => r.TotalHours);
            var pct = total <= 0.0 ? 0.0 : (billable / total);
            return string.Format(
                CultureInfo.CurrentCulture,
                "Billable {0:N1} hrs | Non-billable {1:N1} hrs | Portfolio utilization {2:P1}",
                billable, nonBillable, pct);
        }
    }
    public ObservableCollection<DeliveryRiskRowVm> PagedDeliveryRiskRows => _deliveryRiskGrid.PagedRows;
    public Visibility DeliveryRiskVisibility => _deliveryRiskGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string DeliveryRiskCountText => $"{_deliveryRiskGrid.RowCount:N0} projects";
    public bool CanGoToPreviousDeliveryRiskPage => _deliveryRiskGrid.CanGoPrev;
    public bool CanGoToNextDeliveryRiskPage => _deliveryRiskGrid.CanGoNext;
    public string DeliveryRiskPageText => _deliveryRiskGrid.PageText;
    public string DeliveryRiskSummaryText
    {
        get
        {
            if (_deliveryRiskGrid.RowCount == 0) return string.Empty;
            var critical = _deliveryRiskGrid.AllRows.Count(r => string.Equals(r.DeliveryRisk, "Critical", StringComparison.OrdinalIgnoreCase));
            var atRisk = _deliveryRiskGrid.AllRows.Count(r => string.Equals(r.DeliveryRisk, "At Risk", StringComparison.OrdinalIgnoreCase));
            var overBudget = _deliveryRiskGrid.AllRows.Count(r => r.RemainingHours < 0.0);
            return string.Format(
                CultureInfo.CurrentCulture,
                "Critical {0:N0} | At Risk {1:N0} | Over budget {2:N0}",
                critical, atRisk, overBudget);
        }
    }
    public ObservableCollection<TrendPayerRowVm> PagedTrendPayerRows => _trendPayerGrid.PagedRows;
    public Visibility TrendPayerVisibility => _trendPayerGrid.HasRows ? Visibility.Visible : Visibility.Collapsed;
    public string TrendPayerCountText => $"{_trendPayerGrid.RowCount:N0} projects";
    public bool CanGoToPreviousTrendPayerPage => _trendPayerGrid.CanGoPrev;
    public bool CanGoToNextTrendPayerPage => _trendPayerGrid.CanGoNext;
    public string TrendPayerPageText => _trendPayerGrid.PageText;
    public string TrendPayerSummaryText
    {
        get
        {
            if (_trendPayerGrid.RowCount == 0) return string.Empty;
            if (string.Equals(Title, "Revenue (Earned) (30/90 day)", StringComparison.OrdinalIgnoreCase))
            {
                var earned = _trendPayerGrid.AllRows.Sum(r => r.RevenueAmount);
                var billed = _trendPayerGrid.AllRows.Sum(r => r.BilledAmount);
                var gap = earned - billed;
                var positiveGapCount = _trendPayerGrid.AllRows.Count(r => (r.RevenueAmount - r.BilledAmount) > 0.004);
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
                var billed = _trendPayerGrid.AllRows.Sum(r => r.BilledAmount);
                var ar = _trendPayerGrid.AllRows.Sum(r => r.ArOutstandingAmount);
                var arPct = Math.Abs(billed) <= 0.004 ? 0.0 : (ar / billed);
                var highExposure = _trendPayerGrid.AllRows.Count(r => r.BilledAmount > 0.004 && (r.ArOutstandingAmount / r.BilledAmount) >= 0.5);
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
                var ar = _trendPayerGrid.AllRows.Sum(r => r.ArOutstandingAmount);
                var billed = _trendPayerGrid.AllRows.Sum(r => r.BilledAmount);
                var arPct = Math.Abs(billed) <= 0.004 ? 0.0 : (ar / billed);
                return string.Format(
                    CultureInfo.CurrentCulture,
                    "Outstanding AR {0:C0} | AR/Invoiced {1:P1}",
                    ar,
                    arPct);
            }

            var total = _trendPayerGrid.AllRows.Sum(r => r.Amount);
            var top5 = _trendPayerGrid.AllRows.OrderByDescending(r => r.Amount).Take(5).Sum(r => r.Amount);
            var top5Pct = Math.Abs(total) <= 0.004 ? 0.0 : top5 / Math.Abs(total);
            return string.Format(CultureInfo.CurrentCulture, "Total {0:C0} | Top 5 concentration {1:P1}", total, top5Pct);
        }
    }

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
        IReadOnlyList<KpiCashAccountRow>? cashAccountRows = null,
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

        _projectGrid = new PaginatedGrid<KpiProjectDrilldownRow, ProjectDrilldownRowVm>(
            ProjectPageSize,
            rows => rows,
            r => new ProjectDrilldownRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.OverByHours,
                r.PercentEngUsed,
                r.PercentBilled,
                r.PercentBilledWithUnposted,
                r.HasUnpostedBilling),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(ProjectDrilldownVisibility));
                OnPropertyChanged(nameof(ProjectDrilldownCountText));
            },
            nameof(PagedProjectDrilldownRows),
            nameof(CanGoToPreviousProjectPage),
            nameof(CanGoToNextProjectPage),
            nameof(ProjectPageText));
        _cashHistoryGrid = new UnpagedGrid<KpiCashHistoryRow, CashHistoryRowVm>(
            rows => rows.OrderByDescending(r => r.Period, StringComparer.Ordinal),
            r => new CashHistoryRowVm(r.Period, r.Total, r.Cad, r.Usa, r.Bcc),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(CashHistoryVisibility));
                OnPropertyChanged(nameof(CashHistoryCountText));
            },
            nameof(PagedCashHistoryRows));
        _cashAccountsGrid = new UnpagedGrid<KpiCashAccountRow, CashAccountRowVm>(
            rows => rows
                .OrderBy(r => r.Company ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Account ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => r.Org ?? string.Empty, StringComparer.OrdinalIgnoreCase),
            r => new CashAccountRowVm(r.Company, r.Account, r.Org, r.Currency, r.Balance),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(CashAccountsVisibility));
                OnPropertyChanged(nameof(CashAccountsCountText));
            },
            nameof(PagedCashAccountRows));
        _arOutstandingGrid = new PaginatedGrid<KpiArOutstandingRow, ArOutstandingRowVm>(
            ArPageSize,
            rows => rows.OrderByDescending(r => r.Aged90Plus).ThenByDescending(r => r.Total),
            r => new ArOutstandingRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.Total,
                r.Current,
                r.Aged31To60,
                r.Aged61To90,
                r.Aged90Plus,
                r.OldestInvoiceDate),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(ArOutstandingVisibility));
                OnPropertyChanged(nameof(ArOutstandingCountText));
                OnPropertyChanged(nameof(ArAgingSummaryText));
            },
            nameof(PagedArOutstandingRows),
            nameof(CanGoToPreviousArPage),
            nameof(CanGoToNextArPage),
            nameof(ArPageText));
        _arInvoiceGrid = new PaginatedGrid<KpiArInvoiceRow, ArInvoiceRowVm>(
            ArInvoicePageSize,
            rows => rows.OrderByDescending(r => r.DaysPastDue).ThenByDescending(r => Math.Abs(r.Balance)),
            r => new ArInvoiceRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.InvoiceDate,
                r.DueDate,
                r.DaysPastDue,
                r.Balance),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(ArInvoiceVisibility));
                OnPropertyChanged(nameof(ArInvoiceCountText));
            },
            nameof(PagedArInvoiceRows),
            nameof(CanGoToPreviousArInvoicePage),
            nameof(CanGoToNextArInvoicePage),
            nameof(ArInvoicePageText));
        _wipGrid = new PaginatedGrid<KpiWipUnbilledRow, WipUnbilledRowVm>(
            WipPageSize,
            rows => rows.OrderByDescending(r => r.Overbilled).ThenByDescending(r => r.Earned),
            r => new WipUnbilledRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.Earned,
                r.Overbilled,
                r.Net,
                r.NetAsPercentOfFee,
                r.Period),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(WipUnbilledVisibility));
                OnPropertyChanged(nameof(WipUnbilledCountText));
                OnPropertyChanged(nameof(WipCompositionSummaryText));
            },
            nameof(PagedWipUnbilledRows),
            nameof(CanGoToPreviousWipPage),
            nameof(CanGoToNextWipPage),
            nameof(WipPageText));
        _backlogGrid = new PaginatedGrid<KpiBacklogRow, BacklogRowVm>(
            BacklogPageSize,
            rows => rows.OrderByDescending(r => r.Backlog),
            r => new BacklogRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.Fee,
                r.FeeBilled,
                r.UnpostedFeeBilled,
                r.Backlog,
                r.PercentBilled,
                r.PercentBilledWithUnposted,
                r.HasUnpostedBilling),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(BacklogVisibility));
                OnPropertyChanged(nameof(BacklogCountText));
                OnPropertyChanged(nameof(BacklogSummaryText));
            },
            nameof(PagedBacklogRows),
            nameof(CanGoToPreviousBacklogPage),
            nameof(CanGoToNextBacklogPage),
            nameof(BacklogPageText));
        _billingsGrid = new PaginatedGrid<KpiBillingsRow, BillingsRowVm>(
            BillingsPageSize,
            rows => rows.OrderByDescending(r => r.FeeBilled),
            r => new BillingsRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.FeeBilled,
                r.UnpostedFeeBilled,
                r.Fee,
                r.PercentBilled,
                r.PercentBilledWithUnposted,
                r.ContributionPercent,
                r.HasUnpostedBilling),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(BillingsVisibility));
                OnPropertyChanged(nameof(BillingsCountText));
                OnPropertyChanged(nameof(BillingsSummaryText));
            },
            nameof(PagedBillingsRows),
            nameof(CanGoToPreviousBillingsPage),
            nameof(CanGoToNextBillingsPage),
            nameof(BillingsPageText));
        _budgetBurnGrid = new PaginatedGrid<KpiBudgetBurnRow, BudgetBurnRowVm>(
            BudgetBurnPageSize,
            rows => rows.OrderByDescending(r => r.PercentUsed),
            r => new BudgetBurnRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.EngHours,
                r.EngBudget,
                r.PercentUsed,
                r.RemainingHours),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(BudgetBurnVisibility));
                OnPropertyChanged(nameof(BudgetBurnCountText));
                OnPropertyChanged(nameof(BudgetBurnSummaryText));
            },
            nameof(PagedBudgetBurnRows),
            nameof(CanGoToPreviousBudgetBurnPage),
            nameof(CanGoToNextBudgetBurnPage),
            nameof(BudgetBurnPageText));
        _utilizationGrid = new PaginatedGrid<KpiUtilizationRow, UtilizationRowVm>(
            UtilizationPageSize,
            rows => rows.OrderByDescending(r => r.UtilizationPct).ThenByDescending(r => r.TotalHours),
            r => new UtilizationRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.BillableHours,
                r.NonBillableHours,
                r.TotalHours,
                r.UtilizationPct),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(UtilizationVisibility));
                OnPropertyChanged(nameof(UtilizationCountText));
                OnPropertyChanged(nameof(UtilizationSummaryText));
            },
            nameof(PagedUtilizationRows),
            nameof(CanGoToPreviousUtilizationPage),
            nameof(CanGoToNextUtilizationPage),
            nameof(UtilizationPageText));
        _deliveryRiskGrid = new PaginatedGrid<KpiDeliveryRiskRow, DeliveryRiskRowVm>(
            DeliveryRiskPageSize,
            rows => rows
                .OrderByDescending(r => string.Equals(r.DeliveryRisk, "Critical", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenBy(r => r.RemainingHours),
            r => new DeliveryRiskRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.DeliveryRisk,
                r.BudgetStatus,
                r.PercentEngUsed,
                r.RemainingHours),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(DeliveryRiskVisibility));
                OnPropertyChanged(nameof(DeliveryRiskCountText));
                OnPropertyChanged(nameof(DeliveryRiskSummaryText));
            },
            nameof(PagedDeliveryRiskRows),
            nameof(CanGoToPreviousDeliveryRiskPage),
            nameof(CanGoToNextDeliveryRiskPage),
            nameof(DeliveryRiskPageText));
        _trendPayerGrid = new PaginatedGrid<TrendPayerRow, TrendPayerRowVm>(
            TrendPayerPageSize,
            rows =>
            {
                if (string.Equals(Title, "Revenue (Earned) (30/90 day)", StringComparison.OrdinalIgnoreCase))
                {
                    return rows
                        .OrderByDescending(r => (r.RevenueAmount - r.BilledAmount))
                        .ThenByDescending(r => r.RevenueAmount);
                }

                if (string.Equals(Title, "Billings (Invoiced) (30/90 day)", StringComparison.OrdinalIgnoreCase))
                {
                    return rows
                        .OrderByDescending(r => r.BilledAmount > 0.004 ? (r.ArOutstandingAmount / r.BilledAmount) : 0.0)
                        .ThenByDescending(r => r.BilledAmount);
                }

                if (string.Equals(Title, "AR Outstanding (Recent Months)", StringComparison.OrdinalIgnoreCase))
                {
                    return rows
                        .OrderByDescending(r => r.ArOutstandingAmount)
                        .ThenByDescending(r => r.BilledAmount);
                }

                return rows
                    .OrderByDescending(r => r.Amount)
                    .ThenBy(r => r.PayerName ?? string.Empty, StringComparer.OrdinalIgnoreCase);
            },
            r => new TrendPayerRowVm(
                r.Wbs1,
                r.ProjectName,
                r.Pm,
                r.PayerName,
                r.Amount,
                r.RevenueAmount,
                r.BilledAmount,
                r.ArOutstandingAmount),
            OnPropertyChanged,
            () =>
            {
                OnPropertyChanged(nameof(TrendPayerVisibility));
                OnPropertyChanged(nameof(TrendPayerCountText));
                OnPropertyChanged(nameof(TrendPayerSummaryText));
            },
            nameof(PagedTrendPayerRows),
            nameof(CanGoToPreviousTrendPayerPage),
            nameof(CanGoToNextTrendPayerPage),
            nameof(TrendPayerPageText));

        _projectGrid.Load(projectDrilldownRows ?? Array.Empty<KpiProjectDrilldownRow>());
        _cashHistoryGrid.Load(cashHistoryRows ?? Array.Empty<KpiCashHistoryRow>());
        _cashAccountsGrid.Load(cashAccountRows ?? Array.Empty<KpiCashAccountRow>());
        _arOutstandingGrid.Load(arOutstandingRows ?? Array.Empty<KpiArOutstandingRow>());
        _arInvoiceGrid.Load(arInvoiceRows ?? Array.Empty<KpiArInvoiceRow>());
        _wipGrid.Load(wipUnbilledRows ?? Array.Empty<KpiWipUnbilledRow>());
        _backlogGrid.Load(backlogRows ?? Array.Empty<KpiBacklogRow>());
        _billingsGrid.Load(billingsRows ?? Array.Empty<KpiBillingsRow>());
        _budgetBurnGrid.Load(budgetBurnRows ?? Array.Empty<KpiBudgetBurnRow>());
        _utilizationGrid.Load(utilizationRows ?? Array.Empty<KpiUtilizationRow>());
        _deliveryRiskGrid.Load(deliveryRiskRows ?? Array.Empty<KpiDeliveryRiskRow>());
        _trendPayerGrid.Load(trendPayerRows ?? Array.Empty<TrendPayerRow>());
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
        if (_projectGrid.RowCount > 0)
        {
            lines.Add("");
            lines.Add("Project breakdown:");
            foreach (var row in _projectGrid.AllRows)
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
        if (_cashHistoryGrid.RowCount > 0)
        {
            lines.Add("");
            lines.Add("Cash history:");
            foreach (var row in _cashHistoryGrid.AllRows)
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
        if (_cashAccountsGrid.RowCount > 0)
        {
            lines.Add("");
            lines.Add("Cash accounts:");
            foreach (var row in _cashAccountsGrid.AllRows)
            {
                lines.Add(string.Format(
                    CultureInfo.CurrentCulture,
                    "- {0} {1} ({2}): {3:C0}",
                    row.Company,
                    row.Account,
                    row.Org,
                    row.Balance));
            }
        }
        if (_trendPayerGrid.RowCount > 0)
        {
            lines.Add("");
            lines.Add("Payer breakdown:");
            foreach (var row in _trendPayerGrid.AllRows.Take(25))
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
            (MetricKind.Kpi, "Liquidity (Cash + AR)") => "Exec_Liquidity",
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
        _projectGrid.PrevPage();
    }

    public void GoToNextProjectPage()
    {
        _projectGrid.NextPage();
    }

    public void GoToPreviousArPage()
    {
        _arOutstandingGrid.PrevPage();
    }

    public void GoToNextArPage()
    {
        _arOutstandingGrid.NextPage();
    }

    public void GoToPreviousArInvoicePage()
    {
        _arInvoiceGrid.PrevPage();
    }

    public void GoToNextArInvoicePage()
    {
        _arInvoiceGrid.NextPage();
    }

    public void GoToPreviousWipPage()
    {
        _wipGrid.PrevPage();
    }

    public void GoToNextWipPage()
    {
        _wipGrid.NextPage();
    }

    public void GoToPreviousBacklogPage()
    {
        _backlogGrid.PrevPage();
    }

    public void GoToNextBacklogPage()
    {
        _backlogGrid.NextPage();
    }

    public void GoToPreviousBillingsPage()
    {
        _billingsGrid.PrevPage();
    }

    public void GoToNextBillingsPage()
    {
        _billingsGrid.NextPage();
    }

    public void GoToPreviousBudgetBurnPage()
    {
        _budgetBurnGrid.PrevPage();
    }

    public void GoToNextBudgetBurnPage()
    {
        _budgetBurnGrid.NextPage();
    }

    public void GoToPreviousUtilizationPage()
    {
        _utilizationGrid.PrevPage();
    }

    public void GoToNextUtilizationPage()
    {
        _utilizationGrid.NextPage();
    }

    public void GoToPreviousDeliveryRiskPage()
    {
        _deliveryRiskGrid.PrevPage();
    }

    public void GoToNextDeliveryRiskPage()
    {
        _deliveryRiskGrid.NextPage();
    }

    public void GoToPreviousTrendPayerPage()
    {
        _trendPayerGrid.PrevPage();
    }

    public void GoToNextTrendPayerPage()
    {
        _trendPayerGrid.NextPage();
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
