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
        private double _totalArOutstanding;
        private double _firmBillablePct;
        private double _firmNonBillableHrs;
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
        public ObservableCollection<string> ViewModeOptions { get; } = new() { "Projects", "PM Summary", "Fee Bands", "YoY Trend" };

        public ObservableCollection<string> StatusOptions { get; } = new() { "All", "Active", "Closed" };
        public ObservableCollection<string> HoursFilterOptions { get; } = new() { "All", "Has Hours", "Has Fee", "Has Hours + Fee", "Fee ≥ $25K", "Fee ≥ $25K + Hours" };
        public ObservableCollection<string> PmOptions { get; } = new() { "All" };
        public ObservableCollection<string> YearOptions { get; } = new() { "All" };

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

        public string SearchText
        {
            get => _searchText;
            set { if (SetField(ref _searchText, value)) ApplyFilter(); }
        }

        public string ViewMode
        {
            get => _viewMode;
            set { if (SetField(ref _viewMode, value)) { OnPropertyChanged(nameof(IsProjectView)); OnPropertyChanged(nameof(IsPmSummaryView)); OnPropertyChanged(nameof(IsFeeBandView)); OnPropertyChanged(nameof(IsYoYTrendView)); } }
        }

        public bool IsProjectView => _viewMode == "Projects";
        public bool IsPmSummaryView => _viewMode == "PM Summary";
        public bool IsFeeBandView => _viewMode == "Fee Bands";
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
        public double TotalArOutstanding { get => _totalArOutstanding; private set => SetField(ref _totalArOutstanding, value); }
        public double FirmBillablePct { get => _firmBillablePct; private set => SetField(ref _firmBillablePct, value); }
        public double FirmNonBillableHrs { get => _firmNonBillableHrs; private set => SetField(ref _firmNonBillableHrs, value); }

        // Portfolio averages (for detail card "vs portfolio" comparisons)
        public double AvgEngPct { get => _avgEngPct; private set => SetField(ref _avgEngPct, value); }
        public double AvgFeePerHr { get => _avgFeePerHr; private set => SetField(ref _avgFeePerHr, value); }
        public double AvgNetFeePerHr { get => _avgNetFeePerHr; private set => SetField(ref _avgNetFeePerHr, value); }
        public double AvgSubPct { get => _avgSubPct; private set => SetField(ref _avgSubPct, value); }
        public double AvgBillablePct { get => _avgBillablePct; private set => SetField(ref _avgBillablePct, value); }

        public int LoadedCount => _allRows.Count;

        public void SetUtilization(FirmUtilizationStats stats)
        {
            _firmUtilization = stats;
            FirmBillablePct = stats.BillablePct;
            FirmNonBillableHrs = stats.TotalHrs - stats.BillableHrs;
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

            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filtered = _allRows.AsEnumerable();

            if (!string.IsNullOrEmpty(_selectedStatus) && _selectedStatus == "Active")
                filtered = filtered.Where(r => ActiveStatuses.Contains(r.Status.Trim()));
            else if (!string.IsNullOrEmpty(_selectedStatus) && _selectedStatus == "Closed")
                filtered = filtered.Where(r => !ActiveStatuses.Contains(r.Status.Trim()));

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
                filtered = filtered.Where(r => r.Pm.Equals(_selectedPm, StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                var term = _searchText.Trim();
                filtered = filtered.Where(r =>
                    r.Wbs1.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    r.Name.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var list = filtered.ToList();
            Rows.ReplaceAll(list);
            RecomputeSummary(list);
            RecomputePmSummary(list);
            RecomputeFeeBands(list);
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
            P25FeePerHr = n == 0 ? 0 : sortedFph[Math.Min(n - 1, (int)(n * 0.25))];
            P75FeePerHr = n == 0 ? 0 : sortedFph[Math.Min(n - 1, (int)(n * 0.75))];

            // Portfolio medians for detail card comparison (median resists outliers)
            AvgEngPct = Median(engPcts);
            AvgFeePerHr = MedianFeePerHr;
            AvgNetFeePerHr = Median(netFeePerHrs);
            AvgSubPct = Median(subPcts);
            AvgBillablePct = Median(billPcts);

            // Budget accuracy — closed projects only (active projects have incomplete hours),
            // ≥50 eng hrs, ±35% threshold
            var engDeltas = new List<double>();
            var draftDeltas = new List<double>();
            var withinThreshold = 0;
            var totalComparable = 0;
            foreach (var r in visible)
            {
                var isClosed = !ActiveStatuses.Contains(r.Status.Trim());
                if (r.EstEngBudget > 0 && r.EngHrs >= MinProdHrs && isClosed)
                {
                    engDeltas.Add(r.EngBudgetDelta);
                    draftDeltas.Add(r.DraftBudgetDelta);
                    totalComparable++;
                    var engRatio = r.EngHrs / r.EstEngBudget;
                    if (engRatio >= 0.65 && engRatio <= 1.35) withinThreshold++;
                }
            }
            MeanEngDelta = engDeltas.Count > 0 ? engDeltas.Sum() / engDeltas.Count : 0;
            MeanDraftDelta = draftDeltas.Count > 0 ? draftDeltas.Sum() / draftDeltas.Count : 0;
            BudgetAccuracyPct = totalComparable > 0 ? (double)withinThreshold / totalComparable : 0;
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
                        TotalArOutstanding = rows.Sum(r => r.ArTotal),
                        TotalAr90Plus = rows.Sum(r => r.Ar90Plus),
                        AvgEngDelta = comparable.Count > 0 ? comparable.Average(r => r.EngBudgetDelta) : 0,
                        AvgDraftDelta = comparable.Count > 0 ? comparable.Average(r => r.DraftBudgetDelta) : 0,
                    };
                })
                .OrderByDescending(r => r.TotalFee)
                .ToList();

            PmSummaryRows.ReplaceAll(groups);
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

                // Per-band budget accuracy: closed projects with 50+ eng hrs, ±35%
                var comparable = rows.Where(r =>
                    !ActiveStatuses.Contains(r.Status.Trim()) && r.EstEngBudget > 0 && r.EngHrs >= MinHrs).ToList();
                var within = comparable.Count > 0
                    ? comparable.Count(r => { var ratio = r.EngHrs / r.EstEngBudget; return ratio >= 0.65 && ratio <= 1.35; })
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

            // Find closed projects with 50+ production hours, fee within ±50%, same phase if available
            var feeMin = sel.Fee * 0.5;
            var feeMax = sel.Fee * 1.5;
            var phase = (sel.Phase ?? "").Trim();

            var candidates = _allRows
                .Where(r => r.Wbs1 != sel.Wbs1                               // not the same project
                    && !ActiveStatuses.Contains(r.Status.Trim())              // closed only
                    && r.TotalEngDraft >= 50                                  // meaningful hours
                    && r.Fee >= feeMin && r.Fee <= feeMax)                    // fee within ±50%
                .ToList();

            // Prefer same phase; if too few matches, use all phases
            var phaseMatches = string.IsNullOrWhiteSpace(phase)
                ? candidates
                : candidates.Where(r => (r.Phase ?? "").Trim().Equals(phase, StringComparison.OrdinalIgnoreCase)).ToList();

            var pool = phaseMatches.Count >= 3 ? phaseMatches : candidates;

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
