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

                var elements = new ObservableCollection<FeeElementRow>();
                using var cn = factory.Create();
                await Task.Run(() => cn.Open());
                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = 15;
                cmd.CommandText = $@"
SELECT WBS2, Fee, Name
FROM [{catalog}].dbo.PR
WHERE WBS1 = ?
  AND WBS2 IS NOT NULL AND LTRIM(RTRIM(WBS2)) <> ''
  AND Fee > 0
ORDER BY WBS2";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.NVarChar, Value = _wbs1 });
                using var r = await Task.Run(() => cmd.ExecuteReader());
                while (r.Read())
                {
                    elements.Add(new FeeElementRow
                    {
                        Wbs2 = r.GetString(0).Trim(),
                        Fee = Convert.ToDouble(r.GetValue(1)),
                        Name = r.GetString(2).Trim(),
                    });
                }

                if (elements.Count > 0)
                {
                    FeeElementsList.ItemsSource = elements;
                    FeeElementsList.Visibility = Visibility.Visible;
                }
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
            public ObservableCollection<DeliveryTrendPoint> DeliveryTrend { get; } = new();

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

            public ObservableCollection<CfoMetricDisplayRow> CfoMetrics { get; } = new();

            public ProjectFinancialDetailVm(FinancialsProjectRow p, PortfolioHealthCounts? portfolioCounts)
            {
                p ??= new FinancialsProjectRow();
                ProjectName = (p?.Name ?? string.Empty).Trim();
                Wbs1 = (p?.Wbs1 ?? string.Empty).Trim();
                Pm = (p?.Pm ?? string.Empty).Trim();
                Phase = (p?.Phase ?? string.Empty).Trim();
                TotalInspections = p?.TotalInspections ?? 0;
                LastMonthInspections = p?.LastMonthInspections ?? 0;

                Fee = p?.Fee ?? 0.0;
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

                // Stub history: most recent point is current computed confidence.
                DeliveryTrend.Add(new DeliveryTrendPoint { Label = "T-5", Level = DeliveryConfidenceLevel.HighConfidence, Tooltip = "Stub history (no persistence yet)." });
                DeliveryTrend.Add(new DeliveryTrendPoint { Label = "T-4", Level = DeliveryConfidenceLevel.Stable, Tooltip = "Stub history (no persistence yet)." });
                DeliveryTrend.Add(new DeliveryTrendPoint { Label = "T-3", Level = DeliveryConfidenceLevel.AtRisk, Tooltip = "Stub history (no persistence yet)." });
                DeliveryTrend.Add(new DeliveryTrendPoint { Label = "T-2", Level = DeliveryConfidenceLevel.Stable, Tooltip = "Stub history (no persistence yet)." });
                DeliveryTrend.Add(new DeliveryTrendPoint { Label = "T-1", Level = DeliveryConfidenceLevel.HighConfidence, Tooltip = "Stub history (no persistence yet)." });
                DeliveryTrend.Add(new DeliveryTrendPoint { Label = "Now", Level = ConfidenceLevel, Tooltip = DeliveryConfidenceSummary });

                // CFO metrics: centralized registry, computed from the existing project snapshot (no upstream logic changes).
                try
                {
                    var registry = new CfoMetricRegistry();
                    var ctx = ProjectData.FromProject(p ?? new FinancialsProjectRow(), portfolioCounts);
                    foreach (var m in registry.GetAllMetrics())
                    {
                        var v = m.ComputeValue(ctx);
                        CfoMetrics.Add(CfoMetricDisplayRow.FromMetric(m, v, ctx));
                    }
                }
                catch
                {
                    // Non-critical: if anything fails, we still show the core project detail.
                }
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
                    candidates.Add((0, pctOver, $"Fee billed is {pctOver:P0} over the contracted fee."));
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

        internal sealed class DeliveryTrendPoint
        {
            public string Label { get; set; } = "";
            public DeliveryConfidenceLevel Level { get; set; } = DeliveryConfidenceLevel.Stable;
            public string Tooltip { get; set; } = "";
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

    internal sealed class FeeElementRow
    {
        public string Wbs2 { get; init; } = "";
        public double Fee { get; init; }
        public string Name { get; init; } = "";
    }
}
