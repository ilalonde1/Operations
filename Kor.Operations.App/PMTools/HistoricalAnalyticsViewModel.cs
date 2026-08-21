#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Kor.Operations.Core;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    internal sealed class HistoricalAnalyticsViewModel : ObservableObject, Kor.Operations.Services.IAiContextProvider
    {
        private readonly List<HistoricalProjectRow> _allRows = new();
        private bool _isLoading;
        private string _errorMessage = "";

        // Filters
        private string _selectedStatus = "All";
        private static readonly HashSet<string> ActiveStatuses =
            new(StringComparer.OrdinalIgnoreCase) { "A", "ACTIVE" };
        private static readonly HashSet<string> _excludedEmployeeIds = BuildExcludedEmployeeIds();
        private string _selectedPm = "All";
        private string _selectedHoursFilter = "Has Hours + Fee";
        private string _selectedYear = "All";
        private string _selectedDraftingMgr = "All";
        private string _selectedConstructionType = "All";
        private string _selectedProjectCategory = "All";
        private string _selectedDraftingType = "All";
        private string _searchText = "";

        // View mode
        private string _viewMode = "Projects";

        // Selection
        private HistoricalProjectRow? _selectedRow;

        // Summary stats
        private int _visibleCount;
        private double _totalFee;
        private double _totalEngHrs;
        private double _totalDraftHrs;
        private double _weightedEngPct;
        private double _weightedDraftPct;
        private double _medianFeePerHr;
        private double _meanFeePerHr;
        private double _p25FeePerHr;
        private double _p75FeePerHr;
        private double _empMedianFeePerHr;
        private double _empP25FeePerHr;
        private double _empP75FeePerHr;
        private double _totalAllHrs;
        private double _weightedBillablePct;
        private double _weightedOverheadRatio;
        private double _totalSubCost;
        private double _meanEngDelta;
        private double _meanDraftDelta;
        private double _budgetAccuracyPct;
        private double _medianAbsError;
        private double _totalArOutstanding;
        private double _firmBillablePct;
        private FirmUtilizationStats? _firmUtilization;

        // Portfolio averages for detail card comparison
        private double _avgEngPct;
        private double _avgFeePerHr;
        private double _avgNetFeePerHr;
        private double _avgSubPct;
        private double _avgBillablePct;

        public BulkObservableCollection<HistoricalProjectRow> Rows { get; } = new();
        public BulkObservableCollection<PmPerformanceSummaryRow> PmSummaryRows { get; } = new();
        public BulkObservableCollection<FeeBandSummaryRow> FeeBandRows { get; } = new();
        public BulkObservableCollection<YearTrendRow> YearTrendRows { get; } = new();
        public BulkObservableCollection<ConstructionTypeSummaryRow> ConstructionTypeRows { get; } = new();
        public BulkObservableCollection<EmployeeSummaryRow> EmployeeSummaryRows { get; } = new();
        private List<EmployeeProjectHours> _employeeProjectHours = new();
        private List<EmployeeWeeklyHours> _employeeWeeklyHours = new();
        private List<EmployeeRate> _employeeRates = new();
        private App.Options.DeltekOdbcOptions? _opts;
        public ObservableCollection<string> ViewModeOptions { get; } = new() { "Projects", "PM Summary", "DM Summary", "Employee Summary", "Fee Bands", "Construction Type", "YoY Trend" };

        public ObservableCollection<string> StatusOptions { get; } = new() { "All", "Active", "Closed" };
        public ObservableCollection<string> HoursFilterOptions { get; } = new() { "All", "Has Hours", "Has Fee", "Has Hours + Fee", "Fee ≥ $25K", "Fee ≥ $25K + Hours" };
        public ObservableCollection<string> PmOptions { get; } = new() { "All" };
        public ObservableCollection<string> DraftingMgrOptions { get; } = new() { "All" };
        public BulkObservableCollection<PmPerformanceSummaryRow> DmSummaryRows { get; } = new();
        public ObservableCollection<string> YearOptions { get; } = new() { "All" };
        public ObservableCollection<string> ConstructionTypeOptions { get; } = new() { "All" };
        public ObservableCollection<string> ProjectCategoryOptions { get; } = new() { "All" };
        public ObservableCollection<string> DraftingTypeOptions { get; } = new() { "All" };

        public bool IsLoading { get => _isLoading; set { SetField(ref _isLoading, value); OnPropertyChanged(nameof(IsNotLoading)); } }
        public bool IsNotLoading => !_isLoading;
        public string ErrorMessage { get => _errorMessage; set => SetField(ref _errorMessage, value); }

        public string SelectedStatus
        {
            get => _selectedStatus;
            set { if (SetField(ref _selectedStatus, value)) ApplyFilter(); }
        }

        public string SelectedPm
        {
            get => _selectedPm;
            set { if (SetField(ref _selectedPm, value)) ApplyFilter(); }
        }

        public string SelectedHoursFilter
        {
            get => _selectedHoursFilter;
            set { if (SetField(ref _selectedHoursFilter, value)) ApplyFilter(); }
        }

        public string SelectedYear
        {
            get => _selectedYear;
            set { if (SetField(ref _selectedYear, value)) ApplyFilter(); }
        }

        public string SelectedDraftingMgr
        {
            get => _selectedDraftingMgr;
            set { if (SetField(ref _selectedDraftingMgr, value)) ApplyFilter(); }
        }

        public string SelectedConstructionType
        {
            get => _selectedConstructionType;
            set { if (SetField(ref _selectedConstructionType, value)) ApplyFilter(); }
        }

        public string SelectedProjectCategory
        {
            get => _selectedProjectCategory;
            set { if (SetField(ref _selectedProjectCategory, value)) ApplyFilter(); }
        }

        public string SelectedDraftingType
        {
            get => _selectedDraftingType;
            set { if (SetField(ref _selectedDraftingType, value)) ApplyFilter(); }
        }

        public string SearchText
        {
            get => _searchText;
            set { if (SetField(ref _searchText, value)) ApplyFilter(); }
        }

        public string ViewMode
        {
            get => _viewMode;
            set { if (SetField(ref _viewMode, value)) { OnPropertyChanged(nameof(IsProjectView)); OnPropertyChanged(nameof(IsPmSummaryView)); OnPropertyChanged(nameof(IsDmSummaryView)); OnPropertyChanged(nameof(IsEmployeeSummaryView)); OnPropertyChanged(nameof(IsFeeBandView)); OnPropertyChanged(nameof(IsConstructionTypeView)); OnPropertyChanged(nameof(IsYoYTrendView)); OnPropertyChanged(nameof(MedianFeePerHr)); OnPropertyChanged(nameof(FeePerHrComparisonRate)); OnPropertyChanged(nameof(FeePerHrComparisonTooltip)); OnPropertyChanged(nameof(P25FeePerHr)); OnPropertyChanged(nameof(P75FeePerHr)); OnPropertyChanged(nameof(KpiCountLabel)); OnPropertyChanged(nameof(KpiCount)); OnPropertyChanged(nameof(KpiCountUnit)); } }
        }

        public bool IsProjectView => _viewMode == "Projects";
        public bool IsPmSummaryView => _viewMode == "PM Summary";
        public bool IsDmSummaryView => _viewMode == "DM Summary";
        public bool IsEmployeeSummaryView => _viewMode == "Employee Summary";
        public bool IsFeeBandView => _viewMode == "Fee Bands";
        public bool IsConstructionTypeView => _viewMode == "Construction Type";
        public bool IsYoYTrendView => _viewMode == "YoY Trend";

        public string KpiCountLabel => _viewMode switch
        {
            "Employee Summary" => "EMPLOYEES SHOWN",
            "PM Summary" => "PMs SHOWN",
            "DM Summary" => "DMs SHOWN",
            _ => "PROJECTS SHOWN"
        };

        public int KpiCount => _viewMode switch
        {
            "Employee Summary" => EmployeeSummaryRows.Count,
            "PM Summary" => PmSummaryRows.Count,
            "DM Summary" => DmSummaryRows.Count,
            _ => VisibleCount
        };

        public string KpiCountUnit => _viewMode switch
        {
            "Employee Summary" => "employees ·",
            "PM Summary" => "PMs ·",
            "DM Summary" => "DMs ·",
            _ => "projects ·"
        };

        public HistoricalProjectRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetField(ref _selectedRow, value)) return;
                OnPropertyChanged(nameof(HasSelection));
                RecomputeSimilarProjects();
            }
        }

        public bool HasSelection => _selectedRow != null;

        // Summary detail — generic key-value pairs for non-project views
        private string _detailTitle = "";
        private string _detailSubtitle = "";
        public string DetailTitle { get => _detailTitle; set => SetField(ref _detailTitle, value); }
        public string DetailSubtitle { get => _detailSubtitle; set => SetField(ref _detailSubtitle, value); }
        public BulkObservableCollection<DetailMetric> DetailMetrics { get; } = new();

        // Similar projects — peer matching
        public BulkObservableCollection<HistoricalProjectRow> SimilarProjects { get; } = new();
        private int _peerCount;
        private double _peerMedianEngHrs;
        private double _peerMedianDraftHrs;
        private double _peerMedianTotalHrs;
        private double _peerMedianFeePerHr;
        public int PeerCount { get => _peerCount; private set => SetField(ref _peerCount, value); }
        public double PeerMedianEngHrs { get => _peerMedianEngHrs; private set => SetField(ref _peerMedianEngHrs, value); }
        public double PeerMedianDraftHrs { get => _peerMedianDraftHrs; private set => SetField(ref _peerMedianDraftHrs, value); }
        public double PeerMedianTotalHrs { get => _peerMedianTotalHrs; private set => SetField(ref _peerMedianTotalHrs, value); }
        public double PeerMedianFeePerHr { get => _peerMedianFeePerHr; private set => SetField(ref _peerMedianFeePerHr, value); }

        // Summary stats
        public int VisibleCount { get => _visibleCount; private set { if (SetField(ref _visibleCount, value)) OnPropertyChanged(nameof(KpiCount)); } }
        public double TotalFee { get => _totalFee; private set => SetField(ref _totalFee, value); }
        public double TotalEngHrs { get => _totalEngHrs; private set => SetField(ref _totalEngHrs, value); }
        public double TotalDraftHrs { get => _totalDraftHrs; private set => SetField(ref _totalDraftHrs, value); }
        public double WeightedEngPct { get => _weightedEngPct; private set => SetField(ref _weightedEngPct, value); }
        public double WeightedDraftPct { get => _weightedDraftPct; private set => SetField(ref _weightedDraftPct, value); }
        public double MedianFeePerHr
        {
            get => IsEmployeeSummaryView ? _empMedianFeePerHr : _medianFeePerHr;
            private set
            {
                if (SetField(ref _medianFeePerHr, value))
                {
                    OnPropertyChanged(nameof(FeePerHrComparisonRate));
                    OnPropertyChanged(nameof(FeePerHrComparisonTooltip));
                }
            }
        }
        public double MeanFeePerHr { get => _meanFeePerHr; private set => SetField(ref _meanFeePerHr, value); }
        public double P25FeePerHr { get => IsEmployeeSummaryView ? _empP25FeePerHr : _p25FeePerHr; private set => SetField(ref _p25FeePerHr, value); }
        public double P75FeePerHr { get => IsEmployeeSummaryView ? _empP75FeePerHr : _p75FeePerHr; private set => SetField(ref _p75FeePerHr, value); }
        public double TotalAllHrs { get => _totalAllHrs; private set => SetField(ref _totalAllHrs, value); }
        public double WeightedBillablePct { get => _weightedBillablePct; private set => SetField(ref _weightedBillablePct, value); }
        public double WeightedOverheadRatio { get => _weightedOverheadRatio; private set => SetField(ref _weightedOverheadRatio, value); }
        public double TotalSubCost { get => _totalSubCost; private set => SetField(ref _totalSubCost, value); }
        public double MeanEngDelta { get => _meanEngDelta; private set => SetField(ref _meanEngDelta, value); }
        public double MeanDraftDelta { get => _meanDraftDelta; private set => SetField(ref _meanDraftDelta, value); }
        public double BudgetAccuracyPct { get => _budgetAccuracyPct; private set => SetField(ref _budgetAccuracyPct, value); }
        public double MedianAbsError { get => _medianAbsError; private set => SetField(ref _medianAbsError, value); }
        public double TotalArOutstanding { get => _totalArOutstanding; private set => SetField(ref _totalArOutstanding, value); }
        public double FirmBillablePct { get => _firmBillablePct; private set => SetField(ref _firmBillablePct, value); }

        // Portfolio averages (for detail card "vs portfolio" comparisons)
        public double AvgEngPct { get => _avgEngPct; private set => SetField(ref _avgEngPct, value); }
        public double AvgFeePerHr { get => _avgFeePerHr; private set => SetField(ref _avgFeePerHr, value); }
        public double AvgNetFeePerHr { get => _avgNetFeePerHr; private set => SetField(ref _avgNetFeePerHr, value); }
        public double AvgSubPct { get => _avgSubPct; private set => SetField(ref _avgSubPct, value); }
        public double AvgBillablePct { get => _avgBillablePct; private set => SetField(ref _avgBillablePct, value); }
        public double FeePerHrComparisonRate => MedianFeePerHr;
        public string FeePerHrComparisonTooltip => HistoricalAnalyticsTooltipText.FeePerHourComparison(FeePerHrComparisonRate);

        public int LoadedCount => _allRows.Count;
        internal IReadOnlyList<HistoricalProjectRow> AllRows => _allRows;
        internal IReadOnlyList<EmployeeProjectHours> EmployeeProjectHoursList => _employeeProjectHours;

        public void SetPmSummaryDetail(PmPerformanceSummaryRow? row, string role = "PM")
        {
            if (row == null) { DetailMetrics.ReplaceAll(Array.Empty<DetailMetric>()); DetailTitle = ""; DetailSubtitle = ""; return; }
            DetailTitle = row.Pm;
            DetailSubtitle = role;
            DetailMetrics.ReplaceAll(new[]
            {
                DetailMetric.Header("PORTFOLIO", "#2563EB"),
                new DetailMetric("Projects", row.ProjectCount.ToString(), "Number of projects managed"),
                new DetailMetric("Total Fee", row.TotalFee.ToString("$#,##0"), "Sum of all project fees"),
                new DetailMetric("% Billed", row.WeightedPctBilled.ToString("P0"), "How much of the total fee has been invoiced"),

                DetailMetric.Header("HOURS", "#16A34A"),
                new DetailMetric("Engineering Hours", row.TotalEngHrs.ToString("N0"), "Total engineering + checking hours"),
                new DetailMetric("Drafting Hours", row.TotalDraftHrs.ToString("N0"), "Total drafting hours"),
                new DetailMetric("Eng / Draft Split", $"{row.EngPct:P0} / {row.DraftPct:P0}", "Production time split"),
                new DetailMetric("Fee Per Hour", row.AvgFeePerHr.ToString("$#,##0"), "Full project fee ÷ production hours. Not split by who did the work."),

                DetailMetric.Header("BILLING VELOCITY", "#0891B2"),
                new DetailMetric("Avg Months to First Bill", row.AvgMonthsToFirstBill > 0 ? $"{row.AvgMonthsToFirstBill:N1} mo" : "—", "Average months from project open to first revenue recognition"),
                new DetailMetric("% Billed Within 6 Months", row.PctBilledWithin6Months.ToString("P0"), "Portion of total fee billed within first 6 months of opening"),

                DetailMetric.Header("SUBCONSULTANTS & AR", "#EA580C"),
                new DetailMetric("Subconsultant %", row.SubPctOfFee.ToString("P0"), "What % of fee went to outside firms"),
                new DetailMetric("Internal Fee/Hr", row.InternalFeePerHr.ToString("$#,##0"), "Fee minus sub costs ÷ production hours. True internal revenue rate."),
                new DetailMetric("Subconsultant Cost", row.TotalSubCost.ToString("$#,##0"), "Total paid to subconsultants"),
                new DetailMetric("AR Outstanding", row.TotalArOutstanding.ToString("$#,##0"), "Total unpaid invoices"),
                new DetailMetric("AR 90+ Days", row.TotalAr90Plus.ToString("$#,##0"), "Invoices over 90 days overdue"),

                DetailMetric.Header("CLIENT RETENTION", "#059669"),
                new DetailMetric("Unique Clients", row.UniqueClients.ToString(), "Distinct clients across all projects"),
                new DetailMetric("Repeat Clients", row.RepeatClients.ToString(), "Clients with 2+ projects"),
                new DetailMetric("Repeat Rate", row.RepeatRate.ToString("P0"), "% of clients who came back"),

                DetailMetric.Header("BUDGET ACCURACY", "#7C3AED"),
                new DetailMetric("Avg Eng Delta", row.AvgEngDelta.ToString("N0") + " hrs", "Avg estimated − actual eng hours. Positive = overestimate."),
                new DetailMetric("Avg Draft Delta", row.AvgDraftDelta.ToString("N0") + " hrs", "Avg estimated − actual draft hours"),

                DetailMetric.Header($"PERFORMANCE GRADE: {row.PerformanceGrade}", row.PerformanceColor),
                new DetailMetric("Delivery Health", $"{row.DeliveryHealthScore:N0}/100", $"% of projects NOT over budget (eng hrs ≤ estimated × {AnalyticsThresholds.OverBudgetFactor}). Weight: 30%."),
                new DetailMetric("Estimation Accuracy", $"{row.EstimationAccuracyScore:N0}/100", "Budget accuracy percentile — lower |delta| = higher score. Weight: 30%."),
                new DetailMetric("Revenue Efficiency", $"{row.RevenueEfficiencyScore:N0}/100", "Fee/Hr percentile rank vs peers. Weight: 20%."),
                new DetailMetric("AR Management", $"{row.ArManagementScore:N0}/100", "% of AR NOT 90+ days overdue. Weight: 20%."),
            });
        }

        public async Task SetEmployeeSummaryDetail(EmployeeSummaryRow? row)
        {
            if (row == null) { DetailMetrics.ReplaceAll(Array.Empty<DetailMetric>()); DetailTitle = ""; DetailSubtitle = ""; return; }
            DetailTitle = row.EmployeeName;
            DetailSubtitle = row.PrimaryRole;

            // Load trend data from snapshots
            var trendMetrics = new List<DetailMetric>();
            try
            {
                var cs = Kor.Operations.Services.AppServices.Get<App.Options.DatabaseOptions>().KorTransmittalsDb;
                if (!string.IsNullOrWhiteSpace(cs))
                {
                    var store = new EmployeeScoreSnapshotStore(cs);
                    var snapshots = await store.LoadTrendAsync(row.EmployeeId);
                    if (snapshots.Count >= 2)
                    {
                        var newest = snapshots[snapshots.Count - 1];
                        var oldest = snapshots[0];
                        var delta = newest.ProductivityScore - oldest.ProductivityScore;
                        var arrow = delta > 3 ? "↑ Improving" : delta < -3 ? "↓ Declining" : "→ Stable";

                        trendMetrics.Add(new DetailMetric("Latest Quarter", $"{newest.ProductivityScore:N0} ({newest.ProductivityGrade})",
                            $"Q{((snapshots[snapshots.Count - 1].SnapshotDate.Month - 1) / 3) + 1} {newest.SnapshotDate:yyyy} — score from that quarter's hours only"));
                        trendMetrics.Add(new DetailMetric("Trajectory", arrow,
                            $"{oldest.ProductivityGrade} → {newest.ProductivityGrade} over {snapshots.Count} quarters ({delta:+0;-0;0} pts)"));

                        // Show last 8 quarters as grade sequence
                        var recent = snapshots.Count > 8 ? snapshots.Skip(snapshots.Count - 8).ToList() : snapshots;
                        var gradeSeq = string.Join(" → ", recent.Select(s => s.ProductivityGrade));
                        trendMetrics.Add(new DetailMetric("Recent Quarters", gradeSeq,
                            "Grade progression (most recent 8 quarters)"));

                        trendMetrics.Add(DetailMetric.Explanation(
                            "The Grade in the grid is computed from ALL historical data (cumulative career). " +
                            "Quarterly grades show performance in each individual quarter. " +
                            "These can differ — a strong recent quarter doesn't erase a weak cumulative track record, " +
                            "and vice versa.", "#F0F9FF"));
                    }
                    else
                    {
                        trendMetrics.Add(new DetailMetric("Status", "Collecting data — trend appears after 2 quarters",
                            "Snapshots save automatically each quarter when this view loads."));
                    }
                }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to load employee trend data."); }
            var metrics = new List<DetailMetric>
            {
                DetailMetric.Header("PROFILE", "#6366F1"),
                new DetailMetric("Tenure", row.TenureDisplay, row.HireDate.HasValue ? $"Hired {row.HireDate.Value:MMM yyyy}" : "From EMMain.TotalYearsWithThisFirm"),
                new DetailMetric("Workload Pattern", row.ConsistencyLabel, $"CV of hours across projects: {row.ConsistencyScore:N2}. Steady < 0.3, Variable < 0.6, Erratic ≥ 0.6"),

                DetailMetric.Header("WORKLOAD", "#2563EB"),
                new DetailMetric("Projects Worked On", row.ProjectCount.ToString(), "Number of different projects this person logged hours on"),
                new DetailMetric("Engineering Hours", row.TotalEngHrs.ToString("N0"), "Total engineering + checking hours logged"),
                new DetailMetric("Drafting Hours", row.TotalDraftHrs.ToString("N0"), "Total drafting hours logged"),
                new DetailMetric("Eng / Draft Split", $"{row.EngPct:P0} / {row.DraftPct:P0}", "Production time split between engineering and drafting"),
                new DetailMetric("Total All Hours", row.TotalAllHrs.ToString("N0"), "ALL hours including overhead, vacation, sick, admin"),
                new DetailMetric("Billable %", row.BillablePct.ToString("P0"), "Hours on real projects ÷ total hours. Overhead/vacation (99XXX) is non-billable."),

                DetailMetric.Header("REVENUE", "#16A34A"),
                new DetailMetric("Fee Attributed", row.AttributedFee.ToString("$#,##0"), "Fee credited proportionally — 30% of a project's hours = 30% of the fee"),
                new DetailMetric("Fee Per Hour", row.FeePerHr.ToString("$#,##0"), "Attributed fee ÷ production hours. Proportional, not full project fee."),
                new DetailMetric("Avg Project Fee", row.AvgProjectFee.ToString("$#,##0"), "Average fee of projects they worked on"),

                DetailMetric.Header($"CUMULATIVE GRADE: {row.ProductivityScore:N0} ({row.ProductivityGrade})", row.ProductivityColor),
                new DetailMetric("Billable Rate", $"{row.BillableRateScore:N0}/100", "% of total hours on real billable projects. Weight: 30%."),
                new DetailMetric("Efficiency", $"{row.EfficiencyScore:N0}/100", "Fee/Hr percentile rank vs portfolio. 50 = median. Weight: 40%."),
                new DetailMetric("Project Health", $"{row.ProjectHealthScore:N0}/100", "% of hours on projects NOT over budget. Weight: 30%."),

                DetailMetric.Header("PEER COMPARISON", "#7C3AED"),
                new DetailMetric("Primary Type", string.IsNullOrWhiteSpace(row.PrimaryConstructionType) ? "—" : row.PrimaryConstructionType, "Construction type where this person spends the most hours"),
                new DetailMetric("Fee/Hr", row.FeePerHr.ToString("$#,##0"), "This employee's fee per production hour"),
                new DetailMetric("Peer Median Fee/Hr", row.PeerCount >= 2 ? row.PeerGroupMedianFeePerHr.ToString("$#,##0") : "—", $"Median fee/hr of {row.PeerCount} employees on same type"),
                new DetailMetric("vs Peers", row.PeerCount >= 2 ? $"{row.VsPeerPct:N0}%" : "—", "This employee's fee/hr as % of peer median. >100% = above peers"),

                DetailMetric.Header("QUARTERLY TREND", "#0891B2"),
            };
            metrics.AddRange(trendMetrics);
            metrics.Add(DetailMetric.Explanation(
                "How scoring works:\n\n" +
                "The CUMULATIVE GRADE above uses ALL historical data — the employee's entire track record.\n\n" +
                "The QUARTERLY TREND shows performance in each individual quarter. " +
                "A strong recent quarter doesn't erase a weak track record, and vice versa.\n\n" +
                "Billable Rate (30%) — Time on billable projects vs overhead/admin.\n" +
                "Efficiency (40%) — Fee-per-hour vs peers. 50 = average.\n" +
                "Project Health (30%) — % of hours on projects within budget.",
                "#F0FDF4"));
            DetailMetrics.ReplaceAll(metrics);
        }

        public void SetFeeBandDetail(FeeBandSummaryRow? row)
        {
            if (row == null) { DetailMetrics.ReplaceAll(Array.Empty<DetailMetric>()); DetailTitle = ""; DetailSubtitle = ""; return; }
            DetailTitle = row.Band;
            DetailSubtitle = "Fee Band";
            DetailMetrics.ReplaceAll(new[]
            {
                DetailMetric.Header("PORTFOLIO", "#2563EB"),
                new DetailMetric("Projects", row.ProjectCount.ToString(), "Number of projects in this fee range"),
                new DetailMetric("Total Fee", row.TotalFee.ToString("$#,##0"), "Sum of all project fees in this band"),

                DetailMetric.Header("RATES", "#16A34A"),
                new DetailMetric("Fee Per Hour", row.AvgFeePerHr.ToString("$#,##0"), "Fee per production hour"),
                new DetailMetric("Net Fee Per Hour", row.AvgNetFeePerHr.ToString("$#,##0"), "Fee minus subconsultants per hour — the real rate"),
                new DetailMetric("Median Fee Per Hour", row.MedianFeePerHr.ToString("$#,##0"), "Typical $/hr — less affected by outliers"),
                new DetailMetric("Eng / Draft Split", $"{row.WeightedEngPct:P0} / {(1 - row.WeightedEngPct):P0}", "Production time split"),
                new DetailMetric("Subconsultant %", row.AvgSubPct.ToString("P0"), "What % of fee goes to outside firms"),

                DetailMetric.Header("COLLECTIONS & ACCURACY", "#EA580C"),
                new DetailMetric("AR Outstanding", row.TotalArOutstanding.ToString("$#,##0"), "Total unpaid invoices"),
                new DetailMetric("Budget Accuracy", row.BudgetAccuracyPct.ToString("P0"), "% of closed projects where total estimated hours (eng+draft) were within ±50% of actual production hours"),
                new DetailMetric("Accuracy Sample", row.ClosedProjectCount.ToString() + " projects", "Closed projects used for accuracy"),
            });
        }

        public void SetConstructionTypeDetail(ConstructionTypeSummaryRow? row)
        {
            if (row == null) { DetailMetrics.ReplaceAll(Array.Empty<DetailMetric>()); DetailTitle = ""; DetailSubtitle = ""; return; }
            DetailTitle = row.ConstructionType;
            DetailSubtitle = "Construction Type";
            DetailMetrics.ReplaceAll(new[]
            {
                DetailMetric.Header("PORTFOLIO", "#2563EB"),
                new DetailMetric("Projects", row.ProjectCount.ToString(), "Number of projects with this construction type"),
                new DetailMetric("Total Fee", row.TotalFee.ToString("$#,##0"), "Sum of all project fees"),

                DetailMetric.Header("RATES", "#16A34A"),
                new DetailMetric("Fee Per Hour", row.AvgFeePerHr.ToString("$#,##0"), "Fee per production hour — varies by construction type"),
                new DetailMetric("Net Fee Per Hour", row.AvgNetFeePerHr.ToString("$#,##0"), "Fee minus subconsultants per hour"),
                new DetailMetric("Median Fee Per Hour", row.MedianFeePerHr.ToString("$#,##0"), "Typical $/hr for this type"),
                new DetailMetric("Eng / Draft Split", $"{row.WeightedEngPct:P0} / {(1 - row.WeightedEngPct):P0}", "Concrete vs wood frame can differ significantly"),
                new DetailMetric("Subconsultant %", row.AvgSubPct.ToString("P0"), "What % of fee goes to outside firms"),

                DetailMetric.Header("COLLECTIONS & ACCURACY", "#EA580C"),
                new DetailMetric("AR Outstanding", row.TotalArOutstanding.ToString("$#,##0"), "Total unpaid invoices"),
                new DetailMetric("Budget Accuracy", row.BudgetAccuracyPct.ToString("P0"), "% of closed projects where total estimated hours (eng+draft) were within ±50% of actual production hours"),
                new DetailMetric("Accuracy Sample", row.ClosedProjectCount.ToString() + " projects", "Closed projects used for accuracy"),
            });
        }

        public void SetYearTrendDetail(YearTrendRow? row)
        {
            if (row == null) { DetailMetrics.ReplaceAll(Array.Empty<DetailMetric>()); DetailTitle = ""; DetailSubtitle = ""; return; }
            DetailTitle = row.Year.ToString();
            DetailSubtitle = "Year Opened";
            DetailMetrics.ReplaceAll(new[]
            {
                DetailMetric.Header("PORTFOLIO", "#2563EB"),
                new DetailMetric("Projects", row.ProjectCount.ToString(), "Number of projects opened this year"),
                new DetailMetric("Total Fee", row.TotalFee.ToString("$#,##0"), "Sum of all project fees"),
                new DetailMetric("Average Project Fee", row.AvgFee.ToString("$#,##0"), "Typical project size this year"),

                DetailMetric.Header("RATES & EFFICIENCY", "#16A34A"),
                new DetailMetric("Fee Per Hour", row.AvgFeePerHr.ToString("$#,##0"), "Fee per production hour — is the firm getting more efficient?"),
                new DetailMetric("Net Fee Per Hour", row.AvgNetFeePerHr.ToString("$#,##0"), "Fee minus subconsultants per hour"),
                new DetailMetric("Eng / Draft Split", $"{row.WeightedEngPct:P0} / {(1 - row.WeightedEngPct):P0}", "Production time split this year"),
                new DetailMetric("Subconsultant %", row.AvgSubPct.ToString("P0"), "What % of fee went to outside firms"),
                new DetailMetric("Firm Billable %", row.FirmBillablePct.ToString("P0"), "What % of the firm's hours went to real projects (not overhead/vacation)"),

                DetailMetric.Header("COLLECTIONS", "#EA580C"),
                new DetailMetric("AR Outstanding", row.TotalArOutstanding.ToString("$#,##0"), "Total unpaid invoices from projects opened this year"),
            });
        }

        public void SetEmployeeHours(List<EmployeeProjectHours> hours)
        {
            _employeeProjectHours = hours ?? new List<EmployeeProjectHours>();
            if (_allRows.Count > 0)
            {
                RecomputeProjectProfitability();
                ApplyFilter();
            }
        }

        public void SetEmployeeWeeklyHours(List<EmployeeWeeklyHours> hours)
        {
            _employeeWeeklyHours = hours ?? new List<EmployeeWeeklyHours>();
        }

        public void SetEmployeeRates(List<EmployeeRate> rates)
        {
            _employeeRates = rates ?? new List<EmployeeRate>();
            if (_allRows.Count > 0)
            {
                RecomputeProjectProfitability();
                ApplyFilter();
            }
        }

        public void SetOptions(App.Options.DeltekOdbcOptions? opts)
        {
            _opts = opts;
        }

        public void SetUtilization(FirmUtilizationStats? stats)
        {
            _firmUtilization = stats;
            if (stats == null) return;
            FirmBillablePct = stats.BillablePct;
        }
        public int ExcludedCount => _allRows.Count - _visibleCount;

        public void SetRows(List<HistoricalProjectRow> rows)
        {
            _allRows.Clear();
            _allRows.AddRange(rows);
            RecomputeProjectProfitability();

            // Rebuild PM options
            var savedPm = _selectedPm;
            _selectedPm = "All";
            PmOptions.Clear();
            PmOptions.Add("All");
            foreach (var pm in rows.Select(r => r.Pm).Where(p => !string.IsNullOrWhiteSpace(p))
                                   .Distinct(StringComparer.OrdinalIgnoreCase)
                                   .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                PmOptions.Add(pm);
            _selectedPm = PmOptions.Contains(savedPm) ? savedPm : "All";
            OnPropertyChanged(nameof(SelectedPm));

            // Rebuild Year options
            var savedYear = _selectedYear;
            _selectedYear = "All";
            YearOptions.Clear();
            YearOptions.Add("All");
            foreach (var y in rows.Where(r => r.OpenYear.HasValue)
                                  .Select(r => r.OpenYear!.Value.ToString())
                                  .Distinct()
                                  .OrderByDescending(y => y))
                YearOptions.Add(y);
            _selectedYear = YearOptions.Contains(savedYear) ? savedYear : "All";
            OnPropertyChanged(nameof(SelectedYear));

            // Rebuild DM options
            RebuildFilterOptions(DraftingMgrOptions, rows.Select(r => r.DraftingManager), ref _selectedDraftingMgr, nameof(SelectedDraftingMgr));

            // Rebuild Construction Type, Category, Drafting Type options
            RebuildFilterOptions(ConstructionTypeOptions, rows.Select(r => r.ConstructionType), ref _selectedConstructionType, nameof(SelectedConstructionType));
            RebuildFilterOptions(ProjectCategoryOptions, rows.Select(r => r.ProjectCategory), ref _selectedProjectCategory, nameof(SelectedProjectCategory));
            RebuildFilterOptions(DraftingTypeOptions, rows.Select(r => r.DraftingType), ref _selectedDraftingType, nameof(SelectedDraftingType));

            ApplyFilter();
        }

        private void RecomputeProjectProfitability()
        {
            var rateByEmp = _employeeRates
                .GroupBy(r => r.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().EffectiveCostRate, StringComparer.OrdinalIgnoreCase);
            var costByProject = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

            foreach (var h in _employeeProjectHours)
            {
                if (string.IsNullOrWhiteSpace(h.Wbs1)) continue;
                var rate = rateByEmp.GetValueOrDefault(h.EmployeeId);
                // Uses BillableHrs (all billable categories: eng + draft + inspection + doc-prep + general)
                // to reflect total labor cost to serve the project, not just production-hour cost.
                // Matches the firm-level cost-to-serve intent rather than a narrower eng/draft-only calc.
                var projectCost = h.BillableHrs * rate;
                costByProject[h.Wbs1] = costByProject.GetValueOrDefault(h.Wbs1) + projectCost;
            }

            foreach (var row in _allRows)
            {
                row.TotalCost = costByProject.GetValueOrDefault(row.Wbs1);
            }
        }

        private void ApplyFilter()
        {
            var filtered = _allRows.AsEnumerable();

            if (!string.IsNullOrEmpty(_selectedStatus) && _selectedStatus == "Active")
                filtered = filtered.Where(r => ActiveStatuses.Contains((r.Status ?? "").Trim()));
            else if (!string.IsNullOrEmpty(_selectedStatus) && _selectedStatus == "Closed")
                filtered = filtered.Where(r => !ActiveStatuses.Contains((r.Status ?? "").Trim()));

            if (!string.IsNullOrEmpty(_selectedHoursFilter) && _selectedHoursFilter == "Has Hours")
                filtered = filtered.Where(r => r.TotalEngDraft > 0);
            else if (_selectedHoursFilter == "Has Fee")
                filtered = filtered.Where(r => r.TotalFee > 0);
            else if (_selectedHoursFilter == "Has Hours + Fee")
                filtered = filtered.Where(r => r.TotalEngDraft > 0 && r.TotalFee > 0);
            else if (_selectedHoursFilter == "Fee ≥ $25K")
                filtered = filtered.Where(r => r.TotalFee >= 25_000);
            else if (_selectedHoursFilter == "Fee ≥ $25K + Hours")
                filtered = filtered.Where(r => r.TotalFee >= 25_000 && r.TotalEngDraft > 0);

            if (!string.IsNullOrEmpty(_selectedYear) && _selectedYear != "All"
                && int.TryParse(_selectedYear, out var yr))
                filtered = filtered.Where(r => r.OpenYear == yr);

            if (!string.IsNullOrEmpty(_selectedPm) && _selectedPm != "All")
                filtered = filtered.Where(r => (r.Pm ?? "").Equals(_selectedPm, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var term = _searchText.Trim();
                filtered = filtered.Where(r =>
                    r.Wbs1.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (!string.IsNullOrEmpty(_selectedDraftingMgr) && _selectedDraftingMgr != "All")
                filtered = filtered.Where(r => (r.DraftingManager ?? "").Equals(_selectedDraftingMgr, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(_selectedConstructionType) && _selectedConstructionType != "All")
                filtered = filtered.Where(r => (r.ConstructionType ?? "").Equals(_selectedConstructionType, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(_selectedProjectCategory) && _selectedProjectCategory != "All")
                filtered = filtered.Where(r => (r.ProjectCategory ?? "").Equals(_selectedProjectCategory, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(_selectedDraftingType) && _selectedDraftingType != "All")
                filtered = filtered.Where(r => (r.DraftingType ?? "").Equals(_selectedDraftingType, StringComparison.OrdinalIgnoreCase));

            var list = filtered.ToList();
            Rows.ReplaceAll(list);

            // Clear project selection if the selected row was filtered out
            if (_selectedRow != null && !list.Contains(_selectedRow))
                SelectedRow = null;

            RecomputeSummary(list);
            RecomputePmSummary(list);
            RecomputeDmSummary(list);
            RecomputeFeeBands(list);
            RecomputeConstructionTypeSummary(list);
            RecomputeEmployeeSummary(list);
            RecomputeYearTrend(list);
        }

        private void RecomputeSummary(List<HistoricalProjectRow> visible)
        {
            VisibleCount = visible.Count;
            OnPropertyChanged(nameof(ExcludedCount));

            var fee = 0.0;
            var eng = 0.0;
            var draft = 0.0;
            var allHrs = 0.0;
            var billableHrs = 0.0;
            var subCost = 0.0;
            var arOutstanding = 0.0;
            var feePerHrs = new List<double>();
            var netFeePerHrs = new List<double>();
            var engPcts = new List<double>();
            var subPcts = new List<double>();
            var billPcts = new List<double>();

            // Minimum production hours to include in $/hr and ratio stats.
            // Projects with trivial hours produce absurd $/hr values that skew everything.
            const double MinProdHrs = 50.0;

            foreach (var r in visible)
            {
                fee += r.TotalFee;
                eng += r.EngHrs;
                draft += r.DraftHrs;
                allHrs += r.TotalAllHrs;
                billableHrs += r.BillableHrs;
                subCost += r.SubCost;
                arOutstanding += r.ArTotal;
                if (r.FeePerHr > 0 && r.TotalEngDraft >= MinProdHrs) feePerHrs.Add(r.FeePerHr);
                if (r.NetFeePerHr > 0 && r.TotalEngDraft >= MinProdHrs) netFeePerHrs.Add(r.NetFeePerHr);
                if (r.TotalEngDraft >= MinProdHrs) engPcts.Add(r.EngPct);
                if (r.TotalFee > 0 && r.TotalEngDraft >= MinProdHrs) subPcts.Add(r.SubPctOfFee);
                if (r.TotalAllHrs >= MinProdHrs) billPcts.Add(r.BillablePct);
            }

            TotalFee = fee;
            TotalSubCost = subCost;
            TotalArOutstanding = arOutstanding;
            TotalEngHrs = eng;
            TotalDraftHrs = draft;
            TotalAllHrs = allHrs;

            var totalProd = eng + draft;
            WeightedEngPct = totalProd > 0 ? eng / totalProd : 0;
            WeightedDraftPct = totalProd > 0 ? draft / totalProd : 0;
            WeightedBillablePct = allHrs > 0 ? billableHrs / allHrs : 0;
            var overheadHrs = visible.Sum(r => r.AdminHrs + r.NonBillHrs);
            WeightedOverheadRatio = allHrs > 0 ? overheadHrs / allHrs : 0;

            MedianFeePerHr = PerformanceScoring.Median(feePerHrs);
            MeanFeePerHr = feePerHrs.Count == 0 ? 0 : feePerHrs.Sum() / feePerHrs.Count;
            // Percentiles need a sorted copy
            var sortedFph = new List<double>(feePerHrs);
            sortedFph.Sort();
            var n = sortedFph.Count;
            P25FeePerHr = n == 0 ? 0 : sortedFph[Math.Min(n - 1, Math.Max(0, (int)Math.Ceiling(n * 0.25) - 1))];
            P75FeePerHr = n == 0 ? 0 : sortedFph[Math.Min(n - 1, Math.Max(0, (int)Math.Ceiling(n * 0.75) - 1))];

            // Portfolio medians for detail card comparison (median resists outliers)
            AvgEngPct = PerformanceScoring.Median(engPcts);
            AvgFeePerHr = MedianFeePerHr;
            AvgNetFeePerHr = PerformanceScoring.Median(netFeePerHrs);
            AvgSubPct = PerformanceScoring.Median(subPcts);
            AvgBillablePct = PerformanceScoring.Median(billPcts);

            // Budget accuracy — closed projects only (active projects have incomplete hours),
            // ≥50 production hrs, ±50% threshold on TOTAL production hours (eng+draft combined).
            // Total hours is the right metric — eng/draft split varies project to project
            // but the combined budget is what matters for delivery.
            // ±50% is the industry-appropriate threshold — structural projects have inherent
            // complexity variance that makes ±35% unrealistic even for experienced PMs.
            var engDeltas = new List<double>();
            var draftDeltas = new List<double>();
            var absErrors = new List<double>();
            var withinThreshold = 0;
            var totalComparable = 0;
            foreach (var r in visible)
            {
                var isClosed = !ActiveStatuses.Contains((r.Status ?? "").Trim());
                var estTotal = r.EstEngBudget + r.EstDraftBudget;
                if (estTotal > 0 && r.TotalEngDraft >= MinProdHrs && isClosed)
                {
                    engDeltas.Add(r.EngBudgetDelta);
                    draftDeltas.Add(r.DraftBudgetDelta);
                    totalComparable++;
                    var totalRatio = r.TotalEngDraft / estTotal;
                    absErrors.Add(Math.Abs(totalRatio - 1.0));
                    if (totalRatio >= 0.50 && totalRatio <= 1.50) withinThreshold++;
                }
            }
            MeanEngDelta = engDeltas.Count > 0 ? engDeltas.Sum() / engDeltas.Count : 0;
            MeanDraftDelta = draftDeltas.Count > 0 ? draftDeltas.Sum() / draftDeltas.Count : 0;
            BudgetAccuracyPct = totalComparable > 0 ? (double)withinThreshold / totalComparable : 0;
            MedianAbsError = PerformanceScoring.Median(absErrors);
        }

        private void RecomputePmSummary(List<HistoricalProjectRow> visible)
        {
            var groups = PmPerformanceService.Build(visible);
            PmSummaryRows.ReplaceAll(groups);
            OnPropertyChanged(nameof(KpiCount));
        }

        private void RecomputeDmSummary(List<HistoricalProjectRow> visible)
        {
            var groups = DmPerformanceService.Build(visible);
            DmSummaryRows.ReplaceAll(groups);
            OnPropertyChanged(nameof(KpiCount));
        }

        private static readonly (string Label, double Min, double Max)[] FeeBands =
        {
            ("$0 – $25K",      0.0,         25_000.0),
            ("$25K – $50K",    25_000.0,    50_000.0),
            ("$50K – $100K",   50_000.0,    100_000.0),
            ("$100K – $250K",  100_000.0,   250_000.0),
            ("$250K – $500K",  250_000.0,   500_000.0),
            ("$500K – $1M",    500_000.0,   1_000_000.0),
            ("$1M+",           1_000_000.0, double.MaxValue),
        };

        private void RecomputeFeeBands(List<HistoricalProjectRow> visible)
        {
            const double MinHrs = 50.0;
            var results = new List<FeeBandSummaryRow>();
            foreach (var (label, min, max) in FeeBands)
            {
                var rows = visible.Where(r => r.TotalFee >= min && r.TotalFee < max).ToList();
                if (rows.Count == 0) continue;
                var totalEng = rows.Sum(r => r.EngHrs);
                var totalDraft = rows.Sum(r => r.DraftHrs);
                var totalProd = totalEng + totalDraft;
                var totalAll = rows.Sum(r => r.TotalAllHrs);
                var totalFee = rows.Sum(r => r.TotalFee);
                var overheadHrs = rows.Sum(r => r.AdminHrs + r.NonBillHrs);

                // Per-band budget accuracy: closed projects with 50+ production hrs, ±50% on total hours
                var comparable = rows.Where(r =>
                    !ActiveStatuses.Contains((r.Status ?? "").Trim()) && (r.EstEngBudget + r.EstDraftBudget) > 0 && r.TotalEngDraft >= MinHrs).ToList();
                var within = comparable.Count > 0
                    ? comparable.Count(r => { var ratio = r.TotalEngDraft / (r.EstEngBudget + r.EstDraftBudget); return ratio >= 0.50 && ratio <= 1.50; })
                    : 0;

                // Per-band median $/hr (50+ hrs only)
                var bandFeePerHrs = rows.Where(r => r.FeePerHr > 0 && r.TotalEngDraft >= MinHrs)
                                        .Select(r => r.FeePerHr).ToList();

                results.Add(new FeeBandSummaryRow
                {
                    Band = label,
                    ProjectCount = rows.Count,
                    TotalFee = totalFee,
                    AvgFeePerHr = totalProd > 0 ? totalFee / totalProd : 0,
                    AvgNetFeePerHr = totalProd > 0 ? (totalFee - rows.Sum(r => r.SubCost)) / totalProd : 0,
                    WeightedEngPct = totalProd > 0 ? totalEng / totalProd : 0,
                    WeightedBillablePct = totalAll > 0 ? rows.Sum(r => r.BillableHrs) / totalAll : 0,
                    AvgSubPct = totalFee > 0 ? rows.Sum(r => r.SubCost) / totalFee : 0,
                    AvgOverheadRatio = totalAll > 0 ? overheadHrs / totalAll : 0,
                    TotalArOutstanding = rows.Sum(r => r.ArTotal),
                    BudgetAccuracyPct = comparable.Count > 0 ? (double)within / comparable.Count : 0,
                    MedianFeePerHr = PerformanceScoring.Median(bandFeePerHrs),
                    ClosedProjectCount = comparable.Count,
                });
            }
            FeeBandRows.ReplaceAll(results);
        }

        private void RecomputeConstructionTypeSummary(List<HistoricalProjectRow> visible)
        {
            const double MinHrs = 50.0;
            var results = visible
                .Where(r => !string.IsNullOrWhiteSpace(r.ConstructionType))
                .GroupBy(r => r.ConstructionType.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var rows = g.ToList();
                    var totalEng = rows.Sum(r => r.EngHrs);
                    var totalDraft = rows.Sum(r => r.DraftHrs);
                    var totalProd = totalEng + totalDraft;
                    var totalFee = rows.Sum(r => r.TotalFee);
                    var comparable = rows.Where(r =>
                        !ActiveStatuses.Contains((r.Status ?? "").Trim()) && (r.EstEngBudget + r.EstDraftBudget) > 0 && r.TotalEngDraft >= MinHrs).ToList();
                    var within = comparable.Count > 0
                        ? comparable.Count(r => { var ratio = r.TotalEngDraft / (r.EstEngBudget + r.EstDraftBudget); return ratio >= 0.50 && ratio <= 1.50; })
                        : 0;
                    var bandFeePerHrs = rows.Where(r => r.FeePerHr > 0 && r.TotalEngDraft >= MinHrs)
                                            .Select(r => r.FeePerHr).ToList();
                    return new ConstructionTypeSummaryRow
                    {
                        ConstructionType = g.Key,
                        ProjectCount = rows.Count,
                        TotalFee = totalFee,
                        AvgFeePerHr = totalProd > 0 ? totalFee / totalProd : 0,
                        AvgNetFeePerHr = totalProd > 0 ? (totalFee - rows.Sum(r => r.SubCost)) / totalProd : 0,
                        WeightedEngPct = totalProd > 0 ? totalEng / totalProd : 0,
                        AvgSubPct = totalFee > 0 ? rows.Sum(r => r.SubCost) / totalFee : 0,
                        TotalArOutstanding = rows.Sum(r => r.ArTotal),
                        MedianFeePerHr = PerformanceScoring.Median(bandFeePerHrs),
                        BudgetAccuracyPct = comparable.Count > 0 ? (double)within / comparable.Count : 0,
                        ClosedProjectCount = comparable.Count,
                    };
                })
                .OrderByDescending(r => r.TotalFee)
                .ToList();

            ConstructionTypeRows.ReplaceAll(results);
        }

        private void RecomputeYearTrend(List<HistoricalProjectRow> visible)
        {
            YearTrendRows.ReplaceAll(YearTrendService.Build(visible, _firmUtilization));
        }

        private void RecomputeSimilarProjects()
        {
            var sel = _selectedRow;
            if (sel == null || sel.TotalFee <= 0)
            {
                SimilarProjects.ReplaceAll(Array.Empty<HistoricalProjectRow>());
                PeerCount = 0;
                PeerMedianEngHrs = 0;
                PeerMedianDraftHrs = 0;
                PeerMedianTotalHrs = 0;
                PeerMedianFeePerHr = 0;
                return;
            }

            // Find closed projects with 50+ production hours, adaptive fee range, tiered matching
            var phase = (sel.Phase ?? "").Trim();
            var constType = (sel.ConstructionType ?? "").Trim();
            var projCat = (sel.ProjectCategory ?? "").Trim();
            var hasPhase = !string.IsNullOrWhiteSpace(phase);
            var hasType = !string.IsNullOrWhiteSpace(constType);
            var hasCat = !string.IsNullOrWhiteSpace(projCat);

            // Adaptive fee range: ±15% tight, ±30% fallback (matches PeerBudgetEstimator)
            List<HistoricalProjectRow>? pool = null;
            foreach (var pct in new[] { 0.15, 0.30 })
            {
                var feeMin = sel.TotalFee * (1.0 - pct);
                var feeMax = sel.TotalFee * (1.0 + pct);

                var candidates = _allRows
                    .Where(r => r.Wbs1 != sel.Wbs1
                        && !ActiveStatuses.Contains((r.Status ?? "").Trim())
                        && r.TotalEngDraft >= 50
                        && r.TotalFee >= feeMin && r.TotalFee <= feeMax)
                    .ToList();

                if (candidates.Count < 3) continue;

                // Tiered: type+cat+phase → type+phase → type+cat → type → phase → all
                var pools = new List<List<HistoricalProjectRow>>();

                if (hasType && hasCat && hasPhase)
                    pools.Add(candidates.Where(r =>
                        (r.ConstructionType ?? "").Trim().Equals(constType, StringComparison.OrdinalIgnoreCase)
                        && (r.ProjectCategory ?? "").Trim().Equals(projCat, StringComparison.OrdinalIgnoreCase)
                        && (r.Phase ?? "").Trim().Equals(phase, StringComparison.OrdinalIgnoreCase)).ToList());

                if (hasType && hasPhase)
                    pools.Add(candidates.Where(r =>
                        (r.ConstructionType ?? "").Trim().Equals(constType, StringComparison.OrdinalIgnoreCase)
                        && (r.Phase ?? "").Trim().Equals(phase, StringComparison.OrdinalIgnoreCase)).ToList());

                if (hasType && hasCat)
                    pools.Add(candidates.Where(r =>
                        (r.ConstructionType ?? "").Trim().Equals(constType, StringComparison.OrdinalIgnoreCase)
                        && (r.ProjectCategory ?? "").Trim().Equals(projCat, StringComparison.OrdinalIgnoreCase)).ToList());

                if (hasType)
                    pools.Add(candidates.Where(r =>
                        (r.ConstructionType ?? "").Trim().Equals(constType, StringComparison.OrdinalIgnoreCase)).ToList());

                if (hasPhase)
                    pools.Add(candidates.Where(r =>
                        (r.Phase ?? "").Trim().Equals(phase, StringComparison.OrdinalIgnoreCase)).ToList());

                pools.Add(candidates);

                pool = pools.FirstOrDefault(p => p.Count >= 3);
                if (pool != null) break;
            }

            if (pool == null || pool.Count < 3) pool = new List<HistoricalProjectRow>();

            // Rank by fee proximity, take top 8
            var peers = pool
                .OrderBy(r => Math.Abs(r.TotalFee - sel.TotalFee))
                .Take(8)
                .ToList();

            SimilarProjects.ReplaceAll(peers);
            PeerCount = peers.Count;

            if (peers.Count > 0)
            {
                PeerMedianEngHrs = PerformanceScoring.Median(peers.Select(r => r.EngHrs).ToList());
                PeerMedianDraftHrs = PerformanceScoring.Median(peers.Select(r => r.DraftHrs).ToList());
                PeerMedianTotalHrs = PerformanceScoring.Median(peers.Select(r => r.TotalEngDraft).ToList());
                PeerMedianFeePerHr = PerformanceScoring.Median(peers.Where(r => r.FeePerHr > 0).Select(r => r.FeePerHr).ToList());
            }
            else
            {
                PeerMedianEngHrs = 0;
                PeerMedianDraftHrs = 0;
                PeerMedianTotalHrs = 0;
                PeerMedianFeePerHr = 0;
            }
        }

        private void RecomputeEmployeeSummary(List<HistoricalProjectRow> visible)
        {
            if (_employeeProjectHours.Count == 0)
            {
                EmployeeSummaryRows.ReplaceAll(Array.Empty<EmployeeSummaryRow>());
                return;
            }

            var groups = EmployeePerformanceService.Build(visible, _employeeProjectHours, _excludedEmployeeIds);

            // Employee fee/hr distribution for KPI bar (matches $/Hr column in grid)
            var empFeePerHrs = groups.Where(r => r.FeePerHr > 0).Select(r => r.FeePerHr).OrderBy(v => v).ToList();
            var ne = empFeePerHrs.Count;
            _empMedianFeePerHr = PerformanceScoring.Median(empFeePerHrs);
            _empP25FeePerHr = ne > 0 ? empFeePerHrs[Math.Min(ne - 1, Math.Max(0, (int)Math.Ceiling(ne * 0.25) - 1))] : 0;
            _empP75FeePerHr = ne > 0 ? empFeePerHrs[Math.Min(ne - 1, Math.Max(0, (int)Math.Ceiling(ne * 0.75) - 1))] : 0;
            OnPropertyChanged(nameof(MedianFeePerHr));
            OnPropertyChanged(nameof(FeePerHrComparisonRate));
            OnPropertyChanged(nameof(FeePerHrComparisonTooltip));
            OnPropertyChanged(nameof(P25FeePerHr));
            OnPropertyChanged(nameof(P75FeePerHr));

            EmployeeSummaryRows.ReplaceAll(groups);
            OnPropertyChanged(nameof(KpiCount));

            // Auto-save quarterly snapshot + one-time backfill if table is empty
            if (groups.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var cs = Kor.Operations.Services.AppServices.Get<App.Options.DatabaseOptions>().KorTransmittalsDb;
                        if (string.IsNullOrWhiteSpace(cs)) return;
                        var store = new EmployeeScoreSnapshotStore(cs);
                        var quarterStart = new DateTime(DateTime.Now.Year, ((DateTime.Now.Month - 1) / 3) * 3 + 1, 1);
                        await store.SaveSnapshotsAsync(quarterStart, groups).ConfigureAwait(false);

                        // One-time backfill from time-sliced tkDetail data
                        var sample = await store.LoadTrendAsync(groups[0].EmployeeId).ConfigureAwait(false);
                        if (sample.Count <= 1)
                        {
                            Serilog.Log.Information("Running one-time historical employee score backfill from tkDetail quarterly data.");
                            await BackfillFromQuarterlyHoursAsync(store).ConfigureAwait(false);
                        }
                    }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to save employee score snapshots."); }
                });
            }
        }

        private async Task BackfillFromQuarterlyHoursAsync(EmployeeScoreSnapshotStore store)
        {
            var odbcOpts = Kor.Operations.Services.AppServices.Get<App.Options.DeltekOdbcOptions>();
            var financialsOpts = Kor.Operations.Services.AppServices.GetOptional<App.Options.FinancialsOptions>();
            var svc = new HistoricalAnalyticsService(odbcOpts, financialsOpts);
            var quarterlyHours = await Task.Run(() => svc.LoadQuarterlyEmployeeHoursSync(CancellationToken.None)).ConfigureAwait(false);
            if (quarterlyHours.Count == 0) return;

            var projectLookup = new Dictionary<string, HistoricalProjectRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in _allRows) projectLookup.TryAdd(r.Wbs1, r);

            var quarters = quarterlyHours
                .Select(h => (h.Year, h.Quarter))
                .Distinct()
                .OrderBy(q => q.Year).ThenBy(q => q.Quarter)
                .ToList();

            var saved = 0;
            foreach (var (yr, qtr) in quarters)
            {
                var qDate = new DateTime(yr, (qtr - 1) * 3 + 1, 1);
                var qHours = quarterlyHours.Where(h => h.Year == yr && h.Quarter == qtr).ToList();

                var groups = qHours
                    .GroupBy(h => h.EmployeeId, StringComparer.OrdinalIgnoreCase)
                    .Select(g =>
                    {
                        var entries = g.ToList();
                        var billable = entries.Where(e => projectLookup.ContainsKey(e.Wbs1)).ToList();
                        if (billable.Count == 0) return null;

                        var engHrs = billable.Sum(e => e.EngHrs);
                        var draftHrs = billable.Sum(e => e.DraftHrs);
                        var billableHrs = billable.Sum(e => e.BillableHrs);
                        var totalAllHrs = entries.Sum(e => e.TotalHrs);
                        if (totalAllHrs <= 0) return null;

                        var attributedFee = 0.0;
                        foreach (var e in billable)
                            if (projectLookup.TryGetValue(e.Wbs1, out var proj) && proj.TotalEngDraft > 0)
                                attributedFee += proj.TotalFee * ((e.EngHrs + e.DraftHrs) / proj.TotalEngDraft);

                        var healthyHrs = 0.0;
                        var totalProjHrs = 0.0;
                        foreach (var e in billable)
                            if (projectLookup.TryGetValue(e.Wbs1, out var proj))
                            {
                                var hrs = e.EngHrs + e.DraftHrs;
                                totalProjHrs += hrs;
                                if (proj.EstEngBudget <= 0 || proj.EngHrs <= proj.EstEngBudget * AnalyticsThresholds.OverBudgetFactor) healthyHrs += hrs;
                            }

                        var primaryType = billable
                            .Where(e => projectLookup.ContainsKey(e.Wbs1))
                            .GroupBy(e => (projectLookup[e.Wbs1].ConstructionType ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                            .Where(tg => !string.IsNullOrWhiteSpace(tg.Key))
                            .OrderByDescending(tg => tg.Sum(x => x.EngHrs + x.DraftHrs))
                            .Select(tg => tg.Key)
                            .FirstOrDefault() ?? "";

                        return new EmployeeSummaryRow
                        {
                            EmployeeId = g.Key,
                            EmployeeName = entries.First().EmployeeName,
                            ProjectCount = billable.Select(e => e.Wbs1).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                            TotalEngHrs = engHrs, TotalDraftHrs = draftHrs,
                            TotalBillableHrs = billableHrs, TotalAllHrs = totalAllHrs,
                            AttributedFee = attributedFee,
                            PrimaryRole = engHrs >= draftHrs ? "Engineering" : "Drafting",
                            PrimaryConstructionType = primaryType,
                            BillableRateScore = Math.Min(100, (totalAllHrs > 0 ? billableHrs / totalAllHrs : 0) * 100),
                            ProjectHealthScore = totalProjHrs > 0 ? (healthyHrs / totalProjHrs) * 100 : 100,
                        };
                    })
                    .OfType<EmployeeSummaryRow>()
                    .ToList();

                if (groups.Count == 0) continue;

                PerformanceScoring.ScoreEmployeesBackfillPass(groups);

                await store.SaveSnapshotsAsync(qDate, groups, CancellationToken.None).ConfigureAwait(false);
                saved++;
            }

            Serilog.Log.Information("Backfilled {Count} quarterly employee score snapshots from tkDetail.", saved);
        }

        private void RebuildFilterOptions(ObservableCollection<string> options, IEnumerable<string> values, ref string selected, string propertyName)
        {
            var saved = selected;
            selected = "All";
            options.Clear();
            options.Add("All");
            foreach (var v in values.Where(v => !string.IsNullOrWhiteSpace(v))
                                    .Distinct(StringComparer.OrdinalIgnoreCase)
                                    .OrderBy(v => v, StringComparer.OrdinalIgnoreCase))
                options.Add(v);
            selected = options.Contains(saved) ? saved : "All";
            OnPropertyChanged(propertyName);
        }

        private static HashSet<string> BuildExcludedEmployeeIds()
        {
            var raw = System.Configuration.ConfigurationManager.AppSettings["EmployeeSummaryExcludedIds"] ?? "";
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                set.Add(id);
            return set;
        }

        // ── IAiContextProvider ──────────────────────────────────────────

        string Services.IAiContextProvider.ProviderName => "Historical Analytics";
        bool Services.IAiContextProvider.HasData => _allRows.Count > 0;

        string Services.IAiContextProvider.BuildContext()
        {
            return AnalyticsAiService.BuildContext(this);
        }

        string Services.IAiContextProvider.BuildLocalContext()
        {
            if (SelectedRow is { } sel)
            {
                return $"Selected project: {sel.Wbs1} — {sel.Name}, PM: {sel.Pm}, Fee: ${sel.TotalFee:N0}, " +
                       $"Sub%: {sel.SubPctOfFee:P0}, $/Hr: ${sel.FeePerHr:N0}, Eng: {sel.EngHrs:N0}hrs, Draft: {sel.DraftHrs:N0}hrs";
            }
            if (!string.IsNullOrWhiteSpace(DetailTitle))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Selected: {DetailTitle} ({DetailSubtitle})");
                foreach (var m in DetailMetrics)
                    if (!m.IsHeader && !m.IsExplanation && !string.IsNullOrWhiteSpace(m.Value))
                        sb.AppendLine($"  {m.Label}: {m.Value}");
                return sb.ToString();
            }
            return "";
        }

    }
}
