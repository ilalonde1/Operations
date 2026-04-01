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
    public partial class ProjectWeekDrillDownWindow : Window
    {
        private readonly ProjectDetailRow   _project;
        private readonly StaffUtilizationRow _staff;
        private readonly DeltekOdbcOptions   _odbcOptions;

        public ProjectWeekDrillDownWindow(ProjectDetailRow project, StaffUtilizationRow staff, DeltekOdbcOptions odbcOptions)
        {
            _project = project ?? throw new ArgumentNullException(nameof(project));
            _staff = staff;
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            Title = $"Project Hours  {_project.ProjectName}";
            ProjectNameText.Text = _project.ProjectName;
            SummaryText.Text = BuildSummary(_project);
            StatusText.Text = "Loading…";

            var startDate = DateTime.Today.AddDays(-84);

            var weekRows = await Task.Run(() =>
            {
                var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
                var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User ?? "",
                    _odbcOptions.Password ?? "", () => new Dictionary<string, string>());

                using var cn = factory.Create();
                cn.Open();
                return LoadWeekRowsForProject(cn, startDate);
            }).ConfigureAwait(true);

            WeekGrid.ItemsSource = weekRows;
            StatusText.Text = $"{weekRows.Count} weeks shown";
        }

        private static string BuildSummary(ProjectDetailRow project)
        {
            return $"{project.Wbs1}   {project.Phase}   {project.TotalHrs:0.0} hrs over 12 wk   {project.PctOfTotal:P0} of total";
        }

        private List<WeekDetailRow> LoadWeekRowsForProject(OdbcConnection cn, DateTime startDate)
        {
            var catalog = string.IsNullOrWhiteSpace(_odbcOptions.Catalog) ? "C0000052267P_1_KOR00000000" : _odbcOptions.Catalog;
            using var cmd = cn.CreateCommand();
            cmd.CommandTimeout = SqlTimeouts.UiFacing;
            cmd.CommandText = $@"
SELECT
    t.TransDate,
    SUM(COALESCE(t.RegHrs,0)) AS RegHrs,
    SUM(COALESCE(t.OvtHrs,0)) AS OvtHrs
FROM [{catalog}].dbo.tkDetail t
WHERE t.Employee = ?
  AND t.TransDate >= ?
  AND t.WBS1 = ?
GROUP BY t.TransDate
ORDER BY t.TransDate";

            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = _staff.EmployeeId });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = startDate });
            cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = _project.Wbs1 });

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

                var dow = (int)date.DayOfWeek;
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
                var total = kvp.Value.Reg + kvp.Value.Ovt;
                var sunday = kvp.Key.AddDays(6);
                return new WeekDetailRow
                {
                    WeekStart = kvp.Key,
                    WeekLabel = $"{kvp.Key:MMM d} – {sunday:MMM d, yyyy}",
                    RegHrs = kvp.Value.Reg,
                    OvtHrs = kvp.Value.Ovt,
                    TotalHrs = total,
                    VsTarget = 0,
                    VsTargetDisplay = "",
                    VsTargetStatus = "OnTarget",
                };
            }).OrderByDescending(w => w.WeekStart).ToList();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private static string GetTrimmed(System.Data.IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return "";
            return (Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "").Trim();
        }

        private static double GetDouble(System.Data.IDataRecord r, int i)
        {
            if (r.IsDBNull(i)) return 0.0;
            var v = r.GetValue(i);
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is decimal m) return (double)m;
            if (v is long l) return l;
            if (v is int n) return n;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            return 0.0;
        }
    }
}
