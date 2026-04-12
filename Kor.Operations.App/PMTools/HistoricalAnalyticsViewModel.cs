#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using Kor.Operations.Core;

namespace Kor.Operations.PMTools
{
    internal sealed class HistoricalAnalyticsViewModel : ObservableObject
    {
        private readonly List<HistoricalProjectRow> _allRows = new();
        private bool _isLoading;
        private string _errorMessage = "";

        // Filters
        private string _selectedStatus = "All";
        private static readonly HashSet<string> ActiveStatuses =
            new(StringComparer.OrdinalIgnoreCase) { "A", "ACTIVE" };
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
            set { if (SetField(ref _viewMode, value)) { OnPropertyChanged(nameof(IsProjectView)); OnPropertyChanged(nameof(IsPmSummaryView)); OnPropertyChanged(nameof(IsDmSummaryView)); OnPropertyChanged(nameof(IsEmployeeSummaryView)); OnPropertyChanged(nameof(IsFeeBandView)); OnPropertyChanged(nameof(IsConstructionTypeView)); OnPropertyChanged(nameof(IsYoYTrendView)); } }
        }

        public bool IsProjectView => _viewMode == "Projects";
        public bool IsPmSummaryView => _viewMode == "PM Summary";
        public bool IsDmSummaryView => _viewMode == "DM Summary";
        public bool IsEmployeeSummaryView => _viewMode == "Employee Summary";
        public bool IsFeeBandView => _viewMode == "Fee Bands";
        public bool IsConstructionTypeView => _viewMode == "Construction Type";
        public bool IsYoYTrendView => _viewMode == "YoY Trend";

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
        public int VisibleCount { get => _visibleCount; private set => SetField(ref _visibleCount, value); }
        public double TotalFee { get => _totalFee; private set => SetField(ref _totalFee, value); }
        public double TotalEngHrs { get => _totalEngHrs; private set => SetField(ref _totalEngHrs, value); }
        public double TotalDraftHrs { get => _totalDraftHrs; private set => SetField(ref _totalDraftHrs, value); }
        public double WeightedEngPct { get => _weightedEngPct; private set => SetField(ref _weightedEngPct, value); }
        public double WeightedDraftPct { get => _weightedDraftPct; private set => SetField(ref _weightedDraftPct, value); }
        public double MedianFeePerHr { get => _medianFeePerHr; private set => SetField(ref _medianFeePerHr, value); }
        public double MeanFeePerHr { get => _meanFeePerHr; private set => SetField(ref _meanFeePerHr, value); }
        public double P25FeePerHr { get => _p25FeePerHr; private set => SetField(ref _p25FeePerHr, value); }
        public double P75FeePerHr { get => _p75FeePerHr; private set => SetField(ref _p75FeePerHr, value); }
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

        public int LoadedCount => _allRows.Count;

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

                DetailMetric.Header("SUBCONSULTANTS & AR", "#EA580C"),
                new DetailMetric("Subconsultant %", row.SubPctOfFee.ToString("P0"), "What % of fee went to outside firms"),
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
                new DetailMetric("Delivery Health", $"{row.DeliveryHealthScore:N0}/100", "% of projects NOT over budget (eng hrs ≤ estimated × 1.35). Weight: 30%."),
                new DetailMetric("Estimation Accuracy", $"{row.EstimationAccuracyScore:N0}/100", "Budget accuracy percentile — lower |delta| = higher score. Weight: 30%."),
                new DetailMetric("Revenue Efficiency", $"{row.RevenueEfficiencyScore:N0}/100", "Fee/Hr percentile rank vs peers. Weight: 20%."),
                new DetailMetric("AR Management", $"{row.ArManagementScore:N0}/100", "% of AR NOT 90+ days overdue. Weight: 20%."),
            });
        }

        public async void SetEmployeeSummaryDetail(EmployeeSummaryRow? row)
        {
            if (row == null) { DetailMetrics.ReplaceAll(Array.Empty<DetailMetric>()); DetailTitle = ""; DetailSubtitle = ""; return; }
            DetailTitle = row.EmployeeName;
            DetailSubtitle = row.PrimaryRole;

            // Load trend data from snapshots
            var trendLine = "";
            try
            {
                var cs = Kor.Operations.Services.AppServices.Get<App.Options.DatabaseOptions>().KorTransmittalsDb;
                if (!string.IsNullOrWhiteSpace(cs))
                {
                    var store = new EmployeeScoreSnapshotStore(cs);
                    var snapshots = await store.LoadTrendAsync(row.EmployeeId);
                    if (snapshots.Count >= 2)
                    {
                        var oldest = snapshots[0].ProductivityScore;
                        var newest = snapshots[snapshots.Count - 1].ProductivityScore;
                        var delta = newest - oldest;
                        var arrow = delta > 3 ? "↑" : delta < -3 ? "↓" : "→";
                        trendLine = $"{arrow} {snapshots[0].ProductivityGrade} → {snapshots[snapshots.Count - 1].ProductivityGrade} over {snapshots.Count} quarters ({delta:+0;-0;0} pts)";
                    }
                }
            }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to load employee trend data."); }
            DetailMetrics.ReplaceAll(new[]
            {
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

                DetailMetric.Header($"PRODUCTIVITY SCORE: {row.ProductivityScore:N0} ({row.ProductivityGrade})", row.ProductivityColor),
                new DetailMetric("Billable Rate", $"{row.BillableRateScore:N0}/100", "% of total hours on real billable projects. Weight: 30%."),
                new DetailMetric("Efficiency", $"{row.EfficiencyScore:N0}/100", "Fee/Hr percentile rank vs portfolio. 50 = median. Weight: 40%."),
                new DetailMetric("Project Health", $"{row.ProjectHealthScore:N0}/100", "% of hours on projects NOT over budget. Weight: 30%."),

                DetailMetric.Header("PEER COMPARISON", "#7C3AED"),
                new DetailMetric("Primary Type", string.IsNullOrWhiteSpace(row.PrimaryConstructionType) ? "—" : row.PrimaryConstructionType, "Construction type where this person spends the most hours"),
                new DetailMetric("Fee/Hr", row.FeePerHr.ToString("$#,##0"), "This employee's fee per production hour"),
                new DetailMetric("Peer Median Fee/Hr", row.PeerCount >= 2 ? row.PeerGroupMedianFeePerHr.ToString("$#,##0") : "—", $"Median fee/hr of {row.PeerCount} employees on same type"),
                new DetailMetric("vs Peers", row.PeerCount >= 2 ? $"{row.VsPeerPct:N0}%" : "—", "This employee's fee/hr as % of peer median. >100% = above peers"),

                DetailMetric.Header("TREND", "#0891B2"),
                new DetailMetric("Trajectory", string.IsNullOrWhiteSpace(trendLine) ? "No history yet" : trendLine,
                    "Score trajectory across quarterly snapshots. Snapshots save automatically each quarter."),

                DetailMetric.Explanation(
                    "How the Productivity Score works:\n\n" +
                    "Billable Rate (30%) — How much of their total time goes to real billable projects " +
                    "vs overhead, vacation, sick leave, and admin.\n\n" +
                    "Efficiency (40%) — How does their fee-per-hour compare to peers? " +
                    "50 means they're average; 100 means they generate more fee revenue per hour than anyone else.\n\n" +
                    "Project Health (30%) — Are the projects they work on staying within budget? " +
                    "100 means all their projects are on track; lower means they tend to be on projects that bleed hours.\n\n" +
                    "Example: 70% billable, 75th percentile efficiency, 80% healthy projects:\n" +
                    "(70 × 0.30) + (75 × 0.40) + (80 × 0.30) = 21 + 30 + 24 = 75 → Grade: B",
                    "#F0FDF4"),
            });
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
                filtered = filtered.Where(r => r.Fee > 0);
            else if (_selectedHoursFilter == "Has Hours + Fee")
                filtered = filtered.Where(r => r.TotalEngDraft > 0 && r.Fee > 0);
            else if (_selectedHoursFilter == "Fee ≥ $25K")
                filtered = filtered.Where(r => r.Fee >= 25_000);
            else if (_selectedHoursFilter == "Fee ≥ $25K + Hours")
                filtered = filtered.Where(r => r.Fee >= 25_000 && r.TotalEngDraft > 0);

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
                fee += r.Fee;
                eng += r.EngHrs;
                draft += r.DraftHrs;
                allHrs += r.TotalAllHrs;
                billableHrs += r.BillableHrs;
                subCost += r.SubCost;
                arOutstanding += r.ArTotal;
                if (r.FeePerHr > 0 && r.TotalEngDraft >= MinProdHrs) feePerHrs.Add(r.FeePerHr);
                if (r.NetFeePerHr > 0 && r.TotalEngDraft >= MinProdHrs) netFeePerHrs.Add(r.NetFeePerHr);
                if (r.TotalEngDraft >= MinProdHrs) engPcts.Add(r.EngPct);
                if (r.Fee > 0 && r.TotalEngDraft >= MinProdHrs) subPcts.Add(r.SubPctOfFee);
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

            MedianFeePerHr = Median(feePerHrs);
            MeanFeePerHr = feePerHrs.Count == 0 ? 0 : feePerHrs.Sum() / feePerHrs.Count;
            // Percentiles need a sorted copy
            var sortedFph = new List<double>(feePerHrs);
            sortedFph.Sort();
            var n = sortedFph.Count;
            P25FeePerHr = n == 0 ? 0 : sortedFph[Math.Min(n - 1, Math.Max(0, (int)Math.Ceiling(n * 0.25) - 1))];
            P75FeePerHr = n == 0 ? 0 : sortedFph[Math.Min(n - 1, Math.Max(0, (int)Math.Ceiling(n * 0.75) - 1))];

            // Portfolio medians for detail card comparison (median resists outliers)
            AvgEngPct = Median(engPcts);
            AvgFeePerHr = MedianFeePerHr;
            AvgNetFeePerHr = Median(netFeePerHrs);
            AvgSubPct = Median(subPcts);
            AvgBillablePct = Median(billPcts);

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
            MedianAbsError = Median(absErrors);
        }

        private void RecomputePmSummary(List<HistoricalProjectRow> visible)
        {
            var groups = visible
                .Where(r => !string.IsNullOrWhiteSpace(r.Pm))
                .GroupBy(r => r.Pm, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var rows = g.ToList();
                    var comparable = rows.Where(r => r.EstEngBudget > 0 && r.EngHrs > 0).ToList();
                    var totalAr = rows.Sum(r => r.ArTotal);
                    var ar90 = rows.Sum(r => r.Ar90Plus);
                    var healthyCount = rows.Count(r => r.EstEngBudget <= 0 || r.EngHrs <= r.EstEngBudget * 1.35);
                    var clientGroups = rows.Where(r => !string.IsNullOrWhiteSpace(r.ClientId)).GroupBy(r => r.ClientId, StringComparer.OrdinalIgnoreCase).ToList();
                    var uniqueClients = clientGroups.Count;
                    var repeatClients = clientGroups.Count(cg => cg.Count() >= 2);

                    return new PmPerformanceSummaryRow
                    {
                        Pm = g.Key,
                        ProjectCount = rows.Count,
                        TotalFee = rows.Sum(r => r.Fee),
                        TotalFeeBilled = rows.Sum(r => r.FeeBilled),
                        TotalEngHrs = rows.Sum(r => r.EngHrs),
                        TotalDraftHrs = rows.Sum(r => r.DraftHrs),
                        TotalAllHrs = rows.Sum(r => r.TotalAllHrs),
                        TotalSubCost = rows.Sum(r => r.SubCost),
                        TotalArOutstanding = totalAr,
                        TotalAr90Plus = ar90,
                        AvgEngDelta = comparable.Count > 0 ? comparable.Average(r => r.EngBudgetDelta) : 0,
                        AvgDraftDelta = comparable.Count > 0 ? comparable.Average(r => r.DraftBudgetDelta) : 0,
                        DeliveryHealthScore = rows.Count > 0 ? (double)healthyCount / rows.Count * 100 : 100,
                        ArManagementScore = totalAr > 0 ? Math.Max(0, (1.0 - ar90 / totalAr) * 100) : 100,
                        UniqueClients = uniqueClients,
                        RepeatClients = repeatClients,
                    };
                })
                .OrderByDescending(r => r.TotalFee)
                .ToList();

            ScorePmDmGroups(groups);
            PmSummaryRows.ReplaceAll(groups);
        }

        private void RecomputeDmSummary(List<HistoricalProjectRow> visible)
        {
            var groups = visible
                .Where(r => !string.IsNullOrWhiteSpace(r.DraftingManager))
                .GroupBy(r => r.DraftingManager, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var rows = g.ToList();
                    var comparable = rows.Where(r => r.EstEngBudget > 0 && r.EngHrs > 0).ToList();
                    var totalAr = rows.Sum(r => r.ArTotal);
                    var ar90 = rows.Sum(r => r.Ar90Plus);
                    var healthyCount = rows.Count(r => r.EstEngBudget <= 0 || r.EngHrs <= r.EstEngBudget * 1.35);
                    var clientGroups = rows.Where(r => !string.IsNullOrWhiteSpace(r.ClientId)).GroupBy(r => r.ClientId, StringComparer.OrdinalIgnoreCase).ToList();
                    var uniqueClients = clientGroups.Count;
                    var repeatClients = clientGroups.Count(cg => cg.Count() >= 2);

                    return new PmPerformanceSummaryRow
                    {
                        Pm = g.Key,
                        ProjectCount = rows.Count,
                        TotalFee = rows.Sum(r => r.Fee),
                        TotalFeeBilled = rows.Sum(r => r.FeeBilled),
                        TotalEngHrs = rows.Sum(r => r.EngHrs),
                        TotalDraftHrs = rows.Sum(r => r.DraftHrs),
                        TotalAllHrs = rows.Sum(r => r.TotalAllHrs),
                        TotalSubCost = rows.Sum(r => r.SubCost),
                        TotalArOutstanding = totalAr,
                        TotalAr90Plus = ar90,
                        AvgEngDelta = comparable.Count > 0 ? comparable.Average(r => r.EngBudgetDelta) : 0,
                        AvgDraftDelta = comparable.Count > 0 ? comparable.Average(r => r.DraftBudgetDelta) : 0,
                        DeliveryHealthScore = rows.Count > 0 ? (double)healthyCount / rows.Count * 100 : 100,
                        ArManagementScore = totalAr > 0 ? Math.Max(0, (1.0 - ar90 / totalAr) * 100) : 100,
                        UniqueClients = uniqueClients,
                        RepeatClients = repeatClients,
                    };
                })
                .OrderByDescending(r => r.TotalFee)
                .ToList();

            ScorePmDmGroups(groups);
            DmSummaryRows.ReplaceAll(groups);
        }

        private static void ScorePmDmGroups(List<PmPerformanceSummaryRow> groups)
        {
            if (groups.Count == 0) return;

            // Estimation Accuracy: percentile rank of |AvgEngDelta| — LOWER absolute delta = HIGHER score
            var absDeltas = groups.Where(r => Math.Abs(r.AvgEngDelta) > 0).Select(r => Math.Abs(r.AvgEngDelta)).OrderBy(v => v).ToList();
            var nDelta = absDeltas.Count;

            // Revenue Efficiency: percentile rank of AvgFeePerHr — HIGHER = better
            var feePerHrs = groups.Where(r => r.AvgFeePerHr > 0).Select(r => r.AvgFeePerHr).OrderBy(v => v).ToList();
            var nFee = feePerHrs.Count;

            foreach (var row in groups)
            {
                // Estimation Accuracy — inverted percentile (lower delta = higher rank)
                if (nDelta > 1 && Math.Abs(row.AvgEngDelta) > 0)
                {
                    var absDelta = Math.Abs(row.AvgEngDelta);
                    var above = absDeltas.Count(v => v > absDelta);
                    row.EstimationAccuracyScore = Math.Min(100, ((double)above / (nDelta - 1)) * 100);
                }

                // Revenue Efficiency — standard percentile
                if (nFee > 1 && row.AvgFeePerHr > 0)
                {
                    var below = feePerHrs.Count(v => v < row.AvgFeePerHr);
                    row.RevenueEfficiencyScore = Math.Min(100, ((double)below / (nFee - 1)) * 100);
                }

                // Composite: Delivery 30% + Estimation 30% + Revenue 20% + AR 20%
                row.PerformanceScore = Math.Round(
                    row.DeliveryHealthScore * 0.30 +
                    row.EstimationAccuracyScore * 0.30 +
                    row.RevenueEfficiencyScore * 0.20 +
                    row.ArManagementScore * 0.20, 0);
            }
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
                var rows = visible.Where(r => r.Fee >= min && r.Fee < max).ToList();
                if (rows.Count == 0) continue;
                var totalEng = rows.Sum(r => r.EngHrs);
                var totalDraft = rows.Sum(r => r.DraftHrs);
                var totalProd = totalEng + totalDraft;
                var totalAll = rows.Sum(r => r.TotalAllHrs);
                var totalFee = rows.Sum(r => r.Fee);
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
                    MedianFeePerHr = Median(bandFeePerHrs),
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
                    var totalFee = rows.Sum(r => r.Fee);
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
                        MedianFeePerHr = Median(bandFeePerHrs),
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
            var results = visible
                .Where(r => r.OpenYear.HasValue)
                .GroupBy(r => r.OpenYear!.Value)
                .Select(g =>
                {
                    var rows = g.ToList();
                    var totalEng = rows.Sum(r => r.EngHrs);
                    var totalDraft = rows.Sum(r => r.DraftHrs);
                    var totalProd = totalEng + totalDraft;
                    var totalAll = rows.Sum(r => r.TotalAllHrs);
                    var totalFee = rows.Sum(r => r.Fee);
                    var overheadHrs = rows.Sum(r => r.AdminHrs + r.NonBillHrs);
                    return new YearTrendRow
                    {
                        Year = g.Key,
                        ProjectCount = rows.Count,
                        TotalFee = totalFee,
                        AvgFee = rows.Count > 0 ? totalFee / rows.Count : 0,
                        AvgFeePerHr = totalProd > 0 ? totalFee / totalProd : 0,
                        AvgNetFeePerHr = totalProd > 0 ? (totalFee - rows.Sum(r => r.SubCost)) / totalProd : 0,
                        WeightedEngPct = totalProd > 0 ? totalEng / totalProd : 0,
                        WeightedBillablePct = totalAll > 0 ? rows.Sum(r => r.BillableHrs) / totalAll : 0,
                        AvgSubPct = totalFee > 0 ? rows.Sum(r => r.SubCost) / totalFee : 0,
                        WeightedOverheadRatio = totalAll > 0 ? overheadHrs / totalAll : 0,
                        TotalArOutstanding = rows.Sum(r => r.ArTotal),
                        FirmBillablePct = _firmUtilization?.ByYear.TryGetValue(g.Key, out var u) == true && u.Total > 0
                            ? u.Billable / u.Total : 0,
                    };
                })
                .OrderByDescending(r => r.Year)
                .ToList();

            YearTrendRows.ReplaceAll(results);
        }

        private void RecomputeSimilarProjects()
        {
            var sel = _selectedRow;
            if (sel == null || sel.Fee <= 0)
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

            List<HistoricalProjectRow>? pool = null;
            foreach (var pct in new[] { 0.30, 0.50 })
            {
                var feeMin = sel.Fee * (1.0 - pct);
                var feeMax = sel.Fee * (1.0 + pct);

                var candidates = _allRows
                    .Where(r => r.Wbs1 != sel.Wbs1
                        && !ActiveStatuses.Contains((r.Status ?? "").Trim())
                        && r.TotalEngDraft >= 50
                        && r.Fee >= feeMin && r.Fee <= feeMax)
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
                .OrderBy(r => Math.Abs(r.Fee - sel.Fee))
                .Take(8)
                .ToList();

            SimilarProjects.ReplaceAll(peers);
            PeerCount = peers.Count;

            if (peers.Count > 0)
            {
                PeerMedianEngHrs = Median(peers.Select(r => r.EngHrs).ToList());
                PeerMedianDraftHrs = Median(peers.Select(r => r.DraftHrs).ToList());
                PeerMedianTotalHrs = Median(peers.Select(r => r.TotalEngDraft).ToList());
                PeerMedianFeePerHr = Median(peers.Where(r => r.FeePerHr > 0).Select(r => r.FeePerHr).ToList());
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

            var projectLookup = new Dictionary<string, HistoricalProjectRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in visible)
                projectLookup.TryAdd(r.Wbs1, r);

            // Group ALL employee hours (including overhead/admin projects) for total hours,
            // but only count hours from visible (billable) projects for billable metrics.
            var groups = _employeeProjectHours
                .GroupBy(ep => ep.EmployeeId, StringComparer.OrdinalIgnoreCase)
                .Select(g =>
                {
                    var allEntries = g.ToList();
                    var billableEntries = allEntries.Where(e => projectLookup.ContainsKey(e.Wbs1)).ToList();

                    // Primary construction type — the type where this employee spent the most hours
                    var primaryType = billableEntries
                        .Where(e => projectLookup.TryGetValue(e.Wbs1, out _))
                        .GroupBy(e => (projectLookup[e.Wbs1].ConstructionType ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                        .Where(tg => !string.IsNullOrWhiteSpace(tg.Key))
                        .OrderByDescending(tg => tg.Sum(x => x.EngHrs + x.DraftHrs))
                        .Select(tg => tg.Key)
                        .FirstOrDefault() ?? "";

                    // Production hours from billable projects only
                    var engHrs = billableEntries.Sum(e => e.EngHrs);
                    var draftHrs = billableEntries.Sum(e => e.DraftHrs);
                    var billableHrs = billableEntries.Sum(e => e.TotalHrs);

                    // Total hours from ALL projects (including overhead, vacation, etc.)
                    var totalAllHrs = allEntries.Sum(e => e.TotalHrs);

                    var attributedFee = 0.0;
                    var projectFees = new List<double>();
                    foreach (var entry in billableEntries)
                    {
                        if (projectLookup.TryGetValue(entry.Wbs1, out var proj) && proj.TotalEngDraft > 0)
                        {
                            var entryProd = entry.EngHrs + entry.DraftHrs;
                            var share = entryProd / proj.TotalEngDraft;
                            attributedFee += proj.Fee * share;
                            projectFees.Add(proj.Fee);
                        }
                    }

                    // Project Health: % of hours on projects that are NOT over-budget
                    var healthyHrs = 0.0;
                    var totalProjHrs = 0.0;
                    foreach (var entry in billableEntries)
                    {
                        if (projectLookup.TryGetValue(entry.Wbs1, out var proj))
                        {
                            var entryHrs = entry.EngHrs + entry.DraftHrs;
                            totalProjHrs += entryHrs;
                            // Project is "healthy" if actual eng hours haven't exceeded estimated budget
                            // (or if there's no estimate to compare against)
                            var isHealthy = proj.EstEngBudget <= 0 || proj.EngHrs <= proj.EstEngBudget * 1.35;
                            if (isHealthy) healthyHrs += entryHrs;
                        }
                    }

                    return new EmployeeSummaryRow
                    {
                        EmployeeId = g.Key,
                        EmployeeName = allEntries.First().EmployeeName,
                        ProjectCount = billableEntries.Select(e => e.Wbs1).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                        TotalEngHrs = engHrs,
                        TotalDraftHrs = draftHrs,
                        TotalBillableHrs = billableHrs,
                        TotalAllHrs = totalAllHrs,
                        AttributedFee = attributedFee,
                        AvgProjectFee = projectFees.Count > 0 ? projectFees.Average() : 0,
                        PrimaryRole = engHrs >= draftHrs ? "Engineering" : "Drafting",
                        PrimaryConstructionType = primaryType,
                        // Raw scores — efficiency normalized in second pass
                        BillableRateScore = Math.Min(100, (totalAllHrs > 0 ? billableHrs / totalAllHrs : 0) * 100),
                        ProjectHealthScore = totalProjHrs > 0 ? (healthyHrs / totalProjHrs) * 100 : 100,
                    };
                })
                .Where(r => r.TotalAllHrs > 0)
                .OrderByDescending(r => r.AttributedFee)
                .ToList();

            // Second pass: normalize Efficiency Score using portfolio FeePerHr distribution
            if (groups.Count > 0)
            {
                var feePerHrs = groups.Where(r => r.FeePerHr > 0).Select(r => r.FeePerHr).OrderBy(v => v).ToList();
                var n = feePerHrs.Count;
                foreach (var row in groups)
                {
                    if (n > 1 && row.FeePerHr > 0)
                    {
                        // Percentile rank: (values below this) / (n - 1) gives 0-100 range
                        // Lowest = 0, highest = 100, properly distributed
                        var below = feePerHrs.Count(v => v < row.FeePerHr);
                        row.EfficiencyScore = Math.Min(100, ((double)below / (n - 1)) * 100);
                    }
                    else if (n == 1 && row.FeePerHr > 0)
                    {
                        // Only one employee with hours — default to median
                        row.EfficiencyScore = 50;
                    }
                    // else: no FeePerHr data — EfficiencyScore stays at 50 (default)

                    // Composite: Billable Rate 30% + Efficiency 40% + Project Health 30%
                    row.ProductivityScore = Math.Round(
                        row.BillableRateScore * 0.30 +
                        row.EfficiencyScore * 0.40 +
                        row.ProjectHealthScore * 0.30, 0);

                    // Peer comparison: compare Fee/Hr against employees with the same primary construction type
                    if (row.FeePerHr > 0 && !string.IsNullOrWhiteSpace(row.PrimaryConstructionType))
                    {
                        var peerGroup = groups
                            .Where(p => p != row
                                && p.FeePerHr > 0
                                && p.PrimaryConstructionType.Equals(row.PrimaryConstructionType, StringComparison.OrdinalIgnoreCase))
                            .Select(p => p.FeePerHr)
                            .ToList();

                        if (peerGroup.Count >= 2)
                        {
                            var median = Shared.PeerBudgetEstimator.Median(peerGroup);
                            row.PeerGroupMedianFeePerHr = median;
                            row.VsPeerPct = median > 0 ? (row.FeePerHr / median) * 100 : 0;
                            row.PeerCount = peerGroup.Count;
                        }
                    }
                }
            }

            EmployeeSummaryRows.ReplaceAll(groups);

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

                    }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "Failed to save employee score snapshots."); }
                });
            }
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

        private static double Median(List<double> values)
        {
            if (values.Count == 0) return 0;
            var sorted = new List<double>(values);
            sorted.Sort();
            var n = sorted.Count;
            return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2.0;
        }
    }
}
