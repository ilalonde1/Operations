#nullable enable
using System.Data;
using System.Data.Odbc;
using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Crm;
using Kor.Opportunities.Data.Opportunities;

namespace Kor.BdSeedImport;

internal static class Program
{
    private const string DefaultCatalog = "C0000052267P_1_KOR00000000";
    private const decimal UsdToCad = 1.36m;

    private static readonly Dictionary<string, string> RegionBySheet = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Vancouver; Lower Mainland"] = "VAN",
        ["Vancouver Island"] = "VIS",
        ["USA"] = "USA",
        ["Okanagan; BC Interior"] = "OKN",
        ["Alberta"] = "ALB",
        ["Eastern Canada"] = "EAS",
    };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ImportOptions.Parse(args);
            var config = ImportConfig.FromEnvironment();
            Directory.CreateDirectory(options.OutputDir);

            var log = new RunLog(options.OutputDir);
            var stats = new ImportStats();
            var ct = CancellationToken.None;

            var opportunityStore = new SqlOpportunityStore(config.OpportunitiesDb);
            var crmStore = new SqlCrmEngagementStore(config.OpportunitiesDb);
            var deltek = new DeltekLookupCache(config);

            log.Line($"[START] BdSeedImport dry-run={options.DryRun}; xlsx={options.XlsxPath}");
            var clients = deltek.LoadClients();
            log.Line($"[DELTEK] Loaded {clients.Count} active client candidate(s).");

            using var workbook = new XLWorkbook(options.XlsxPath);
            foreach (var sheet in workbook.Worksheets)
            {
                if (!RegionBySheet.TryGetValue(sheet.Name, out var regionCode))
                {
                    log.Line($"[WARN] Skipping sheet '{sheet.Name}' - not in region map.");
                    continue;
                }

                stats.SheetsProcessed++;
                await ProcessSheetAsync(sheet, regionCode, clients, deltek, opportunityStore, crmStore, options, log, stats, ct)
                    .ConfigureAwait(false);
            }

            log.WriteReviewCsv();
            log.Flush();
            WriteSummary(options, stats);
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BdSeedImport failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static async Task ProcessSheetAsync(
        IXLWorksheet sheet,
        string regionCode,
        IReadOnlyList<DeltekClientCandidate> clients,
        DeltekLookupCache deltek,
        IOpportunityStore opportunityStore,
        ICrmEngagementStore crmStore,
        ImportOptions options,
        RunLog log,
        ImportStats stats,
        CancellationToken ct)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        for (var rowNumber = 4; rowNumber <= lastRow; rowNumber++)
        {
            stats.RowsSeen++;
            var rowRef = $"{sheet.Name}!A{rowNumber}";
            var row = sheet.Row(rowNumber);

            if (!TryGetSequence(row.Cell(1), out var seq))
            {
                Skip(log, stats, rowRef, "Column A is empty or non-numeric.");
                continue;
            }

            var company = NullIfBlank(row.Cell(5).GetString());
            if (company is null)
            {
                Skip(log, stats, rowRef, "Column E Company is empty.");
                continue;
            }

            if (!TryGetExcelSerial(row.Cell(2), out var serial) || serial is < 1d or > 60000d)
            {
                Skip(log, stats, rowRef, "Column B Date is empty or not a valid Excel serial in range [1, 60000].");
                continue;
            }

            var rowDate = DateOnly.FromDateTime(DateTime.FromOADate(serial));
            var rowInstant = new DateTimeOffset(rowDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc));
            var key = $"BD-{regionCode}-{rowDate:yyyyMMdd}-{seq:D3}";
            var contactName = NullIfBlank(row.Cell(4).GetString());
            var contactInfo = NullIfBlank(row.Cell(6).GetString());
            var location = NullIfBlank(row.Cell(7).GetString());
            var potentialProjects = NullIfBlank(row.Cell(9).GetString());
            var proposalsSubmitted = ConvertCurrency(ParseMoney(row.Cell(10)), regionCode);
            var proposalsAccepted = ConvertCurrency(ParseMoney(row.Cell(11)), regionCode);
            var notes = NullIfBlank(row.Cell(12).GetString());

            var buyerContactEmail = contactInfo?.Contains('@') == true ? contactInfo : null;
            var buyerContactPhone = buyerContactEmail is null && contactInfo?.Any(char.IsDigit) == true ? contactInfo : null;

            var match = DeltekFuzzyMatch.FindCompany(company, clients);
            string? deltekClientId = null;
            if (match.TopScore >= 1.0d && match.Top.Count > 0)
            {
                deltekClientId = match.Top[0].ClientId;
                stats.AutoMatched++;
                log.Line($"[MATCH] {key}: {company} -> {match.Top[0].ClientId} {match.Top[0].Name} ({match.Top[0].SimilarityScore:0.###})");
            }
            else if (match.TopScore >= 0.85d)
            {
                stats.NeedsReview++;
                log.Line($"[REVIEW] {key}: {company} best={match.Top[0].ClientId} {match.Top[0].Name} ({match.Top[0].SimilarityScore:0.###})");
                log.Review(key, company, match.Top);
            }
            else
            {
                stats.NoMatch++;
                log.Line($"[NO-MATCH] {key}: {company}");
            }

            string? deltekContactId = null;
            if (deltekClientId is not null && (contactName is not null || buyerContactEmail is not null))
            {
                deltekContactId = deltek.FindContact(deltekClientId, contactName, buyerContactEmail);
            }

            var status = OpportunityStatus.Identified;
            WonLostOutcome? outcome = null;
            DateTimeOffset? proposalSubmittedAt = null;
            DateTimeOffset? outcomeAt = null;
            decimal? awardedValue = null;
            decimal? estimatedValue = null;

            if (proposalsAccepted > 0m)
            {
                status = OpportunityStatus.Won;
                outcome = WonLostOutcome.Won;
                awardedValue = proposalsAccepted;
                outcomeAt = rowInstant;
                estimatedValue = proposalsSubmitted > 0m ? proposalsSubmitted : proposalsAccepted;
            }
            else if (proposalsSubmitted > 0m)
            {
                status = OpportunityStatus.ProposalSubmitted;
                estimatedValue = proposalsSubmitted;
                proposalSubmittedAt = rowInstant;
            }

            var draft = new Opportunity
            {
                OpportunityKey = key,
                Name = Truncate($"{company} - {potentialProjects ?? "(no project description)"}", 200),
                BuyerName = company,
                BuyerType = BuyerType.Unknown,
                DeltekClientId = deltekClientId,
                DeltekContactId = deltekContactId,
                BuyerContactName = contactName,
                BuyerContactEmail = buyerContactEmail,
                BuyerContactPhone = buyerContactPhone,
                ProjectCity = location,
                Discipline = OpportunityDiscipline.Unknown,
                EstimatedValue = estimatedValue,
                EstimatedValueCurrency = "CAD",
                Status = status,
                IdentifiedAtUtc = rowInstant,
                ProposalSubmittedAtUtc = proposalSubmittedAt,
                OutcomeAtUtc = outcomeAt,
                WonLostOutcome = outcome,
                AwardedValue = awardedValue,
                OutcomeReason = notes,
            };

            var existing = await opportunityStore.GetByKeyAsync(key, ct).ConfigureAwait(false);
            Opportunity persisted;
            if (existing is null)
            {
                if (options.DryRun)
                {
                    stats.InsertedOpps++;
                    log.Line($"[INSERT] {key}: planned Opportunity insert for {company}.");
                    persisted = draft;
                }
                else
                {
                    persisted = await opportunityStore.InsertAsync(draft, options.Actor, ct).ConfigureAwait(false);
                    stats.InsertedOpps++;
                    log.Line($"[INSERT] {key}: Opportunity Id={persisted.Id}.");
                }
            }
            else
            {
                var updated = existing with
                {
                    Name = draft.Name,
                    BuyerName = draft.BuyerName,
                    BuyerType = draft.BuyerType,
                    DeltekClientId = draft.DeltekClientId,
                    DeltekContactId = draft.DeltekContactId,
                    BuyerContactName = draft.BuyerContactName,
                    BuyerContactEmail = draft.BuyerContactEmail,
                    BuyerContactPhone = draft.BuyerContactPhone,
                    ProjectCity = draft.ProjectCity,
                    Discipline = draft.Discipline,
                    EstimatedValue = draft.EstimatedValue,
                    EstimatedValueCurrency = draft.EstimatedValueCurrency,
                    Status = draft.Status,
                    IdentifiedAtUtc = draft.IdentifiedAtUtc,
                    ProposalSubmittedAtUtc = draft.ProposalSubmittedAtUtc,
                    OutcomeAtUtc = draft.OutcomeAtUtc,
                    WonLostOutcome = draft.WonLostOutcome,
                    AwardedValue = draft.AwardedValue,
                    OutcomeReason = draft.OutcomeReason,
                };

                if (options.DryRun)
                {
                    persisted = updated;
                    stats.UpdatedOpps++;
                    log.Line($"[UPDATE] {key}: planned Opportunity update Id={existing.Id}.");
                }
                else
                {
                    persisted = await opportunityStore.UpdateAsync(updated, options.Actor, ct).ConfigureAwait(false);
                    stats.UpdatedOpps++;
                    log.Line($"[UPDATE] {key}: Opportunity Id={persisted.Id}.");
                }
            }

            if (proposalsSubmitted > 0m || proposalsAccepted > 0m)
            {
                await UpsertEngagementAsync(crmStore, persisted, proposalsSubmitted, proposalsAccepted, rowInstant, options, log, stats, ct)
                    .ConfigureAwait(false);
            }
        }
    }

    private static async Task UpsertEngagementAsync(
        ICrmEngagementStore crmStore,
        Opportunity opportunity,
        decimal? proposalsSubmitted,
        decimal? proposalsAccepted,
        DateTimeOffset rowInstant,
        ImportOptions options,
        RunLog log,
        ImportStats stats,
        CancellationToken ct)
    {
        var stage = proposalsAccepted > 0m ? CrmEngagementStage.Won : CrmEngagementStage.ProposalSubmitted;
        var proposedFee = proposalsSubmitted > 0m ? proposalsSubmitted : proposalsAccepted;

        CrmEngagement? existing = null;
        if (opportunity.Id > 0)
        {
            existing = await crmStore.GetByOpportunityAsync(opportunity.Id, ct).ConfigureAwait(false);
        }

        if (existing is null)
        {
            stats.InsertedEngs++;
            log.Line($"[INSERT] {opportunity.OpportunityKey}: planned CrmEngagement insert stage={stage}.");
            if (!options.DryRun && opportunity.Id > 0)
            {
                _ = await crmStore.InsertAsync(new CrmEngagement
                {
                    OpportunityId = opportunity.Id,
                    Stage = stage,
                    ProposedFee = proposedFee,
                    OpenedAtUtc = rowInstant,
                    ClosedAtUtc = stage == CrmEngagementStage.Won ? rowInstant : null,
                }, options.Actor, ct).ConfigureAwait(false);
            }

            return;
        }

        stats.UpdatedEngs++;
        log.Line($"[UPDATE] {opportunity.OpportunityKey}: planned CrmEngagement update Id={existing.Id} stage={stage}.");
        if (!options.DryRun)
        {
            _ = await crmStore.UpdateAsync(existing with
            {
                Stage = stage,
                ProposedFee = proposedFee,
                OpenedAtUtc = rowInstant,
                ClosedAtUtc = stage == CrmEngagementStage.Won ? rowInstant : null,
            }, options.Actor, ct).ConfigureAwait(false);
        }
    }

    private static void Skip(RunLog log, ImportStats stats, string rowRef, string reason)
    {
        stats.RowsSkipped++;
        log.Line($"[SKIP] {rowRef}: {reason}");
    }

    private static decimal? ConvertCurrency(decimal? value, string regionCode)
        => value.HasValue && regionCode == "USA" ? decimal.Round(value.Value * UsdToCad, 2) : value;

    private static bool TryGetSequence(IXLCell cell, out int seq)
    {
        seq = 0;
        if (cell.TryGetValue<double>(out var d))
        {
            seq = (int)d;
            return Math.Abs(d - seq) < 0.0001d;
        }

        return int.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out seq);
    }

    private static bool TryGetExcelSerial(IXLCell cell, out double serial)
    {
        serial = 0d;
        if (cell.TryGetValue<double>(out serial)) return true;
        if (cell.TryGetValue<DateTime>(out var dt))
        {
            serial = dt.ToOADate();
            return true;
        }

        return double.TryParse(cell.GetString().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out serial);
    }

    private static decimal? ParseMoney(IXLCell cell)
    {
        if (cell.IsEmpty()) return null;
        if (cell.TryGetValue<decimal>(out var dec)) return dec == 0m ? null : dec;

        var raw = cell.GetString().Trim();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        raw = raw.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim();
        return decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowParentheses, CultureInfo.InvariantCulture, out var parsed)
            ? parsed == 0m ? null : parsed
            : null;
    }

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static string Truncate(string value, int max)
        => value.Length <= max ? value : value.Substring(0, max);

    private static void WriteSummary(ImportOptions options, ImportStats stats)
    {
        Console.WriteLine($"BdSeedImport (dry-run={options.DryRun.ToString().ToLowerInvariant()}) complete.");
        Console.WriteLine($"  Sheets processed: {stats.SheetsProcessed}");
        Console.WriteLine($"  Rows seen:        {stats.RowsSeen}");
        Console.WriteLine($"  Rows skipped:     {stats.RowsSkipped}");
        Console.WriteLine($"  Inserted opps:    {stats.InsertedOpps}");
        Console.WriteLine($"  Updated opps:     {stats.UpdatedOpps}");
        Console.WriteLine($"  Inserted engs:    {stats.InsertedEngs}");
        Console.WriteLine($"  Updated engs:     {stats.UpdatedEngs}");
        Console.WriteLine($"  Auto-matched:     {stats.AutoMatched}");
        Console.WriteLine($"  Needs review:     {stats.NeedsReview}  (see output/bd-import-review.csv)");
        Console.WriteLine($"  No match:         {stats.NoMatch}");
    }
}

internal sealed record ImportOptions(string XlsxPath, bool DryRun, string Actor, string OutputDir)
{
    public static ImportOptions Parse(string[] args)
    {
        string? xlsx = null;
        var dryRun = false;
        var actor = "BdSeedImport";
        var outputDir = Path.Combine("tools", "BdSeedImport", "output");

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--xlsx":
                    xlsx = RequireValue(args, ref i, "--xlsx");
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--actor":
                    actor = RequireValue(args, ref i, "--actor");
                    break;
                case "--output-dir":
                    outputDir = RequireValue(args, ref i, "--output-dir");
                    break;
                default:
                    throw new ArgumentException($"Unknown argument '{args[i]}'.");
            }
        }

        if (string.IsNullOrWhiteSpace(xlsx)) throw new ArgumentException("--xlsx <path> is required.");
        if (!File.Exists(xlsx)) throw new FileNotFoundException("XLSX file not found.", xlsx);
        return new ImportOptions(xlsx, dryRun, actor, outputDir);
    }

    private static string RequireValue(string[] args, ref int i, string name)
    {
        if (i + 1 >= args.Length) throw new ArgumentException($"{name} requires a value.");
        i++;
        return args[i];
    }
}

internal sealed record ImportConfig(string OpportunitiesDb, string Dsn, string User, string Password, string Catalog)
{
    public static ImportConfig FromEnvironment()
    {
        return new ImportConfig(
            Required("KOR_BD_OPPORTUNITIESDB"),
            Required("KOR_BD_DELTEK_DSN"),
            Required("KOR_BD_DELTEK_USER"),
            Required("KOR_BD_DELTEK_PWD"),
            Environment.GetEnvironmentVariable("KOR_BD_DELTEK_CATALOG")?.Trim() is { Length: > 0 } catalog
                ? catalog
                : ProgramDefaultCatalog);
    }

    public string OdbcConnectionString => $"DSN={Dsn};UID={User};PWD={Password};";

    private const string ProgramDefaultCatalog = "C0000052267P_1_KOR00000000";

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

internal sealed class DeltekLookupCache
{
    private readonly ImportConfig _config;

    public DeltekLookupCache(ImportConfig config)
    {
        _config = config;
    }

    public IReadOnlyList<DeltekClientCandidate> LoadClients()
    {
        using var cn = new OdbcConnection(_config.OdbcConnectionString);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT ClientID, Name, Type
FROM [{_config.Catalog}].dbo.Clendor
WHERE ClientInd = 'Y'
  AND (Status IS NULL OR Status <> 'I')";
        using var r = cmd.ExecuteReader();
        var rows = new List<DeltekClientCandidate>();
        while (r.Read())
        {
            var id = GetTrimmed(r, 0);
            var name = GetTrimmed(r, 1);
            if (id.Length == 0 || name.Length == 0) continue;
            rows.Add(new DeltekClientCandidate(id, name, r.IsDBNull(2) ? null : Convert.ToString(r.GetValue(2))?.Trim(), 0d));
        }

        return rows;
    }

    public string? FindContact(string clientId, string? fullName, string? email)
    {
        using var cn = new OdbcConnection(_config.OdbcConnectionString);
        cn.Open();
        using var cmd = cn.CreateCommand();
        cmd.CommandTimeout = 30;
        cmd.CommandText = $@"
SELECT ContactID, FirstName, LastName, EMail, Title
FROM [{_config.Catalog}].dbo.Contacts
WHERE ClientID = ?
  AND (ContactStatus IS NULL OR ContactStatus IN ('A', 'Active'))";
        cmd.Parameters.Add(new OdbcParameter("@client", OdbcType.NVarChar, 32) { Value = clientId });
        using var r = cmd.ExecuteReader();
        var contacts = new List<DeltekContactCandidate>();
        while (r.Read())
        {
            var contactId = GetTrimmed(r, 0);
            if (contactId.Length == 0) continue;
            var first = GetTrimmed(r, 1);
            var last = GetTrimmed(r, 2);
            contacts.Add(new DeltekContactCandidate(
                contactId,
                clientId,
                string.Join(' ', new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x))),
                r.IsDBNull(3) ? null : Convert.ToString(r.GetValue(3))?.Trim(),
                r.IsDBNull(4) ? null : Convert.ToString(r.GetValue(4))?.Trim(),
                0d));
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            var exact = contacts.FirstOrDefault(c => string.Equals(c.Email, email, StringComparison.OrdinalIgnoreCase));
            if (exact is not null) return exact.ContactId;
        }

        var normalized = DeltekFuzzyMatch.NormalizePersonName(fullName ?? string.Empty);
        if (normalized.Length == 0) return null;

        return contacts
            .Select(c => c with { SimilarityScore = DeltekFuzzyMatch.Similarity(normalized, DeltekFuzzyMatch.NormalizePersonName(c.FullName)) })
            .FirstOrDefault(c => c.SimilarityScore >= 1.0d)
            ?.ContactId;
    }

    private static string GetTrimmed(IDataRecord r, int i)
        => r.IsDBNull(i) ? string.Empty : Convert.ToString(r.GetValue(i))?.Trim() ?? string.Empty;
}

internal sealed record DeltekClientCandidate(string ClientId, string Name, string? Type, double SimilarityScore);

internal sealed record DeltekContactCandidate(string ContactId, string ClientId, string FullName, string? Email, string? Title, double SimilarityScore);

internal sealed record CompanyMatch(IReadOnlyList<DeltekClientCandidate> Top)
{
    public double TopScore => Top.Count == 0 ? 0d : Top[0].SimilarityScore;
}

internal static class DeltekFuzzyMatch
{
    internal static CompanyMatch FindCompany(string company, IReadOnlyList<DeltekClientCandidate> clients)
    {
        var normalized = NormalizeCompany(company);
        if (normalized.Length == 0) return new CompanyMatch(Array.Empty<DeltekClientCandidate>());

        var top = clients
            .Select(c => c with { SimilarityScore = Similarity(normalized, NormalizeCompany(c.Name)) })
            .OrderByDescending(c => c.SimilarityScore)
            .ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        return new CompanyMatch(top);
    }

    internal static string NormalizeCompany(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lower = s.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            if (char.IsLetterOrDigit(ch) || ch == ' ') sb.Append(ch);
            else if (ch == ',' || ch == '.' || ch == '-' || ch == '/' || ch == '&') sb.Append(' ');
        }

        var tokens = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var kept = new List<string>(tokens.Length);
        foreach (var t in tokens)
        {
            if (CompanySuffixTokens.Contains(t)) continue;
            kept.Add(t);
        }

        return string.Join(' ', kept);
    }

    internal static double Similarity(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0.0;
        if (a == b) return 1.0;
        var distance = Levenshtein(a, b);
        var maxLen = Math.Max(a.Length, b.Length);
        return 1.0 - ((double)distance / maxLen);
    }

    internal static string NormalizePersonName(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var lower = s.ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        foreach (var ch in lower)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch == ' ' ? ch : ' ');
        }

        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static readonly HashSet<string> CompanySuffixTokens = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "ltd", "limited", "llc", "llp", "lp",
        "corp", "corporation", "co", "company",
        "group", "holdings", "international", "intl",
        "properties", "property",
        "construction", "constructors",
        "development", "developments", "developers", "dev",
        "consulting", "consultants",
        "architects", "architecture",
        "engineering", "engineers"
    };

    internal static int Levenshtein(string a, string b)
    {
        if (a.Length < b.Length) (a, b) = (b, a);
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }

            (prev, cur) = (cur, prev);
        }

        return prev[b.Length];
    }
}

internal sealed class RunLog
{
    private readonly string _logPath;
    private readonly string _reviewPath;
    private readonly List<string> _lines = new();
    private readonly List<string[]> _reviews = new();

    public RunLog(string outputDir)
    {
        _logPath = Path.Combine(outputDir, "bd-import-log.txt");
        _reviewPath = Path.Combine(outputDir, "bd-import-review.csv");
    }

    public void Line(string line)
    {
        _lines.Add(line);
        Console.WriteLine(line);
    }

    public void Review(string key, string company, IReadOnlyList<DeltekClientCandidate> candidates)
    {
        var fields = new List<string> { key, company };
        for (var i = 0; i < 3; i++)
        {
            if (i < candidates.Count)
            {
                fields.Add(candidates[i].ClientId);
                fields.Add(candidates[i].Name);
                fields.Add(candidates[i].SimilarityScore.ToString("0.###", CultureInfo.InvariantCulture));
            }
            else
            {
                fields.Add("");
                fields.Add("");
                fields.Add("");
            }
        }

        _reviews.Add(fields.ToArray());
    }

    public void WriteReviewCsv()
    {
        var rows = new List<string>
        {
            "OpportunityKey,Company,Candidate1Id,Candidate1Name,Candidate1Score,Candidate2Id,Candidate2Name,Candidate2Score,Candidate3Id,Candidate3Name,Candidate3Score"
        };
        rows.AddRange(_reviews.Select(r => string.Join(",", r.Select(Csv))));
        File.WriteAllLines(_reviewPath, rows, Encoding.UTF8);
    }

    public void Flush()
    {
        File.WriteAllLines(_logPath, _lines, Encoding.UTF8);
    }

    private static string Csv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r')) return value;
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}

internal sealed class ImportStats
{
    public int SheetsProcessed { get; set; }
    public int RowsSeen { get; set; }
    public int RowsSkipped { get; set; }
    public int InsertedOpps { get; set; }
    public int UpdatedOpps { get; set; }
    public int InsertedEngs { get; set; }
    public int UpdatedEngs { get; set; }
    public int AutoMatched { get; set; }
    public int NeedsReview { get; set; }
    public int NoMatch { get; set; }
}
