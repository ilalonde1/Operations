#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ClosedXML.Excel;
using Kor.Operations.Core;
using Kor.Operations.Data;
using Kor.Operations.App.Options;
namespace Kor.Operations.Financials
{
    public sealed class FinancialsViewModel : ObservableObject, Kor.Operations.Services.IAiContextProvider
    {
        private readonly FinancialsService _svc;
        private readonly SqlFinancialPortfolioSnapshotStore _portfolioStore;
        internal DeltekOdbcOptions? _odbcOptions;
        internal FinancialsOptions? _financialsOptions;
        public ExecutiveSummaryViewModel ExecutiveSummary { get; }
        public BillingManagerReportViewModel BillingManagerReport { get; }
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
        private bool _showWatchlistOnly = true;
        private double _engRate = 474;
        private double _draftRate = 655;
        private double _targetBilling = 185;
        private bool _useTargetRateBudget;
        private int _watchlistCount;
        private int _allActiveCount;

        public ObservableCollection<FinancialsProjectRow> Rows { get; } = new();
        public ObservableCollection<UtilizationRow> UtilizationRows { get; } = new();
        public ObservableCollection<DraftUtilizationRow> DraftUtilizationRows { get; } = new();
        public ObservableCollection<ClientRollupRow> ClientRows { get; } = new();
        public ICollectionView ClientView { get; }
        private string _clientSearchText = "";
        private ClientRollupRow? _selectedClient;
        // Selected project row in the Active Projects DataGrid — TwoWay-bound
        // from FinancialsWindow.xaml so the AI bar's BuildLocalContext can
        // emit "user is asking about THIS project" when an engineer clicks
        // a row and asks a follow-up. Null when nothing is selected.
        private FinancialsProjectRow? _selectedProject;
        public string ClientSearchText
        {
            get => _clientSearchText;
            set { if (SetField(ref _clientSearchText, value)) { ClientView.Refresh(); OnPropertyChanged(nameof(ClientCountDisplay)); } }
        }
        public ClientRollupRow? SelectedClient
        {
            get => _selectedClient;
            set { if (SetField(ref _selectedClient, value)) OnPropertyChanged(nameof(SelectedClientHasValue)); }
        }
        public bool SelectedClientHasValue => _selectedClient != null;

        public FinancialsProjectRow? SelectedProject
        {
            get => _selectedProject;
            set { if (SetField(ref _selectedProject, value)) OnPropertyChanged(nameof(SelectedProjectHasValue)); }
        }
        public bool SelectedProjectHasValue => _selectedProject != null;
        public string ClientCountDisplay
        {
            get
            {
                var visible = ClientView?.Cast<object>().Count() ?? 0;
                return $"{visible:N0} clients";
            }
        }

        // ── Portfolio-level KPIs (computed from full ClientRows, not the filtered view) ──
        public int ClientsTotalCount => ClientRows.Count;
        public int ClientsRepeatCount => ClientRows.Count(c => c.IsRepeatClient);
        public double ClientsRepeatPercent => ClientsTotalCount > 0
            ? (double)ClientsRepeatCount / ClientsTotalCount : 0;
        public double ClientsLifetimeFee => ClientRows.Sum(c => c.LifetimeFee);
        public double ClientsLifetimeBilled => ClientRows.Sum(c => c.LifetimeBilled);
        public double ClientsOutstanding => ClientRows.Sum(c => c.Outstanding);
        public double ClientsOutstanding90Plus => ClientRows.Sum(c => c.Outstanding90Plus);
        // 2026-05 follow-up: Active vs In-Collections AR split for the headline
        // tile — leadership wanted the legal-limbo bucket called out separately
        // from actively-recoverable AR rather than mashed into a single number.
        // Falls through to ClientsOutstanding / 90+ when MCP isn't configured.
        public double ClientsOutstandingInCollections => ClientRows.Sum(c => c.OutstandingInCollections);
        public double ClientsOutstandingRegular => ClientRows.Sum(c => c.OutstandingRegular);
        public double ClientsOutstanding90PlusInCollections => ClientRows.Sum(c => c.Outstanding90PlusInCollections);
        public double ClientsOutstanding90PlusRegular => ClientRows.Sum(c => c.Outstanding90PlusRegular);
        public bool   ClientsHasCollectionsExposure => ClientsOutstandingInCollections > 0.5;
        public int ClientsAtRiskCount => ClientRows.Count(c => c.HasArRisk);
        public int ClientsColdCount => ClientRows.Count(c => c.IsCold);
        public string TopClientName
        {
            get
            {
                var top = ClientRows
                    .Where(c => !string.IsNullOrEmpty(c.ClientName) && c.ClientName != "(unknown)")
                    .OrderByDescending(c => c.LifetimeFee)
                    .FirstOrDefault();
                return top?.ClientName ?? "—";
            }
        }
        public double TopClientFee
        {
            get
            {
                var top = ClientRows
                    .Where(c => !string.IsNullOrEmpty(c.ClientName) && c.ClientName != "(unknown)")
                    .OrderByDescending(c => c.LifetimeFee)
                    .FirstOrDefault();
                return top?.LifetimeFee ?? 0;
            }
        }
        public double TopClientPercentOfTotal => ClientsLifetimeFee > 0
            ? TopClientFee / ClientsLifetimeFee : 0;

        // ── Revenue Forecast section ──
        public ObservableCollection<RevenueMonthRow> ForecastTimeline { get; } = new();
        public double ForecastTrailing12 { get; private set; }
        public double ForecastTrailing3 { get; private set; }
        public double ForecastBacklog { get; private set; }
        public double ForecastNext3 { get; private set; }
        public double ForecastNext6 { get; private set; }
        public double ForecastNext12 { get; private set; }
        public double ForecastMonthsOfBacklog { get; private set; }
        public double ForecastChartMaxValue { get; private set; }
        public string ForecastBaselineDisplay { get; private set; } = "—";

        /// <summary>
        /// Recompute the forecast timeline (24 months actual + 12 months projected).
        /// Model (median-based — robust to lumpy BilledFee invoicing):
        ///   1. Detect partial months — recent months with revenue &lt; 40% of historical median are
        ///      treated as still-being-posted (typical 60-90 day Deltek billing lag) and excluded
        ///      from baseline/trend/seasonal computation. Still shown in the chart with visual
        ///      distinction.
        ///   2. Baseline = (3 × trailing-6mo median + 1 × trailing-12mo median) / 4 of COMPLETE
        ///      months only. Median is robust to single-month invoicing spikes — a $200K invoice
        ///      can't dominate the baseline.
        ///   3. Trend slope = Theil-Sen median slope on the trailing 12 complete months — robust
        ///      regression. Clamped to ±15% of baseline so a noisy window can't blow up the
        ///      projection.
        ///   4. Seasonal index = each calendar month's median ÷ overall median, damped 50%
        ///      toward 1.0; requires at least 2 observations of that month or defaults to 1.0.
        ///   5. forecast[i] = (baseline + slope × i) × seasonal_index[calendar_month]. NO backlog
        ///      cap — the headline forecast assumes the firm continues to win new work at historical
        ///      pace. The "Months of Runway" KPI is the separate no-new-wins warning.
        /// </summary>
        private void RecomputeForecast(IReadOnlyList<RevenueMonthRow> history, IReadOnlyList<FinancialsProjectRow> activeRows, double usdToCadRate)
        {
            ForecastTimeline.Clear();

            if (history == null || history.Count == 0)
            {
                ForecastTrailing12 = 0;
                ForecastTrailing3 = 0;
                ForecastBacklog = 0;
                ForecastNext3 = 0;
                ForecastNext6 = 0;
                ForecastNext12 = 0;
                ForecastMonthsOfBacklog = 0;
                ForecastChartMaxValue = 0;
                ForecastBaselineDisplay = "—";
                NotifyForecastProperties();
                return;
            }

            // 1) Detect partial months in the trailing window.
            //    Reference median uses non-zero months OLDER than the most recent 4 (skip the
            //    potentially-partial recent window so partials don't drag the reference down).
            var ordered = history.OrderBy(h => h.MonthStart).ToList();
            var n = ordered.Count;

            // Clear IsPartial from any prior refresh — these RevenueMonthRow instances
            // are shared with snap.RevenueHistory and would otherwise persist a stale flag
            // even after Deltek catches up and the month is fully posted.
            foreach (var m in ordered)
                m.IsPartial = false;

            var stableWindow = ordered
                .Take(Math.Max(0, n - 4))
                .Where(m => m.Revenue > 0)
                .Select(m => m.Revenue)
                .OrderBy(v => v)
                .ToList();
            var refMedian = stableWindow.Count > 0
                ? stableWindow[stableWindow.Count / 2]
                : 0;

            // Walk backward from the end. Mark months as partial until we hit a complete month.
            // Only check up to 4 months back — older partials are unlikely.
            if (refMedian > 0)
            {
                var partialThreshold = refMedian * 0.40;
                for (int i = n - 1; i >= Math.Max(0, n - 4); i--)
                {
                    if (ordered[i].Revenue < partialThreshold)
                        ordered[i].IsPartial = true;
                    else
                        break;
                }
            }

            // 2) Add historical actuals to the timeline (preserves IsPartial flags).
            foreach (var m in ordered)
                ForecastTimeline.Add(m);

            // 3) Use only COMPLETE months for all baseline/trend/seasonal computation.
            var completeMonths = ordered.Where(m => !m.IsPartial).ToList();
            if (completeMonths.Count == 0)
            {
                ForecastTrailing12 = 0;
                ForecastTrailing3 = 0;
                ForecastBacklog = activeRows?.Sum(r =>
                {
                    var fx = OrgFx.IsUsaOrg(r.Org) ? usdToCadRate : 1.0;
                    return Math.Max(0, (r.TotalFee - r.FeeBilledWithUnposted) * fx);
                }) ?? 0;
                ForecastNext3 = 0;
                ForecastNext6 = 0;
                ForecastNext12 = 0;
                ForecastMonthsOfBacklog = 0;
                ForecastChartMaxValue = ForecastTimeline.Count > 0 ? ForecastTimeline.Max(m => m.Revenue) : 0;
                ForecastBaselineDisplay = "(insufficient complete history)";
                NotifyForecastProperties();
                return;
            }

            var trailing12Months = completeMonths.TakeLast(12).ToList();
            var trailing6Months = completeMonths.TakeLast(6).ToList();
            var trailing3Months = completeMonths.TakeLast(3).ToList();

            ForecastTrailing12 = trailing12Months.Sum(m => m.Revenue);
            ForecastTrailing3 = trailing3Months.Sum(m => m.Revenue);
            // Median is robust to single-month invoicing spikes (BilledFee is lumpy).
            var trailing6Pace = trailing6Months.Count > 0 ? Median(trailing6Months.Select(m => m.Revenue)) : 0;
            var trailing12Pace = trailing12Months.Count > 0 ? Median(trailing12Months.Select(m => m.Revenue)) : 0;

            // 4) Backlog = unbilled fee on active projects, FX-converted to CAD-equivalent
            //    so it's comparable with the trailing-12 history (also CAD-equivalent).
            //    Subtracts FeeBilledWithUnposted (posted PRSummaryMain + open AR not yet
            //    posted) — billed reality. Posted-only would overstate runway during the
            //    GL/PRSummary posting lag.
            ForecastBacklog = activeRows?.Sum(r =>
            {
                var fx = OrgFx.IsUsaOrg(r.Org) ? usdToCadRate : 1.0;
                return Math.Max(0, (r.TotalFee - r.FeeBilledWithUnposted) * fx);
            }) ?? 0;

            // 5) Blended baseline weighted toward recent pace.
            var baseline = (3.0 * trailing6Pace + 1.0 * trailing12Pace) / 4.0;

            // Theil-Sen median slope: median of all pairwise (y_j - y_i)/(j - i) — robust to outliers.
            var slope = 0.0;
            if (trailing12Months.Count >= 6)
            {
                var values = trailing12Months.Select(m => m.Revenue).ToArray();
                var pairwise = new List<double>();
                for (int j = 1; j < values.Length; j++)
                {
                    for (int i = 0; i < j; i++)
                    {
                        pairwise.Add((values[j] - values[i]) / (j - i));
                    }
                }
                slope = pairwise.Count > 0 ? Median(pairwise) : 0;
                var maxSlope = Math.Abs(baseline) * 0.15;
                slope = Math.Max(-maxSlope, Math.Min(maxSlope, slope));
            }

            // 7) Seasonal index per calendar month from COMPLETE months only.
            var seasonalIndex = new double[13];
            for (int i = 0; i < 13; i++) seasonalIndex[i] = 1.0;
            var nonZeroComplete = completeMonths.Where(m => m.Revenue > 0).ToList();
            if (nonZeroComplete.Count >= 6)
            {
                var overallPace = Median(nonZeroComplete.Select(m => m.Revenue));
                if (overallPace > 0)
                {
                    for (int month = 1; month <= 12; month++)
                    {
                        var sameMonth = nonZeroComplete.Where(m => m.MonthStart.Month == month).ToList();
                        if (sameMonth.Count >= 2)
                        {
                            var monthPace = Median(sameMonth.Select(m => m.Revenue));
                            var rawIdx = monthPace / overallPace;
                            seasonalIndex[month] = 0.5 + 0.5 * rawIdx; // damp 50% toward 1.0
                        }
                    }
                }
            }

            ForecastBaselineDisplay = baseline > 0
                ? (slope != 0 ? $"{baseline:C0}/mo · trend {(slope >= 0 ? "+" : "")}{slope:C0}/mo"
                              : $"{baseline:C0}/mo")
                : "—";

            // 8) Project 12 months forward starting from the month AFTER the most recent
            //    actual month in the chart (whether complete or partial). NO backlog cap —
            //    headline forecast assumes steady-state new business.
            var lastActualMonth = ordered[n - 1].MonthStart;
            ForecastNext3 = 0;
            ForecastNext6 = 0;
            ForecastNext12 = 0;

            for (int i = 1; i <= 12; i++)
            {
                var monthStart = lastActualMonth.AddMonths(i);
                var trended = baseline + slope * i;
                var seasonal = seasonalIndex[monthStart.Month];
                var projected = Math.Max(0, trended * seasonal);

                ForecastTimeline.Add(new RevenueMonthRow
                {
                    MonthStart = monthStart,
                    Revenue = projected,
                    IsActual = false,
                });
                if (i <= 3) ForecastNext3 += projected;
                if (i <= 6) ForecastNext6 += projected;
                ForecastNext12 += projected;
            }

            // Months-of-runway: backlog ÷ baseline. Independent "no-new-wins" warning.
            ForecastMonthsOfBacklog = Math.Abs(baseline) > AnalyticsThresholds.RoundingDollarFloor ? ForecastBacklog / baseline : 0;

            ForecastChartMaxValue = ForecastTimeline.Count > 0
                ? ForecastTimeline.Max(m => m.Revenue)
                : 0;

            NotifyForecastProperties();
        }

        private static double Median(IEnumerable<double> values)
        {
            if (values == null) return 0;
            var sorted = values.OrderBy(v => v).ToArray();
            var len = sorted.Length;
            if (len == 0) return 0;
            if (len % 2 == 1) return sorted[len / 2];
            return (sorted[len / 2 - 1] + sorted[len / 2]) / 2.0;
        }

        private void NotifyForecastProperties()
        {
            OnPropertyChanged(nameof(ForecastTrailing12));
            OnPropertyChanged(nameof(ForecastTrailing3));
            OnPropertyChanged(nameof(ForecastBacklog));
            OnPropertyChanged(nameof(ForecastNext3));
            OnPropertyChanged(nameof(ForecastNext6));
            OnPropertyChanged(nameof(ForecastNext12));
            OnPropertyChanged(nameof(ForecastMonthsOfBacklog));
            OnPropertyChanged(nameof(ForecastChartMaxValue));
            OnPropertyChanged(nameof(ForecastBaselineDisplay));
        }
        public ObservableCollection<PortfolioTrendPoint> PortfolioTrend { get; } = new();
        public ObservableCollection<string> UtilizationPmOptions { get; } = new();
        public ObservableCollection<string> UtilizationRiskOptions { get; } = new() { "All", "Over budget", "At risk", "Healthy" };
        public ICollectionView UtilizationView { get; }
        public ICollectionView DraftUtilizationView { get; }

        public bool ShowWatchlistOnly
        {
            get => _showWatchlistOnly;
            set { if (SetField(ref _showWatchlistOnly, value)) { OnPropertyChanged(nameof(IsWatchlistOnly)); OnPropertyChanged(nameof(IsAllActive)); } }
        }
        public bool IsWatchlistOnly => _showWatchlistOnly;
        public bool IsAllActive => !_showWatchlistOnly;
        public string WatchlistCountDisplay => _watchlistCount > 0 ? $"({_watchlistCount:N0})" : "";
        public string AllActiveCountDisplay => _allActiveCount > 0 ? $"({_allActiveCount:N0})" : "";

        public double EngRate { get => _engRate; set { if (SetField(ref _engRate, value)) OnPropertyChanged(nameof(CombinedRate)); } }
        public double DraftRate { get => _draftRate; set { if (SetField(ref _draftRate, value)) OnPropertyChanged(nameof(CombinedRate)); } }
        public double CombinedRate => (_engRate > 0 && _draftRate > 0) ? Math.Round(1.0 / (1.0 / _engRate + 1.0 / _draftRate), 0) : 0;
        public double TargetBilling { get => _targetBilling; set => SetField(ref _targetBilling, value); }
        public bool IsPeerBudgetMode { get => !_useTargetRateBudget; set { if (value) { _useTargetRateBudget = false; OnPropertyChanged(); OnPropertyChanged(nameof(IsTargetRateBudgetMode)); } } }
        public bool IsTargetRateBudgetMode { get => _useTargetRateBudget; set { if (value) { _useTargetRateBudget = true; OnPropertyChanged(); OnPropertyChanged(nameof(IsPeerBudgetMode)); } } }
        public Visibility BudgetModePillVisibility => Rows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public FinancialsHeadlineKpis Headline
        {
            get => _headline;
            private set { _headline = value; OnPropertyChanged(); }
        }

        public DateTime? MaxPostedPeriod { get; private set; }

        public string PostingLagBanner
        {
            get
            {
                if (!MaxPostedPeriod.HasValue)
                    return "";

                var postedMonth = new DateTime(MaxPostedPeriod.Value.Year, MaxPostedPeriod.Value.Month, 1);
                var currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                var monthLag = (currentMonth.Year - postedMonth.Year) * 12 + currentMonth.Month - postedMonth.Month;
                if (monthLag <= 1)
                    return "";

                return $"Deltek posted through {postedMonth.ToString("MMM yyyy", CultureInfo.CurrentCulture)} — figures may reflect a {monthLag}-month posting lag.";
            }
        }

        public Visibility PostingLagVisibility => string.IsNullOrEmpty(PostingLagBanner) ? Visibility.Collapsed : Visibility.Visible;

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
                var v = value < 0 ? 0 : (value > 5 ? 5 : value);
                if (_sectionIndex == v) return;
                _sectionIndex = v;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CommandCenterVisibility));
                OnPropertyChanged(nameof(ExecutiveSummaryVisibility));
                OnPropertyChanged(nameof(ProfitLossVisibility));
                OnPropertyChanged(nameof(BillingManagerVisibility));
                OnPropertyChanged(nameof(ClientsVisibility));
                OnPropertyChanged(nameof(ForecastVisibility));
                OnPropertyChanged(nameof(NonScrollableVisibility));
                OnPropertyChanged(nameof(IsCommandCenterSelected));
                OnPropertyChanged(nameof(IsExecutiveSummarySelected));
                OnPropertyChanged(nameof(IsProfitLossSelected));
                OnPropertyChanged(nameof(IsBillingManagerSelected));
                OnPropertyChanged(nameof(IsClientsSelected));
                OnPropertyChanged(nameof(IsForecastSelected));
            }
        }

        public bool IsCommandCenterSelected => SectionIndex == 0;
        public bool IsExecutiveSummarySelected => SectionIndex == 1;
        public bool IsProfitLossSelected => SectionIndex == 2;
        public bool IsBillingManagerSelected => SectionIndex == 3;
        public bool IsClientsSelected => SectionIndex == 4;
        public bool IsForecastSelected => SectionIndex == 5;
        public Visibility CommandCenterVisibility => SectionIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ExecutiveSummaryVisibility => SectionIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ProfitLossVisibility => SectionIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility BillingManagerVisibility => SectionIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ClientsVisibility => SectionIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ForecastVisibility => SectionIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        /// <summary>The outer ScrollViewer hides for sections that manage their own layout (Clients, Forecast).</summary>
        public Visibility NonScrollableVisibility => (SectionIndex == 4 || SectionIndex == 5) ? Visibility.Collapsed : Visibility.Visible;
        // Backward-compat alias for the old binding name on the ScrollViewer
        public Visibility NonClientsVisibility => NonScrollableVisibility;

        public bool CanRefresh => !_isLoading;
        public bool IsRefreshing => _isLoading;

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
        public int PortfolioWatchCount { get; private set; }
        public int PortfolioHighConfidenceCount { get; private set; }

        public double PortfolioCriticalPct { get; private set; }
        public double PortfolioAtRiskPct { get; private set; }
        public double PortfolioWatchPct { get; private set; }
        public double PortfolioHighConfidencePct { get; private set; }

        public double PortfolioRiskExposureFee { get; private set; }

        public FinancialsViewModel(
            FinancialsService svc,
            SqlFinancialPortfolioSnapshotStore portfolioStore,
            ExecutiveSummaryViewModel executiveSummary,
            BillingManagerReportViewModel billingManagerReport,
            DeltekOdbcOptions odbcOptions,
            FinancialsOptions financialsOptions)
        {
            _svc = svc ?? throw new ArgumentNullException(nameof(svc));
            _portfolioStore = portfolioStore ?? throw new ArgumentNullException(nameof(portfolioStore));
            ExecutiveSummary = executiveSummary ?? throw new ArgumentNullException(nameof(executiveSummary));
            BillingManagerReport = billingManagerReport ?? throw new ArgumentNullException(nameof(billingManagerReport));
            _odbcOptions = odbcOptions;
            _financialsOptions = financialsOptions;
            if (odbcOptions != null)
            {
                _engRate = odbcOptions.EngRate;
                _draftRate = odbcOptions.DraftRate;
                _targetBilling = odbcOptions.TargetBillingRate > 0 ? odbcOptions.TargetBillingRate : 185;
            }
            UtilizationView = CollectionViewSource.GetDefaultView(UtilizationRows);
            UtilizationView.Filter = UtilizationFilter;
            DraftUtilizationView = CollectionViewSource.GetDefaultView(DraftUtilizationRows);
            DraftUtilizationView.Filter = DraftUtilizationFilter;
            ClientView = CollectionViewSource.GetDefaultView(ClientRows);
            ClientView.Filter = ClientFilter;
            UtilizationPmOptions.Add("All");
        }

        private bool ClientFilter(object item)
        {
            if (item is not ClientRollupRow row) return false;
            var q = (_clientSearchText ?? "").Trim();
            if (q.Length == 0) return true;
            return row.ClientName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || row.ClientId.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
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
            OnPropertyChanged(nameof(IsRefreshing));
            OnPropertyChanged(nameof(StatusHint));
            OnPropertyChanged(nameof(CanExportUtilization));

            try
            {
                var snap = await _svc.GetSnapshotAsync(forceRefresh, ct, watchlistOnly: _showWatchlistOnly);

                Rows.Clear();
                foreach (var r in snap.Rows)
                    Rows.Add(r);
                UpdateScopeCounts(snap.Rows);
                ExecutiveSummary.SetScope(IsWatchlistOnly ? "Watchlist" : "All Active", Rows.Count);

                Headline = snap.Headline;
                MaxPostedPeriod = snap.MaxPostedPeriod;
                OnPropertyChanged(nameof(MaxPostedPeriod));
                OnPropertyChanged(nameof(PostingLagBanner));
                OnPropertyChanged(nameof(PostingLagVisibility));
                OnPropertyChanged(nameof(BudgetModePillVisibility));
                _lastRefreshed = snap.RefreshedAt;

                UtilizationRows.Clear();
                foreach (var r in snap.Rows)
                    UtilizationRows.Add(UtilizationRow.FromProject(r));
                DraftUtilizationRows.Clear();
                foreach (var r in snap.Rows)
                    DraftUtilizationRows.Add(DraftUtilizationRow.FromProject(r));

                ClientRows.Clear();
                foreach (var c in snap.ClientRollups)
                    ClientRows.Add(c);
                ClientView.Refresh();
                RecomputeForecast(snap.RevenueHistory, snap.Rows, snap.UsdToCadRate);
                OnPropertyChanged(nameof(ClientCountDisplay));
                OnPropertyChanged(nameof(ClientsTotalCount));
                OnPropertyChanged(nameof(ClientsRepeatCount));
                OnPropertyChanged(nameof(ClientsRepeatPercent));
                OnPropertyChanged(nameof(ClientsLifetimeFee));
                OnPropertyChanged(nameof(ClientsLifetimeBilled));
                OnPropertyChanged(nameof(ClientsOutstanding));
                OnPropertyChanged(nameof(ClientsOutstanding90Plus));
                OnPropertyChanged(nameof(ClientsOutstandingInCollections));
                OnPropertyChanged(nameof(ClientsOutstandingRegular));
                OnPropertyChanged(nameof(ClientsOutstanding90PlusInCollections));
                OnPropertyChanged(nameof(ClientsOutstanding90PlusRegular));
                OnPropertyChanged(nameof(ClientsHasCollectionsExposure));
                OnPropertyChanged(nameof(ClientsAtRiskCount));
                OnPropertyChanged(nameof(ClientsColdCount));
                OnPropertyChanged(nameof(TopClientName));
                OnPropertyChanged(nameof(TopClientFee));
                OnPropertyChanged(nameof(TopClientPercentOfTotal));

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

                _ = BillingManagerReport.RefreshAsync(snap, ct);

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
                catch (Exception fallbackEx)
                {
                    Serilog.Log.Warning(fallbackEx, "Executive Summary fallback refresh failed.");
                }
            }
            finally
            {
                _isLoading = false;
                OnPropertyChanged(nameof(CanRefresh));
                OnPropertyChanged(nameof(IsRefreshing));
                OnPropertyChanged(nameof(StatusHint));
                OnPropertyChanged(nameof(CanExportUtilization));
            }
        }

        private void UpdateScopeCounts(IReadOnlyList<FinancialsProjectRow> rows)
        {
            if (_showWatchlistOnly)
            {
                _watchlistCount = rows.Count;
            }
            else
            {
                _allActiveCount = rows.Count;
                _watchlistCount = rows.Count(r => r.IsOnHotlist);
            }

            OnPropertyChanged(nameof(WatchlistCountDisplay));
            OnPropertyChanged(nameof(AllActiveCountDisplay));
        }

        private void RecalcPortfolio()
        {
            var total = UtilizationRows.Count;
            if (total <= 0)
            {
                PortfolioCriticalCount = 0;
                PortfolioAtRiskCount = 0;
                PortfolioWatchCount = 0;
                PortfolioHighConfidenceCount = 0;

                PortfolioCriticalPct = 0.0;
                PortfolioAtRiskPct = 0.0;
                PortfolioWatchPct = 0.0;
                PortfolioHighConfidencePct = 0.0;
                PortfolioRiskExposureFee = 0.0;

                OnPropertyChanged(nameof(PortfolioCriticalCount));
                OnPropertyChanged(nameof(PortfolioAtRiskCount));
                OnPropertyChanged(nameof(PortfolioWatchCount));
                OnPropertyChanged(nameof(PortfolioHighConfidenceCount));
                OnPropertyChanged(nameof(PortfolioCriticalPct));
                OnPropertyChanged(nameof(PortfolioAtRiskPct));
                OnPropertyChanged(nameof(PortfolioWatchPct));
                OnPropertyChanged(nameof(PortfolioHighConfidencePct));
                OnPropertyChanged(nameof(PortfolioRiskExposureFee));
                return;
            }

            var critical = 0;
            var atRisk = 0;
            var watch = 0;
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
                    case DeliveryConfidenceLevel.Watch:
                        watch++;
                        break;
                    default:
                        high++;
                        break;
                }
            }

            PortfolioCriticalCount = critical;
            PortfolioAtRiskCount = atRisk;
            PortfolioWatchCount = watch;
            PortfolioHighConfidenceCount = high;

            PortfolioCriticalPct = (double)critical / total;
            PortfolioAtRiskPct = (double)atRisk / total;
            PortfolioWatchPct = (double)watch / total;
            PortfolioHighConfidencePct = (double)high / total;
            PortfolioRiskExposureFee = riskExposureFee;

            OnPropertyChanged(nameof(PortfolioCriticalCount));
            OnPropertyChanged(nameof(PortfolioAtRiskCount));
            OnPropertyChanged(nameof(PortfolioWatchCount));
            OnPropertyChanged(nameof(PortfolioHighConfidenceCount));
            OnPropertyChanged(nameof(PortfolioCriticalPct));
            OnPropertyChanged(nameof(PortfolioAtRiskPct));
            OnPropertyChanged(nameof(PortfolioWatchPct));
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
                // Healthy = High Confidence + Watch, Watch = At Risk, Critical = Critical.
                var healthy = PortfolioHighConfidenceCount + PortfolioWatchCount;
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

        // ── IAiContextProvider ──────────────────────────────────────────

        string Services.IAiContextProvider.ProviderName => "Financials (Active Projects)";
        bool Services.IAiContextProvider.HasData => Rows.Count > 0;

        string Services.IAiContextProvider.BuildContext()
        {
            var sb = new System.Text.StringBuilder();
            var headlineBilled = _headline.HasUnpostedBilling
                ? $"Billed: ${_headline.TotalFeeBilled:N0} posted ({_headline.PercentFeeUnbilled:P0} unbilled posted), ${_headline.TotalFeeBilledWithUnposted:N0} all-in including ${_headline.TotalUnpostedFeeBilled:N0} unposted invoicing in AR not yet posted to PRSummaryMain"
                : $"Billed: ${_headline.TotalFeeBilled:N0} ({_headline.PercentFeeUnbilled:P0} unbilled)";
            sb.AppendLine($"Active Portfolio: {Rows.Count} projects, Total Fee: ${_headline.TotalFees:N0}, " +
                $"{headlineBilled}, " +
                $"Hours Spent: {_headline.HoursSpent:N0}/{_headline.HoursBudgeted:N0} ({_headline.PercentHoursSpent:P0})");
            // Active vs In-Collections split — match the headline tile so AI
            // narrative + the dashboard agree. When there's no collections
            // exposure the call-out collapses to a single line.
            if (ClientsHasCollectionsExposure)
            {
                sb.AppendLine($"Active AR: ${ClientsOutstandingRegular:N0} (90+: ${ClientsOutstanding90PlusRegular:N0})");
                sb.AppendLine($"In Collections (legal limbo): ${ClientsOutstandingInCollections:N0} (90+: ${ClientsOutstanding90PlusInCollections:N0})");
            }
            else
            {
                sb.AppendLine($"Total Outstanding AR: ${ClientsOutstanding:N0}");
                sb.AppendLine($"Total 90+ AR: ${ClientsOutstanding90Plus:N0}");
            }
            sb.AppendLine($"Delivery Confidence: {PortfolioHighConfidencePct:P0} Healthy, {PortfolioWatchPct:P0} Watch, " +
                $"{PortfolioAtRiskPct:P0} At Risk, {PortfolioCriticalPct:P0} Critical");
            sb.AppendLine($"Risk Exposure Fee: ${PortfolioRiskExposureFee:N0}");
            var cashKpi = ExecutiveSummary.Kpis.FirstOrDefault(k => string.Equals(k.Title, "Cash Position", StringComparison.OrdinalIgnoreCase));
            if (cashKpi != null)
            {
                sb.AppendLine($"Cash Position: {cashKpi.ValueText} CAD-equivalent");
                if (!string.IsNullOrWhiteSpace(cashKpi.SubText))
                    sb.AppendLine($"Cash breakdown: {cashKpi.SubText}");
                if (cashKpi.CashAccountRows != null && cashKpi.CashAccountRows.Count > 0)
                {
                    sb.AppendLine("Cash accounts included:");
                    foreach (var account in cashKpi.CashAccountRows)
                    {
                        sb.AppendLine(string.Format(
                            System.Globalization.CultureInfo.CurrentCulture,
                            "  {0} {1} ({2}): {3:C0}",
                            account.Company,
                            account.Account,
                            account.Org,
                            account.Balance));
                    }
                }
            }
            sb.AppendLine();

            foreach (var r in Rows.Take(200))
            {
                sb.Append($"  {r.Wbs1} {r.Name} | PM: {r.Pm} | DM: {r.DraftingManager} | ");
                var rowBilled = r.HasUnpostedBilling
                    ? $"Billed: {r.PercentBilled:P0} posted, {r.PercentBilledWithUnposted:P0} all-in (+${r.UnpostedFeeBilled:N0} unposted)"
                    : $"Billed: {r.PercentBilled:P0}";
                sb.Append($"Fee: ${r.TotalFee:N0} | {rowBilled} | ");
                sb.Append($"Eng: {r.EngHrs:N0}/{r.EngBudget:N0} ({r.EngPercent:P0}) | ");
                sb.Append($"Draft: {r.DraftHrs:N0}/{r.DraftBudget:N0} ({r.DraftPercent:P0})");
                if (r.IsOnHotlist) sb.Append(" [HOTLIST]");
                sb.AppendLine();
            }

            if (ClientRows.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("--- CLIENT SUMMARY ---");
                foreach (var c in ClientRows.Take(30))
                {
                    var flags = "";
                    if (c.IsCold) flags += " [COLD]";
                    if (c.HasArRisk) flags += " [AR-RISK]";
                    if (c.IsRepeatClient) flags += " [REPEAT]";
                    var lastActivity = c.LastActivityDate.HasValue ? c.LastActivityDate.Value.ToString("yyyy-MM") : "n/a";
                    sb.AppendLine($"  {c.ClientName} | {c.ProjectCount} proj | ${c.LifetimeFee:N0} lifetime | " +
                        $"{c.ActiveProjectCount} active | last {lastActivity} | Out ${c.Outstanding:N0} | " +
                        $"90+ ${c.Outstanding90Plus:N0} | Tenure {c.YearsAsClient:N1}yr{flags}");
                }
            }

            // Methodology emission removed in Batch 92c — MCP tool descriptions
            // + system prompt carry the canonical methodology (LaborCodes 10-60
            // included, 70/80 excluded, etc.). Cached LLM-side.
            return sb.ToString();
        }

        // BuildLocalContext is "user is focused on THIS specific element" —
        // the row the engineer clicked before asking the question. Distinct
        // from BuildContext (full screen snapshot reached via the registered
        // builder). When both a project row AND a client row are highlighted,
        // emit both; when neither is, return empty (the builder's screen-level
        // BuildContext still gives Claude plenty to work with).
        //
        // Batch 64 introduced this — previously this method either returned
        // "" (pre-Batch 60, AI blind) or duplicated BuildContext (Batch 62,
        // worked but wasted tokens with no extra signal).
        string Services.IAiContextProvider.BuildLocalContext()
        {
            var sb = new System.Text.StringBuilder();
            var p = _selectedProject;
            if (p != null)
            {
                sb.AppendLine("Selected project (user is asking about this row):");
                sb.AppendLine($"  WBS1: {p.Wbs1}  Name: {p.Name}");
                sb.AppendLine($"  PM: {p.Pm}  Drafting Manager: {p.DraftingManager}  Phase: {p.Phase}");
                sb.AppendLine($"  Fee: ${p.TotalFee:N0}  (Fixed: ${p.Fee:N0}  Hourly: ${p.HourlyRevenue:N0})");
                var rowBilled = p.HasUnpostedBilling
                    ? $"posted ${p.FeeBilled:N0} ({p.PercentBilled:P0}); all-in ${p.FeeBilledWithUnposted:N0} ({p.PercentBilledWithUnposted:P0}) including ${p.UnpostedFeeBilled:N0} unposted in AR"
                    : $"${p.FeeBilled:N0} ({p.PercentBilled:P0})";
                sb.AppendLine($"  Billed: {rowBilled}");
                sb.AppendLine($"  Eng Hrs: {p.EngHrs:N0}/{p.EngBudget:N0} ({p.EngPercent:P0})  Draft Hrs: {p.DraftHrs:N0}/{p.DraftBudget:N0} ({p.DraftPercent:P0})");
                sb.AppendLine($"  Health: {p.HealthStatus}");
                sb.AppendLine($"  Direct Labor Cost: ${p.TotalDirectLaborCost:N0}  Sub Cost: ${p.SubconsultantCost:N0}");
                sb.AppendLine($"  Multiplier: {p.Multiplier:N2}  Margin: {p.Margin:P1}  Profit: ${p.ProfitDollars:N0}");
            }
            var c = _selectedClient;
            if (c != null)
            {
                if (sb.Length > 0) sb.AppendLine();
                sb.AppendLine("Selected client (user is asking about this client rollup):");
                sb.AppendLine($"  {c.ClientName}  ({c.ProjectCount} projects total, {c.ActiveProjectCount} active)");
                sb.AppendLine($"  Lifetime Fee: ${c.LifetimeFee:N0}  Billed: ${c.LifetimeBilledWithUnposted:N0}");
                sb.AppendLine($"  Lifetime Direct Labor Cost: ${c.LifetimeDirectLaborCost:N0}  Sub Cost: ${c.LifetimeSubconsultantCost:N0}");
                sb.AppendLine($"  Lifetime Multiplier: {c.LifetimeMultiplier:N2}  Profit: ${c.LifetimeProfitDollars:N0}");
                sb.AppendLine($"  Outstanding AR: ${c.Outstanding:N0}  90+: ${c.Outstanding90Plus:N0}  Tenure: {c.YearsAsClient:N1} years");
            }
            return sb.ToString();
        }
    }
}
