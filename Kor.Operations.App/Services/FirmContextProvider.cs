#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Financials;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.Services;

internal sealed class FirmContextProvider : IAiContextProvider
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<FirmContextProvider> _logger;
    private readonly object _cacheLock = new();
    private FirmBaselineSummary? _cachedSummary;
    private DateTime _cachedAt = DateTime.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public FirmContextProvider(IServiceScopeFactory scopeFactory, ILogger<FirmContextProvider> logger)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => "Firm Baseline";

    public bool HasData => true;

    public string BuildContext()
    {
        try
        {
            var now = DateTime.UtcNow;
            FirmBaselineSummary summary;

            lock (_cacheLock)
            {
                if (_cachedSummary is not null && (now - _cachedAt) <= CacheTtl)
                {
                    return FormatContext(_cachedSummary);
                }
            }

            // IAiContextProvider is synchronous, so this provider must bridge to the
            // cached async FinancialsService call here.
            summary = GetFirmBaselineSummaryAsync(CancellationToken.None).GetAwaiter().GetResult();

            lock (_cacheLock)
            {
                _cachedSummary = summary;
                _cachedAt = now;
            }

            return FormatContext(summary);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Firm-wide baseline failed to compute.");
            return "Firm-wide baseline unavailable.";
        }
    }

    public string BuildLocalContext() => "";

    private async Task<FirmBaselineSummary> GetFirmBaselineSummaryAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var financialsService = scope.ServiceProvider.GetRequiredService<FinancialsService>();
            var snapshot = await financialsService.GetSnapshotAsync(
                forceRefresh: false,
                ct,
                watchlistOnly: false).ConfigureAwait(false);

            var activeProjectCount = snapshot.Rows.Count;
            decimal totalActiveFee = 0m;
            var zeroFeeProjectCount = 0;
            var unknownClientProjectCount = 0;
            var topClients = snapshot.ClientRollups
                .OrderByDescending(rollup => rollup.LifetimeFee)
                .Take(10)
                .Select(rollup => new TopClientLine(
                    rollup.ClientName,
                    Convert.ToDecimal(rollup.LifetimeFee),
                    rollup.ProjectCount,
                    rollup.ActiveProjectCount,
                    rollup.LastActivityDate))
                .ToList();
            var firmAr90PlusTotal = snapshot.ClientRollups.Sum(rollup => Convert.ToDecimal(rollup.Outstanding90Plus));
            var topArClients = snapshot.ClientRollups
                .Where(rollup => rollup.Outstanding90Plus > 0)
                .OrderByDescending(rollup => rollup.Outstanding90Plus)
                .Take(5)
                .Select(rollup => new TopArLine(
                    rollup.ClientName,
                    Convert.ToDecimal(rollup.Outstanding90Plus),
                    rollup.ActiveProjectCount,
                    rollup.LastActivityDate))
                .ToList();
            // snapshot.RevenueHistory ends at the max posted period; take the last 12 months directly
            // rather than filtering by calendar date (which would read across any empty trailing period).
            var trailing12Months = snapshot.RevenueHistory
                .OrderByDescending(month => month.MonthStart)
                .Take(12)
                .OrderBy(month => month.MonthStart)
                .ToList();
            var firmTrailing12MonthRevenue = trailing12Months.Sum(month => Convert.ToDecimal(month.Revenue));
            var monthsCoveredInTrailing12 = trailing12Months.Count;
            var topPmsByActiveLoad = snapshot.Rows
                .Where(row => !string.IsNullOrWhiteSpace(row.Pm))
                .GroupBy(row => row.Pm)
                .Select(group => new TopPmByLoadLine(
                    group.Key,
                    group.Count(),
                    group.Sum(row => Convert.ToDecimal(row.TotalFee))))
                .OrderByDescending(line => line.ActiveProjectCount)
                .Take(5)
                .ToList();
            var overComputedBudget = snapshot.Rows.Count(r =>
                r.EngPercent > 1.35 || r.DraftPercent > 1.35);
            var budgetSourceBreakdown = snapshot.Rows
                .GroupBy(r => NormalizeBudgetSource(r.BudgetSource))
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
            double firmTotalHoursSpent = 0;

            foreach (var row in snapshot.Rows)
            {
                totalActiveFee += Convert.ToDecimal(row.TotalFee);
                firmTotalHoursSpent += row.EngHrs + row.DraftHrs;
                if (row.TotalFee == 0)
                {
                    zeroFeeProjectCount++;
                }

                if (string.IsNullOrWhiteSpace(row.ClientId) || string.IsNullOrWhiteSpace(row.ClientName))
                {
                    unknownClientProjectCount++;
                }
            }
            var firmBlendedFeePerHr = firmTotalHoursSpent > 0
                ? totalActiveFee / Convert.ToDecimal(firmTotalHoursSpent)
                : 0m;

            return new FirmBaselineSummary(
                activeProjectCount,
                totalActiveFee,
                zeroFeeProjectCount,
                unknownClientProjectCount,
                topClients,
                firmAr90PlusTotal,
                topArClients,
                firmTrailing12MonthRevenue,
                monthsCoveredInTrailing12,
                firmBlendedFeePerHr,
                firmTotalHoursSpent,
                topPmsByActiveLoad,
                overComputedBudget,
                budgetSourceBreakdown);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load firm baseline summary.");
            throw;
        }
    }

    private static string FormatContext(FirmBaselineSummary summary)
        => "Firm-wide baseline (cached up to 5 min):\n" +
           $"- Active projects (firm): {summary.ActiveProjectCount:N0}\n" +
           $"- Total active fee: ${summary.TotalActiveFee:N0}\n" +
           $"- Active projects with $0 fee: {summary.ZeroFeeProjectCount:N0}\n" +
           $"- Active projects with unassigned client: {summary.UnknownClientProjectCount:N0}\n\n" +
           "Top 10 clients by lifetime fee:\n" +
           string.Join("\n", summary.TopClients.Select((client, index)
               => $"{index + 1}. {client.ClientName}  ${client.LifetimeFee:N0} ({client.ProjectCount} total, {client.ActiveProjectCount} active, last {(client.LastActivity.HasValue ? client.LastActivity.Value.ToString("yyyy-MM") : "n/a")})")) +
           "\n\nAR 90+ exposure (firm-wide):\n" +
           $"- Total 90+ AR: ${summary.FirmAr90PlusTotal:N0}\n" +
           "- Top 5 clients by 90+ AR:\n" +
           string.Join("\n", summary.TopArClients.Select((client, index)
               => $"{index + 1}. {client.ClientName}  ${client.Ar90Plus:N0} ({client.ActiveProjectCount} active, last {(client.LastActivity.HasValue ? client.LastActivity.Value.ToString("yyyy-MM") : "n/a")})")) +
           "\n\n" +
           $"Trailing 12-month revenue recognized (firm): ${summary.FirmTrailing12MonthRevenue:N0} over {summary.MonthsCoveredInTrailing12} months" +
           "\n\n" +
           $"Firm blended Fee/Hr (active portfolio, includes $0-fee projects): ${summary.FirmBlendedFeePerHr:N2}/hr across {summary.FirmTotalHoursSpent:N0} hours spent" +
           "\n\nTop 5 PMs by active project load:\n" +
           string.Join("\n", summary.TopPmsByActiveLoad.Select((line, index)
               => $"{index + 1}. {line.Pm}  {line.ActiveProjectCount} projects, ${line.TotalActiveFee:N0} active fee")) +
           "\n\n" +
           $"Active projects over computed budget (>1.35 eng or draft hrs / budget): {summary.OverComputedBudgetActiveCount:N0}\n" +
           "Note: budget denominator is sourced per-project from Deltek actual, peer-median, or formula/target-rate fallback. See breakdown below.\n\n" +
           "Active projects by budget source:\n" +
           BuildBudgetSourceLines(summary.ActiveProjectsByBudgetSource);

    private static string NormalizeBudgetSource(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return "Unknown";
        var trimmed = source.Trim();
        if (trimmed.StartsWith("Peers", StringComparison.OrdinalIgnoreCase)) return "Peers";
        return trimmed;
    }

    private static string BuildBudgetSourceLines(IReadOnlyDictionary<string, int> breakdown)
    {
        var preferredOrder = new[] { "Deltek", "Peers", "Formula", "Target Rate", "Unknown" };
        var lines = new List<string>();
        foreach (var key in preferredOrder)
        {
            if (breakdown.TryGetValue(key, out var count))
            {
                lines.Add($"- {key}: {count:N0}");
            }
        }

        return string.Join("\n", lines);
    }

    private sealed record FirmBaselineSummary(
        int ActiveProjectCount,
        decimal TotalActiveFee,
        int ZeroFeeProjectCount,
        int UnknownClientProjectCount,
        IReadOnlyList<TopClientLine> TopClients,
        decimal FirmAr90PlusTotal,
        IReadOnlyList<TopArLine> TopArClients,
        decimal FirmTrailing12MonthRevenue,
        int MonthsCoveredInTrailing12,
        decimal FirmBlendedFeePerHr,
        double FirmTotalHoursSpent,
        IReadOnlyList<TopPmByLoadLine> TopPmsByActiveLoad,
        int OverComputedBudgetActiveCount,
        IReadOnlyDictionary<string, int> ActiveProjectsByBudgetSource);

    private sealed record TopClientLine(
        string ClientName,
        decimal LifetimeFee,
        int ProjectCount,
        int ActiveProjectCount,
        DateTime? LastActivity);

    private sealed record TopArLine(
        string ClientName,
        decimal Ar90Plus,
        int ActiveProjectCount,
        DateTime? LastActivity);

    private sealed record TopPmByLoadLine(
        string Pm,
        int ActiveProjectCount,
        decimal TotalActiveFee);
}
