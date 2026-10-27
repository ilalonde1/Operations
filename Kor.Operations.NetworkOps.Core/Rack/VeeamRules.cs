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
    /// <param name="detailJson">What veeam-detail.ps1 returned ON BK01 (every job's last session, Backup Copy jobs included,
    /// and the newest SureBackup sessions with each VM's result), or null when it was not read.</param>
    /// <param name="detailReadUtc">When that detail was read (it is read at most hourly; between reads it is re-judged).</param>
    /// <param name="detailError">Why the detail could not be read, when it could not.</param>
    public static RackResult Evaluate(string jobsJson, string reposJson, DateTime nowUtc, IReadOnlyDictionary<string, string>? previousFacts = null,
        int staleHours = 36, int repoFreeWarnPct = 15, string? serverVersion = null, string? detailJson = null, DateTime? detailReadUtc = null,
        string? detailError = null)
    {
        var b = new RackBuilder();
        // The B&R server build, for VersionBaselineRules (Class 1): Veeam below the secure build carries CVE-2025-64393.
        if (!string.IsNullOrWhiteSpace(serverVersion)) b.Fact("veeam.version", serverVersion);
        using var jobs = JsonDocument.Parse(jobsJson);
        using var repos = JsonDocument.Parse(reposJson);
        int ok = 0, total = 0;
        var restJobs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var j in jobs.RootElement.GetProperty("data").EnumerateArray())
        {
            total++;
            var name = j.GetProperty("name").GetString() ?? "?";
            restJobs.Add(name);
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
        var restore = Detail(b, detailJson, detailReadUtc, detailError, restJobs, nowUtc, previousFacts, staleHours, ref ok, ref total);
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
        return b.Done($"{ok} of {total} jobs OK{restore}");
    }

    /// <summary>The read-only script run ON the Veeam server for the detail (Rack/veeam-detail.ps1, embedded).</summary>
    public static string DetailScript
    {
        get
        {
            using var s = typeof(VeeamRules).Assembly.GetManifestResourceStream("Rack.veeam-detail.ps1")
                ?? throw new InvalidOperationException("no embedded Rack.veeam-detail.ps1");
            return new StreamReader(s).ReadToEnd();
        }
    }

    /// <summary>A SureBackup restore test older than this has stopped running (it runs nightly after the backups).</summary>
    public const int RestoreTestStaleDays = 8;

    /// <summary>Hours without a successful detail read before NetworkOps says it cannot see the copy jobs and restore tests.</summary>
    public const int DetailUnreadHours = 6;

    /// <summary>
    /// The jobs the REST API does not list (Backup Copy) judged exactly like the others -- failed, warning, disabled,
    /// stale -- and each SureBackup job's latest FINISHED restore test. Returns the summary's restore-test clause.
    /// </summary>
    private static string Detail(RackBuilder b, string? detailJson, DateTime? readUtc, string? error, HashSet<string> restJobs, DateTime nowUtc,
        IReadOnlyDictionary<string, string>? previousFacts, int staleHours, ref int ok, ref int total)
    {
        if (detailJson is null)
        {
            // Not read: say so, and once it has been blind for hours, raise it -- blindness is the class this exists for.
            var lastRead = previousFacts?.GetValueOrDefault("veeam.detail.read");
            b.Fact("veeam.detail.read", lastRead);
            if (error is not null) b.Fact("veeam.detail.error", error);
            var since = lastRead is not null && DateTime.TryParse(lastRead, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t.ToUniversalTime() : (DateTime?)null;
            if (error is not null && (since is null || (nowUtc - since.Value).TotalHours > DetailUnreadHours))
                b.Raise("veeam.detail-unread", Severity.Warning, "NetworkOps cannot read the Backup Copy jobs or the SureBackup restore tests",
                    $"{error}; last read {Local(since)}");
            return "";
        }
        b.Fact("veeam.detail.read", (readUtc ?? nowUtc).ToString("o", CultureInfo.InvariantCulture));
        using var doc = JsonDocument.Parse(detailJson);
        var root = doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0 ? doc.RootElement[0] : doc.RootElement;

        foreach (var j in Items(root, "Jobs"))
        {
            var name = Str(j, "Name") ?? "?";
            if (restJobs.Contains(name)) continue;   // the REST read already judged it
            total++;
            var type = Str(j, "Type") ?? "job";
            var state = Str(j, "State") ?? "";
            var result = Str(j, "Result") ?? "";
            var start = Time(j, "Start");
            if (start is { } s0) b.Metric("job.age.hours", Math.Round((nowUtc - s0).TotalHours, 1), name);
            if (j.TryGetProperty("Enabled", out var en) && en.ValueKind == JsonValueKind.False)
            { b.Raise($"veeam.disabled:{name}", Severity.Warning, $"{type} job {name} is disabled", "it will not run until re-enabled"); continue; }
            var running = state.Equals("Working", StringComparison.OrdinalIgnoreCase);
            var finished = !running && result.Length > 0 && !result.Equals("None", StringComparison.OrdinalIgnoreCase);
            var judged = finished ? result : previousFacts?.GetValueOrDefault($"veeam.result:{name}") ?? result;
            if (finished) b.Fact($"veeam.result:{name}", result);
            else if (previousFacts?.GetValueOrDefault($"veeam.result:{name}") is { } kept) b.Fact($"veeam.result:{name}", kept);
            var retrying = running ? " (a new run is in progress now)" : "";
            if (judged.Equals("Failed", StringComparison.OrdinalIgnoreCase))
                b.Raise($"veeam.failed:{name}", Severity.Critical, $"{type} job {name} failed", $"its last finished run ended Failed{retrying}");
            else if (judged.Equals("Warning", StringComparison.OrdinalIgnoreCase))
                b.Raise($"veeam.warning:{name}", Severity.Warning, $"{type} job {name} finished with warnings", $"its last finished run ended Warning{retrying}");
            else ok++;
            if (!running && start is { } last && (nowUtc - last).TotalHours > staleHours)
                b.Raise($"veeam.stale:{name}", Severity.Critical, $"{type} job {name} has not run for {(nowUtc - last).TotalDays:0.#} days",
                    $"last run {Local(last)}: the silent-failure class");
        }

        var clauses = new List<string>();
        foreach (var g in Items(root, "SureBackup").GroupBy(s => Str(s, "Job") ?? "?"))
        {
            var job = g.Key;
            var newest = g.First();
            if (Time(newest, "Start") is { } ns && (nowUtc - ns).TotalDays > RestoreTestStaleDays)
                b.Raise($"veeam.surebackup-stale:{job}", Severity.Warning, $"Restore test {job} has not run for {(nowUtc - ns).TotalDays:0} days",
                    $"last run {Local(ns)}: nobody has proven the backups restore since");
            // The verdict is the newest FINISHED test; one running now is not a result yet.
            if (g.FirstOrDefault(s => !(Str(s, "State") ?? "").Equals("Working", StringComparison.OrdinalIgnoreCase)) is not { ValueKind: JsonValueKind.Object } done) continue;
            var result = Str(done, "Result") ?? "";
            var when = Local(Time(done, "End") ?? Time(done, "Start"));
            var vms = string.Join(", ", Items(done, "Vms").Select(v => $"{Str(v, "Name")} {Vm(Str(v, "Result"))}"));
            if (result.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                b.Raise($"veeam.surebackup-failed:{job}", Severity.Critical, $"Restore test {job} failed",
                    $"{when}: {(vms.Length > 0 ? vms : "no VM results")}; the backups were not proven to restore");
                clauses.Add("restore test failed");
            }
            else if (result.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            {
                b.Raise($"veeam.surebackup-warning:{job}", Severity.Warning, $"Restore test {job} finished with warnings", $"{when}: {vms}");
                clauses.Add("restore test warning");
            }
            else if (result.Equals("Success", StringComparison.OrdinalIgnoreCase)) clauses.Add("restore test OK");
        }
        return clauses.Count == 0 ? "" : " · " + string.Join(", ", clauses.Distinct());
    }

    private static string Vm(string? result) => result switch
    {
        null or "" => "(no result)",
        "NotRunning" => "not tested",
        _ => result,
    };

    private static IEnumerable<JsonElement> Items(JsonElement e, string p)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(p, out var a)
            ? a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToList() : a.ValueKind == JsonValueKind.Object ? [a] : []
            : [];

    private static string? Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static DateTime? Time(JsonElement e, string p)
        => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String
           && DateTimeOffset.TryParse(v.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t.UtcDateTime : null;

    private static string Local(DateTime? utc) => utc is { } u ? u.ToLocalTime().ToString("ddd d MMM HH:mm", CultureInfo.InvariantCulture) : "never";
}
