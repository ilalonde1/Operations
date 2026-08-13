#nullable enable
using System.Data.Odbc;
using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Kor.Operations.FileSync.Service.ControlPlane;
using Kor.Operations.FileSync.Service.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Kor.Operations.FileSync.Service.Jobs.KorMapSync;

// Keeps korstructural.com's project map in step with Deltek.
//
// Direction is one-way and deliberate: Deltek is read-only upstream, this job
// does the work on KOR-APP01, and WordPress receives finished rows. The website
// holds no Deltek credentials, no DSN and no route back into Deltek, so owning
// the web host gets you nothing here. It also never geocodes: coordinates are
// resolved here and pushed explicitly.
//
// Scope of what gets mapped (matches the set agreed 2026-08-07): top-level
// projects, ChargeType 'R', a street address AND a city, and at least one
// tkDetail hour -- i.e. work KOR was actually paid to do. Anything without a
// real address is left off rather than pinned to a city centroid.
internal sealed class KorMapSyncRunner : IJobRunner
{
    public const string Name = "KorMapSync";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    // Deltek Principal / ProjMgr -> team_member bio slug.
    //
    // The key must be exactly what the Deltek query builds for a person:
    // FirstName + ' ' + LastName off the EM table. Get it wrong and the name
    // silently fails the ContainsKey below -- no error, the person just never
    // appears on their own map. That is how Conor and Omar went missing until
    // 2026-08-08, when their profiles were linked from the Team page and the
    // empty maps gave it away.
    //
    // Bryson and Zickmantel are deliberately absent: excluded as PEOPLE by Ian
    // 2026-08-07. Their projects still map, they just carry no attribution.
    private static readonly Dictionary<string, string> BioSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["James DesRoches"] = "jim-desroches",
        ["John Markulin"] = "john-a-markulin",
        ["Rory Beirne"] = "rory-beirne",
        ["Jason Stuart"] = "jason-stuart",
        ["Kevin Wurmlinger"] = "kevin-wurmlinger",
        ["Simon Szarkiewicz"] = "simon-szarkiewicz",
        ["Conor Murtagh"] = "conor-murtagh",                     // EM E0005
        ["Omar Alcazar Pastrana"] = "omar-alcazar-pastrana",     // EM E0014
    };

    private static readonly HashSet<string> LowerMainland = new(StringComparer.OrdinalIgnoreCase)
    {
        "Vancouver","Burnaby","Coquitlam","North Vancouver","Port Coquitlam","West Vancouver","Langley",
        "Chilliwack","Richmond","New Westminster","Surrey","Maple Ridge","Delta","Abbotsford","Port Moody",
        "Whistler","Squamish","Pitt Meadows","White Rock","Mission",
    };

    private static readonly HashSet<string> Okanagan = new(StringComparer.OrdinalIgnoreCase)
    {
        "Kelowna","West Kelowna","Penticton","Vernon","Kamloops","Nelson","Princeton","Revelstoke",
        "Summerland","Osoyoos","Salmon Arm","Merritt","Cranbrook","Trail","Castlegar",
    };

    private static readonly HashSet<string> Island = new(StringComparer.OrdinalIgnoreCase)
    {
        "Victoria","Nanaimo","Parksville","Courtenay","Duncan","Langford","Sidney","Sooke","Esquimalt",
        "Saanich","Campbell River","Port Alberni","Comox","Quadra Island",
    };

    private static readonly HashSet<string> NorthernBc = new(StringComparer.OrdinalIgnoreCase)
    {
        "Prince George","Terrace","Kitimat","Smithers","Quesnel","Dawson Creek","Fort St John",
        "Fort St James","Hazelton","New Hazelton","South Hazelton","Prince Rupert","Williams Lake",
    };

    private static readonly HashSet<string> UsStates = new(StringComparer.OrdinalIgnoreCase)
    { "CA","WA","OR","TX","NV","AZ","NM" };

    private static readonly HashSet<string> EastProvinces = new(StringComparer.OrdinalIgnoreCase)
    { "ON","MB","NS","SK","NB","QC","PE","NL" };

    private readonly IControlPlaneStore _store;
    private readonly FileSyncOptions _cfg;
    private readonly ILogger<KorMapSyncRunner> _logger;

    public KorMapSyncRunner(
        IControlPlaneStore store,
        IOptions<FileSyncOptions> cfg,
        ILogger<KorMapSyncRunner> logger)
    {
        _store = store;
        _cfg = cfg.Value;
        _logger = logger;
    }

    public string JobName => Name;

    public async Task<JobRunResult> RunAsync(JobConfig config, string triggerSource, string? args, CancellationToken ct)
    {
        var knobs = await _store.GetKnobsAsync(Name, ct).ConfigureAwait(false);
        var opts = KorMapSyncOptions.FromKnobs(knobs);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(_cfg.DeltekUser)) missing.Add("KOR_FILESYNC_DELTEKUSER");
        if (string.IsNullOrWhiteSpace(_cfg.DeltekPassword)) missing.Add("KOR_FILESYNC_DELTEKPASSWORD");
        if (string.IsNullOrWhiteSpace(_cfg.MapboxToken)) missing.Add("KOR_FILESYNC_MAPBOXTOKEN");
        if (string.IsNullOrWhiteSpace(_cfg.KorSyncSecret)) missing.Add("KOR_FILESYNC_KORSYNCSECRET");
        if (missing.Count > 0)
        {
            var msg = "Missing environment variables on this host: " + string.Join(", ", missing);
            _logger.LogError("{Msg}", msg);
            return new JobRunResult(false, msg);
        }

        var isShadow = string.Equals(config.Mode, "Shadow", StringComparison.OrdinalIgnoreCase);
        var summary = new StringBuilder();
        summary.Append(CultureInfo.InvariantCulture, $"Mode={config.Mode}");

        List<DeltekProject> projects;
        try
        {
            projects = await ReadDeltekAsync(opts, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Deltek read failed");
            return new JobRunResult(false, "Deltek read failed: " + ex.Message);
        }

        summary.Append(CultureInfo.InvariantCulture, $"; DeltekMappable={projects.Count}");

        // What is already on the map? Keyed on WBS1 so re-runs are idempotent.
        Dictionary<string, WpLocation> existing;
        try
        {
            existing = await FetchExistingAsync(opts, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WordPress read failed");
            return new JobRunResult(false, "WordPress read failed: " + ex.Message);
        }

        summary.Append(CultureInfo.InvariantCulture, $"; OnMap={existing.Count}");

        var creates = new List<object>();
        var updates = new List<object>();
        var regionRewrites = new List<string>();
        var geocodeFailures = new List<string>();
        var geocoded = 0;

        foreach (var p in projects)
        {
            var people = string.Join(",", new[] { p.Principal, p.ProjectManager }
                .Where(n => !string.IsNullOrWhiteSpace(n) && BioSlugs.ContainsKey(n!))
                .Select(n => BioSlugs[n!])
                .Distinct()
                .OrderBy(s => s, StringComparer.Ordinal));
            var region = ResolveRegion(p.City, p.State);

            if (existing.TryGetValue(p.Wbs1, out var already))
            {
                // Already mapped: only refresh attribution. Never re-geocode --
                // the pin may have been corrected by hand.
                //
                // NEVER BLANK AN EXISTING VALUE. This job derives region/people
                // from Deltek's City/State and Principal; anything it can't
                // classify comes back empty. A lot of pins were classified by
                // reverse-geocoding or by hand, which Deltek knows nothing
                // about, so an empty result means "I don't know", never
                // "clear it". Only send fields that actually have a value and
                // actually differ.
                var patch = new Dictionary<string, object> { ["id"] = already.Id };
                if (!string.IsNullOrEmpty(people) && people != already.People) patch["people"] = people;
                // Billed grows as a job invoices, so unlike region/people this
                // genuinely changes run to run and a stale value silently keeps
                // a project off the map after it has outgrown the floor. Sent
                // only when it differs, so a settled job costs nothing.
                if (p.BilledMeta != (already.Billed ?? string.Empty)) patch["billed"] = p.BilledMeta;
                if (!string.IsNullOrEmpty(region) && region != already.Region)
                {
                    patch["region"] = region;
                    // Re-regioning a pin that already had a different, non-empty
                    // region is the signature of a resolver regression. Count it.
                    if (!string.IsNullOrEmpty(already.Region))
                    {
                        regionRewrites.Add($"{already.Id} {p.Wbs1} {p.Name}: {already.Region} -> {region}");
                    }
                }
                if (patch.Count > 1)
                {
                    // Only claim a pin for KOR if nothing has claimed it yet.
                    // This used to fire whenever any field changed, which was
                    // harmless while patches were rare -- but `billed` differs
                    // on the first run for EVERY matched pin, and that would
                    // have rewritten Jim's pre-KOR towers to era=KOR in one
                    // pass. PRIOR is a deliberate, hand-set claim about whose
                    // work a building is; this job does not get to overrule it.
                    if (string.IsNullOrWhiteSpace(already.Era)) patch["era"] = "KOR";
                    updates.Add(patch);
                }

                continue;
            }

            if (geocoded >= opts.GeocodeBatchLimit)
            {
                continue; // picked up next run; keeps quota predictable
            }

            var hit = await GeocodeAsync(opts, p, ct).ConfigureAwait(false);
            geocoded++;
            if (hit is null)
            {
                geocodeFailures.Add($"{p.Wbs1} {p.Name}");
                continue;
            }

            creates.Add(new
            {
                name = p.Name.Length > 120 ? p.Name[..120] : p.Name,
                address = p.FullAddress,
                region,
                era = "KOR",
                people,
                wbs1 = p.Wbs1,
                billed = p.BilledMeta,
                lat = hit.Value.Lat,
                lng = hit.Value.Lng,
            });
        }

        summary.Append(CultureInfo.InvariantCulture,
            $"; Create={creates.Count}; Update={updates.Count}; RegionRewrites={regionRewrites.Count}; GeocodeTried={geocoded}; GeocodeRejected={geocodeFailures.Count}");

        // Per-person tally in the run log. A name whose BioSlugs key does not
        // match Deltek's "FirstName LastName" fails silently -- the person just
        // never appears on their own map, which is how Conor and Omar stayed
        // missing. A slug that reads 0 here, or vanishes from the line
        // entirely, says so on the very next run.
        var tally = projects
            .SelectMany(p => new[] { p.Principal, p.ProjectManager })
            .Where(n => !string.IsNullOrWhiteSpace(n) && BioSlugs.ContainsKey(n!))
            .Select(n => BioSlugs[n!])
            .GroupBy(s => s)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key}:{g.Count()}");
        var unmatchedSlugs = BioSlugs.Values.Distinct()
            .Except(projects.SelectMany(p => new[] { p.Principal, p.ProjectManager })
                            .Where(n => !string.IsNullOrWhiteSpace(n) && BioSlugs.ContainsKey(n!))
                            .Select(n => BioSlugs[n!]))
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        summary.Append(CultureInfo.InvariantCulture, $"; People={string.Join(",", tally)}");
        if (unmatchedSlugs.Count > 0)
        {
            summary.Append(CultureInfo.InvariantCulture, $"; PeopleWithNoProjects={string.Join(",", unmatchedSlugs)}");
            _logger.LogWarning("KorMapSync: bio slugs matched nothing in Deltek this run: {Slugs}. " +
                               "Usually means the BioSlugs key no longer matches EM FirstName+LastName.",
                               string.Join(", ", unmatchedSlugs));
        }

        // PRE-FLIGHT. Everything above is computed; nothing has been written.
        // If this run wants to move more already-correct pins than the
        // threshold allows, refuse to push and hand back the list. Shadow runs
        // skip the gate -- their whole job is to show you the damage.
        if (!isShadow && regionRewrites.Count > opts.RegionChangeAbortThreshold)
        {
            var dir = Directory.CreateDirectory(Path.Combine(
                opts.ShadowOutputDir,
                "ABORTED-" + DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))).FullName;
            await File.WriteAllTextAsync(
                Path.Combine(dir, "region-rewrites.txt"),
                string.Join(Environment.NewLine, regionRewrites),
                ct).ConfigureAwait(false);
            var msg = summary
                + $"; PRE-FLIGHT ABORT: would re-region {regionRewrites.Count} pins that already have one"
                + $" (limit {opts.RegionChangeAbortThreshold}). Nothing pushed. See {dir}";
            _logger.LogError("{Msg}", msg);
            return new JobRunResult(false, msg);
        }

        if (isShadow)
        {
            var dir = Directory.CreateDirectory(Path.Combine(
                opts.ShadowOutputDir,
                DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture))).FullName;
            await File.WriteAllTextAsync(
                Path.Combine(dir, "planned-creates.json"),
                JsonSerializer.Serialize(creates, new JsonSerializerOptions { WriteIndented = true }),
                ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "planned-updates.json"),
                JsonSerializer.Serialize(updates, new JsonSerializerOptions { WriteIndented = true }),
                ct).ConfigureAwait(false);
            await File.WriteAllTextAsync(
                Path.Combine(dir, "geocode-rejected.txt"),
                string.Join(Environment.NewLine, geocodeFailures),
                ct).ConfigureAwait(false);
            summary.Append(CultureInfo.InvariantCulture, $"; Shadow -> {dir} (nothing pushed)");
            return new JobRunResult(true, summary.ToString());
        }

        var pushed = 0;
        try
        {
            foreach (var chunk in Chunk(creates, opts.PushChunkSize))
            {
                pushed += await PushAsync(opts, new { creates = chunk }, ct).ConfigureAwait(false);
            }

            foreach (var chunk in Chunk(updates, opts.PushChunkSize))
            {
                await PushAsync(opts, new { updates = chunk }, ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Push to WordPress failed");
            return new JobRunResult(false, summary + "; PUSH FAILED: " + ex.Message);
        }

        summary.Append(CultureInfo.InvariantCulture, $"; Pushed={pushed}");
        if (geocodeFailures.Count > 0)
        {
            _logger.LogWarning(
                "{Count} projects rejected by geocode validation (left off the map on purpose): {Sample}",
                geocodeFailures.Count,
                string.Join(" | ", geocodeFailures.Take(5)));
        }

        return new JobRunResult(true, summary.ToString());
    }

    private static IEnumerable<List<T>> Chunk<T>(List<T> src, int size)
    {
        for (var i = 0; i < src.Count; i += size)
        {
            yield return src.GetRange(i, Math.Min(size, src.Count - i));
        }
    }

    // City wins over State. Deltek's State field carries data-entry errors -- a
    // New Westminster project tagged NB routed to prairies-east until this was
    // reordered. A recognised city is unambiguous; State is only the fallback.
    private static string ResolveRegion(string city, string state)
    {
        if (LowerMainland.Contains(city)) return "vancouver";
        if (Okanagan.Contains(city)) return "kelowna";
        if (Island.Contains(city)) return "nanaimo";
        if (NorthernBc.Contains(city)) return "northern-bc";
        if (UsStates.Contains(state)) return "united-states";
        if (string.Equals(state, "AB", StringComparison.OrdinalIgnoreCase)) return "edmonton";
        if (EastProvinces.Contains(state)) return "prairies-east";
        return string.Empty;
    }

    private async Task<List<DeltekProject>> ReadDeltekAsync(KorMapSyncOptions opts, CancellationToken ct)
    {
        // Billed = billable labour on this job. The map uses it to keep trivial
        // work off the portfolio view: before this existed the only test was
        // "somebody charged time to it", so a $100 vault-lid repair and an
        // $11,780 peer review sat on the map as equals with a 42-storey tower.
        //
        // BillExt, not PR.Fee -- Fee is blank on 30% of jobs, so a fee test
        // would drop real projects for a data-entry reason.
        var sql = $@"
SELECT p.WBS1, p.Name, p.Address1, p.City, p.State,
       ISNULL(ep.FirstName,'') + ' ' + ISNULL(ep.LastName,'') AS PrincipalName,
       ISNULL(em.FirstName,'') + ' ' + ISNULL(em.LastName,'') AS PmName,
       ISNULL(bt.Billed,0) AS Billed
FROM {opts.DeltekCatalog}.dbo.PR p
LEFT JOIN {opts.DeltekCatalog}.dbo.EM ep ON ep.Employee = p.Principal
LEFT JOIN {opts.DeltekCatalog}.dbo.EM em ON em.Employee = p.ProjMgr
LEFT JOIN (
    SELECT WBS1, SUM(ISNULL(BillExt,0)) AS Billed
    FROM {opts.DeltekCatalog}.dbo.tkDetail
    GROUP BY WBS1
) bt ON bt.WBS1 = p.WBS1
WHERE (p.WBS2 IS NULL OR p.WBS2 = ' ') AND (p.WBS3 IS NULL OR p.WBS3 = ' ')
  AND p.WBS1 <> '~WDEF~' AND p.ChargeType = 'R'
  AND p.Address1 IS NOT NULL AND p.Address1 <> ' '
  AND p.City IS NOT NULL AND p.City <> ' '
  AND EXISTS (SELECT 1 FROM {opts.DeltekCatalog}.dbo.tkDetail t WHERE t.WBS1 = p.WBS1)";

        var rows = new List<DeltekProject>();
        await using var con = new OdbcConnection(opts.BuildOdbcConnectionString(_cfg.DeltekUser, _cfg.DeltekPassword));
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.CommandTimeout = 300;
        await using var rdr = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await rdr.ReadAsync(ct).ConfigureAwait(false))
        {
            rows.Add(new DeltekProject(
                rdr.GetString(0).Trim(),
                rdr.GetString(1).Trim(),
                rdr.GetString(2).Trim(),
                rdr.GetString(3).Trim(),
                rdr.IsDBNull(4) ? string.Empty : rdr.GetString(4).Trim(),
                rdr.IsDBNull(5) ? null : rdr.GetString(5).Trim(),
                rdr.IsDBNull(6) ? null : rdr.GetString(6).Trim(),
                rdr.IsDBNull(7) ? 0m : rdr.GetDecimal(7)));
        }

        return rows;
    }

    private HttpRequestMessage Authed(KorMapSyncOptions opts, HttpMethod method, string path)
    {
        var req = new HttpRequestMessage(method, opts.WordPressBaseUrl + path);
        // Shared secret, not a WordPress login: Application Passwords are
        // disabled on this site and a cookie session is not appropriate for a
        // service. The WP side compares this against KOR_SYNC_SECRET.
        req.Headers.Add("X-KOR-Sync-Secret", _cfg.KorSyncSecret);
        return req;
    }

    private async Task<Dictionary<string, WpLocation>> FetchExistingAsync(KorMapSyncOptions opts, CancellationToken ct)
    {
        using var req = Authed(opts, HttpMethod.Get, "/wp-json/kor/v1/locations?fields=sync");
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        var rows = await resp.Content.ReadFromJsonAsync<List<WpLocation>>(cancellationToken: ct).ConfigureAwait(false)
                   ?? new List<WpLocation>();
        var map = new Dictionary<string, WpLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            if (!string.IsNullOrWhiteSpace(r.Wbs1)) map[r.Wbs1!] = r;
        }

        return map;
    }

    private async Task<int> PushAsync(KorMapSyncOptions opts, object payload, CancellationToken ct)
    {
        using var req = Authed(opts, HttpMethod.Post, "/wp-json/kor/v1/locations-import");
        req.Content = JsonContent.Create(payload);
        using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        return doc.RootElement.TryGetProperty("created", out var c) ? c.GetInt32() : 0;
    }

    // Geocode and verify: the returned place must actually mention the city
    // Deltek recorded. Without this check addresses drift to other cities and
    // (verified 2026-08-07) other countries -- Surrey landing in New York,
    // Calgary in New Jersey, Surrey in England.
    private async Task<(double Lat, double Lng)?> GeocodeAsync(
        KorMapSyncOptions opts, DeltekProject p, CancellationToken ct)
    {
        var url = "https://api.mapbox.com/geocoding/v5/mapbox.places/"
                  + Uri.EscapeDataString(p.FullAddress)
                  + ".json?limit=1&access_token=" + _cfg.MapboxToken;
        try
        {
            using var resp = await Http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("features", out var feats) || feats.GetArrayLength() == 0) return null;
            var f = feats[0];
            var placeName = f.GetProperty("place_name").GetString() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(p.City)
                && placeName.IndexOf(p.City, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return null;
            }

            var coords = f.GetProperty("geometry").GetProperty("coordinates");
            return (coords[1].GetDouble(), coords[0].GetDouble());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Geocode failed for {Wbs1}", p.Wbs1);
            return null;
        }
    }

    private sealed record DeltekProject(
        string Wbs1, string Name, string Address1, string City, string State,
        string? Principal, string? ProjectManager, decimal Billed)
    {
        public string FullAddress => string.Join(", ",
            new[] { Address1, City, State }.Where(s => !string.IsNullOrWhiteSpace(s)));

        /// Whole dollars. The map only ever compares this against a threshold,
        /// so cents would be noise that rewrites the meta on every run.
        public string BilledMeta =>
            decimal.Round(Billed, 0, MidpointRounding.AwayFromZero)
                   .ToString("F0", CultureInfo.InvariantCulture);
    }

    private sealed record WpLocation
    {
        public long Id { get; init; }
        public string? Wbs1 { get; init; }
        public string? People { get; init; }
        public string? Region { get; init; }
        public string? Billed { get; init; }
        public string? Era { get; init; }
    }
}
