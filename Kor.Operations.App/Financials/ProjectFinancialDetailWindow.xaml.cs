#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Data.Odbc;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using static Kor.Operations.Core.MathHelpers;
using Kor.Operations.Financials.CfoMetrics;
using Microsoft.Extensions.DependencyInjection;
namespace Kor.Operations.Financials
{
    public partial class ProjectFinancialDetailWindow : Window
    {
        private readonly string _wbs1;
        private readonly double _projectHoursSpent;
        private readonly PortfolioHealthCounts? _portfolioCounts;
        private bool _teamLoaded;
        private bool _teamLoading;
        private bool _feeBreakdownExpanded;
        private readonly ObservableCollection<TeamMemberHoursRow> _teamRows = new();

        public ProjectFinancialDetailWindow(FinancialsProjectRow project)
            : this(project, portfolioCounts: null)
        {
        }

        public ProjectFinancialDetailWindow(FinancialsProjectRow project, PortfolioHealthCounts? portfolioCounts)
        {
            project ??= new FinancialsProjectRow();
            InitializeComponent();
            _wbs1 = (project.Wbs1 ?? string.Empty).Trim();
            _portfolioCounts = portfolioCounts;
            _projectHoursSpent =
                (project.EngHrs) +
                (project.DraftHrs) +
                (project.InspHrs) +
                (project.DocPrepHrs) +
                (project.GenHrs) +
                (project.AdminHrs) +
                (project.NonBillHrs);

            TeamBreakdownGrid.ItemsSource = _teamRows;
            DataContext = new ProjectFinancialDetailVm(project, _portfolioCounts);
            _ = LoadFeeElementsAsync();
        }

        private async Task LoadFeeElementsAsync()
        {
            try
            {
                var options = Kor.Operations.Services.AppServices.Get<DeltekOdbcOptions>();
                var catalog = string.IsNullOrWhiteSpace(options.Catalog) ? "C0000052267P_1_KOR00000000" : options.Catalog;
                var dsn = string.IsNullOrWhiteSpace(options.Dsn) ? "Deltek" : options.Dsn;
                var factory = new VpOdbcDsnFactory(dsn, options.User ?? "", options.Password ?? "",
                    () => new System.Collections.Generic.Dictionary<string, string>());

                // Raw data collectors
                var prRows = new System.Collections.Generic.List<(string Wbs2, string Wbs3, string ChargeType, string RevMethod, double Fee, string Name)>();
                double parentFee = 0;
                var revLookup = new System.Collections.Generic.Dictionary<(string, string), double>();
                var hrsLookup = new System.Collections.Generic.Dictionary<(string, string), double>();

                using var cn = factory.Create();
                await Task.Run(() => cn.Open());

                // Query 1: All PR elements at WBS2+WBS3 granularity
                using (var cmd = cn.CreateCommand())
                {
                    cmd.CommandTimeout = 30;
                    cmd.CommandText = $@"
SELECT COALESCE(WBS2,''), COALESCE(WBS3,''), COALESCE(ChargeType,''), COALESCE(RevenueMethod,''), Fee, Name
FROM [{catalog}].dbo.PR
WHERE WBS1 = ?
  AND WBS2 IS NOT NULL AND LTRIM(RTRIM(WBS2)) <> ''
ORDER BY WBS2, WBS3";
                    cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r = await Task.Run(() => cmd.ExecuteReader());
                    while (r.Read())
                    {
                        var wbs2 = r.GetString(0).Trim();
                        var wbs3 = r.GetString(1).Trim();
                        var ct = r.GetString(2).Trim();
                        var rm = r.GetString(3).Trim();
                        var fee = Convert.ToDouble(r.GetValue(4));
                        var name = r.GetString(5).Trim();
                        prRows.Add((wbs2, wbs3, ct, rm, fee, name));
                    }
                }

                // Query 2: Parent row
                using (var cmd2 = cn.CreateCommand())
                {
                    cmd2.CommandTimeout = 15;
                    cmd2.CommandText = $@"
SELECT Fee, COALESCE(ChargeType,''), COALESCE(RevenueMethod,''), Name
FROM [{catalog}].dbo.PR
WHERE WBS1 = ? AND (WBS2 IS NULL OR LTRIM(RTRIM(WBS2)) = '')";
                    cmd2.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r2 = await Task.Run(() => cmd2.ExecuteReader());
                    if (r2.Read())
                    {
                        parentFee = Convert.ToDouble(r2.GetValue(0));
                    }
                }

                // Query 3: PRSummaryMain revenue at WBS2+WBS3 granularity
                using (var cmd3 = cn.CreateCommand())
                {
                    cmd3.CommandTimeout = 30;
                    cmd3.CommandText = $@"
SELECT COALESCE(WBS2,''), COALESCE(WBS3,''),
       SUM(CASE WHEN BilledFee <> 0 THEN BilledFee ELSE COALESCE(Revenue,0) END) AS EffectiveRevenue,
       SUM(COALESCE(BilledTaxes,0))
FROM [{catalog}].dbo.PRSummaryMain
WHERE WBS1 = ?
GROUP BY WBS2, WBS3
ORDER BY WBS2, WBS3";
                    cmd3.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r3 = await Task.Run(() => cmd3.ExecuteReader());
                    while (r3.Read())
                    {
                        var wbs2 = r3.GetString(0).Trim();
                        var wbs3 = r3.GetString(1).Trim();
                        var rev = Convert.ToDouble(r3.GetValue(2));
                        revLookup[(wbs2, wbs3)] = rev;
                    }
                }

                // Query 4: tkDetail hours at WBS2+WBS3 granularity
                using (var cmd4 = cn.CreateCommand())
                {
                    cmd4.CommandTimeout = 30;
                    cmd4.CommandText = $@"
SELECT COALESCE(WBS2,''), COALESCE(WBS3,''),
       SUM(COALESCE(RegHrs,0)), SUM(COALESCE(OvtHrs,0))
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 = ?
GROUP BY WBS2, WBS3
ORDER BY WBS2, WBS3";
                    cmd4.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                    using var r4 = await Task.Run(() => cmd4.ExecuteReader());
                    while (r4.Read())
                    {
                        var wbs2 = r4.GetString(0).Trim();
                        var wbs3 = r4.GetString(1).Trim();
                        var reg = Convert.ToDouble(r4.GetValue(2));
                        var ovt = Convert.ToDouble(r4.GetValue(3));
                        var total = reg + ovt;
                        hrsLookup[(wbs2, wbs3)] = total;
                    }
                }

                // Classify elements into breakdown categories
                var initialPhases = new ObservableCollection<FeeBreakdownRow>();
                var fixedExtras = new ObservableCollection<FeeBreakdownRow>();
                var absorbedExtras = new ObservableCollection<FeeBreakdownRow>();
                var hourlyExtras = new ObservableCollection<FeeBreakdownRow>();

                foreach (var row in prRows)
                {
                    bool isExtra = row.Wbs2.StartsWith("X", StringComparison.OrdinalIgnoreCase);

                    // Look up hours for every row (keys use empty string for blank WBS3)
                    hrsLookup.TryGetValue((row.Wbs2, row.Wbs3), out var rowHrs);

                    if (!isExtra && string.IsNullOrEmpty(row.Wbs3))
                    {
                        // Initial contract phase (WBS2-level, e.g. 1.PD, 2.SD)
                        initialPhases.Add(new FeeBreakdownRow { Wbs2 = row.Wbs2, Wbs3 = row.Wbs3, Name = row.Name, Fee = row.Fee, Hours = rowHrs });
                    }
                    else if (isExtra && string.IsNullOrEmpty(row.Wbs3))
                    {
                        // X-group rollup row — skip to avoid double-counting
                        continue;
                    }
                    else if (isExtra && row.Fee > 0)
                    {
                        // Fixed fee extra (WBS3-level with fee, e.g. X.4/03 Restart Fee)
                        fixedExtras.Add(new FeeBreakdownRow { Wbs2 = row.Wbs2, Wbs3 = row.Wbs3, Name = row.Name, Fee = row.Fee, Hours = rowHrs });
                    }
                    else if (isExtra && row.Fee == 0)
                    {
                        revLookup.TryGetValue((row.Wbs2, row.Wbs3), out var rv);
                        if (rv > 0)
                        {
                            // Hourly extra — actual revenue billed
                            hourlyExtras.Add(new FeeBreakdownRow
                            {
                                Wbs2 = row.Wbs2,
                                Wbs3 = row.Wbs3,
                                Name = row.Name,
                                Revenue = rv,
                                Hours = rowHrs,
                            });
                        }
                        else if (rowHrs > 0)
                        {
                            // Absorbed extra — contract work tracked separately, covered by fixed fee
                            absorbedExtras.Add(new FeeBreakdownRow
                            {
                                Wbs2 = row.Wbs2,
                                Wbs3 = row.Wbs3,
                                Name = row.Name,
                                Hours = rowHrs,
                            });
                        }
                    }
                }

                // Calculate subtotals
                double initialFeeTotal = 0, initialHrsTotal = 0;
                foreach (var p in initialPhases) { initialFeeTotal += p.Fee; initialHrsTotal += p.Hours; }
                double fixedExtrasFeeTotal = 0, fixedExtrasHrsTotal = 0;
                foreach (var e in fixedExtras) { fixedExtrasFeeTotal += e.Fee; fixedExtrasHrsTotal += e.Hours; }
                double absorbedHrsTotal = 0;
                foreach (var a in absorbedExtras) absorbedHrsTotal += a.Hours;
                double hourlyRevTotal = 0, hourlyHrsTotal = 0;
                foreach (var h in hourlyExtras) { hourlyRevTotal += h.Revenue; hourlyHrsTotal += h.Hours; }

                double fixedFeeTotal = parentFee;
                double fixedFeeHrsTotal = initialHrsTotal + fixedExtrasHrsTotal + absorbedHrsTotal;
                double grandTotal = fixedFeeTotal + hourlyRevTotal;
                double grandHrsTotal = fixedFeeHrsTotal + hourlyHrsTotal;

                // Populate UI — Initial Contract
                InitialPhasesList.ItemsSource = initialPhases;
                InitialContractSubtotal.Text = $"${initialFeeTotal:#,##0}";
                InitialContractHrsSubtotal.Text = $"{initialHrsTotal:N1} hrs";

                // Fixed Fee Extras
                if (fixedExtras.Count > 0)
                {
                    FixedExtrasHeader.Visibility = Visibility.Visible;
                    FixedExtrasList.ItemsSource = fixedExtras;
                    FixedExtrasSep.Visibility = Visibility.Visible;
                    FixedExtrasSubtotalRow.Visibility = Visibility.Visible;
                    FixedExtrasSubtotal.Text = $"${fixedExtrasFeeTotal:#,##0}";
                    FixedExtrasHrsSubtotal.Text = $"{fixedExtrasHrsTotal:N1} hrs";
                }

                // Absorbed Extras (contract work tracked separately)
                if (absorbedExtras.Count > 0)
                {
                    AbsorbedExtrasHeader.Visibility = Visibility.Visible;
                    AbsorbedExtrasList.ItemsSource = absorbedExtras;
                    AbsorbedExtrasSep.Visibility = Visibility.Visible;
                    AbsorbedExtrasSubtotalRow.Visibility = Visibility.Visible;
                    AbsorbedExtrasHrsSubtotal.Text = $"{absorbedHrsTotal:N1} hrs";
                }

                FixedFeeTotal.Text = $"${fixedFeeTotal:#,##0}";
                FixedFeeHrsTotal.Text = $"{fixedFeeHrsTotal:N1} hrs";
                if (fixedFeeHrsTotal > 0)
                    FixedFeePerHrTotal.Text = $"${fixedFeeTotal / fixedFeeHrsTotal:N0}/hr";

                // Hourly Extras
                if (hourlyExtras.Count > 0)
                {
                    HourlyExtrasHeader.Visibility = Visibility.Visible;
                    HourlyExtrasList.ItemsSource = hourlyExtras;
                    HourlyExtrasSep.Visibility = Visibility.Visible;
                    HourlyExtrasSubtotalRow.Visibility = Visibility.Visible;
                    HourlyExtrasSubtotal.Text = $"${hourlyRevTotal:#,##0}";
                    HourlyExtrasHoursTotal.Text = $"{hourlyHrsTotal:N1} hrs";
                }

                GrandTotal.Text = $"${grandTotal:#,##0}";
                GrandTotalHours.Text = $"{grandHrsTotal:N1} hrs";
                if (grandHrsTotal > 0)
                    GrandTotalFeePerHr.Text = $"${grandTotal / grandHrsTotal:N0}/hr";

                // Populate summary card with hourly + total
                if (hourlyRevTotal > 0 || hourlyHrsTotal > 0)
                {
                    HourlyRevenueSummaryValue.Text = $"${hourlyRevTotal:#,##0}";
                    HourlyRevenueSummaryHours.Text = $"({hourlyHrsTotal:N1} hrs)";
                    HourlyRevenueSummaryPanel.Visibility = Visibility.Visible;

                    TotalValueSummaryValue.Text = $"${grandTotal:#,##0}";
                    TotalValueSummaryPanel.Visibility = Visibility.Visible;
                }

                // Summary line for collapsed view
                var summaryParts = $"Fixed: ${fixedFeeTotal:#,##0}";
                if (hourlyRevTotal > 0)
                    summaryParts += $"  +  Hourly: ${hourlyRevTotal:#,##0} ({hourlyHrsTotal:N1} hrs)";
                FeeBreakdownSummaryText.Text = summaryParts;
                FeeBreakdownSummaryTotal.Text = $"${grandTotal:#,##0}";

                // Start collapsed
                _feeBreakdownExpanded = true;
                FeeBreakdownContent.Visibility = Visibility.Visible;
                FeeBreakdownSummary.Visibility = Visibility.Collapsed;
                FeeBreakdownChevron.Text = "\u25BC";

                FeeBreakdownCard.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load fee elements for {Wbs1}", _wbs1);
            }
        }

        private void MetricDictionaryBtn_Click(object sender, RoutedEventArgs e)
        {
            var win = new FinancialMetricDictionaryWindow { Owner = this };
            win.Show();
        }

        private void FeeBreakdown_ToggleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _feeBreakdownExpanded = !_feeBreakdownExpanded;
            FeeBreakdownContent.Visibility = _feeBreakdownExpanded ? Visibility.Visible : Visibility.Collapsed;
            FeeBreakdownSummary.Visibility = _feeBreakdownExpanded ? Visibility.Collapsed : Visibility.Visible;
            FeeBreakdownChevron.Text = _feeBreakdownExpanded ? "\u25BC" : "\u25B6";
        }

        private async void HourlyExtra_DrillDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not FeeBreakdownRow row)
                return;

            // No drill-down if no hours logged
            if (row.Hours <= 0)
                return;

            // Toggle visibility if already loaded
            if (row.IsDetailLoaded)
            {
                row.DrillDownVisibility = row.DrillDownVisibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                return;
            }

            // Query tkDetail joined with EMMain for employee names + detail
            try
            {
                var options = Kor.Operations.Services.AppServices.Get<DeltekOdbcOptions>();
                var catalog = string.IsNullOrWhiteSpace(options.Catalog) ? "C0000052267P_1_KOR00000000" : options.Catalog;
                var dsn = string.IsNullOrWhiteSpace(options.Dsn) ? "Deltek" : options.Dsn;
                var factory = new VpOdbcDsnFactory(dsn, options.User ?? "", options.Password ?? "",
                    () => new System.Collections.Generic.Dictionary<string, string>());

                using var cn = factory.Create();
                await Task.Run(() => cn.Open());

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 30;
                cmd.CommandText = $@"
SELECT tk.Employee,
       COALESCE(em.FirstName,'') + ' ' + COALESCE(em.LastName,'') AS EmployeeName,
       tk.Category,
       SUM(COALESCE(tk.RegHrs,0) + COALESCE(tk.OvtHrs,0)) AS TotalHrs,
       MIN(tk.TransDate) AS FirstEntry,
       MAX(tk.TransDate) AS LastEntry
FROM [{catalog}].dbo.tkDetail tk
LEFT JOIN [{catalog}].dbo.EMMain em ON em.Employee = tk.Employee
WHERE tk.WBS1 = ? AND tk.WBS2 = ? AND tk.WBS3 = ?
GROUP BY tk.Employee, COALESCE(em.FirstName,'') + ' ' + COALESCE(em.LastName,''), tk.Category
HAVING SUM(COALESCE(tk.RegHrs,0) + COALESCE(tk.OvtHrs,0)) > 0
ORDER BY SUM(COALESCE(tk.RegHrs,0) + COALESCE(tk.OvtHrs,0)) DESC";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = row.Wbs2 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = row.Wbs3 });

                using var r = await Task.Run(() => cmd.ExecuteReader());
                while (r.Read())
                {
                    var empCode = r.GetString(0).Trim();
                    var name = r.GetString(1).Trim();
                    var category = r.IsDBNull(2) ? "" : r.GetString(2).Trim();
                    var hrs = Convert.ToDouble(r.GetValue(3));
                    var firstDate = r.IsDBNull(4) ? (DateTime?)null : r.GetDateTime(4);
                    var lastDate = r.IsDBNull(5) ? (DateTime?)null : r.GetDateTime(5);
                    row.EmployeeBreakdown.Add(new EmployeeHoursRow
                    {
                        EmployeeCode = empCode,
                        Employee = name,
                        Category = category,
                        Hours = hrs,
                        FirstEntry = firstDate,
                        LastEntry = lastDate,
                    });
                }

                row.IsDetailLoaded = true;
                row.DrillDownVisibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load employee drill-down for {Wbs1}/{Wbs2}/{Wbs3}", _wbs1, row.Wbs2, row.Wbs3);
            }
        }

        private async void EmployeeEntry_DrillDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe)
                return;

            // Walk up to find the FeeBreakdownRow context for WBS2/WBS3
            var empRow = fe.DataContext as EmployeeHoursRow;
            if (empRow == null) return;

            // Find parent FeeBreakdownRow by walking up the visual tree
            var parent = fe;
            FeeBreakdownRow? feeRow = null;
            while (parent != null)
            {
                parent = System.Windows.Media.VisualTreeHelper.GetParent(parent) as FrameworkElement;
                if (parent?.DataContext is FeeBreakdownRow fr)
                {
                    feeRow = fr;
                    break;
                }
            }
            if (feeRow == null) return;

            // Toggle if already loaded
            if (empRow.IsDetailLoaded)
            {
                empRow.DrillDownVisibility = empRow.DrillDownVisibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                return;
            }

            try
            {
                var options = Kor.Operations.Services.AppServices.Get<DeltekOdbcOptions>();
                var catalog = string.IsNullOrWhiteSpace(options.Catalog) ? "C0000052267P_1_KOR00000000" : options.Catalog;
                var dsn = string.IsNullOrWhiteSpace(options.Dsn) ? "Deltek" : options.Dsn;
                var factory = new VpOdbcDsnFactory(dsn, options.User ?? "", options.Password ?? "",
                    () => new System.Collections.Generic.Dictionary<string, string>());

                using var cn = factory.Create();
                await Task.Run(() => cn.Open());

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 30;
                cmd.CommandText = $@"
SELECT TransDate, Category, RegHrs, OvtHrs, TransComment
FROM [{catalog}].dbo.tkDetail
WHERE WBS1 = ? AND WBS2 = ? AND WBS3 = ? AND Employee = ? AND Category = ?
ORDER BY TransDate";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = feeRow.Wbs2 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = feeRow.Wbs3 });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = empRow.EmployeeCode });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = empRow.Category });

                var rawEntries = new System.Collections.Generic.List<(DateTime Date, double Reg, double Ovt, string? Comment)>();
                using var r = await Task.Run(() => cmd.ExecuteReader());
                while (r.Read())
                {
                    var date = r.GetDateTime(0);
                    var reg = Convert.ToDouble(r.GetValue(2));
                    var ovt = Convert.ToDouble(r.GetValue(3));
                    var comment = r.IsDBNull(4) ? null : r.GetString(4).Trim();
                    rawEntries.Add((date, reg, ovt, string.IsNullOrWhiteSpace(comment) ? null : comment));
                }

                // Group by month
                var grouped = new System.Collections.Generic.SortedDictionary<(int Year, int Month), System.Collections.Generic.List<(DateTime Date, double Reg, double Ovt, string? Comment)>>();
                foreach (var entry in rawEntries)
                {
                    var key = (entry.Date.Year, entry.Date.Month);
                    if (!grouped.ContainsKey(key))
                        grouped[key] = new();
                    grouped[key].Add(entry);
                }

                foreach (var kvp in grouped)
                {
                    var days = new ObservableCollection<TimesheetDayEntry>();
                    double monthTotal = 0;
                    foreach (var d in kvp.Value)
                    {
                        var total = d.Reg + d.Ovt;
                        monthTotal += total;
                        days.Add(new TimesheetDayEntry
                        {
                            Date = d.Date,
                            Hours = total,
                            OvtHrs = d.Ovt,
                            Comment = d.Comment,
                        });
                    }
                    empRow.MonthGroups.Add(new TimesheetMonthGroup
                    {
                        MonthLabel = new DateTime(kvp.Key.Year, kvp.Key.Month, 1).ToString("MMM yyyy"),
                        TotalHours = monthTotal,
                        Days = days,
                    });
                }

                empRow.IsDetailLoaded = true;
                empRow.DrillDownVisibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to load timesheet entries for {Employee} on {Wbs1}/{Wbs2}/{Wbs3}",
                    empRow.EmployeeCode, _wbs1, feeRow.Wbs2, feeRow.Wbs3);
            }
        }

        private async void TeamBreakdownExpander_Expanded(object sender, RoutedEventArgs e)
        {
            if (_teamLoaded || _teamLoading)
                return;

            if (string.IsNullOrWhiteSpace(_wbs1))
                return;

            _teamLoading = true;
            ShowTeamStatus("Loading team breakdown...");

            try
            {
                var options = Kor.Operations.Services.AppServices.Get<DeltekOdbcOptions>();
                var dsn = string.IsNullOrWhiteSpace(options.Dsn) ? "Deltek" : options.Dsn;
                var user = options.User ?? string.Empty;
                var pwd = options.Password ?? string.Empty;
                var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new System.Collections.Generic.Dictionary<string, string>());

                using var cn = factory.Create();
#if NET6_0_OR_GREATER
                await cn.OpenAsync().ConfigureAwait(true);
#else
                cn.Open();
#endif
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.CommandText = @"
SELECT 
    e.FirstName + ' ' + e.LastName AS EmployeeName,
    SUM(t.RegHrs + t.OvtHrs) AS TotalHours
FROM dbo.tkDetail t
LEFT JOIN dbo.EMMain e ON t.Employee = e.Employee
WHERE t.WBS1 = ?
GROUP BY e.FirstName, e.LastName
ORDER BY TotalHours DESC";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = _wbs1 });

#if NET6_0_OR_GREATER
                using var r = await cmd.ExecuteReaderAsync().ConfigureAwait(true);
#else
                using var r = cmd.ExecuteReader();
#endif
                _teamRows.Clear();
                while (r.Read())
                {
                    var name = r.IsDBNull(0) ? "" : (Convert.ToString(r.GetValue(0)) ?? "");
                    name = name.Trim();
                    if (string.IsNullOrWhiteSpace(name))
                        name = "(Unknown)";

                    double hrs = 0.0;
                    if (!r.IsDBNull(1))
                    {
                        var v = r.GetValue(1);
                        if (v is double d) hrs = d;
                        else if (v is float f) hrs = f;
                        else if (v is decimal m) hrs = (double)m;
                        else if (double.TryParse(Convert.ToString(v), out var parsed)) hrs = parsed;
                    }

                    _teamRows.Add(new TeamMemberHoursRow
                    {
                        EmployeeName = name,
                        TotalHours = hrs,
                        PercentOfProject = SafeDiv(hrs, _projectHoursSpent)
                    });
                }

                if (_teamRows.Count == 0)
                    ShowTeamStatus("No team hours found for this project.");
                else
                    HideTeamStatus();

                _teamLoaded = true;
            }
            catch (Exception ex)
            {
                _teamRows.Clear();
                ShowTeamStatus($"Unable to load team breakdown: {ex.Message}");
            }
            finally
            {
                _teamLoaded = true;
                _teamLoading = false;
            }
        }

        private void ShowTeamStatus(string msg)
        {
            TeamBreakdownStatus.Text = msg ?? "";
            TeamBreakdownStatus.Visibility = Visibility.Visible;
        }

        private void HideTeamStatus()
        {
            TeamBreakdownStatus.Text = "";
            TeamBreakdownStatus.Visibility = Visibility.Collapsed;
        }


        private sealed class ProjectFinancialDetailVm
        {
            public string ProjectName { get; }
            public string Wbs1 { get; }
            public string Pm { get; }
            public string Phase { get; }
            public string DeliveryConfidence { get; }
            public string DeliveryConfidenceSummary { get; }
            public string DeliveryConfidenceTooltip { get; }
            public DeliveryConfidenceLevel ConfidenceLevel { get; }
            public ObservableCollection<string> RiskDrivers { get; } = new();
            public Visibility RiskDriversVisibility { get; }
            public Visibility NoRiskDriversVisibility { get; }

            public double FixedFee { get; }
            public double Fee { get; }
            public double FeeBilled { get; }
            public double SubconsultantCost { get; }
            public double PercentBilled { get; }

            public double HoursSpent { get; }
            public double HoursBudgeted { get; }
            public double HoursRemaining { get; }
            public double PercentHoursSpent { get; }

            public double BacklogDollars { get; }
            public double BacklogPercent { get; }

            public Visibility BurnRiskVisibility { get; }
            public Visibility OverBudgetVisibility { get; }
            public Visibility OverbilledVisibility { get; }
            public Visibility SubconsultantCostVisibility { get; }

            public int TotalInspections { get; }
            public int LastMonthInspections { get; }

            public ObservableCollection<DisciplineRow> Disciplines { get; } = new();


            public ProjectFinancialDetailVm(FinancialsProjectRow p, PortfolioHealthCounts? portfolioCounts)
            {
                p ??= new FinancialsProjectRow();
                ProjectName = (p?.Name ?? string.Empty).Trim();
                Wbs1 = (p?.Wbs1 ?? string.Empty).Trim();
                Pm = (p?.Pm ?? string.Empty).Trim();
                Phase = (p?.Phase ?? string.Empty).Trim();
                TotalInspections = p?.TotalInspections ?? 0;
                LastMonthInspections = p?.LastMonthInspections ?? 0;

                FixedFee = p?.Fee ?? 0.0;
                Fee = p?.TotalFee ?? 0.0;
                FeeBilled = p?.FeeBilled ?? 0.0;
                SubconsultantCost = p?.SubconsultantCost ?? 0.0;
                PercentBilled = p?.PercentBilled ?? SafeDiv(FeeBilled, Fee);

                var eng = p?.EngHrs ?? 0.0;
                var draft = p?.DraftHrs ?? 0.0;
                var insp = p?.InspHrs ?? 0.0;
                var docPrep = p?.DocPrepHrs ?? 0.0;
                var gen = p?.GenHrs ?? 0.0;
                var admin = p?.AdminHrs ?? 0.0;
                var nonBill = p?.NonBillHrs ?? 0.0;

                HoursSpent = eng + draft;
                HoursBudgeted = (p?.DraftBudget ?? 0.0) + (p?.EngBudget ?? 0.0);
                HoursRemaining = HoursBudgeted - HoursSpent;
                PercentHoursSpent = SafeDiv(HoursSpent, HoursBudgeted);

                BacklogDollars = Fee - FeeBilled;
                BacklogPercent = SafeDiv(BacklogDollars, Fee);

                AddDiscipline("Eng", eng, HoursSpent);
                AddDiscipline("Draft", draft, HoursSpent);
                // Chk merged into Eng
                AddDiscipline("Insp", insp, HoursSpent);
                AddDiscipline("DocPrep", docPrep, HoursSpent);
                AddDiscipline("Gen", gen, HoursSpent);
                AddDiscipline("Admin", admin, HoursSpent);
                AddDiscipline("NonBill", nonBill, HoursSpent);

                BurnRiskVisibility = PercentHoursSpent > PercentBilled ? Visibility.Visible : Visibility.Collapsed;
                OverBudgetVisibility = HoursRemaining < 0 ? Visibility.Visible : Visibility.Collapsed;
                OverbilledVisibility = FeeBilled > Fee ? Visibility.Visible : Visibility.Collapsed;
                SubconsultantCostVisibility = SubconsultantCost > 0 ? Visibility.Visible : Visibility.Collapsed;

                var dc = DeliveryConfidenceCalculator.Compute(p);
                DeliveryConfidence = dc.Status;
                DeliveryConfidenceSummary = dc.Summary;
                DeliveryConfidenceTooltip = dc.Tooltip;
                ConfidenceLevel =
                    dc.Status == "Critical" ? DeliveryConfidenceLevel.Critical :
                    dc.Status == "At Risk" ? DeliveryConfidenceLevel.AtRisk :
                    dc.Status == "Watch" ? DeliveryConfidenceLevel.Stable :
                    DeliveryConfidenceLevel.HighConfidence;

                BuildRiskDrivers(p!, HoursSpent, HoursBudgeted, HoursRemaining, BacklogDollars);
                RiskDriversVisibility = RiskDrivers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                NoRiskDriversVisibility = RiskDrivers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;


            }

            private void AddDiscipline(string name, double hrs, double total)
            {
                Disciplines.Add(new DisciplineRow
                {
                    Discipline = name,
                    Hours = hrs,
                    PercentOfTotal = SafeDiv(hrs, total)
                });
            }


            private void BuildRiskDrivers(FinancialsProjectRow p, double hoursSpent, double hoursBudgeted, double hoursRemaining, double backlogDollars)
            {
                var candidates = new List<(int Severity, double Magnitude, string Text)>(8);

                // 0 = most severe, higher is less severe.
                var engBudget = p?.EngBudget ?? 0.0;
                var engHours = p?.EngHrs ?? 0.0;
                if (engBudget > 0.0 && engHours > engBudget)
                {
                    var pctOver = SafeDiv(engHours - engBudget, engBudget);
                    candidates.Add((0, pctOver, $"Engineering hours are {pctOver:P0} over budget."));
                }

                if (Fee > 0.0 && FeeBilled > Fee)
                {
                    var pctOver = SafeDiv(FeeBilled - Fee, Fee);
                    candidates.Add((0, pctOver, $"Billed amount is {pctOver:P0} more than the total project fee."));
                }

                if (hoursBudgeted > 0.0)
                {
                    var thresh = 0.10 * hoursBudgeted;
                    if (hoursRemaining >= 0.0 && hoursRemaining < thresh)
                    {
                        var pctRemain = SafeDiv(hoursRemaining, hoursBudgeted);
                        candidates.Add((2, 1.0 - pctRemain, $"Remaining hours are below 10% of budget ({pctRemain:P0} remaining)."));
                    }
                }

                if (backlogDollars < 0.0)
                {
                    candidates.Add((1, Math.Abs(backlogDollars), $"Backlog is negative ({backlogDollars:C0})."));
                }

                foreach (var c in candidates
                    .OrderBy(x => x.Severity)
                    .ThenByDescending(x => x.Magnitude)
                    .Take(3))
                {
                    RiskDrivers.Add(c.Text);
                }
            }
        }

        private sealed class DisciplineRow
        {
            public string Discipline { get; set; } = "";
            public double Hours { get; set; }
            public double PercentOfTotal { get; set; }
        }

        private sealed class TeamMemberHoursRow
        {
            public string EmployeeName { get; set; } = "";
            public double TotalHours { get; set; }
            public double PercentOfProject { get; set; }
        }

        internal sealed class CfoMetricDisplayRow
        {
            public string Name { get; init; } = "";
            public string ValueDisplay { get; init; } = "";
            public string Tooltip { get; init; } = "";

            public static CfoMetricDisplayRow FromMetric(ICfoMetric m, decimal value, ProjectData ctx)
            {
                var display = FormatValue(m, value, ctx);
                var formula = GetFormulaIfPresent(m);
                var tip = string.IsNullOrWhiteSpace(formula) ? m.Description : $"{m.Description}\n\nFORMULA:\n{formula}";

                return new CfoMetricDisplayRow
                {
                    Name = m.Name,
                    ValueDisplay = display,
                    Tooltip = tip
                };
            }

            private static string GetFormulaIfPresent(ICfoMetric m)
            {
                return m switch
                {
                    DeliveryConfidenceMetric x => x.Formula,
                    PercentHoursSpentMetric x => x.Formula,
                    BudgetBurnRateMetric x => x.Formula,
                    PortfolioHealthCountsMetric x => x.Formula,
                    _ => ""
                };
            }

            private static string FormatValue(ICfoMetric m, decimal value, ProjectData ctx)
            {
                if (m is DeliveryConfidenceMetric)
                {
                    var label = ctx.DeliveryConfidenceLevel switch
                    {
                        DeliveryConfidenceLevel.Critical => "Critical",
                        DeliveryConfidenceLevel.AtRisk => "At Risk",
                        DeliveryConfidenceLevel.Stable => "Watch",
                        DeliveryConfidenceLevel.HighConfidence => "High Confidence",
                        _ => "Unknown"
                    };
                    return $"{label} ({value.ToString("0", CultureInfo.CurrentCulture)})";
                }

                if (m is PercentHoursSpentMetric)
                    return value.ToString("P1", CultureInfo.CurrentCulture);

                if (m is BudgetBurnRateMetric)
                    return value == 0m ? "0.00x" : value.ToString("0.00x", CultureInfo.CurrentCulture);

                if (m is PortfolioHealthCountsMetric)
                {
                    if (ctx.PortfolioCounts == null)
                        return "N/A";
                    return value.ToString("N0", CultureInfo.CurrentCulture);
                }

                return value.ToString(CultureInfo.CurrentCulture);
            }
        }
    }

    internal sealed class FeeBreakdownRow : System.ComponentModel.INotifyPropertyChanged
    {
        public string Wbs2 { get; init; } = "";
        public string Wbs3 { get; init; } = "";
        public string Name { get; init; } = "";
        public double Fee { get; init; }
        public double Revenue { get; init; }
        public double Hours { get; init; }
        public string DisplayAmount => Fee > 0 ? $"${Fee:#,##0}" : $"${Revenue:#,##0}";
        public string DisplayHours => Hours > 0 ? $"{Hours:N1} hrs" : "";
        public string DisplayFeePerHour
        {
            get
            {
                var amount = Fee > 0 ? Fee : Revenue;
                return (amount > 0 && Hours > 0) ? $"${amount / Hours:N0}/hr" : "";
            }
        }

        public double FeePerHour
        {
            get
            {
                var amount = Fee > 0 ? Fee : Revenue;
                return (amount > 0 && Hours > 0) ? amount / Hours : 0;
            }
        }

        public bool IsBelowTarget => FeePerHour > 0 && FeePerHour < AnalyticsThresholds.DefaultTargetBillingRate;
        public System.Windows.Media.Brush FeePerHourForeground => IsBelowTarget
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(220, 38, 38))   // red-600
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(107, 114, 128)); // gray-500
        public System.Windows.Media.Brush RowBackground => IsBelowTarget
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 226, 226)) // red-100 #FEE2E2
            : System.Windows.Media.Brushes.Transparent;

        public Visibility HasHoursVisibility => Hours > 0 ? Visibility.Visible : Visibility.Hidden;
        public ObservableCollection<EmployeeHoursRow> EmployeeBreakdown { get; } = new();
        public bool IsDetailLoaded { get; set; }

        private Visibility _drillDownVisibility = Visibility.Collapsed;
        public Visibility DrillDownVisibility
        {
            get => _drillDownVisibility;
            set { _drillDownVisibility = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DrillDownVisibility))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    internal sealed class EmployeeHoursRow : System.ComponentModel.INotifyPropertyChanged
    {
        public string EmployeeCode { get; init; } = "";
        public string Employee { get; init; } = "";
        public string Category { get; init; } = "";
        public double Hours { get; init; }
        public DateTime? FirstEntry { get; init; }
        public DateTime? LastEntry { get; init; }
        public string DisplayHours => $"{Hours:N1} hrs";
        public string DisplayDetail
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrEmpty(Category)) parts.Add(Category);
                if (FirstEntry.HasValue && LastEntry.HasValue)
                {
                    parts.Add(FirstEntry.Value == LastEntry.Value
                        ? FirstEntry.Value.ToString("MMM yyyy")
                        : $"{FirstEntry.Value:MMM yyyy} - {LastEntry.Value:MMM yyyy}");
                }
                return parts.Count > 0 ? string.Join("  |  ", parts) : "";
            }
        }

        public ObservableCollection<TimesheetMonthGroup> MonthGroups { get; } = new();
        public bool IsDetailLoaded { get; set; }

        private Visibility _drillDownVisibility = Visibility.Collapsed;
        public Visibility DrillDownVisibility
        {
            get => _drillDownVisibility;
            set { _drillDownVisibility = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(DrillDownVisibility))); }
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    internal sealed class TimesheetMonthGroup
    {
        public string MonthLabel { get; init; } = "";
        public double TotalHours { get; init; }
        public string DisplayTotal => $"{TotalHours:N1} hrs";
        public ObservableCollection<TimesheetDayEntry> Days { get; init; } = new();
    }

    internal sealed class TimesheetDayEntry
    {
        public DateTime Date { get; init; }
        public double Hours { get; init; }
        public double OvtHrs { get; init; }
        public string? Comment { get; init; }
        public string DisplayDay => Date.ToString("dd");
        public string DisplayHours => $"{Hours:N1}";
        public string Tooltip
        {
            get
            {
                var line = $"{Date:ddd MMM dd, yyyy} — {Hours:N1} hrs";
                if (OvtHrs > 0) line += $" ({Hours - OvtHrs:N1} reg + {OvtHrs:N1} ovt)";
                if (!string.IsNullOrWhiteSpace(Comment)) line += $"\n{Comment}";
                return line;
            }
        }
        public System.Windows.Media.Brush Background => OvtHrs > 0
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(254, 243, 199))  // amber-100
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(243, 244, 246)); // gray-100
    }
}
