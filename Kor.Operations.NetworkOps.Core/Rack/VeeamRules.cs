#nullable enable
using System.Globalization;
using System.Text.Json;
using Kor.Operations.NetworkOps.Core.Health;

namespace Kor.Operations.NetworkOps.Core.Rack;

/// <summary>
/// Veeam on KOR-BK01, from its REST API (/api/v1/jobs/states and /backupInfrastructure/repositories/states), read
/// by a Backup Viewer account. The class of fault it exists for: the 8-day SILENT backup failure -- so the
/// headline rule is freshness, not the last result: a job that has not run is as bad as one that failed.
/// </summary>
public static class VeeamRules
{
    /// <param name="previousFacts">
    /// The device's facts from the last read. While a job RUNS, Veeam reports its last result as "None": the
    /// finished result is kept as a fact (veeam.result:JOB) and judged instead, so a failed job does not look
    /// healthy -- and then re-alert -- every time its retry starts (Kor-FS01, 30 Sep 2026).
    /// </param>
    public static RackResult Evaluate(string jobsJson, string reposJson, DateTime nowUtc, IReadOnlyDictionary<string, string>? previousFacts = null,
        int staleHours = 36, int repoFreeWarnPct = 15)
    {
        var b = new RackBuilder();
        using var jobs = JsonDocument.Parse(jobsJson);
        using var repos = JsonDocument.Parse(reposJson);
        int ok = 0, total = 0;
        foreach (var j in jobs.RootElement.GetProperty("data").EnumerateArray())
        {
            total++;
            var name = j.GetProperty("name").GetString() ?? "?";
            var result = j.TryGetProperty("lastResult", out var lr) ? lr.GetString() ?? "" : "";
            var status = j.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
            var lastRun = Time(j, "lastRun");
            var nextRun = Time(j, "nextRun");
            var disabled = j.TryGetProperty("isDisabled", out var d) && d.ValueKind == JsonValueKind.True;
            if (lastRun is { } lrun) b.Metric("job.age.hours", Math.Round((nowUtc - lrun).TotalHours, 1), name);

            if (disabled) { b.Raise($"veeam.disabled:{name}", Severity.Warning, $"Backup job {name} is disabled", "it will not run until re-enabled"); continue; }
            var running = status.Equals("running", StringComparison.OrdinalIgnoreCase);
            var finished = !running && result is { Length: > 0 } && !result.Equals("None", StringComparison.OrdinalIgnoreCase);
            // The last FINISHED result: this read's if the job is idle, else the one remembered from before it started.
            var judged = finished ? result : previousFacts?.GetValueOrDefault($"veeam.result:{name}") ?? result;
            if (finished) b.Fact($"veeam.result:{name}", result);
            else if (previousFacts?.GetValueOrDefault($"veeam.result:{name}") is { } kept) b.Fact($"veeam.result:{name}", kept);
            var retrying = running ? " (a new run is in progress now)" : "";
            if (judged.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                b.Raise($"veeam.failed:{name}", Severity.Critical, $"Backup job {name} FAILED", $"its last finished run ended Failed{retrying}");
            else if (judged.Equals("Warning", StringComparison.OrdinalIgnoreCase))
                b.Raise($"veeam.warning:{name}", Severity.Warning, $"Backup job {name} finished with warnings", $"its last finished run ended Warning{retrying}");
            else ok++;

            // Freshness: a scheduled job whose last run is older than staleHours has silently stopped. A running job is fresh.
            var scheduled = nextRun is not null;
            if (status != "running" && lastRun is { } last && (nowUtc - last).TotalHours > staleHours && (scheduled || (nowUtc - last).TotalDays > 7))
                b.Raise($"veeam.stale:{name}", Severity.Critical, $"Backup job {name} has not run for {(nowUtc - last).TotalDays:0.#} days",
                    $"last run {Local(lastRun)}; next scheduled {(nextRun is { } n ? Local(n) : "never")} -- the silent-failure class");
        }
        b.Metric("jobs.count", total);

        foreach (var r in repos.RootElement.GetProperty("data").EnumerateArray())
        {
            var name = r.GetProperty("name").GetString() ?? "?";
            var online = !r.TryGetProperty("isOnline", out var on) || on.ValueKind != JsonValueKind.False;
            if (!online) { b.Raise($"veeam.repo-offline:{name}", Severity.Critical, $"Backup repository {name} is offline", "jobs that target it will fail"); continue; }
            var cap = r.TryGetProperty("capacityGB", out var c) ? c.GetDouble() : 0;
            var free = r.TryGetProperty("freeGB", out var f) ? f.GetDouble() : 0;
            if (cap <= 0) continue;
            var pct = 100 * free / cap;
            b.Metric("repo.free.pct", Math.Round(pct, 1), name);
            if (pct < repoFreeWarnPct)
                b.Raise($"veeam.repo-full:{name}", pct < repoFreeWarnPct / 2.0 ? Severity.Critical : Severity.Warning, $"Backup repository {name} is filling up",
                    $"{free / 1000:0.0} TB free of {cap / 1000:0.0} TB ({pct:0.0}%)");
        }
        return b.Done($"{ok} of {total} jobs OK");
    }

    private static DateTime? Time(JsonElement e, string p)
        => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.UtcDateTime : null;

    private static string Local(DateTime? utc) => utc is { } u ? u.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture) : "never";
}
