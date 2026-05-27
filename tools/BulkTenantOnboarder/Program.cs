#nullable enable
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace Kor.BulkTenantOnboarder;

/// <summary>
/// Bulk-onboards Bonfire (RSS) and bids&amp;tenders (Playwright) tenants as
/// OpportunitySources, so a whole platform tenant-directory can be absorbed in
/// one pass instead of one migration per tenant. Idempotent on source Name.
///
/// Input: a JSON array of tenants (see sample-tenants.json). Reuses the existing
/// Rss provider (SourceType 3) and bids&amp;tenders scraper (SourceType 10); new
/// enabled sources auto-run via OpportunitySourceCronScheduler — no redeploy.
///
///   dotnet run -- --file tenants.json [--db &lt;conn&gt;] [--dry-run]
/// </summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            var file = GetArg(args, "--file");
            var db = GetArg(args, "--db") ?? Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB");
            var dryRun = args.Contains("--dry-run");

            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file))
            {
                Console.Error.WriteLine("Missing/invalid --file <tenants.json>.");
                return 2;
            }

            if (!dryRun && string.IsNullOrWhiteSpace(db))
            {
                Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB or pass --db.");
                return 2;
            }

            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var tenants = JsonSerializer.Deserialize<List<Tenant>>(await File.ReadAllTextAsync(file).ConfigureAwait(false), opts)
                          ?? new List<Tenant>();

            Console.WriteLine($"Loaded {tenants.Count} tenant(s) from {file}; dry-run={dryRun.ToString().ToLowerInvariant()}");

            SqlConnection? con = null;
            if (!dryRun)
            {
                con = new SqlConnection(db);
                await con.OpenAsync().ConfigureAwait(false);
            }

            int added = 0, skipped = 0, invalid = 0;
            var byPlatform = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var t in tenants)
            {
                var spec = Build(t);
                if (spec is null)
                {
                    invalid++;
                    Console.Error.WriteLine($"[SKIP] invalid tenant: platform='{t.Platform}' subdomain='{t.Subdomain}' buyer='{t.Buyer}'");
                    continue;
                }

                if (dryRun)
                {
                    Console.WriteLine($"[DRY-RUN] {spec.Name}  type={spec.SourceType}  {spec.BaseUrl}");
                    continue;
                }

                var result = await UpsertAsync(con!, spec).ConfigureAwait(false);
                if (result)
                {
                    added++;
                    byPlatform.TryGetValue(t.Platform!, out var c);
                    byPlatform[t.Platform!] = c + 1;
                    Console.WriteLine($"[ADD]  {spec.Name}  ({spec.BaseUrl})");
                }
                else
                {
                    skipped++;
                    Console.WriteLine($"[SKIP] {spec.Name} already exists");
                }
            }

            con?.Dispose();
            Console.WriteLine();
            Console.WriteLine($"Onboard complete: added={added}, skipped(existing)={skipped}, invalid={invalid}.");
            foreach (var (p, c) in byPlatform.OrderBy(x => x.Key))
            {
                Console.WriteLine($"  {p}: {c} added");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BulkTenantOnboarder failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static SourceSpec? Build(Tenant t)
    {
        var platform = t.Platform?.Trim().ToLowerInvariant();
        var subdomain = t.Subdomain?.Trim();
        var buyer = t.Buyer?.Trim();
        if (string.IsNullOrWhiteSpace(platform) || string.IsNullOrWhiteSpace(subdomain) || string.IsNullOrWhiteSpace(buyer))
        {
            return null;
        }

        var key = !string.IsNullOrWhiteSpace(t.Key) ? Sanitize(t.Key!) : Sanitize(subdomain);
        if (key.Length == 0)
        {
            return null;
        }

        return platform switch
        {
            "bonfire" => new SourceSpec(
                Name: "Bonfire_" + key,
                SourceType: 3,
                BaseUrl: $"https://{subdomain}.bonfirehub.{(string.IsNullOrWhiteSpace(t.Domain) ? "ca" : t.Domain!.Trim())}/opportunities/rss",
                CrawlDelaySeconds: 7200,
                RequestTimeoutSeconds: 30,
                Mappings: new[]
                {
                    ("rss.buyerFromTitle", "false"),
                    ("rss.buyerOverride", buyer),
                    ("rss.titleFormat", "BonfireReferenceName"),
                }),
            "bidsandtenders" => new SourceSpec(
                Name: "BidsTenders_" + key,
                SourceType: 10,
                BaseUrl: $"https://{subdomain}.bidsandtenders.ca/Module/Tenders/en",
                CrawlDelaySeconds: 21600,
                RequestTimeoutSeconds: 60,
                Mappings: new[]
                {
                    ("bidsandtenders.buyer", buyer),
                }),
            _ => null,
        };
    }

    private static async Task<bool> UpsertAsync(SqlConnection con, SourceSpec spec)
    {
        // Idempotent on Name. Returns true if a new source was inserted.
        await using (var existsCmd = new SqlCommand("SELECT Id FROM opportunities.OpportunitySources WHERE Name = @n;", con))
        {
            existsCmd.Parameters.Add("@n", SqlDbType.NVarChar, 200).Value = spec.Name;
            var existing = await existsCmd.ExecuteScalarAsync().ConfigureAwait(false);
            if (existing is Guid existingId)
            {
                // Source exists; ensure mappings present (idempotent), but report skip.
                await InsertMappingsAsync(con, existingId, spec.Mappings).ConfigureAwait(false);
                return false;
            }
        }

        var id = Guid.NewGuid();
        const string insertSql = @"
INSERT INTO opportunities.OpportunitySources
    (Id, Name, SourceType, BaseUrl, IsEnabled, CrawlDelaySeconds, RequestTimeoutSeconds, CreatedAtUtc, UpdatedAtUtc, IsHistorical)
VALUES
    (@id, @name, @type, @url, 1, @crawl, @timeout, sysdatetimeoffset(), sysdatetimeoffset(), 0);";
        await using (var cmd = new SqlCommand(insertSql, con))
        {
            cmd.Parameters.Add("@id", SqlDbType.UniqueIdentifier).Value = id;
            cmd.Parameters.Add("@name", SqlDbType.NVarChar, 200).Value = spec.Name;
            cmd.Parameters.Add("@type", SqlDbType.Int).Value = spec.SourceType;
            cmd.Parameters.Add("@url", SqlDbType.NVarChar, 1000).Value = spec.BaseUrl;
            cmd.Parameters.Add("@crawl", SqlDbType.Int).Value = spec.CrawlDelaySeconds;
            cmd.Parameters.Add("@timeout", SqlDbType.Int).Value = spec.RequestTimeoutSeconds;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }

        await InsertMappingsAsync(con, id, spec.Mappings).ConfigureAwait(false);
        return true;
    }

    private static async Task InsertMappingsAsync(SqlConnection con, Guid sourceId, (string Key, string Value)[] mappings)
    {
        foreach (var (key, value) in mappings)
        {
            const string sql = @"
IF NOT EXISTS (SELECT 1 FROM opportunities.OpportunitySourceMappings WHERE OpportunitySourceId = @sid AND [Key] = @k)
    INSERT INTO opportunities.OpportunitySourceMappings (OpportunitySourceId, [Key], ValueJson, UpdatedAtUtc)
    VALUES (@sid, @k, @v, sysdatetimeoffset());";
            await using var cmd = new SqlCommand(sql, con);
            cmd.Parameters.Add("@sid", SqlDbType.UniqueIdentifier).Value = sourceId;
            cmd.Parameters.Add("@k", SqlDbType.NVarChar, 100).Value = key;
            cmd.Parameters.Add("@v", SqlDbType.NVarChar, -1).Value = value;
            await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
        }
    }

    private static string Sanitize(string raw)
    {
        var sb = new StringBuilder(raw.Length);
        var upperNext = true;
        foreach (var ch in raw)
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(upperNext ? char.ToUpperInvariant(ch) : ch);
                upperNext = false;
            }
            else
            {
                upperNext = true; // word boundary -> PascalCase
            }
        }

        return sb.ToString();
    }

    private static string? GetArg(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private sealed record SourceSpec(
        string Name,
        int SourceType,
        string BaseUrl,
        int CrawlDelaySeconds,
        int RequestTimeoutSeconds,
        (string Key, string Value)[] Mappings);

    private sealed class Tenant
    {
        public string? Platform { get; set; }      // "bonfire" | "bidsandtenders"
        public string? Key { get; set; }            // short Name suffix (optional; derived from subdomain if absent)
        public string? Subdomain { get; set; }      // platform subdomain
        public string? Buyer { get; set; }          // display / buyer name
        public string? Domain { get; set; }         // bonfire only: "ca" (default) | "com"
        public string? Province { get; set; }       // optional, informational
        public string? Region { get; set; }         // optional, informational
    }
}
