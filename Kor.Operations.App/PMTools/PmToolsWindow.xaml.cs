#nullable enable
using System;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClosedXML.Excel;
using Microsoft.Win32;
using Kor.Operations.App.Options;
using Kor.Operations.App.PMTools;
using Kor.Operations.Financials;

namespace Kor.Operations.PMTools
{
    public partial class PmToolsWindow : Window
    {
        private readonly PmToolsViewModel _vm = new();
        private readonly WorkloadMeetingPanelViewModel _meetingPanel;
        private DeltekOdbcOptions? _odbcOptions;
        private CancellationTokenSource? _cts;
        private bool _isSyncingMeetingPriorities;

        public PmToolsWindow(WorkloadMeetingPanelViewModel meetingPanel, DeltekOdbcOptions odbcOptions)
        {
            _meetingPanel = meetingPanel ?? throw new ArgumentNullException(nameof(meetingPanel));
            _odbcOptions = odbcOptions;
            _vm.EngRate = odbcOptions?.EngRate ?? 474;
            _vm.DraftRate = odbcOptions?.DraftRate ?? 655;
            _vm.TargetBilling = odbcOptions?.TargetBillingRate ?? 185;
            InitializeComponent();
            DataContext = _vm;
            _meetingPanel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName is nameof(WorkloadMeetingPanelViewModel.CurrentProjects)
                                    or nameof(WorkloadMeetingPanelViewModel.SelectedMeeting))
                    SyncMeetingPrioritiesToRows();
            };
            _meetingPanel.CurrentProjects.CollectionChanged += (_, _) => SyncMeetingPrioritiesToRows();
        }

        public WorkloadMeetingPanelViewModel MeetingPanel => _meetingPanel;

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _ = ApplyHeaderAsync();
            if (!_vm.HasData || _vm.IsDataStale)
            {
                _cts = new CancellationTokenSource();
                await _vm.RefreshAsync(forceRefresh: false, _cts.Token);
            }

            await _meetingPanel.LoadAsync();
            SyncMeetingPrioritiesToRows();
        }

        private async Task ApplyHeaderAsync()
        {
            try
            {
                await global::Kor.Operations.HeaderLoader.ApplyAsync(HeaderBar);
                _vm.CurrentUserName = HeaderBar.UserDisplayName ?? "";
            }
            catch
            {
            }
        }

        private async void RefreshBtn_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
            SyncMeetingPrioritiesToRows();
        }

        private async void DeleteMeetingBtn_Click(object sender, RoutedEventArgs e)
        {
            var meeting = _meetingPanel.SelectedMeeting;
            if (meeting == null) return;

            var label = meeting.MeetingDate.ToString("MMM d, yyyy");
            var result = MessageBox.Show(
                this,
                $"Delete the meeting from {label} and all its priority data?\n\nThis cannot be undone.",
                "Delete Meeting",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes) return;

            await _meetingPanel.DeleteMeetingAsync();
            SyncMeetingPrioritiesToRows();
        }

        private void CloseBtn_Click(object sender, RoutedEventArgs e) => Close();
        private void KpiDictionaryBtn_Click(object sender, RoutedEventArgs e)
            => new Financials.FinancialMetricDictionaryWindow { Owner = this }.Show();
        private void StaffUtilizationBtn_Click(object sender, RoutedEventArgs e)
            => new StaffUtilizationWindow { Owner = this }.Show();
        private void HistoricalAnalyticsBtn_Click(object sender, RoutedEventArgs e)
            => new HistoricalAnalyticsWindow { Owner = this }.Show();

        private async void ScopeWatchlist_Click(object sender, RoutedEventArgs e)
        {
            if (_vm.ShowWatchlistOnly) return;
            _vm.ShowWatchlistOnly = true;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
            SyncMeetingPrioritiesToRows();
        }

        private async void ScopeAllActive_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.ShowWatchlistOnly) return;
            _vm.ShowWatchlistOnly = false;
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
            SyncMeetingPrioritiesToRows();
        }

        private async void RecalculateBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_odbcOptions == null) return;
            // Push UI values back to the singleton options so FinancialsService picks them up
            _odbcOptions.EngRate = _vm.EngRate;
            _odbcOptions.DraftRate = _vm.DraftRate;
            _odbcOptions.TargetBillingRate = _vm.TargetBilling;
            // Force a full refresh — re-queries Deltek and recomputes all budgets
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            await _vm.RefreshAsync(forceRefresh: true, _cts.Token);
            SyncMeetingPrioritiesToRows();
        }
        private void PhaseAll_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "All";
        private void PhaseSD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "SD";
        private void PhaseDD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "DD";
        private void PhaseCD_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "CD";
        private void PhaseCA_Click(object sender, RoutedEventArgs e) => _vm.SelectedPhase = "CA";
        private void SortByFee_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 0;
        private void SortByUnbilled_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 3;
        private void SortByName_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 1;
        private void SortByAtRisk_Click(object sender, RoutedEventArgs e) => _vm.PmGroupSortMode = 2;
        private void ShowEngineeringCapacityRisk_Click(object sender, RoutedEventArgs e) => _vm.CapacityRiskViewIndex = 0;
        private void ShowDraftingCapacityRisk_Click(object sender, RoutedEventArgs e) => _vm.CapacityRiskViewIndex = 1;


        private void PmGroupExpand_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: PmGroupViewModel group })
                group.IsExpanded = !group.IsExpanded;
        }

        private void ProjectGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (sender is not DataGrid { SelectedItem: PmProjectRow row } || !IsDataGridRowDoubleClick(e))
                return;

            // Priority column (index 0) is interactive  don't treat its double-click as a row open
            if (GetClickedCell(e)?.Column.DisplayIndex == 0)
                return;

            var counts = BuildPortfolioCounts();
            var win = new Financials.ProjectFinancialDetailWindow(row.Source, counts) { Owner = this };
            win.Show();
        }

        private void UtilizationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (UtilizationGrid.SelectedItem is not UtilizationRow row || !IsDataGridRowDoubleClick(e))
                return;

            var counts = BuildPortfolioCounts();
            var win = new Financials.ProjectFinancialDetailWindow(row.Project, counts) { Owner = this };
            win.Show();
        }

        private void DraftUtilizationGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (DraftUtilizationGrid.SelectedItem is not DraftUtilizationRow row || !IsDataGridRowDoubleClick(e))
                return;

            var counts = BuildPortfolioCounts();
            var win = new Financials.ProjectFinancialDetailWindow(row.Project, counts) { Owner = this };
            win.Show();
        }

        private static XLColor PctBarColor(double pct, bool isBudgetBurn = false)
        {
            if (isBudgetBurn)
                return pct >= 1.0 ? XLColor.FromHtml("#DC2626") : pct >= 0.85 ? XLColor.FromHtml("#EA580C") : pct >= 0.50 ? XLColor.FromHtml("#16A34A") : XLColor.FromHtml("#6B7280");
            return pct >= 0.95 ? XLColor.FromHtml("#DC2626") : pct >= 0.85 ? XLColor.FromHtml("#EA580C") : pct >= 0.50 ? XLColor.FromHtml("#16A34A") : XLColor.FromHtml("#6B7280");
        }

        private static XLColor RiskColor(string risk) => risk switch
        {
            "Critical" => XLColor.FromHtml("#DC2626"),
            "At Risk" => XLColor.FromHtml("#EA580C"),
            "Watch" => XLColor.FromHtml("#D97706"),
            _ => XLColor.FromHtml("#16A34A"),
        };

        private static void WriteBudgetCell(IXLCell cell, double budget, bool isEstimated)
        {
            cell.Value = budget;
            cell.Style.NumberFormat.Format = "0.0";
            if (isEstimated)
            {
                cell.Style.Font.FontColor = XLColor.FromHtml("#7C3AED");
                cell.Style.Font.Italic = true;
            }
        }

        private static void WritePctCell(IXLCell cell, double pct, string text, bool isBudgetBurn = false)
        {
            cell.Value = text;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = PctBarColor(pct, isBudgetBurn);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void WriteRiskCell(IXLCell cell, string risk)
        {
            cell.Value = risk;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Fill.BackgroundColor = RiskColor(risk);
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        }

        private static void WriteHeaderRow(IXLWorksheet ws, int row, string[] headers)
        {
            for (var c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(row, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold = true;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#F3F4F6");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.BottomBorderColor = XLColor.FromHtml("#E5E7EB");
            }
        }

        private async void ExportUtilizationBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanExportUtilization)
                return;

            var exportEng = _vm.IsEngineeringCapacitySelected;
            var label = exportEng ? "Engineering" : "Drafting";

            var sfd = new SaveFileDialog
            {
                Title = "Export Utilization",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"PmUtilization_{DateTime.Now:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (sfd.ShowDialog(this) != true) return;

            _vm.SetExporting(true);
            try
            {
                var path = sfd.FileName;
                await Task.Run(() =>
                {
                    using var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add($"{label} Utilization");

                    var titleCell = ws.Cell(1, 1);
                    titleCell.Value = $"{label} Capacity — {DateTime.Now:MMM d, yyyy}";
                    titleCell.Style.Font.Bold = true;
                    titleCell.Style.Font.FontSize = 14;
                    ws.Range(1, 1, 1, 5).Merge();

                    string[] headers = exportEng
                        ? new[] { "Project", "Project #", "PM", "Phase", "Const Type", "Fee", "% Billed", "Eng Budget", "Eng Hours", "Remaining", "% Used", "Fee/Hrs", "Billed/Hrs", "Risk" }
                        : new[] { "Project", "Project #", "PM", "Phase", "Const Type", "Fee", "% Billed", "Draft Budget", "Draft Hours", "Remaining", "% Used", "Fee/Hrs", "Billed/Hrs", "Risk" };

                    WriteHeaderRow(ws, 3, headers);
                    ws.SheetView.FreezeRows(3);

                    if (exportEng)
                    {
                        var rows = _vm.UtilizationView.Cast<UtilizationRow>().ToList();
                        var ri = 4;
                        foreach (var r in rows)
                        {
                            ws.Cell(ri, 1).Value = r.ProjectName;
                            ws.Cell(ri, 2).Value = r.Wbs1;
                            ws.Cell(ri, 3).Value = r.Pm;
                            ws.Cell(ri, 4).Value = r.Phase;
                            ws.Cell(ri, 5).Value = r.ConstructionType;
                            ws.Cell(ri, 6).Value = r.Fee;              ws.Cell(ri, 6).Style.NumberFormat.Format = "$#,##0";
                            WritePctCell(ws.Cell(ri, 7), r.PercentBilled, r.PercentBilled.ToString("P0"));
                            WriteBudgetCell(ws.Cell(ri, 8), r.EngBudget, r.Project.EngBudgetActual <= 0);
                            ws.Cell(ri, 9).Value = r.EngHours;         ws.Cell(ri, 9).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 10).Value = r.RemainingEngHours; ws.Cell(ri, 10).Style.NumberFormat.Format = "0.0";
                            if (r.RemainingEngHours < 0) { ws.Cell(ri, 10).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 10).Style.Font.Bold = true; }
                            WritePctCell(ws.Cell(ri, 11), r.PercentEngUsed, r.PercentEngUsed.ToString("P0"), isBudgetBurn: true);
                            ws.Cell(ri, 12).Value = r.Project.FeePerHours;    ws.Cell(ri, 12).Style.NumberFormat.Format = "$#,##0";
                            ws.Cell(ri, 13).Value = r.Project.BilledPerHours; ws.Cell(ri, 13).Style.NumberFormat.Format = "$#,##0";
                            WriteRiskCell(ws.Cell(ri, 14), r.DeliveryConfidence);
                            ri++;
                        }
                    }
                    else
                    {
                        var rows = _vm.DraftUtilizationView.Cast<DraftUtilizationRow>().ToList();
                        var ri = 4;
                        foreach (var r in rows)
                        {
                            ws.Cell(ri, 1).Value = r.ProjectName;
                            ws.Cell(ri, 2).Value = r.Wbs1;
                            ws.Cell(ri, 3).Value = r.Pm;
                            ws.Cell(ri, 4).Value = r.Phase;
                            ws.Cell(ri, 5).Value = r.ConstructionType;
                            ws.Cell(ri, 6).Value = r.Fee;                ws.Cell(ri, 6).Style.NumberFormat.Format = "$#,##0";
                            WritePctCell(ws.Cell(ri, 7), r.PercentBilled, r.PercentBilled.ToString("P0"));
                            WriteBudgetCell(ws.Cell(ri, 8), r.DraftBudget, r.Project.DraftBudgetActual <= 0);
                            ws.Cell(ri, 9).Value = r.DraftHours;         ws.Cell(ri, 9).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 10).Value = r.RemainingDraftHours; ws.Cell(ri, 10).Style.NumberFormat.Format = "0.0";
                            if (r.RemainingDraftHours < 0) { ws.Cell(ri, 10).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 10).Style.Font.Bold = true; }
                            WritePctCell(ws.Cell(ri, 11), r.PercentDraftUsed, r.PercentDraftUsed.ToString("P0"), isBudgetBurn: true);
                            ws.Cell(ri, 12).Value = r.Project.FeePerHours;    ws.Cell(ri, 12).Style.NumberFormat.Format = "$#,##0";
                            ws.Cell(ri, 13).Value = r.Project.BilledPerHours; ws.Cell(ri, 13).Style.NumberFormat.Format = "$#,##0";
                            WriteRiskCell(ws.Cell(ri, 14), r.DeliveryConfidence);
                            ri++;
                        }
                    }

                    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 3;
                    ws.Range(3, 1, lastRow, headers.Length).SetAutoFilter();
                    ws.Columns(1, headers.Length).AdjustToContents();
                    wb.SaveAs(path);
                }).ConfigureAwait(true);

                MessageBox.Show(this, "Export completed.", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _vm.SetExporting(false);
            }
        }

        private async void ExportPmGroupsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!_vm.CanExportPmGroups) return;

            var sfd = new SaveFileDialog
            {
                Title = "Export PM Groups",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"PmGroups_{DateTime.Now:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };
            if (sfd.ShowDialog(this) != true) return;

            _vm.SetExporting(true);
            try
            {
                var path   = sfd.FileName;
                var groups = _vm.PmGroups.ToList();
                await Task.Run(() =>
                {
                    using var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add("PM Groups");

                    var titleCell = ws.Cell(1, 1);
                    titleCell.Value = $"PM Groups — {DateTime.Now:MMM d, yyyy}";
                    titleCell.Style.Font.Bold = true;
                    titleCell.Style.Font.FontSize = 14;
                    ws.Range(1, 1, 1, 5).Merge();

                    string[] headers = { "PM", "Project #", "Project Name", "Phase", "Const Type", "Category", "Draft Type",
                                          "Fee", "% Billed", "Unbilled",
                                          "Drafting Mgr", "Eng Budget", "Eng Hrs", "Eng %", "Eng Remaining",
                                          "Draft Budget", "Draft Hrs", "Draft %", "Draft Remaining",
                                          "Chk", "Insp", "Fee/Hrs", "Billed/Hrs", "Delivery Risk" };

                    WriteHeaderRow(ws, 3, headers);
                    ws.SheetView.FreezeRows(3);

                    var ri = 4;
                    foreach (var group in groups)
                    {
                        // PM summary row
                        var sumRow = ws.Range(ri, 1, ri, headers.Length);
                        sumRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
                        sumRow.Style.Font.Bold = true;
                        ws.Cell(ri, 1).Value = group.PmName;
                        ws.Cell(ri, 2).Value = $"{group.ProjectCount} projects";
                        ws.Cell(ri, 8).Value = group.TotalFee;         ws.Cell(ri, 8).Style.NumberFormat.Format = "$#,##0";
                        ws.Cell(ri, 10).Value = group.TotalUnbilled;   ws.Cell(ri, 10).Style.NumberFormat.Format = "$#,##0";
                        ws.Cell(ri, 12).Value = group.TotalEngBudget;  ws.Cell(ri, 12).Style.NumberFormat.Format = "0.0";
                        ws.Cell(ri, 13).Value = group.TotalEngHrs;     ws.Cell(ri, 13).Style.NumberFormat.Format = "0.0";
                        ws.Cell(ri, 16).Value = group.TotalDraftBudget; ws.Cell(ri, 16).Style.NumberFormat.Format = "0.0";
                        ws.Cell(ri, 17).Value = group.TotalDraftHrs;   ws.Cell(ri, 17).Style.NumberFormat.Format = "0.0";
                        if (group.AtRiskOrCriticalCount > 0)
                            WriteRiskCell(ws.Cell(ri, 24), $"{group.AtRiskOrCriticalCount} at risk");
                        else
                        {
                            ws.Cell(ri, 24).Value = "Healthy";
                            ws.Cell(ri, 24).Style.Font.FontColor = XLColor.FromHtml("#166534");
                        }
                        ri++;

                        // Project detail rows
                        foreach (var p in group.Projects)
                        {
                            ws.Cell(ri, 1).Value = "";
                            ws.Cell(ri, 2).Value = p.Wbs1;
                            ws.Cell(ri, 3).Value = p.Name;
                            ws.Cell(ri, 4).Value = p.Phase;
                            ws.Cell(ri, 5).Value = p.ConstructionType;
                            ws.Cell(ri, 6).Value = p.ProjectCategory;
                            ws.Cell(ri, 7).Value = p.DraftingType;
                            ws.Cell(ri, 8).Value = p.Fee;              ws.Cell(ri, 8).Style.NumberFormat.Format = "$#,##0";
                            WritePctCell(ws.Cell(ri, 9), p.PercentBilled, p.PercentBilledText);
                            ws.Cell(ri, 10).Value = p.FeeRemaining;    ws.Cell(ri, 10).Style.NumberFormat.Format = "$#,##0";
                            if (p.FeeRemaining < 0) { ws.Cell(ri, 10).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 10).Style.Font.Bold = true; }
                            ws.Cell(ri, 11).Value = p.DraftingManager;
                            WriteBudgetCell(ws.Cell(ri, 12), p.EngBudget, p.IsEngBudgetEstimated);
                            ws.Cell(ri, 13).Value = p.EngHrs;          ws.Cell(ri, 13).Style.NumberFormat.Format = "0.0";
                            WritePctCell(ws.Cell(ri, 14), p.EngPercent, p.EngPercentText, isBudgetBurn: true);
                            ws.Cell(ri, 15).Value = p.RemainingEngHours; ws.Cell(ri, 15).Style.NumberFormat.Format = "0.0";
                            if (p.IsEngOverBudget) { ws.Cell(ri, 15).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 15).Style.Font.Bold = true; }
                            WriteBudgetCell(ws.Cell(ri, 16), p.DraftBudget, p.IsDraftBudgetEstimated);
                            ws.Cell(ri, 17).Value = p.DraftHrs;        ws.Cell(ri, 17).Style.NumberFormat.Format = "0.0";
                            WritePctCell(ws.Cell(ri, 18), p.DraftPercent, p.DraftPercentText, isBudgetBurn: true);
                            ws.Cell(ri, 19).Value = p.RemainingDraftHours; ws.Cell(ri, 19).Style.NumberFormat.Format = "0.0";
                            if (p.IsDraftOverBudget) { ws.Cell(ri, 19).Style.Font.FontColor = XLColor.FromHtml("#DC2626"); ws.Cell(ri, 19).Style.Font.Bold = true; }
                            ws.Cell(ri, 20).Value = p.ChkHrs;         ws.Cell(ri, 20).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 21).Value = p.InspHrs;        ws.Cell(ri, 21).Style.NumberFormat.Format = "0.0";
                            ws.Cell(ri, 22).Value = p.FeePerHours;    ws.Cell(ri, 22).Style.NumberFormat.Format = "$#,##0";
                            ws.Cell(ri, 23).Value = p.BilledPerHours; ws.Cell(ri, 23).Style.NumberFormat.Format = "$#,##0";
                            WriteRiskCell(ws.Cell(ri, 24), p.DeliveryRisk);
                            ri++;
                        }
                    }

                    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 3;
                    ws.Range(3, 1, lastRow, headers.Length).SetAutoFilter();
                    ws.Columns(1, headers.Length).AdjustToContents();
                    wb.SaveAs(path);
                }).ConfigureAwait(true);

                MessageBox.Show(this, "Export completed.", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _vm.SetExporting(false);
            }
        }

        private async void ExportMeetingBtn_Click(object sender, RoutedEventArgs e)
        {
            var meeting = _meetingPanel.SelectedMeeting;
            if (meeting == null)
                return;

            var dateLabel = meeting.MeetingDate.ToString("MMM d, yyyy");
            var sfd = new SaveFileDialog
            {
                Title = "Export Workload Meeting",
                Filter = "Excel Workbook|*.xlsx",
                FileName = $"WorkloadMeeting_{meeting.MeetingDate:yyyyMMdd}.xlsx",
                AddExtension = true,
                OverwritePrompt = true,
            };

            if (sfd.ShowDialog(this) != true)
                return;

            var priorityRows = _meetingPanel.PriorityProjects.ToList();
            var priorityByWbs1 = new System.Collections.Generic.Dictionary<string, WorkloadMeetingProjectRow>(StringComparer.OrdinalIgnoreCase);
            foreach (var pr in priorityRows)
                priorityByWbs1.TryAdd(pr.Wbs1, pr);

            var groups = _vm.PmGroups.ToList();
            var notes = _meetingPanel.MeetingNotes ?? string.Empty;
            var path = sfd.FileName;

            try
            {
                await Task.Run(() =>
                {
                    using var wb = new XLWorkbook();
                    var ws = wb.Worksheets.Add("Workload Meeting");

                    // Title row
                    var titleCell = ws.Cell(1, 1);
                    titleCell.Value = $"Workload Meeting — {dateLabel}";
                    titleCell.Style.Font.Bold = true;
                    titleCell.Style.Font.FontSize = 14;
                    ws.Range(1, 1, 1, 6).Merge();

                    // Column headers
                    string[] headers = { "Priority", "Project #", "Project Name", "PM", "Phase", "Const Type", "Fee", "% Billed",
                                          "Eng %", "Draft %", "Fee/Hrs", "Billed/Hrs", "Delivery Risk", "Notes" };
                    WriteHeaderRow(ws, 3, headers);
                    ws.SheetView.FreezeRows(3);

                    static XLColor PriorityBg(int p) => p switch
                    {
                        1 => XLColor.FromHtml("#DC2626"),
                        2 => XLColor.FromHtml("#EA580C"),
                        3 => XLColor.FromHtml("#D97706"),
                        4 => XLColor.FromHtml("#2563EB"),
                        5 => XLColor.FromHtml("#6B7280"),
                        _ => XLColor.NoColor,
                    };

                    var ri = 4;
                    foreach (var group in groups)
                    {
                        // PM header row
                        var sumRow = ws.Range(ri, 1, ri, headers.Length);
                        sumRow.Style.Fill.BackgroundColor = XLColor.FromHtml("#EFF6FF");
                        sumRow.Style.Font.Bold = true;
                        ws.Cell(ri, 4).Value = group.PmName;
                        ws.Cell(ri, 2).Value = $"{group.ProjectCount} projects";
                        ws.Cell(ri, 7).Value = group.TotalFee; ws.Cell(ri, 7).Style.NumberFormat.Format = "$#,##0";
                        if (group.AtRiskOrCriticalCount > 0)
                            WriteRiskCell(ws.Cell(ri, 13), $"{group.AtRiskOrCriticalCount} at risk");
                        else
                        {
                            ws.Cell(ri, 13).Value = "Healthy";
                            ws.Cell(ri, 13).Style.Font.FontColor = XLColor.FromHtml("#166534");
                        }
                        ri++;

                        // All projects for this PM
                        foreach (var p in group.Projects)
                        {
                            // Priority badge if this project is in the meeting
                            if (priorityByWbs1.TryGetValue(p.Wbs1, out var mp) && mp.Priority >= 1 && mp.Priority <= 5)
                            {
                                var pCell = ws.Cell(ri, 1);
                                pCell.Value = mp.PriorityLabel;
                                pCell.Style.Font.Bold = true;
                                pCell.Style.Font.FontColor = XLColor.White;
                                pCell.Style.Fill.BackgroundColor = PriorityBg(mp.Priority);
                                pCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            }

                            ws.Cell(ri, 2).Value = p.Wbs1;
                            ws.Cell(ri, 3).Value = p.Name;
                            ws.Cell(ri, 4).Value = p.Pm;
                            ws.Cell(ri, 5).Value = p.Phase;
                            ws.Cell(ri, 6).Value = p.ConstructionType;
                            ws.Cell(ri, 7).Value = p.Fee; ws.Cell(ri, 7).Style.NumberFormat.Format = "$#,##0";
                            WritePctCell(ws.Cell(ri, 8), p.PercentBilled, p.PercentBilledText);
                            WritePctCell(ws.Cell(ri, 9), p.EngPercent, p.EngPercentText, isBudgetBurn: true);
                            WritePctCell(ws.Cell(ri, 10), p.DraftPercent, p.DraftPercentText, isBudgetBurn: true);
                            ws.Cell(ri, 11).Value = p.FeePerHours;    ws.Cell(ri, 11).Style.NumberFormat.Format = "$#,##0";
                            ws.Cell(ri, 12).Value = p.BilledPerHours; ws.Cell(ri, 12).Style.NumberFormat.Format = "$#,##0";
                            WriteRiskCell(ws.Cell(ri, 13), p.DeliveryRisk);

                            // Meeting notes for priority projects
                            if (priorityByWbs1.TryGetValue(p.Wbs1, out var mn) && !string.IsNullOrWhiteSpace(mn.Notes))
                            {
                                var notesCell = ws.Cell(ri, 14);
                                notesCell.Value = mn.Notes;
                                notesCell.Style.Alignment.WrapText = true;
                            }

                            ri++;
                        }
                    }

                    // Overall meeting notes
                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        ri += 1;
                        var lbl = ws.Cell(ri, 1);
                        lbl.Value = "Overall Notes:";
                        lbl.Style.Font.Bold = true;
                        ws.Range(ri, 1, ri, 2).Merge();

                        ri++;
                        var nCell = ws.Cell(ri, 1);
                        nCell.Value = notes;
                        nCell.Style.Alignment.WrapText = true;
                        ws.Range(ri, 1, ri, headers.Length).Merge();
                    }

                    var lastRow = ws.LastRowUsed()?.RowNumber() ?? 3;
                    ws.Range(3, 1, lastRow, headers.Length).SetAutoFilter();
                    ws.Columns(1, headers.Length).AdjustToContents();
                    ws.Column(11).Width = 50; // Notes column wider
                    wb.SaveAs(path);
                }).ConfigureAwait(true);

                MessageBox.Show(this, "Export completed.", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Export failed:\n{ex.Message}", "Export to Excel", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void Window_Closing(object? sender, CancelEventArgs e)
        {
            _cts?.Cancel();
            try
            {
                using var flushCts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _meetingPanel.ForceSaveAllAsync(flushCts.Token);
            }
            catch { }
            _meetingPanel.Dispose();
        }

        private void SyncMeetingPrioritiesToRows()
        {
            _isSyncingMeetingPriorities = true;
            try
            {
                var lookup = _meetingPanel.CurrentProjects
                    .ToDictionary(p => p.Wbs1, p => p.Priority, StringComparer.OrdinalIgnoreCase);
                foreach (var row in _vm.ProjectRows)
                    row.MeetingPriority = lookup.TryGetValue(row.Wbs1, out var p) ? p : 0;

                var projectLookup = _vm.ProjectRows
                    .ToDictionary(r => r.Wbs1, r => r, StringComparer.OrdinalIgnoreCase);

                var enrichedRows = _meetingPanel.CurrentProjects
                    .Select(p =>
                    {
                        projectLookup.TryGetValue(p.Wbs1, out var proj);
                        return new Kor.Operations.App.PMTools.WorkloadMeetingProjectRow
                        {
                            MeetingId = p.MeetingId,
                            Wbs1 = p.Wbs1,
                            Priority = p.Priority,
                            Notes = p.Notes ?? string.Empty,
                            ProjectName = proj?.Name ?? p.Wbs1,
                            PmName = proj?.Pm ?? string.Empty,
                        };
                    })
                    .OrderBy(r => r.Priority)
                    .ThenBy(r => r.ProjectName);

                _meetingPanel.SetPriorityProjectRows(enrichedRows);
            }
            finally
            {
                _isSyncingMeetingPriorities = false;
            }
        }

        private async void PriorityComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is not ComboBox cb) return;
            if (cb.DataContext is not PmProjectRow row) return;
            if (_isSyncingMeetingPriorities) return;
            if (!_meetingPanel.IsCurrentMeeting || _meetingPanel.SelectedMeeting == null) return;
            var priority = cb.SelectedIndex; // 0=unset,1=P1,...,5=P5
            await _meetingPanel.UpsertPriorityFromUiAsync(row.Wbs1, priority);
        }

        private Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts BuildPortfolioCounts()
        {
            var watch = _vm.ProjectRows.Count - _vm.PortfolioHighConfidenceCount - _vm.PortfolioCriticalCount;
            return new Kor.Operations.Financials.CfoMetrics.PortfolioHealthCounts(
                Healthy: _vm.PortfolioHighConfidenceCount,
                Watch: watch,
                Critical: _vm.PortfolioCriticalCount);
        }

        private static bool IsDataGridRowDoubleClick(MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d)
                return false;

            DependencyObject? cur = d;
            while (cur != null && cur is not DataGridRow)
                cur = VisualTreeHelper.GetParent(cur);

            return cur is DataGridRow;
        }

        private static DataGridCell? GetClickedCell(MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not DependencyObject d) return null;
            DependencyObject? cur = d;
            while (cur != null && cur is not DataGridCell)
                cur = VisualTreeHelper.GetParent(cur);
            return cur as DataGridCell;
        }
    }
}
