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
using System.Windows.Input;
using System.Windows.Media;
using Kor.Operations.App.Options;
using Kor.Operations.Data;
using static Kor.Operations.Data.DataReaderHelpers;
using Kor.Operations.Financials;
using Microsoft.Extensions.DependencyInjection;

namespace Kor.Operations.PMTools
{
    public partial class StaffUtilizationWindow : Window
    {
        private readonly DeltekOdbcOptions _odbcOptions;
        private readonly double _usdToCadRate;
        private readonly List<StaffUtilizationRow> _rows = new();

        public StaffUtilizationWindow(DeltekOdbcOptions odbcOptions, FinancialsOptions financialsOptions)
        {
            _odbcOptions = odbcOptions ?? throw new ArgumentNullException(nameof(odbcOptions));
            _usdToCadRate = OrgFx.ParseUsdToCadRate(financialsOptions?.BilledUsdToCadRate);
            InitializeComponent();

            // Push the per-employee rows + current search filter to AI so
            // "who is underutilized" answers see what is actually on screen.
            // Pre-Batch 102 this registration was missing and AI ran blind on
            // this window; the AiPanelContextProviderTests gate now prevents
            // the same class of regression.
            Kor.Operations.Services.AppServices.GetOptional<Kor.Operations.Services.AppAiContextBuilder>()?.Register(this);

            AiPanel.Initialize(Kor.Operations.Services.AppServices.Get<Kor.Operations.Services.AppAiService>());
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e) => await LoadAsync();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            CollectionViewSource.GetDefaultView(UtilGrid.ItemsSource)?.Refresh();
            UpdateStatus();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            RefreshBtn.IsEnabled = false;
            try { await LoadAsync(); }
            finally { RefreshBtn.IsEnabled = true; }
        }

        private void UtilGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UtilGrid.SelectedItem is not StaffUtilizationRow row)
                return;

            // Confirm click landed on a real row, not the header
            DependencyObject? hit = e.OriginalSource as DependencyObject;
            while (hit != null && hit is not DataGridRow)
                hit = VisualTreeHelper.GetParent(hit);
            if (hit is not DataGridRow)
                return;

            var win = new StaffHoursDetailWindow(row, _odbcOptions) { Owner = this };
            win.Show();
        }

        private async Task LoadAsync()
        {
            StatusText.Text = "Loading...";

            var rows = await Task.Run(() =>
            {
                var loaded = new List<StaffUtilizationRow>();
                var dsn     = string.IsNullOrWhiteSpace(_odbcOptions.Dsn) ? "Deltek" : _odbcOptions.Dsn;
                var catalog = DeltekCatalogValidator.ResolveCatalog(_odbcOptions.Catalog);
                var factory = new VpOdbcDsnFactory(dsn, _odbcOptions.User ?? "", _odbcOptions.Password ?? "",
                    () => new Dictionary<string, string>());

                using var cn = factory.Create();
                cn.Open();

                using var cmd = cn.CreateCommand();
                cmd.CommandTimeout = SqlTimeouts.UiFacing;
                // Cost columns are FX'd to CAD-equivalent: tkDetail.RegAmt is
                // denominated in the project's currency (verified 2026-05-06
                // against KOR's catalog), so we LEFT JOIN PR for the master
                // row's Org and CASE-FX USA-org rows. Hours columns are
                // currency-agnostic and stay raw.
                var fxRate = _usdToCadRate;
                cmd.CommandText = $@"
SELECT
    t.Employee,
    e.FirstName + ' ' + e.LastName AS EmployeeName,
    SUM(CASE WHEN t.TransDate >= ? THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS WeekHrs,
    SUM(CASE WHEN t.TransDate >= ? THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS FourWkHrs,
    SUM(COALESCE(t.RegHrs,0))                                     AS TwelveWkRegHrs,
    SUM(COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0))         AS TwelveWkOvtHrs,
    SUM(CASE WHEN t.LaborCode NOT IN ({LaborCodes.Admin}, {LaborCodes.NonBillable})
              AND t.WBS1 NOT LIKE '[A-Z]%'
              AND t.WBS1 NOT LIKE '9[A-Z]%'
              AND t.WBS1 NOT LIKE '99%'
             THEN COALESCE(t.RegHrs,0)+COALESCE(t.OvtHrs,0)+COALESCE(t.SpecialOvtHrs,0) ELSE 0 END) AS BillableHrs,
    COUNT(DISTINCT t.WBS1) AS ProjectCount,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA'
             THEN (COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)) * ?
             ELSE  COALESCE(t.RegAmt,0)+COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)
        END
    ) AS TwelveWkLaborCost,
    SUM(
        CASE WHEN UPPER(LTRIM(RTRIM(COALESCE(pr.Org,'')))) = 'USA'
             THEN (COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)) * ?
             ELSE  COALESCE(t.OvtAmt,0)+COALESCE(t.SpecialOvtAmt,0)
        END
    ) AS TwelveWkOvertimeCost
FROM [{catalog}].dbo.tkDetail t
LEFT JOIN [{catalog}].dbo.EMMain e ON t.Employee = e.Employee
LEFT JOIN [{catalog}].dbo.EMCompany ec ON ec.Employee = t.Employee
LEFT JOIN [{catalog}].dbo.PR pr
       ON pr.WBS1 = t.WBS1
      AND (pr.WBS2 IS NULL OR LTRIM(RTRIM(pr.WBS2)) = '')
WHERE t.TransDate >= ?
  AND t.Employee IS NOT NULL
  AND LTRIM(RTRIM(t.Employee)) <> ''
  AND UPPER(COALESCE(ec.Status, 'A')) = 'A'
  AND COALESCE(t.LineItemApprovalStatus,'') <> 'R'
GROUP BY t.Employee, e.FirstName, e.LastName
ORDER BY (SUM(COALESCE(t.RegHrs,0)) + SUM(COALESCE(t.OvtHrs,0)) + SUM(COALESCE(t.SpecialOvtHrs,0))) DESC";

                // Parameters in positional order: WeekHrs date, FourWkHrs date,
                // TwelveWkLaborCost rate, TwelveWkOvertimeCost rate, TwelveWkHrs window date.
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = DateTime.Today.AddDays(-7) });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = DateTime.Today.AddDays(-28) });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Double, Value = fxRate });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.Double, Value = fxRate });
                cmd.Parameters.Add(new OdbcParameter { OdbcType = OdbcType.DateTime, Value = DateTime.Today.AddDays(-84) });

                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    var employeeId         = GetTrimmed(r, 0);
                    var employeeName       = GetTrimmed(r, 1);
                    var weekHrs            = GetDouble(r, 2);
                    var fourWkHrs          = GetDouble(r, 3);
                    var regHrs12           = GetDouble(r, 4);
                    var ovtHrs12           = GetDouble(r, 5);
                    var billableHrs        = GetDouble(r, 6);
                    var projectCount       = (int)GetDouble(r, 7);
                    var twelveWkLaborCost  = GetDouble(r, 8);
                    var twelveWkOvtCost    = GetDouble(r, 9);

                    var twelveWkHrs    = regHrs12 + ovtHrs12;
                    var fourWkAvg      = fourWkHrs / 4.0;
                    var twelveWkAvg    = twelveWkHrs / 12.0;
                    var utilizationPct = twelveWkAvg / 37.5;
                    var billablePct    = twelveWkHrs > 0 ? billableHrs / twelveWkHrs : 0.0;
                    var costPerBillableHr = billableHrs > 0 ? twelveWkLaborCost / billableHrs : 0.0;

                    // Trend: is the 4-wk pace meaningfully above/below the 12-wk average?
                    var trend = fourWkAvg > twelveWkAvg * 1.10 ? "↑"
                              : fourWkAvg < twelveWkAvg * 0.90 ? "↓"
                              : "→";

                    loaded.Add(new StaffUtilizationRow
                    {
                        EmployeeId            = employeeId,
                        EmployeeName          = string.IsNullOrWhiteSpace(employeeName) ? $"({employeeId})" : employeeName,
                        WeekHrs               = weekHrs,
                        FourWkAvg             = fourWkAvg,
                        TwelveWkHrs           = twelveWkHrs,
                        TwelveWkAvg           = twelveWkAvg,
                        OvtHrs12Wk            = ovtHrs12,
                        BillablePct           = billablePct,
                        ProjectCount          = projectCount,
                        TwelveWkLaborCost     = twelveWkLaborCost,
                        TwelveWkOvertimeCost  = twelveWkOvtCost,
                        CostPerBillableHr     = costPerBillableHr,
                        Trend                 = trend,
                        UtilizationPct = utilizationPct,
                        Status = utilizationPct >= 0.90 ? "High"
                               : utilizationPct >= 0.60 ? "Normal"
                               : "Low",
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
            if (item is not StaffUtilizationRow row) return false;
            var q = (SearchBox.Text ?? "").Trim();
            if (q.Length == 0) return true;
            return row.EmployeeName.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || row.Status.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
                || row.Trend.Contains(q);
        }

        private void UpdateStatus()
        {
            var view = CollectionViewSource.GetDefaultView(UtilGrid.ItemsSource);
            if (view == null) { StatusText.Text = "Loading..."; return; }
            StatusText.Text = $"{view.Cast<object>().Count():N0} staff members  ·  double-click a row for details";
        }

    }

    public sealed class StaffUtilizationRow
    {
        public string EmployeeId    { get; set; } = "";
        public string EmployeeName  { get; set; } = "";
        public double WeekHrs       { get; set; }
        public double FourWkAvg     { get; set; }
        public double TwelveWkHrs   { get; set; }
        public double TwelveWkAvg   { get; set; }
        public double OvtHrs12Wk    { get; set; }
        public double BillablePct   { get; set; }
        public int    ProjectCount  { get; set; }
        public double TwelveWkLaborCost    { get; set; }
        public double TwelveWkOvertimeCost { get; set; }
        public double CostPerBillableHr    { get; set; }
        public string Trend         { get; set; } = "→";
        public double UtilizationPct { get; set; }
        public string Status        { get; set; } = "";
        public string BillableStatus => BillablePct >= 0.85 ? "Good" : BillablePct >= 0.65 ? "Fair" : "Low";
    }
}
