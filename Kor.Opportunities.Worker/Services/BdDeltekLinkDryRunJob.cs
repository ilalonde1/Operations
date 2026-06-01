#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Odbc;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Worker.Options;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quartz;

namespace Kor.Opportunities.Worker.Services;

[DisallowConcurrentExecution]
public sealed class BdDeltekLinkDryRunJob : IJob
{
    private static readonly string[] TargetKinds = { "Buyer", "Architect" };

    private readonly IOptions<OpportunitiesWorkerOptions> _options;
    private readonly ILogger<BdDeltekLinkDryRunJob> _logger;

    public BdDeltekLinkDryRunJob(
        IOptions<OpportunitiesWorkerOptions> options,
        ILogger<BdDeltekLinkDryRunJob> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task Execute(IJobExecutionContext context)
    {
        var opt = _options.Value;
        if (!opt.BdDeltekLinkDryRunEnabled)
        {
            _logger.LogDebug(
                "{Job} skipped: feature disabled via {Flag}.",
                nameof(BdDeltekLinkDryRunJob),
                nameof(opt.BdDeltekLinkDryRunEnabled));
            return;
        }

        var sw = Stopwatch.StartNew();
        var ct = context.CancellationToken;
        try
        {
            var config = DeltekLinkConfig.FromEnvironment();
            var clients = LoadClients(config);
            var linked = await LoadLinkedAsync(opt.OpportunitiesDb, ct).ConfigureAwait(false);
            var targets = await LoadTargetsAsync(opt.OpportunitiesDb, TargetKinds, ct).ConfigureAwait(false);

            var maxTargets = Math.Max(1, opt.BdDeltekLinkDryRunMaxTargets);
            if (targets.Count > maxTargets)
            {
                var message = $"BdDeltekLinkDryRun ABORT: targets={targets.Count:N0} exceeds safety cap={maxTargets:N0}.";
                _logger.LogError(
                    "BdDeltekLinkDryRun ABORT: targets={Targets} exceeds safety cap={Cap}. Refusing to run matcher.",
                    targets.Count,
                    maxTargets);
                context.Result = message;
                return;
            }

            var plan = BuildPlan(targets, clients, linked);
            WriteCsvs(opt.BdDeltekLinkDryRunOutputDir, DateTimeOffset.Now, plan);

            if (plan.LinkRows.Count > opt.BdDeltekLinkDryRunAlertThreshold)
            {
                _logger.LogWarning(
                    "BdDeltekLinkDryRun alert: auto-link count {AutoLink} exceeds threshold {Threshold}. Review matcher output before applying.",
                    plan.LinkRows.Count,
                    opt.BdDeltekLinkDryRunAlertThreshold);
            }

            var summary =
                $"BdDeltekLinkDryRun: targets={targets.Count} auto-link={plan.LinkRows.Count} " +
                $"review={plan.ReviewRows.Count} dedup={plan.DedupRows.Count} " +
                $"no-match={plan.NoMatchCount} (clients={clients.Count})";
            _logger.LogInformation(
                "BdDeltekLinkDryRun: targets={Targets} auto-link={AutoLink} review={Review} dedup={Dedup} no-match={NoMatch} (clients={Clients}) elapsedMs={Elapsed}.",
                targets.Count,
                plan.LinkRows.Count,
                plan.ReviewRows.Count,
                plan.DedupRows.Count,
                plan.NoMatchCount,
                clients.Count,
                sw.ElapsedMilliseconds);
            context.Result = summary;
        }
        catch (OperationCanceledException ex) when (ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "BdDeltekLinkDryRun canceled after {ElapsedMs}ms.", sw.ElapsedMilliseconds);
            context.Result = "BdDeltekLinkDryRun canceled.";
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("Required environment variable ", StringComparison.Ordinal))
        {
            _logger.LogError(ex, "BdDeltekLinkDryRun aborted: {Message}", ex.Message);
            context.Result = "BdDeltekLinkDryRun aborted: " + ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BdDeltekLinkDryRun failed after {ElapsedMs}ms.", sw.ElapsedMilliseconds);
            context.Result = $"BdDeltekLinkDryRun failed: {ex.GetType().Name}: {ex.Message}";
        }
    }

    private static LinkPlan BuildPlan(
        IReadOnlyList<CanonicalOrgTarget> targets,
        IReadOnlyList<DeltekClientCandidate> clients,
        IReadOnlyDictionary<string, IReadOnlyList<long>> linked)
    {
        var linkRows = new List<LinkPlanRow>();
        var reviewRows = new List<ReviewRow>();
        var dedupRows = new List<DedupCandidateRow>();
        var noMatch = 0;
        var plannedByClientId = new Dictionary<string, LinkPlanRow>(StringComparer.OrdinalIgnoreCase);

        foreach (var target in targets)
        {
            var match = DeltekFuzzyMatch.FindCompany(target.DisplayName, clients);
            if (match.Top.Count == 0)
            {
                noMatch++;
                continue;
            }

            var top = match.Top[0];
            var targetTokenCount = DeltekFuzzyMatch.NormalizeCompany(target.DisplayName)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var candidateTokenCount = DeltekFuzzyMatch.NormalizeCompany(top.Name)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var twoTokenSafe = targetTokenCount >= 2 && candidateTokenCount >= 2;

            if (match.TopScore >= 1.0d && twoTokenSafe)
            {
                if (linked.TryGetValue(top.ClientId, out var existingOrgIds) && existingOrgIds.Count > 0)
                {
                    dedupRows.Add(new DedupCandidateRow(
                        target.Id,
                        target.DisplayName,
                        existingOrgIds[0],
                        top.ClientId,
                        top.Name));
                    continue;
                }

                if (plannedByClientId.TryGetValue(top.ClientId, out var planned))
                {
                    dedupRows.Add(new DedupCandidateRow(
                        target.Id,
                        target.DisplayName,
                        planned.OrgId,
                        top.ClientId,
                        top.Name));
                    continue;
                }

                var row = new LinkPlanRow(target.Id, target.DisplayName, target.Kind, top.ClientId, top.Name, top.SimilarityScore);
                linkRows.Add(row);
                plannedByClientId[top.ClientId] = row;
            }
            else if (match.TopScore >= 0.85d ||
                     (match.TopScore >= 1.0d && !twoTokenSafe))
            {
                reviewRows.Add(new ReviewRow(target.Id, target.DisplayName, target.Kind, top.ClientId, top.Name, top.SimilarityScore));
            }
            else
            {
                noMatch++;
            }
        }

        return new LinkPlan(linkRows, reviewRows, dedupRows, noMatch);
    }

    private static IReadOnlyList<DeltekClientCandidate> LoadClients(DeltekLinkConfig config)
    {
        using var cn = new OdbcConnection(config.OdbcConnectionString);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT ClientID, Name, Type
FROM [{config.Catalog}].dbo.Clendor
WHERE ClientInd = 'Y'
  AND (Status IS NULL OR Status <> 'I')";
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekClientCandidate>();
        while (r.Read())
        {
            var id = GetTrimmed(r, 0);
            var name = GetTrimmed(r, 1);
            if (id.Length == 0 || name.Length == 0)
            {
                continue;
            }

            rows.Add(new DeltekClientCandidate(id, name, r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2))?.Trim(), 0d));
        }

        return rows;
    }

    private static async Task<IReadOnlyDictionary<string, IReadOnlyList<long>>> LoadLinkedAsync(
        string connectionString,
        CancellationToken ct)
    {
        const string sql = @"
SELECT Id, ClendorClientId
FROM opportunities.CanonicalOrg
WHERE ClendorClientId IS NOT NULL;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };

        var rows = new Dictionary<string, List<long>>(StringComparer.OrdinalIgnoreCase);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var orgId = r.GetInt64(0);
            var clientId = r.GetString(1).Trim();
            if (!rows.TryGetValue(clientId, out var orgIds))
            {
                orgIds = new List<long>();
                rows[clientId] = orgIds;
            }

            orgIds.Add(orgId);
        }

        return rows.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyList<long>)kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static async Task<IReadOnlyList<CanonicalOrgTarget>> LoadTargetsAsync(
        string connectionString,
        IReadOnlyList<string> kinds,
        CancellationToken ct)
    {
        var kindParams = kinds.Select((_, i) => $"@kind{i}").ToArray();
        var sql = $@"
SELECT Id, DisplayName, Kind
FROM opportunities.CanonicalOrg
WHERE ClendorClientId IS NULL
  AND Kind IN ({string.Join(", ", kindParams)})
ORDER BY DisplayName;";

        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        for (var i = 0; i < kinds.Count; i++)
        {
            cmd.Parameters.Add($"@kind{i}", SqlDbType.NVarChar, 40).Value = kinds[i];
        }

        var rows = new List<CanonicalOrgTarget>();
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new CanonicalOrgTarget(
                r.GetInt64(0),
                r.GetString(1),
                r.GetString(2)));
        }

        return rows;
    }

    private static void WriteCsvs(string outputDir, DateTimeOffset timestamp, LinkPlan plan)
    {
        Directory.CreateDirectory(outputDir);
        var prefix = timestamp.ToString("yyyy-MM-ddTHH-mm", CultureInfo.InvariantCulture);
        var linkLines = new[] { "OrgId,OrgName,Kind,ClendorClientId,DeltekName,Score" }
            .Concat(plan.LinkRows.Select(r => CsvRow(
                r.OrgId.ToString(CultureInfo.InvariantCulture),
                r.OrgName,
                r.Kind,
                r.ClendorClientId,
                r.DeltekName,
                r.Score.ToString("0.###", CultureInfo.InvariantCulture))))
            .ToArray();
        var reviewLines = new[] { "OrgId,OrgName,Kind,ClendorClientId,DeltekName,Score" }
            .Concat(plan.ReviewRows.Select(r => CsvRow(
                r.OrgId.ToString(CultureInfo.InvariantCulture),
                r.OrgName,
                r.Kind,
                r.ClendorClientId,
                r.DeltekName,
                r.Score.ToString("0.###", CultureInfo.InvariantCulture))))
            .ToArray();
        var dedupLines = new[] { "TargetOrgId,TargetName,ExistingOrgId,ClendorClientId,DeltekName" }
            .Concat(plan.DedupRows.Select(r => CsvRow(
                r.TargetOrgId.ToString(CultureInfo.InvariantCulture),
                r.TargetName,
                r.ExistingOrgId.ToString(CultureInfo.InvariantCulture),
                r.ClendorClientId,
                r.DeltekName)))
            .ToArray();

        WriteBoth(outputDir, prefix, "link-plan.csv", linkLines);
        WriteBoth(outputDir, prefix, "review.csv", reviewLines);
        WriteBoth(outputDir, prefix, "dedup-candidates.csv", dedupLines);
    }

    private static void WriteBoth(string outputDir, string prefix, string fileName, string[] lines)
    {
        File.WriteAllLines(Path.Combine(outputDir, prefix + "-" + fileName), lines, Encoding.UTF8);
        File.WriteAllLines(Path.Combine(outputDir, fileName), lines, Encoding.UTF8);
    }

    private static string GetTrimmed(IDataRecord r, int i)
        => r.IsDBNull(i) ? string.Empty : Convert.ToString(r.GetValue(i))?.Trim() ?? string.Empty;

    private static string CsvRow(params string?[] values)
        => string.Join(",", values.Select(Csv));

    private static string Csv(string? value)
    {
        value ??= string.Empty;
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    private sealed record DeltekLinkConfig(string Dsn, string User, string Password, string Catalog)
    {
        private const string DefaultCatalog = "C0000052267P_1_KOR00000000";

        public string OdbcConnectionString => $"DSN={Dsn};UID={User};PWD={Password};";

        public static DeltekLinkConfig FromEnvironment()
        {
            return new DeltekLinkConfig(
                Required("KOR_BD_DELTEK_DSN"),
                Required("KOR_BD_DELTEK_USER"),
                Required("KOR_BD_DELTEK_PWD"),
                Environment.GetEnvironmentVariable("KOR_BD_DELTEK_CATALOG")?.Trim() is { Length: > 0 } catalog
                    ? catalog
                    : DefaultCatalog);
        }

        private static string Required(string name)
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Required environment variable {name} is missing.");
            }

            return value.Trim();
        }
    }

    private sealed record DeltekClientCandidate(string ClientId, string Name, string? Type, double SimilarityScore);

    private sealed record CompanyMatch(IReadOnlyList<DeltekClientCandidate> Top)
    {
        public double TopScore => Top.Count == 0 ? 0d : Top[0].SimilarityScore;
    }

    private static class DeltekFuzzyMatch
    {
        private static readonly HashSet<string> CompanySuffixTokens = new(StringComparer.Ordinal)
        {
            // Legal-entity boilerplate only. Distinctive-name tokens
            // (properties / construction / architecture / engineering /
            // development / consulting / group / holdings) MUST stay
            // in the name so two firms that share a first word but
            // differ in line-of-business don't collide
            // (R60 audit: Fort Properties Ltd vs FORT Architecture).
            "inc", "incorporated", "ltd", "limited", "llc", "llp", "lp",
            "corp", "corporation", "co", "company",
            "international", "intl",
        };

        public static CompanyMatch FindCompany(string company, IReadOnlyList<DeltekClientCandidate> clients)
        {
            var normalized = NormalizeCompany(company);
            if (normalized.Length == 0)
            {
                return new CompanyMatch(Array.Empty<DeltekClientCandidate>());
            }

            var top = clients
                .Select(c => c with { SimilarityScore = Similarity(normalized, NormalizeCompany(c.Name)) })
                .OrderByDescending(c => c.SimilarityScore)
                .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();
            return new CompanyMatch(top);
        }

        public static string NormalizeCompany(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
            {
                return string.Empty;
            }

            var lower = s.ToLowerInvariant();
            var sb = new StringBuilder(lower.Length);
            foreach (var ch in lower)
            {
                if (char.IsLetterOrDigit(ch) || ch == ' ')
                {
                    sb.Append(ch);
                }
                else if (ch == ',' || ch == '.' || ch == '-' || ch == '/' || ch == '&')
                {
                    sb.Append(' ');
                }
            }

            var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var kept = new List<string>(tokens.Length);
            foreach (var t in tokens)
            {
                if (CompanySuffixTokens.Contains(t))
                {
                    continue;
                }

                kept.Add(t);
            }

            return string.Join(' ', kept);
        }

        public static double Similarity(string a, string b)
        {
            if (a.Length == 0 || b.Length == 0)
            {
                return 0.0;
            }

            if (a == b)
            {
                return 1.0;
            }

            var distance = Levenshtein(a, b);
            var maxLen = Math.Max(a.Length, b.Length);
            return 1.0 - ((double)distance / maxLen);
        }

        public static int Levenshtein(string a, string b)
        {
            if (a.Length < b.Length)
            {
                (a, b) = (b, a);
            }

            var prev = new int[b.Length + 1];
            var cur = new int[b.Length + 1];
            for (var j = 0; j <= b.Length; j++)
            {
                prev[j] = j;
            }

            for (var i = 1; i <= a.Length; i++)
            {
                cur[0] = i;
                for (var j = 1; j <= b.Length; j++)
                {
                    var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }

                (prev, cur) = (cur, prev);
            }

            return prev[b.Length];
        }
    }

    private sealed record CanonicalOrgTarget(long Id, string DisplayName, string Kind);

    private sealed record LinkPlan(
        IReadOnlyList<LinkPlanRow> LinkRows,
        IReadOnlyList<ReviewRow> ReviewRows,
        IReadOnlyList<DedupCandidateRow> DedupRows,
        int NoMatchCount);

    private sealed record LinkPlanRow(long OrgId, string OrgName, string Kind, string ClendorClientId, string DeltekName, double Score);

    private sealed record ReviewRow(long OrgId, string OrgName, string Kind, string ClendorClientId, string DeltekName, double Score);

    private sealed record DedupCandidateRow(long TargetOrgId, string TargetName, long ExistingOrgId, string ClendorClientId, string DeltekName);
}
