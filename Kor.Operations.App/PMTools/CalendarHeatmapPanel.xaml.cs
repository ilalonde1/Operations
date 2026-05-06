#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Kor.Operations.Financials;
using static Kor.Operations.Data.DataReaderHelpers;

namespace Kor.Operations.PMTools
{
    public partial class CalendarHeatmapPanel : UserControl
    {
        private readonly StaffUtilizationRow _staff;
        private readonly DeltekOdbcOptions _odbcOptions;
        private DateTime _currentMonth;
        private Dictionary<DateTime, List<DayProjectEntry>> _allDayData = new();

        public CalendarHeatmapPanel(StaffUtilizationRow staff, DeltekOdbcOptions odbcOptions)
        {
            _staff = staff;
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _currentMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            CalendarDayCells = new ObservableCollection<CalendarDayCell>();
            InitializeComponent();
            DataContext = this;
        }

        public ObservableCollection<CalendarDayCell> CalendarDayCells { get; }

        public async Task LoadAsync()
        {
            var startDate = DateTime.Today.AddDays(-365);

            _allDayData = await Task.Run(() =>
            {
                var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
                var catalog = DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);
                var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User ?? "",
                    _odbcOptions.Password ?? "", () => new Dictionary<string, string>());

                using var cn = factory.Create();
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.CommandText = $@"
SELECT t.TransDate, t.WBS1, pr.Name,
       SUM(COALESCE(t.RegHrs,0)) + SUM(COALESCE(t.OvtHrs,0)) AS TotalHrs
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE t.Employee = ?
  AND t.TransDate >= ?
GROUP BY t.TransDate, t.WBS1, pr.Name
ORDER BY t.TransDate";

                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.VarChar, Value = _staff.EmployeeId });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = startDate });

                var result = new Dictionary<DateTime, List<DayProjectEntry>>();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    if (r.IsDBNull(0)) continue;
                    var raw = r.GetValue(0);
                    DateTime date;
                    if (raw is DateTime dt) date = dt.Date;
                    else if (DateTime.TryParse(Convert.ToString(raw, CultureInfo.InvariantCulture), out var p)) date = p.Date;
                    else continue;

                    var wbs1 = GetTrimmed(r, 1);
                    var name = GetTrimmed(r, 2);
                    var totalHours = GetDouble(r, 3);
                    if (totalHours <= 0) continue;

                    if (!result.TryGetValue(date, out var entries))
                    {
                        entries = new List<DayProjectEntry>();
                        result[date] = entries;
                    }

                    entries.Add(new DayProjectEntry
                    {
                        Wbs1 = wbs1,
                        ProjectName = string.IsNullOrWhiteSpace(name) ? wbs1 : name,
                        Hours = totalHours,
                    });
                }

                return result;
            }).ConfigureAwait(true);

            BuildMonth();
        }

        private void BuildMonth()
        {
            var firstDay = new DateTime(_currentMonth.Year, _currentMonth.Month, 1);
            var lastDay = firstDay.AddMonths(1).AddDays(-1);
            var maxProjectHours = _allDayData.Values
                .SelectMany(e => e)
                .Select(e => e.Hours)
                .DefaultIfEmpty(1)
                .Max();

            var startDow = (int)firstDay.DayOfWeek;
            var padStart = firstDay.AddDays(-startDow).Date;

            var endDow = (int)lastDay.DayOfWeek;
            var padEnd = lastDay.AddDays(endDow == 6 ? 0 : 6 - endDow).Date;

            var cells = new List<CalendarDayCell>();
            for (var day = padStart; day <= padEnd; day = day.AddDays(1))
            {
                var isOutsideMonth = day.Month != _currentMonth.Month || day.Year != _currentMonth.Year;
                var cell = new CalendarDayCell
                {
                    Date = day,
                    DayLabel = day.Day.ToString(CultureInfo.InvariantCulture),
                    IsOutsideMonth = isOutsideMonth,
                    IsEmpty = true,
                    IsWeekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday,
                };

                if (!isOutsideMonth && _allDayData.TryGetValue(day, out var entries) && entries.Count > 0)
                {
                    var totalHours = entries.Sum(x => x.Hours);
                    var ordered = entries
                        .OrderBy(x => x.IsOverhead)
                        .ThenByDescending(x => x.Hours)
                        .ToList();
                    var visibleSegments = ordered.Take(5).ToList();

                    cell.Projects = ordered;
                    cell.TotalHours = totalHours;
                    cell.HasOverhead = ordered.Any(x => x.IsOverhead);
                    var billableHours = ordered.Where(x => !x.IsOverhead).Sum(x => x.Hours);
                    cell.BillablePct = totalHours > 0 ? billableHours / totalHours : 0;
                    cell.BillablePctLabel = $"{cell.BillablePct:P0} billable";
                    cell.BillableBarWidth = cell.BillablePct * 100.0;
                    cell.UnbillableBarWidth = (1.0 - cell.BillablePct) * 100.0;
                    cell.IsEmpty = false;
                    cell.OverflowLabel = ordered.Count > 5 ? $"+{ordered.Count - 5} more" : "";
                    cell.OverflowTooltip = string.Join("\n", ordered.Skip(5).Select(x => $"{TruncateName(x.ProjectName, 24)}  {x.Hours:0.0} hrs"));
                    cell.Segments = visibleSegments.Select(x => new BarSegment
                    {
                        Proportion = totalHours <= 0 ? 0 : x.Hours / totalHours,
                        Wbs1 = x.Wbs1,
                        TooltipText = $"{x.ProjectName}: {x.Hours:0.0} hrs",
                        IsOverhead = x.IsOverhead,
                        WidthPx = (x.Hours / maxProjectHours) * 70.0,
                        ShortLabel = $"{TruncateName(x.ProjectName, 18)}  {x.Hours:0.0}",
                    }).ToList();
                }

                cells.Add(cell);
            }

            CalendarDayCells.Clear();
            foreach (var cell in cells)
                CalendarDayCells.Add(cell);

            MonthLabelText.Text = _currentMonth.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            UpdateNavButtons();
        }

        private void PrevMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(-1);
            BuildMonth();
        }

        private void NextMonth_Click(object sender, RoutedEventArgs e)
        {
            _currentMonth = _currentMonth.AddMonths(1);
            BuildMonth();
        }

        private void UpdateNavButtons()
        {
            var todayMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var oldestMonth = _allDayData.Count == 0
                ? todayMonth
                : new DateTime(_allDayData.Keys.Min().Year, _allDayData.Keys.Min().Month, 1);

            PrevMonthButton.IsEnabled = _currentMonth > oldestMonth;
            NextMonthButton.IsEnabled = _currentMonth < todayMonth;
        }

        private static string TruncateName(string name, int max)
        {
            if (name.Length <= max) return name;
            return name.Substring(0, max - 1) + "";
        }

    }
}
