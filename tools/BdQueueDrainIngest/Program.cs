#nullable enable
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.People;
using Kor.Opportunities.Data.Projects;
using Kor.Opportunities.Data.ResearchEnvelope;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

static int Fail(string m) { Console.Error.WriteLine(m); return 1; }

static string? ReadArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

var kind = ReadArg(args, "--kind");
if (kind is not ("people" or "orgs" or "honing-orgs" or "ab-projects" or "proponents" or "org-name-repair" or "org-classify"))
{
    return Fail("Usage: BdQueueDrainIngest --kind people|orgs|ab-projects|proponents|org-name-repair|org-classify [--dir <path>]");
}

// 2026-06-12: QueueDrain migrated to KOR-APP01; the ingest runs on the dev
// box and reaches the queues via the share.
// org-classify drain lives in classify-unknown-orgs folder, not "org-classify"
var drainFolder = kind switch {
    "org-classify" => "classify-unknown-orgs",
    "honing-orgs"  => "honing-orgs",
    _              => kind
};
var inputDir = ReadArg(args, "--dir")
    ?? Path.Combine(@"\\KOR-APP01\QueueDrain", drainFolder, "outputs");
if (!Directory.Exists(inputDir))
{
    return Fail($"Input dir not found: {inputDir}");
}

var processedDir = Path.Combine(inputDir, "processed");
Directory.CreateDirectory(processedDir);

var cs = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB")
    ?? throw new InvalidOperationException("KOR_OPPORTUNITIES_OPPORTUNITIESDB env var missing");

var services = new ServiceCollection();
services.AddLogging(b => b.AddSimpleConsole(o => o.SingleLine = true).SetMinimumLevel(LogLevel.Information));

// Intel extractors — single source of truth in IntelExtractorBootstrap.
// Adding a new extractor goes there, not here.
foreach (var ex in IntelExtractorBootstrap.GetDefaultExtractors())
{
    var captured = ex;
    services.AddSingleton<IIntelExtractor>(_ => captured);
}
services.AddSingleton<DefaultIntelExtractor>();
services.AddSingleton<IntelExtractorRegistry>();
services.AddSingleton(_ => new IntelPersistenceService(cs));
services.AddSingleton<IEnrichmentTrackingStore>(sp =>
    new SqlEnrichmentTrackingStore(
        cs,
        sp.GetRequiredService<IntelExtractorRegistry>(),
        sp.GetRequiredService<IntelPersistenceService>()));

// Person-side chokepoint.
services.AddSingleton<PersonBriefExtractor>();
services.AddSingleton<IPersonRefreshChokepoint>(sp =>
    new SqlPersonRefreshChokepoint(
        cs,
        sp.GetRequiredService<PersonBriefExtractor>(),
        sp.GetRequiredService<IntelPersistenceService>(),
        sp.GetRequiredService<ILogger<SqlPersonRefreshChokepoint>>()));

// Project-side chokepoint.
services.AddSingleton<IProjectIntelExtractor, ProjectBriefExtractor>();
services.AddSingleton<IProjectIntelExtractor, ProjectBriefHoningExtractor>();
services.AddSingleton<DefaultProjectIntelExtractor>();
services.AddSingleton<ProjectIntelExtractorRegistry>();
services.AddSingleton(sp => new ProjectIntelPersistenceService(
    cs, sp.GetRequiredService<ILogger<ProjectIntelPersistenceService>>()));
services.AddSingleton<IMajorProjectEnrichmentTrackingStore>(sp =>
    new SqlMajorProjectEnrichmentTrackingStore(
        cs,
        sp.GetRequiredService<ProjectIntelExtractorRegistry>(),
        sp.GetRequiredService<ProjectIntelPersistenceService>(),
        sp.GetRequiredService<ILogger<SqlMajorProjectEnrichmentTrackingStore>>()));

// Proponent-side dependencies — resolver + store so we can resolve a
// researched name to an existing CanonicalOrg (auto-resurrects if
// retired) and write the FK back to MPI.
services.AddSingleton<ICanonicalOrgStore>(_ => new SqlCanonicalOrgStore(cs));
services.AddSingleton<CanonicalOrgResolver>();

await using var sp = services.BuildServiceProvider();
var log = sp.GetRequiredService<ILogger<Program>>();

var idPattern = kind switch
{
    "people"          => new Regex(@"^refresh-person-(\d+)\.json$", RegexOptions.IgnoreCase),
    "orgs"            => new Regex(@"^refresh-org-(\d+)\.json$", RegexOptions.IgnoreCase),
    "honing-orgs"     => new Regex(@"^refresh-org-(\d+)\.json$", RegexOptions.IgnoreCase),
    "ab-projects"     => new Regex(@"^refresh-project-(\d+)\.json$", RegexOptions.IgnoreCase),
    "proponents"      => new Regex(@"^refresh-proponent-(\d+)\.json$", RegexOptions.IgnoreCase),
    "org-name-repair" => new Regex(@"^refresh-orgname-(\d+)\.json$", RegexOptions.IgnoreCase),
    "org-classify"    => new Regex(@"^classify-(\d+)\.json$", RegexOptions.IgnoreCase),
    _                 => throw new InvalidOperationException(),
};

var expectedEnvelopeKind = kind switch
{
    "people"          => "person-brief-refresh",
    "orgs"            => "org-brief-refresh",
    "honing-orgs"     => "org-brief-refresh",
    "ab-projects"     => "project-brief-refresh",
    "proponents"      => "proponent-research",
    "org-name-repair" => "org-name-repair",
    "org-classify"    => "org-classify",
    _                 => throw new InvalidOperationException(),
};

var fileGlob = kind == "org-classify" ? "classify-*.json" : "refresh-*.json";
var files = Directory.GetFiles(inputDir, fileGlob);
log.LogInformation("Found {Count} {Kind} output files in {Dir}", files.Length, kind, inputDir);

var ok = 0;
var failed = 0;
var skipped = 0;
var envelopeWrapped = 0;
var legacyShape = 0;
var nextRefresh = DateTimeOffset.UtcNow.AddDays(90);

// BD-Audit-2026-06-09 C1/M2/M3: provider resolution is whitelist-gated and
// refuse-on-miss. On 2026-06-09 the bc-ab-primes drain emitted
// "[providerName: PrimeConsultantResearch]", which the old exact-substring
// detection didn't recognize — 181 outputs silently defaulted to
// "ProjectBrief" and the upsert overwrote the first-pass briefs.
// Resolution rules:
//   1. Root `_providerName` (string) wins, but must be whitelisted; an
//      empty/whitespace or unknown value REJECTS the file (no default).
//   2. Else `[providerName: X]` markers are searched across the whole
//      items[0] payload (tolerant of spacing/case) — honing outputs in the
//      legacy nested `honingPass` shape carry the marker inside honingPass,
//      not in a root description, so scoping to one field would silently
//      default them to first-pass (the exact C1 failure). Multiple DISTINCT
//      provider names in one payload REJECT (ambiguous); unknown X REJECTS.
//   3. No field and no marker -> the kind's default first-pass provider.
var providerMarker = new Regex(@"\[\s*providerName\s*:\s*([A-Za-z0-9._-]+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
var orgProviderWhitelist = new[] { "FirmNarrative", "FirmNarrativeHoning" };
var projectProviderWhitelist = new[] { "ProjectBrief", "ProjectBriefHoning", "PrimeConsultantResearch" };
var personProviderWhitelist = new[] { "PersonBrief", "PersonBriefHoning" };

// BD-Audit-2026-06-09 m6c: honing/special providers refresh on the batch
// generator's 30-day staleness window, not the 90-day first-pass default —
// otherwise rows ingested here look "fresh" for 60 days longer than the
// generator expects and silently leave the honing pool.
DateTimeOffset NextRefreshFor(string provider) =>
    provider.EndsWith("Honing", StringComparison.OrdinalIgnoreCase)
    || string.Equals(provider, "PrimeConsultantResearch", StringComparison.OrdinalIgnoreCase)
        ? DateTimeOffset.UtcNow.AddDays(30)
        : nextRefresh;

// 2026-06-11 maintenance: people briefs that fail to echo displayName used
// to be recovered by hand (5x) — look up the filename ordinal in the queue's
// inputs\batch-*.json, take that row's displayName, and require the name to
// actually appear in the brief content before trusting it (the same evidence
// rule BdPersonBriefRepair used). Ordinals repeat across batches, so content
// evidence is the disambiguator; zero or multiple evidenced candidates REFUSE
// — this fallback never guesses, it only automates the proven manual step.
Dictionary<long, List<string>>? batchNamesByOrdinal = null;

string? ResolvePersonNameFromBatches(long ordinal, string briefContent)
{
    if (batchNamesByOrdinal is null)
    {
        batchNamesByOrdinal = new Dictionary<long, List<string>>();
        var inputsDir = Path.Combine(Directory.GetParent(Path.GetFullPath(inputDir))!.FullName, "inputs");
        if (!Directory.Exists(inputsDir))
        {
            log.LogWarning("Batch-name fallback unavailable: inputs dir not found at {Dir}.", inputsDir);
            return null;
        }

        foreach (var batchFile in Directory.GetFiles(inputsDir, "batch-*.json"))
        {
            try
            {
                using var bd = JsonDocument.Parse(File.ReadAllText(batchFile));
                if (bd.RootElement.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var row in bd.RootElement.EnumerateArray())
                {
                    if (row.ValueKind != JsonValueKind.Object
                        || !row.TryGetProperty("id", out var rid) || !rid.TryGetInt64(out var rowId)
                        || !row.TryGetProperty("displayName", out var rdn) || rdn.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    var rowName = rdn.GetString();
                    if (string.IsNullOrWhiteSpace(rowName))
                    {
                        continue;
                    }

                    if (!batchNamesByOrdinal.TryGetValue(rowId, out var list))
                    {
                        batchNamesByOrdinal[rowId] = list = new List<string>();
                    }

                    if (!list.Contains(rowName, StringComparer.OrdinalIgnoreCase))
                    {
                        list.Add(rowName);
                    }
                }
            }
            catch (JsonException ex)
            {
                log.LogWarning("Batch-name fallback: {File} is not parseable JSON ({Message}); ignored.", Path.GetFileName(batchFile), ex.Message);
            }
        }
    }

    if (!batchNamesByOrdinal.TryGetValue(ordinal, out var candidates) || candidates.Count == 0)
    {
        return null;
    }

    var evidenced = candidates.Where(c => briefContent.Contains(c, StringComparison.OrdinalIgnoreCase)).ToList();
    if (evidenced.Count > 1)
    {
        log.LogWarning(
            "Batch-name fallback: ordinal {Ordinal} matches multiple batch names with content evidence [{Names}] — ambiguous, refusing.",
            ordinal, string.Join(", ", evidenced));
        return null;
    }

    return evidenced.Count == 1 ? evidenced[0] : null;
}

(string? Provider, string? Reason) ResolveDrainProvider(string briefJson, string defaultProvider, string[] whitelist)
{
    try
    {
        using var pDoc = JsonDocument.Parse(briefJson);
        var root = pDoc.RootElement;

        if (root.TryGetProperty("_providerName", out var pn))
        {
            if (pn.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(pn.GetString()))
            {
                return (null, "_providerName is present but empty/non-string — refusing to guess (defaulting would overwrite the first-pass row)");
            }

            var field = pn.GetString()!.Trim();
            var canonical = whitelist.FirstOrDefault(w => string.Equals(w, field, StringComparison.OrdinalIgnoreCase));
            return canonical is not null
                ? (canonical, null)
                : (null, $"_providerName '{field}' is not in the provider whitelist [{string.Join(", ", whitelist)}]");
        }

        var distinctMarked = providerMarker.Matches(briefJson)
            .Select(mm => mm.Groups[1].Value)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (distinctMarked.Count > 1)
        {
            return (null, $"payload contains multiple distinct provider markers [{string.Join(", ", distinctMarked)}] — ambiguous, refusing");
        }

        if (distinctMarked.Count == 1)
        {
            var marked = distinctMarked[0];
            var canonical = whitelist.FirstOrDefault(w => string.Equals(w, marked, StringComparison.OrdinalIgnoreCase));
            return canonical is not null
                ? (canonical, null)
                : (null, $"provider marker '[providerName: {marked}]' is not in the provider whitelist [{string.Join(", ", whitelist)}]");
        }

        return (defaultProvider, null);
    }
    catch (JsonException ex)
    {
        return (null, $"payload is not parseable JSON for provider resolution: {ex.Message}");
    }
}

foreach (var file in files)
{
    var name = Path.GetFileName(file);
    var m = idPattern.Match(name);
    if (!m.Success)
    {
        log.LogWarning("Skipping {Name}: filename doesn't match expected pattern.", name);
        skipped++;
        continue;
    }

    if (!long.TryParse(m.Groups[1].Value, out var id))
    {
        log.LogWarning("Skipping {Name}: couldn't parse id.", name);
        skipped++;
        continue;
    }

    try
    {
        var rawText = await File.ReadAllTextAsync(file).ConfigureAwait(false);

        // R93c: envelope-first parsing. Sonnet should emit
        // { schemaVersion, kind, generatedAtUtc, items: [<the brief>] }.
        // Legacy files (root IS the brief object directly) still pass.
        // Drift in either path is logged loudly instead of producing
        // silent zero-row downstream extracts.
        string briefJson;
        using (var doc = JsonDocument.Parse(rawText))
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, expectedEnvelopeKind);
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array || env.Items.GetArrayLength() != 1)
                {
                    log.LogWarning(
                        "Skipping {Name}: envelope items must be a single-element array (got ValueKind={Vk}, count={Count}).",
                        name, env.Items.ValueKind, env.Items.ValueKind == JsonValueKind.Array ? env.Items.GetArrayLength() : -1);
                    skipped++;
                    continue;
                }
                briefJson = env.Items.EnumerateArray().First().GetRawText();
                envelopeWrapped++;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object
                     && doc.RootElement.TryGetProperty("schemaVersion", out _))
            {
                // Root looks LIKE an envelope (has schemaVersion) but the
                // validator rejected it — usually a kind mismatch or a
                // structural error. Falling through to legacy here would
                // silently push envelope metadata at the chokepoint, which
                // then extracts nothing. Reject loudly instead.
                log.LogWarning(
                    "Skipping {Name}: looks like an envelope but validation failed ({Reason}). Fix the envelope or remove schemaVersion to use legacy.",
                    name, validation.Reason);
                skipped++;
                continue;
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                log.LogInformation(
                    "{Name}: legacy un-enveloped shape (no envelope: {Reason}); ingesting via legacy path.",
                    name, validation.Reason);
                briefJson = rawText;
                legacyShape++;
            }
            else
            {
                log.LogWarning(
                    "Skipping {Name}: payload has neither envelope nor legacy single-object root ({Reason}).",
                    name, validation.Reason);
                skipped++;
                continue;
            }
        }

        var result = new EnrichmentResult(
            EnrichmentStatuses.Ok,
            null,
            briefJson,
            $"Ingested from terminal Sonnet drain at {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");

        switch (kind)
        {
            case "people":
                {
                    // BD-Audit-2026-06-09 M11: honing-people output used to be
                    // un-ingestable — the chokepoint hardcoded the PersonBrief
                    // prefix, so PersonBriefHoning-{id} could never be written;
                    // the batch generator re-selected the same people forever
                    // and honing payloads overwrote first-pass briefs. Resolve
                    // the provider like the other kinds and pass the prefix.
                    var (personProvider, personReject) = ResolveDrainProvider(briefJson, "PersonBrief", personProviderWhitelist);
                    if (personProvider is null)
                    {
                        log.LogWarning("Skipping {Name}: {Reason}.", name, personReject);
                        skipped++;
                        continue;
                    }

                    // 2026-06-10 misattribution incident: people batches carry
                    // ORDINAL ids (the kind sources IntelProjectKeyPerson by
                    // NAME — many subjects have no IntelPerson row yet), and
                    // trusting the filename id wrote ~200 PersonBrief rows onto
                    // whichever IntelPerson happened to own ids 1..N. Identity
                    // is the displayName echoed in the brief, resolved via
                    // NormalizedName. Files without one are REFUSED — an
                    // ordinal can never be trusted as an id.
                    string? subjectName = null;
                    using (var bdoc = JsonDocument.Parse(briefJson))
                    {
                        if (bdoc.RootElement.ValueKind == JsonValueKind.Object
                            && bdoc.RootElement.TryGetProperty("displayName", out var dn)
                            && dn.ValueKind == JsonValueKind.String)
                        {
                            subjectName = dn.GetString();
                        }
                    }

                    if (string.IsNullOrWhiteSpace(subjectName))
                    {
                        subjectName = ResolvePersonNameFromBatches(id, briefJson);
                        if (!string.IsNullOrWhiteSpace(subjectName))
                        {
                            log.LogInformation(
                                "{Name}: brief has no displayName; resolved '{Subject}' from batch row id={Id} (name evidenced in brief content).",
                                name, subjectName, id);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(subjectName))
                    {
                        log.LogWarning(
                            "Skipping {Name}: brief has no displayName and the batch-name fallback found no unambiguous name evidence — people ids are batch ordinals and cannot be trusted. Re-run with a PROMPT that echoes the input displayName.",
                            name);
                        skipped++;
                        continue;
                    }

                    var matches = new List<long>();
                    await using (var pcon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                    {
                        await pcon.OpenAsync().ConfigureAwait(false);
                        await using var pcmd = new Microsoft.Data.SqlClient.SqlCommand(
                            "SELECT Id FROM opportunities.IntelPerson WHERE NormalizedName = @n AND RetiredAtUtc IS NULL;", pcon);
                        pcmd.Parameters.AddWithValue("@n", IntelNaturalKey.Normalize(subjectName));
                        await using var pr = await pcmd.ExecuteReaderAsync().ConfigureAwait(false);
                        while (await pr.ReadAsync().ConfigureAwait(false))
                        {
                            matches.Add(pr.GetInt64(0));
                        }
                    }

                    if (matches.Count == 0 && personProvider == "PersonBrief")
                    {
                        // FIRST-PASS people research discovers NEW people (the
                        // batch kind sources IntelProjectKeyPerson by name) —
                        // create the IntelPerson so the brief has a home,
                        // using PersistAsync's conventions (NaturalKey =
                        // Compute(NormalizedName)). Honing stays strict: it
                        // refreshes existing people only.
                        if (!IntelPersonNameGuard.IsValid(subjectName, out var nameReason))
                        {
                            log.LogWarning("Skipping {Name}: displayName '{Subject}' fails the person name guard ({Reason}).", name, subjectName, nameReason);
                            skipped++;
                            continue;
                        }

                        var normalized = IntelNaturalKey.Normalize(subjectName);
                        await using var icon = new Microsoft.Data.SqlClient.SqlConnection(cs);
                        await icon.OpenAsync().ConfigureAwait(false);
                        // SourceEnrichmentId is FK'd NOT NULL but the brief's
                        // enrichment row doesn't exist until the chokepoint
                        // runs — seed with any valid id; the chokepoint's
                        // extractor MERGEs this person by NaturalKey and
                        // overwrites SourceEnrichmentId with the real row.
                        await using var icmd = new Microsoft.Data.SqlClient.SqlCommand(@"
INSERT INTO opportunities.IntelPerson
    (SourceProviderName, SourceEnrichmentId, SourceConfidence, NaturalKey,
     FirstSeenAtUtc, LastSeenAtUtc, DisplayName, NormalizedName)
SELECT N'PersonBrief', (SELECT MIN(Id) FROM opportunities.CanonicalOrgEnrichment), N'Medium', @naturalKey,
       sysdatetimeoffset(), sysdatetimeoffset(), @displayName, @normalizedName;
SELECT CAST(SCOPE_IDENTITY() AS bigint);", icon);
                        icmd.Parameters.AddWithValue("@naturalKey", IntelNaturalKey.Compute(normalized));
                        icmd.Parameters.AddWithValue("@displayName", subjectName.Length <= 200 ? subjectName : subjectName[..200]);
                        icmd.Parameters.AddWithValue("@normalizedName", normalized.Length <= 200 ? normalized : normalized[..200]);
                        matches.Add((long)(await icmd.ExecuteScalarAsync().ConfigureAwait(false))!);
                        log.LogInformation("{Name}: created IntelPerson {Id} for '{Subject}' (first-pass discovery).", name, matches[0], subjectName);
                    }

                    if (matches.Count == 0)
                    {
                        log.LogWarning(
                            "Skipping {Name}: no active IntelPerson matches displayName '{Subject}' — honing refreshes known people only.",
                            name, subjectName);
                        skipped++;
                        continue;
                    }

                    if (matches.Count > 1)
                    {
                        log.LogWarning(
                            "Skipping {Name}: displayName '{Subject}' is AMBIGUOUS ({Count} active IntelPerson rows: {Ids}) — dedup the persons first.",
                            name, subjectName, matches.Count, string.Join(", ", matches));
                        skipped++;
                        continue;
                    }

                    await sp.GetRequiredService<IPersonRefreshChokepoint>()
                        .RecordAttemptAsync(matches[0], result, NextRefreshFor(personProvider), CancellationToken.None, personProvider)
                        .ConfigureAwait(false);
                }
                break;
            case "orgs":
            case "honing-orgs":
                {
                    var (orgProvider, orgReject) = ResolveDrainProvider(briefJson, "FirmNarrative", orgProviderWhitelist);
                    if (orgProvider is null)
                    {
                        log.LogWarning("Skipping {Name}: {Reason}.", name, orgReject);
                        skipped++;
                        continue;
                    }

                    // Same lifecycle guard the ab-projects path got after audit
                    // M1: orgs can be merged/purged between batch generation and
                    // ingest (2026-06-10: 6 orphan-purged orgs hit the
                    // CanonicalOrgEnrichment FK as raw SqlExceptions). Refuse
                    // cleanly; the file stays in outputs/ so the refusal is
                    // visible.
                    await using (var verifyCon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                    {
                        await verifyCon.OpenAsync().ConfigureAwait(false);
                        await using var verify = new Microsoft.Data.SqlClient.SqlCommand(
                            "SELECT CASE WHEN RetiredAtUtc IS NULL THEN 0 ELSE 1 END FROM opportunities.CanonicalOrg WHERE Id = @id;", verifyCon);
                        verify.Parameters.AddWithValue("@id", id);
                        var orgState = await verify.ExecuteScalarAsync().ConfigureAwait(false);
                        if (orgState is null)
                        {
                            log.LogWarning("{Name}: CanonicalOrg Id={Id} does not exist (merged/purged since batch generation); skipping.", name, id);
                            skipped++;
                            continue;
                        }

                        if ((int)orgState == 1)
                        {
                            log.LogWarning("{Name}: CanonicalOrg Id={Id} is retired; refusing to attach enrichment. Re-point to the survivor and re-run.", name, id);
                            skipped++;
                            continue;
                        }
                    }

                    await sp.GetRequiredService<IEnrichmentTrackingStore>()
                        .RecordAttemptAsync(id, orgProvider, result, NextRefreshFor(orgProvider), CancellationToken.None)
                        .ConfigureAwait(false);
                }
                break;
            case "ab-projects":
                {
                    var (projProvider, projReject) = ResolveDrainProvider(briefJson, "ProjectBrief", projectProviderWhitelist);
                    if (projProvider is null)
                    {
                        log.LogWarning("Skipping {Name}: {Reason}.", name, projReject);
                        skipped++;
                        continue;
                    }

                    // BD-Audit-2026-06-09 M1: 53 intel rows were written to MPIs
                    // that had already been retired (batches generated before the
                    // retirement landed). Refuse here — survivor mapping is a
                    // migration/human decision, not an ingest-time guess. The
                    // file stays in outputs/ so the refusal is visible.
                    await using (var verifyCon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                    {
                        await verifyCon.OpenAsync().ConfigureAwait(false);
                        await using var verify = new Microsoft.Data.SqlClient.SqlCommand(
                            "SELECT CASE WHEN RetiredAtUtc IS NULL THEN 0 ELSE 1 END FROM opportunities.MajorProjectsInventory WHERE Id = @id;", verifyCon);
                        verify.Parameters.AddWithValue("@id", id);
                        var mpiState = await verify.ExecuteScalarAsync().ConfigureAwait(false);
                        if (mpiState is null)
                        {
                            log.LogWarning("{Name}: MPI Id={Id} does not exist; skipping.", name, id);
                            skipped++;
                            continue;
                        }

                        if ((int)mpiState == 1)
                        {
                            log.LogWarning("{Name}: MPI Id={Id} is retired; refusing to attach enrichment/intel. Re-point the batch to the survivor MPI and re-run.", name, id);
                            skipped++;
                            continue;
                        }
                    }

                    await sp.GetRequiredService<IMajorProjectEnrichmentTrackingStore>()
                        .RecordAttemptAsync(id, projProvider, result, NextRefreshFor(projProvider), CancellationToken.None)
                        .ConfigureAwait(false);

                    // 2026-06-11 graph completion: honing output may carry
                    // dedicated architectName / structuralEngineerName
                    // findings. Resolve each against active CanonicalOrgs by
                    // NormalizedName and FILL the MPI link column when it is
                    // currently NULL — never overwrite an existing edge.
                    // Ambiguous or unknown names are logged and left for the
                    // dedup/alias worklists; "confirmed-open" is a finding,
                    // not a name.
                    using (var linkDoc = JsonDocument.Parse(briefJson))
                    {
                        var root2 = linkDoc.RootElement;
                        var hp = root2.TryGetProperty("honingPass", out var hpEl) && hpEl.ValueKind == JsonValueKind.Object ? hpEl : root2;
                        foreach (var (field, column) in new[] { ("architectName", "ArchitectCanonicalOrgId"), ("structuralEngineerName", "StructuralEngineerCanonicalOrgId") })
                        {
                            if (!hp.TryGetProperty(field, out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
                            {
                                continue;
                            }

                            var orgName = nameEl.GetString();
                            if (string.IsNullOrWhiteSpace(orgName)
                                || string.Equals(orgName, "confirmed-open", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(orgName, "unknown", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            var linkMatches = new List<long>();
                            await using (var lcon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                            {
                                await lcon.OpenAsync().ConfigureAwait(false);
                                await using var lcmd = new Microsoft.Data.SqlClient.SqlCommand(
                                    "SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName = @n AND RetiredAtUtc IS NULL;", lcon);
                                lcmd.Parameters.AddWithValue("@n", Kor.Opportunities.Data.Awards.CanonicalOrgResolver.NormalizeName(orgName));
                                await using var lr = await lcmd.ExecuteReaderAsync().ConfigureAwait(false);
                                while (await lr.ReadAsync().ConfigureAwait(false))
                                {
                                    linkMatches.Add(lr.GetInt64(0));
                                }
                            }

                            if (linkMatches.Count != 1)
                            {
                                log.LogInformation("{Name}: {Field} '{Org}' resolved to {Count} active canonicals — link not set.", name, field, orgName, linkMatches.Count);
                                continue;
                            }

                            await using var ucon = new Microsoft.Data.SqlClient.SqlConnection(cs);
                            await ucon.OpenAsync().ConfigureAwait(false);
                            await using var ucmd = new Microsoft.Data.SqlClient.SqlCommand(
                                $"UPDATE opportunities.MajorProjectsInventory SET {column} = @org, UpdatedAtUtc = sysdatetimeoffset() WHERE Id = @mpi AND {column} IS NULL;", ucon);
                            ucmd.Parameters.AddWithValue("@org", linkMatches[0]);
                            ucmd.Parameters.AddWithValue("@mpi", id);
                            var setRows = await ucmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            if (setRows > 0)
                            {
                                log.LogInformation("{Name}: graph edge set — MPI {Mpi} {Column} -> {Org} ('{OrgName}').", name, id, column, linkMatches[0], orgName);
                            }
                        }
                    }
                }
                break;
            case "proponents":
                {
                    // Envelope items[0]: { mpiId, proponentName, proponentWebsite, confidence, evidence, notes }
                    using var pDoc = JsonDocument.Parse(briefJson);
                    var root = pDoc.RootElement;
                    string? proponentName = null;
                    if (root.TryGetProperty("proponentName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
                    {
                        proponentName = nameProp.GetString();
                    }
                    if (string.IsNullOrWhiteSpace(proponentName))
                    {
                        log.LogWarning("{Name}: missing proponentName in envelope items[0]; skipping.", name);
                        skipped++;
                        continue;
                    }

                    // PRE-FLIGHT: verify MPI Id exists BEFORE creating canonical. This
                    // prevents the audit-found gap where ResolveAsync mints a canonical
                    // (or auto-resurrects one) and then the MPI UPDATE finds 0 rows,
                    // leaving an orphan canonical with no back-link. The same SELECT
                    // also reads the existing FK + name (BD-Audit-2026-06-09 m6d) so
                    // an already-linked MPI never touches the resolver at all.
                    var trimmedName = proponentName.Trim();
                    var mpiExists = false;
                    long? existingCanonicalId = null;
                    string? existingProponentName = null;
                    await using (var verifyCon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                    {
                        await verifyCon.OpenAsync().ConfigureAwait(false);
                        await using var verify = new Microsoft.Data.SqlClient.SqlCommand(
                            "SELECT ProponentCanonicalOrgId, ProponentName FROM opportunities.MajorProjectsInventory WHERE Id = @id;", verifyCon);
                        verify.Parameters.AddWithValue("@id", id);
                        await using var verifyReader = await verify.ExecuteReaderAsync().ConfigureAwait(false);
                        if (await verifyReader.ReadAsync().ConfigureAwait(false))
                        {
                            mpiExists = true;
                            existingCanonicalId = verifyReader.IsDBNull(0) ? null : verifyReader.GetInt64(0);
                            existingProponentName = verifyReader.IsDBNull(1) ? null : verifyReader.GetString(1);
                        }
                    }

                    if (!mpiExists)
                    {
                        log.LogWarning("{Name}: MPI Id={Id} does not exist; skipping (no canonical created).", name, id);
                        skipped++;
                        continue;
                    }

                    if (existingCanonicalId.HasValue)
                    {
                        // FK already linked — skip the resolver entirely; only
                        // backfill ProponentName when it's blank.
                        if (string.IsNullOrWhiteSpace(existingProponentName))
                        {
                            await using var nameCon = new Microsoft.Data.SqlClient.SqlConnection(cs);
                            await nameCon.OpenAsync().ConfigureAwait(false);
                            await using var nameCmd = new Microsoft.Data.SqlClient.SqlCommand(
                                @"UPDATE opportunities.MajorProjectsInventory
                                  SET ProponentName = @name
                                  WHERE Id = @id;", nameCon);
                            nameCmd.Parameters.AddWithValue("@name", trimmedName);
                            nameCmd.Parameters.AddWithValue("@id", id);
                            await nameCmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                            log.LogInformation(
                                "Proponent name backfilled: MPI {Id} -> '{Name}' (existing canonical={Canon} untouched).",
                                id, trimmedName, existingCanonicalId);
                        }
                        else
                        {
                            log.LogInformation(
                                "Proponent already linked: MPI {Id} has canonical={Canon} ('{Existing}'); no resolver call.",
                                id, existingCanonicalId, existingProponentName);
                        }
                    }
                    else
                    {
                        // MPI exists and has no FK. Now safe to resolve/create canonical
                        // (auto-resurrects retired matches). Defaults to Kind=Unknown — a
                        // later R95d-style classifier promotes it.
                        var resolver = sp.GetRequiredService<CanonicalOrgResolver>();
                        var canonicalId = await resolver.ResolveAsync(
                            trimmedName, "Unknown", "proponent-drain", CancellationToken.None,
                            allowCreate: true, minConfidenceForCreate: 70).ConfigureAwait(false);

                        // Write back to MPI: ProponentName + ProponentCanonicalOrgId.
                        await using var con = new Microsoft.Data.SqlClient.SqlConnection(cs);
                        await con.OpenAsync().ConfigureAwait(false);
                        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(
                            @"UPDATE opportunities.MajorProjectsInventory
                              SET ProponentName = @name,
                                  ProponentCanonicalOrgId = COALESCE(ProponentCanonicalOrgId, @canonId)
                              WHERE Id = @id;", con);
                        cmd.Parameters.AddWithValue("@name", trimmedName);
                        cmd.Parameters.AddWithValue("@canonId", (object?)canonicalId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@id", id);
                        var rows = await cmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        if (rows == 0)
                        {
                            // Should be impossible after pre-flight check, but handle defensively.
                            log.LogError("{Name}: MPI Id={Id} UPDATE affected 0 rows after pre-flight succeeded — concurrent delete? CanonicalOrg {Canon} may now be orphaned.", name, id, canonicalId);
                            failed++;
                            continue;
                        }
                        log.LogInformation("Proponent applied: MPI {Id} -> '{Name}' (canonical={Canon})", id, trimmedName, canonicalId);
                    }
                }
                break;
            case "org-name-repair":
                {
                    // m126 repair (2026-06-12): rename a suppressed-for-junk-name
                    // org to its VERIFIED name and clear the suppression so it
                    // re-enters normal enrichment (NormalizedName is a computed
                    // column — it follows DisplayName automatically). Identity is
                    // double-checked: the payload must echo both the CanonicalOrg
                    // id AND the current garbled DisplayName, so a mixed-up output
                    // gets refused instead of renaming the wrong org.
                    long? echoedId = null;
                    string? echoedGarbled = null, correctedName = null, sourceUrl = null;
                    var nameIsCorrect = false;
                    double confidence = 0;
                    using (var rdoc = JsonDocument.Parse(briefJson))
                    {
                        var rroot = rdoc.RootElement;
                        if (rroot.ValueKind != JsonValueKind.Object)
                        {
                            log.LogWarning("Skipping {Name}: items[0] is not an object.", name);
                            skipped++;
                            continue;
                        }

                        if (rroot.TryGetProperty("canonicalOrgId", out var cid) && cid.ValueKind == JsonValueKind.Number && cid.TryGetInt64(out var cidVal))
                        {
                            echoedId = cidVal;
                        }

                        if (rroot.TryGetProperty("garbledName", out var gn) && gn.ValueKind == JsonValueKind.String)
                        {
                            echoedGarbled = gn.GetString();
                        }

                        if (rroot.TryGetProperty("correctedName", out var cn) && cn.ValueKind == JsonValueKind.String)
                        {
                            correctedName = cn.GetString()?.Trim();
                        }

                        if (rroot.TryGetProperty("sourceUrl", out var su) && su.ValueKind == JsonValueKind.String)
                        {
                            sourceUrl = su.GetString();
                        }

                        if (rroot.TryGetProperty("nameIsCorrect", out var nic) && nic.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            nameIsCorrect = nic.GetBoolean();
                        }

                        if (rroot.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number)
                        {
                            confidence = cf.GetDouble();
                        }
                    }

                    if (echoedId != id)
                    {
                        log.LogWarning("Skipping {Name}: payload canonicalOrgId={Echoed} does not match filename id={Id} — identity mismatch.", name, echoedId, id);
                        skipped++;
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(echoedGarbled) || string.IsNullOrWhiteSpace(correctedName)
                        || correctedName.Length > 300 || string.IsNullOrWhiteSpace(sourceUrl) || confidence < 0.7)
                    {
                        log.LogWarning(
                            "Skipping {Name}: payload incomplete or below bar (garbledName/correctedName/sourceUrl required, correctedName <= 300 chars, confidence >= 0.7; got confidence={Confidence:0.##}).",
                            name, confidence);
                        skipped++;
                        continue;
                    }

                    string? currentDisplayName = null, currentSuppressedReason = null;
                    var orgRetired = false;
                    var orgExists = false;
                    await using (var vcon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                    {
                        await vcon.OpenAsync().ConfigureAwait(false);
                        await using var vcmd = new Microsoft.Data.SqlClient.SqlCommand(
                            "SELECT DisplayName, RetiredAtUtc, EnrichmentSuppressedReason FROM opportunities.CanonicalOrg WHERE Id = @id;", vcon);
                        vcmd.Parameters.AddWithValue("@id", id);
                        await using var vr = await vcmd.ExecuteReaderAsync().ConfigureAwait(false);
                        if (await vr.ReadAsync().ConfigureAwait(false))
                        {
                            orgExists = true;
                            currentDisplayName = vr.GetString(0);
                            orgRetired = !vr.IsDBNull(1);
                            currentSuppressedReason = vr.IsDBNull(2) ? null : vr.GetString(2);
                        }
                    }

                    if (!orgExists)
                    {
                        log.LogWarning("Skipping {Name}: CanonicalOrg Id={Id} does not exist (merged/purged since batch generation).", name, id);
                        skipped++;
                        continue;
                    }

                    if (orgRetired)
                    {
                        log.LogWarning("Skipping {Name}: CanonicalOrg Id={Id} is retired; a retired org stays retired — repair the survivor instead.", name, id);
                        skipped++;
                        continue;
                    }

                    if (currentSuppressedReason is null || !currentSuppressedReason.StartsWith("m126:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Replay safety: a re-run over an already-applied repair is
                        // a no-op success, not an error. Anything else touched the
                        // org since batch generation — refuse, never clobber.
                        if (string.Equals(currentDisplayName, correctedName, StringComparison.Ordinal))
                        {
                            log.LogInformation("{Name}: repair already applied (org {Id} = '{Corrected}', unsuppressed); no-op.", name, id, correctedName);
                            break;
                        }

                        log.LogWarning("Skipping {Name}: CanonicalOrg Id={Id} is no longer m126-suppressed (reason='{Reason}') — repaired or reclassified since batch generation.", name, id, currentSuppressedReason);
                        skipped++;
                        continue;
                    }

                    if (!string.Equals(echoedGarbled, currentDisplayName, StringComparison.Ordinal))
                    {
                        log.LogWarning(
                            "Skipping {Name}: echoed garbledName '{Echoed}' does not match current DisplayName '{Current}' — identity mismatch or concurrent edit.",
                            name, echoedGarbled, currentDisplayName);
                        skipped++;
                        continue;
                    }

                    if (nameIsCorrect && !string.Equals(correctedName, currentDisplayName, StringComparison.Ordinal))
                    {
                        log.LogWarning("Skipping {Name}: nameIsCorrect=true but correctedName '{Corrected}' differs from the current name — contradictory payload.", name, correctedName);
                        skipped++;
                        continue;
                    }

                    if (!nameIsCorrect)
                    {
                        // A corrected name that collides with another active org is
                        // a MERGE (FK repoint via BdCanonicalDedup --pairs), not a
                        // rename — renaming here would mint the duplicate the dedup
                        // sweeps just cleaned. Park the pair on a worklist CSV.
                        var collisions = new List<long>();
                        await using (var ccon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                        {
                            await ccon.OpenAsync().ConfigureAwait(false);
                            await using var ccmd = new Microsoft.Data.SqlClient.SqlCommand(
                                "SELECT Id FROM opportunities.CanonicalOrg WHERE NormalizedName = @n AND RetiredAtUtc IS NULL AND Id <> @id;", ccon);
                            ccmd.Parameters.AddWithValue("@n", Kor.Opportunities.Data.Awards.CanonicalOrgResolver.NormalizeName(correctedName));
                            ccmd.Parameters.AddWithValue("@id", id);
                            await using var crr = await ccmd.ExecuteReaderAsync().ConfigureAwait(false);
                            while (await crr.ReadAsync().ConfigureAwait(false))
                            {
                                collisions.Add(crr.GetInt64(0));
                            }
                        }

                        if (collisions.Count > 0)
                        {
                            var pairsPath = Path.Combine(inputDir, "name-repair-merge-pairs.csv");
                            if (!File.Exists(pairsPath))
                            {
                                await File.WriteAllTextAsync(pairsPath, "LoserId,SurvivorId,GarbledName,CorrectedName\r\n").ConfigureAwait(false);
                            }

                            await File.AppendAllTextAsync(
                                pairsPath,
                                $"{id},{collisions[0]},\"{currentDisplayName.Replace("\"", "\"\"")}\",\"{correctedName.Replace("\"", "\"\"")}\"\r\n").ConfigureAwait(false);
                            log.LogWarning(
                                "Skipping {Name}: corrected name '{Corrected}' collides with active CanonicalOrg(s) [{Ids}] — parked on {Csv} for a BdCanonicalDedup --pairs merge.",
                                name, correctedName, string.Join(", ", collisions), Path.GetFileName(pairsPath));
                            skipped++;
                            continue;
                        }
                    }

                    await using (var ucon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                    {
                        await ucon.OpenAsync().ConfigureAwait(false);
                        await using var tx = (Microsoft.Data.SqlClient.SqlTransaction)await ucon.BeginTransactionAsync().ConfigureAwait(false);

                        // Optimistic guard on DisplayName: a concurrent edit between
                        // the verify read and this UPDATE makes it a 0-row no-op.
                        await using var ucmd = new Microsoft.Data.SqlClient.SqlCommand(@"
UPDATE opportunities.CanonicalOrg
SET DisplayName = @corrected,
    EnrichmentSuppressedAtUtc = NULL,
    EnrichmentSuppressedReason = NULL,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id AND DisplayName = @expected;", ucon, tx);
                        ucmd.Parameters.AddWithValue("@corrected", correctedName);
                        ucmd.Parameters.AddWithValue("@id", id);
                        ucmd.Parameters.AddWithValue("@expected", currentDisplayName);
                        var updated = await ucmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        if (updated == 0)
                        {
                            await tx.RollbackAsync().ConfigureAwait(false);
                            log.LogWarning("Skipping {Name}: CanonicalOrg Id={Id} changed between verify and update (concurrent edit); re-run.", name, id);
                            skipped++;
                            continue;
                        }

                        if (!nameIsCorrect)
                        {
                            // Preserve the garbled string as an alias so future
                            // ingests that see the same raw form still resolve to
                            // this canonical. (RawName, Source) is unique — guard.
                            await using var acmd = new Microsoft.Data.SqlClient.SqlCommand(@"
IF NOT EXISTS (SELECT 1 FROM opportunities.OrgAlias WHERE RawName = @raw AND Source = N'OrgNameRepair')
INSERT INTO opportunities.OrgAlias (CanonicalOrgId, RawName, Source, Confidence, ClassifiedBy, ClassifiedAtUtc, Notes, CreatedAtUtc)
VALUES (@id, @raw, N'OrgNameRepair', 100, N'BdQueueDrainIngest', sysdatetimeoffset(), @notes, sysdatetimeoffset());", ucon, tx);
                            acmd.Parameters.AddWithValue("@id", id);
                            acmd.Parameters.AddWithValue("@raw", currentDisplayName.Length <= 300 ? currentDisplayName : currentDisplayName[..300]);
                            acmd.Parameters.AddWithValue("@notes", $"m126 name repair; verified via {sourceUrl}");
                            await acmd.ExecuteNonQueryAsync().ConfigureAwait(false);
                        }

                        await tx.CommitAsync().ConfigureAwait(false);
                    }

                    if (nameIsCorrect)
                    {
                        log.LogInformation("{Name}: org {Id} name confirmed correct ('{Corrected}'); suppression cleared.", name, id, correctedName);
                    }
                    else
                    {
                        log.LogInformation("{Name}: org {Id} renamed '{Garbled}' -> '{Corrected}'; suppression cleared, alias preserved.", name, id, currentDisplayName, correctedName);
                    }
                }
                break;
            case "org-classify":
                {
                    // classify-unknown-orgs drain: reads classify-{id}.json, updates CanonicalOrg.Kind
                    // where the org is still Unknown and active. Identity verified via canonicalOrgId echo.
                    long? classifyEchoedId = null;
                    string? classifyDisplayName = null, resolvedKind = null;
                    double classifyConfidence = 0;
                    using (var cdoc = JsonDocument.Parse(briefJson))
                    {
                        var croot = cdoc.RootElement;
                        if (croot.TryGetProperty("canonicalOrgId", out var cid) && cid.TryGetInt64(out var cidVal))
                            classifyEchoedId = cidVal;
                        if (croot.TryGetProperty("displayName", out var cdn) && cdn.ValueKind == JsonValueKind.String)
                            classifyDisplayName = cdn.GetString();
                        if (croot.TryGetProperty("resolvedKind", out var rk) && rk.ValueKind == JsonValueKind.String)
                            resolvedKind = rk.GetString()?.Trim();
                        if (croot.TryGetProperty("confidence", out var cf) && cf.ValueKind == JsonValueKind.Number)
                            classifyConfidence = cf.GetDouble();
                    }

                    if (classifyEchoedId != id)
                    {
                        log.LogWarning("Skipping {Name}: payload canonicalOrgId={Echoed} does not match filename id={Id}.", name, classifyEchoedId, id);
                        skipped++;
                        continue;
                    }

                    var validKinds = new[] { "Architect", "Buyer", "GC", "Competitor", "Developer", "KorClient" };
                    var canonicalKind = validKinds.FirstOrDefault(k => string.Equals(k, resolvedKind, StringComparison.OrdinalIgnoreCase));
                    if (canonicalKind is null)
                    {
                        log.LogWarning("Skipping {Name}: resolvedKind '{Kind}' is not in the allowed list [{Valid}].", name, resolvedKind, string.Join(", ", validKinds));
                        skipped++;
                        continue;
                    }

                    if (classifyConfidence < 0.75)
                    {
                        log.LogWarning("Skipping {Name}: confidence {Conf:0.##} below 0.75.", name, classifyConfidence);
                        skipped++;
                        continue;
                    }

                    await using var kcon = new Microsoft.Data.SqlClient.SqlConnection(cs);
                    await kcon.OpenAsync().ConfigureAwait(false);
                    await using var kcmd = new Microsoft.Data.SqlClient.SqlCommand(
                        @"UPDATE opportunities.CanonicalOrg
                          SET Kind = @kind, UpdatedAtUtc = SYSDATETIMEOFFSET()
                          WHERE Id = @id AND Kind = N'Unknown' AND RetiredAtUtc IS NULL;
                          SELECT @@ROWCOUNT;", kcon);
                    kcmd.Parameters.AddWithValue("@kind", canonicalKind);
                    kcmd.Parameters.AddWithValue("@id", id);
                    var classifyRows = (int)(await kcmd.ExecuteScalarAsync().ConfigureAwait(false))!;
                    if (classifyRows == 0)
                    {
                        log.LogWarning("{Name}: CanonicalOrg Id={Id} not updated — already classified, retired, or missing.", name, id);
                        skipped++;
                        continue;
                    }
                    log.LogInformation("{Name}: CanonicalOrg Id={Id} '{Display}' → Kind={Kind} (confidence={Conf:0.##})", name, id, classifyDisplayName, canonicalKind, classifyConfidence);
                }
                break;
        }

        var target = Path.Combine(processedDir, name);
        if (File.Exists(target))
        {
            // Keep the prior audit copy instead of deleting it
            // (BD-Audit-2026-06-09 m6b) — rename it aside as superseded.
            File.Move(target, target + $".superseded-{DateTime.UtcNow:yyyyMMddHHmmss}");
        }

        File.Move(file, target);
        ok++;
    }
    catch (Exception ex)
    {
        log.LogWarning(ex, "Failed to ingest {Name}", name);
        failed++;
    }
}

Console.WriteLine(
    $"Ingest complete. ok={ok} failed={failed} skipped={skipped} " +
    $"(envelope={envelopeWrapped}, legacy={legacyShape}). " +
    $"Expected envelope kind = '{expectedEnvelopeKind}'.");

// BD-Audit-2026-06-09 m6a: an all-skip run means every file was refused
// (bad markers, wrong envelope kind, retired MPIs, ...) — that is a failure
// of the batch, not a quiet no-op. Exit 2 so callers/automation notice.
if (files.Length > 0 && ok == 0 && skipped == files.Length)
{
    Console.Error.WriteLine(
        $"ERROR: all {files.Length} file(s) in {inputDir} were skipped — nothing was ingested. " +
        "Check provider markers / envelope kinds / MPI ids before re-running.");
    return 2;
}

return failed > 0 ? 1 : 0;
