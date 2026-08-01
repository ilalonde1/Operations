#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Options;
using Kor.Operations.Shared;

namespace Kor.Operations.PMTools
{
    /// <summary>
    /// App-side compatibility composer for Historical Analytics.
    /// Data access and computation live in Kor.Operations.Business analytics services;
    /// this wrapper preserves the original WPF call surface.
    /// </summary>
    internal sealed class HistoricalAnalyticsService
    {
        private readonly ProjectAnalyticsService _projectService;
        private readonly EmployeeAnalyticsService _employeeService;
        private readonly FirmAnalyticsService _firmService;

        public HistoricalAnalyticsService(DeltekOdbcOptions opts, FinancialsOptions? financialsOpts = null)
        {
            if (opts == null) throw new ArgumentNullException(nameof(opts));
            _employeeService = new EmployeeAnalyticsService(opts);
            _firmService = new FirmAnalyticsService(opts);
            _projectService = new ProjectAnalyticsService(opts, financialsOpts, EstimatePeerBudget);
        }

        public async Task<(List<HistoricalProjectRow> Rows, FirmUtilizationStats Utilization, List<EmployeeProjectHours> EmployeeHours, List<EmployeeWeeklyHours> WeeklyUtilization, List<EmployeeRate> EmployeeRates)> LoadAsync(CancellationToken ct = default)
        {
            var rowsTask = Task.Run(() => _projectService.LoadProjectRowsSync(ct), ct);
            var timelineTask = Task.Run(() => _projectService.LoadRevenueTimelineSync(ct), ct);
            var utilizationTask = Task.Run(() => _firmService.LoadFirmUtilizationSync(ct), ct);
            var employeeTask = Task.Run(() => _employeeService.LoadEmployeeProjectHoursSync(ct), ct);
            var weeklyTask = Task.Run(() => _employeeService.LoadEmployeeWeeklyUtilizationSync(ct), ct);
            var ratesTask = Task.Run(() => _employeeService.LoadEmployeeRatesSync(ct), ct);
            await Task.WhenAll(rowsTask, timelineTask, utilizationTask, employeeTask, weeklyTask, ratesTask).ConfigureAwait(false);

            var rows = rowsTask.Result;
            _projectService.AttachRevenueTimelines(rows, timelineTask.Result);
            return (rows, utilizationTask.Result, employeeTask.Result, weeklyTask.Result, ratesTask.Result);
        }

        internal List<QuarterlyEmployeeHours> LoadQuarterlyEmployeeHoursSync(CancellationToken ct)
            => _employeeService.LoadQuarterlyEmployeeHoursSync(ct);

        private static (double EngHrs, double DraftHrs, int PeerCount) EstimatePeerBudget(
            double fee,
            string? phase,
            string? constructionType,
            string? projectCategory,
            IReadOnlyList<HistoricalPeerProject> allPeers,
            string excludeWbs1)
        {
            var peers = allPeers
                .Select(p => new PeerBudgetEstimator.PeerProject
                {
                    Wbs1 = p.Wbs1,
                    Fee = p.Fee,
                    Phase = p.Phase,
                    ConstructionType = p.ConstructionType,
                    ProjectCategory = p.ProjectCategory,
                    EngHrs = p.EngHrs,
                    DraftHrs = p.DraftHrs,
                })
                .ToList();
            var (eng, draft, count) = PeerBudgetEstimator.Estimate(fee, phase, constructionType, projectCategory, peers, excludeWbs1);
            return (eng, draft, count);
        }
    }
}
