#nullable enable
using System;
using System.Collections.Generic;
using System.Data.Odbc;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.PMTools
{
    public partial class StaffUtilizationWindow : Window
    {
        private const string Catalog = "C0000052267P_1_KOR00000000";
        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly List<StaffUtilizationRow> _rows = new();

        public StaffUtilizationWindow()
            : this(((global::Kor.Operations.OperationsApp)Application.Current).Services.GetRequiredService<DeltekOdbcOptions>())
        {
        }

        public StaffUtilizationWindow(DeltekOdbcOptions odbcOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(UtilGrid.ItemsSource)?.Refresh();
            UpdateStatus();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private async Task LoadAsync()
        {
            StatusText.Text = "Loading...";

            var rows = await Task.Run(() =>
            {
                var loaded = new List<StaffUtilizationRow>();
                var dsn = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
                var user = _odbcOptions.User ?? string.Empty;
                var pwd = _odbcOptions.Password ?? string.Empty;
                var factory = new VpOdbcDsnFactory(dsn, user, pwd, () => new Dictionary<string, string>());

                using var cn = factory.Create();
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                cmd.CommandText = $@"
SELECT
    e.FirstName + ' ' + e.LastName AS EmployeeName,
    SUM(CASE WHEN t.TransDate >= ? THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0) ELSE 0 END) AS WeekHrs,
    SUM(CASE WHEN t.TransDate >= ? THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0) ELSE 0 END) AS FourWkHrs,
    SUM(COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)) AS TwelveWkHrs
FROM [{Catalog}].dbo.tkDetail t
LEFT JOIN [{Catalog}].dbo.EMMain e ON t.Employee = e.Employee
WHERE t.TransDate >= ?
  AND t.Employee IS NOT NULL
  AND LTRIM(RTRIM(t.Employee)) <> ''
GROUP BY e.Employee, e.FirstName, e.LastName
ORDER BY TwelveWkHrs DESC";
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = DateTime.Today.AddDays(-7) });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = DateTime.Today.AddDays(-28) });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = DateTime.Today.AddDays(-84) });

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var employeeName = GetTrimmed(r, 0);
                    var weekHrs = GetDouble(r, 1);
                    var fourWkHrs = GetDouble(r, 2);
                    var twelveWkHrs = GetDouble(r, 3);
                    var fourWkAvg = fourWkHrs / 4.0;
                    var twelveWkAvg = twelveWkHrs / 12.0;
                    var utilizationPct = twelveWkAvg / 37.5;

                    loaded.Add(new StaffUtilizationRow
                    {
                        EmployeeName = string.IsNullOrWhiteSpace(employeeName) ? "(Unknown)" : employeeName,
                        WeekHrs = weekHrs,
                        FourWkAvg = fourWkAvg,
                        TwelveWkHrs = twelveWkHrs,
                        TwelveWkAvg = twelveWkAvg,
                        UtilizationPct = utilizationPct,
                        Status = utilizationPct >= 0.90 ? "High" : utilizationPct >= 0.60 ? "Normal" : "Low"
                    });
                }

                return loaded;
            }).ConfigureAwait(true);

            _rows.Clear();
            _rows.AddRange(rows);
            UtilGrid.ItemsSource = _rows;
            var view = CollectionViewSource.GetDefaultView(UtilGrid.ItemsSource);
            view.Filter = FilterRow;
            UpdateStatus();
        }

        private bool FilterRow(object item)
        {
            if (item is not StaffUtilizationRow row)
                return false;

            var q = (SearchBox.Text ?? string.Empty).Trim();
            if (q.Length == 0)
                return true;

            return row.EmployeeName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || row.Status.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void UpdateStatus()
        {
            var view = CollectionViewSource.GetDefaultView(UtilGrid.ItemsSource);
            if (view == null)
            {
                StatusText.Text = "Loading...";
                return;
            }

            StatusText.Text = $"{view.Cast<object>().Count():N0} staff members";
        }

        private static string GetTrimmed(System.Data.IDataRecord r, int i)
        {
            if (r.IsDBNull(i))
                return "";
            var v = Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture) ?? "";
            return v.Trim();
        }

        private static double GetDouble(System.Data.IDataRecord r, int i)
        {
            if (r.IsDBNull(i))
                return 0.0;
            var v = r.GetValue(i);
            if (v is double d) return d;
            if (v is float f) return f;
            if (v is decimal m) return (double)m;
            if (v is long l) return l;
            if (v is int n) return n;
            if (double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return 0.0;
        }
    }

    public sealed class StaffUtilizationRow
    {
        public string EmployeeName { get; set; } = "";
        public double WeekHrs { get; set; }
        public double FourWkAvg { get; set; }
        public double TwelveWkHrs { get; set; }
        public double TwelveWkAvg { get; set; }
        public double UtilizationPct { get; set; }
        public string Status { get; set; } = "";
    }
}
