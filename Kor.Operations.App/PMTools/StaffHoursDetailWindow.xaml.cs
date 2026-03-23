#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Kor.Operations.App.Options;
using Kor.Operations.Data;

namespace Kor.Operations.PMTools
{
    public partial class StaffHoursDetailWindow : Window
    {
        private const string Catalog    = "C0000052267P_1_KOR00000000";
        private const double WeekTarget = 37.5;

        private readonly StaffUtilizationRow _staff;
        private readonly DeltekOdbcOptions   _odbcOptions;

        public StaffHoursDetailWindow(StaffUtilizationRow staff, DeltekOdbcOptions odbcOptions)
        {
            _staff       = staff;
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EmployeeNameText.Text = _staff.EmployeeName;
            SummaryText.Text      = BuildSummary(_staff);
            StatusText.Text       = "Loading…";

            var startDate = DateTime.Today.AddDays(-84);

            var (projectRows, weekRows) = await Task.Run(() =>
            {
                var dsn     = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
                var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User ?? "",
                    _odbcOptions.Password ?? "", () => new Dictionary<string, string>());

                using var cn = factory.Create();
                cn.Open();

                var projects = LoadProjectRows(cn, startDate);
                var weeks    = LoadWeekRows(cn, startDate);
                return (projects, weeks);
            }).ConfigureAwait(true);

            ProjectGrid.ItemsSource = projectRows;
            WeekGrid.ItemsSource    = weekRows;
            StatusText.Text = $"{projectRows.Count} projects  ·  {weekRows.Count} weeks shown";
        }

        private static string BuildSummary(StaffUtilizationRow r)
        {
            var parts = new List<string>
            {
                $"12-wk total: {r.TwelveWkHrs:0.0} hrs",
                $"Avg: {r.TwelveWkAvg:0.1} hrs/wk",
                $"Utilization: {r.UtilizationPct:P0}",
                $"Projects: {r.ProjectCount}",
                $"Billable: {r.BillablePct:P0}",
            };
            if (r.OvtHrs12Wk > 0.5)
                parts.Add($"Overtime: {r.OvtHrs12Wk:0.0} hrs");
            parts.Add($"Trend: {r.Trend}");
            return string.Join("   ·   ", parts);
        }

        // ── By-project query ─────────────────────────────────────────────────

        private List<ProjectDetailRow> LoadProjectRows(OdbcConnection cn, DateTime startDate)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            cmd.CommandText = $@"
SELECT
    t.WBS1,
    pr.Name,
    pctf.CustProjectPhase,
    SUM(COALESCE(t.RegHrs,0)) AS RegHrs,
    SUM(COALESCE(t.OvtHrs,0)) AS OvtHrs
FROM [{Catalog}].dbo.tkDetail t
LEFT JOIN [{Catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
LEFT JOIN (
    SELECT WBS1, MAX(CustProjectPhase) AS CustProjectPhase
    FROM [{Catalog}].dbo.ProjectCustomTabFields
    GROUP BY WBS1
) pctf ON pctf.WBS1 = t.WBS1
WHERE t.Employee = ?
  AND t.TransDate >= ?
GROUP BY t.WBS1, pr.Name, pctf.CustProjectPhase
ORDER BY (SUM(COALESCE(t.RegHrs,0)) + SUM(COALESCE(t.OvtHrs,0))) DESC";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar,  Value = _staff.EmployeeId });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = startDate });

            var rows       = new List<ProjectDetailRow>();
            var grandTotal = _staff.TwelveWkHrs > 0 ? _staff.TwelveWkHrs : 1.0;

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var wbs1  = GetTrimmed(r, 0);
                var name  = GetTrimmed(r, 1);
                var phase = GetTrimmed(r, 2);
                var reg   = GetDouble(r, 3);
                var ovt   = GetDouble(r, 4);
                var total = reg + ovt;
                if (total <= 0) continue;

                rows.Add(new ProjectDetailRow
                {
                    Wbs1        = wbs1,
                    ProjectName = string.IsNullOrWhiteSpace(name) ? wbs1 : name,
                    Phase       = phase,
                    RegHrs      = reg,
                    OvtHrs      = ovt,
                    TotalHrs    = total,
                    PctOfTotal  = total / grandTotal,
                });
            }
            return rows;
        }

        // ── By-week query (daily data, grouped in C#) ────────────────────────

        private List<WeekDetailRow> LoadWeekRows(OdbcConnection cn, DateTime startDate)
        {
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            cmd.CommandText = $@"
SELECT
    t.TransDate,
    SUM(COALESCE(t.RegHrs,0)) AS RegHrs,
    SUM(COALESCE(t.OvtHrs,0)) AS OvtHrs
FROM [{Catalog}].dbo.tkDetail t
WHERE t.Employee = ?
  AND t.TransDate >= ?
GROUP BY t.TransDate
ORDER BY t.TransDate";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar,  Value = _staff.EmployeeId });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = startDate });

            // Accumulate daily hours into Mon-anchored week buckets
            var byWeek = new SortedDictionary<DateTime, (double Reg, double Ovt)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (r.IsDBNull(0)) continue;
                var raw = r.GetValue(0);
                DateTime date;
                if (raw is DateTime dt) date = dt.Date;
                else if (DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var p)) date = p.Date;
                else continue;

                // Snap to the Monday of this day's week
                var dow    = (int)date.DayOfWeek; // 0=Sun … 6=Sat
                var monday = date.AddDays(dow == 0 ? -6 : 1 - dow).Date;

                var reg = GetDouble(r, 1);
                var ovt = GetDouble(r, 2);

                if (byWeek.TryGetValue(monday, out var existing))
                    byWeek[monday] = (existing.Reg + reg, existing.Ovt + ovt);
                else
                    byWeek[monday] = (reg, ovt);
            }

            return byWeek.Select(kvp =>
            {
                var total  = kvp.Value.Reg + kvp.Value.Ovt;
                var delta  = total - WeekTarget;
                var sunday = kvp.Key.AddDays(6);
                return new WeekDetailRow
                {
                    WeekStart       = kvp.Key,
                    WeekLabel       = $"{kvp.Key:MMM d} – {sunday:MMM d, yyyy}",
                    RegHrs          = kvp.Value.Reg,
                    OvtHrs          = kvp.Value.Ovt,
                    TotalHrs        = total,
                    VsTarget        = delta,
                    VsTargetDisplay = delta >= 0 ? $"+{delta:0.0}" : $"{delta:0.0}",
                    VsTargetStatus  = delta >= 5 ? "Over" : delta <= -5 ? "Under" : "OnTarget",
                };
            }).OrderByDescending(w => w.WeekStart).ToList(); // most-recent first
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        // ── Helpers ──────────────────────────────────────────────────────────

        private static string GetTrimmed(System.Data.IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return "";
            return (Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "").Trim();
        }

        private static double GetDouble(System.Data.IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return 0.0;
            var v = r.GetValue(i);
            if (v is double d)  return d;
            if (v is float f)   return f;
            if (v is decimal m) return (double)m;
            if (v is long l)    return l;
            if (v is int n)     return n;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            return 0.0;
        }
    }

    // ── Row models ───────────────────────────────────────────────────────────

    public sealed class ProjectDetailRow
    {
        public string Wbs1        { get; set; } = "";
        public string ProjectName { get; set; } = "";
        public string Phase       { get; set; } = "";
        public double RegHrs      { get; set; }
        public double OvtHrs      { get; set; }
        public double TotalHrs    { get; set; }
        public double PctOfTotal  { get; set; }
        public bool   HasOvertime => OvtHrs > 0;
    }

    public sealed class WeekDetailRow
    {
        public DateTime WeekStart       { get; set; }
        public string   WeekLabel       { get; set; } = "";
        public double   RegHrs          { get; set; }
        public double   OvtHrs          { get; set; }
        public double   TotalHrs        { get; set; }
        public double   VsTarget        { get; set; }
        public string   VsTargetDisplay { get; set; } = "";
        /// <summary>Over / OnTarget / Under — used for DataTrigger colouring.</summary>
        public string   VsTargetStatus  { get; set; } = "OnTarget";
        public bool     HasOvertime     => OvtHrs > 0;
    }
}
