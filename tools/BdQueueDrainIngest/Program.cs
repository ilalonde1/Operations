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
if (kind is not ("people" or "orgs" or "ab-projects" or "proponents"))
{
    return Fail("Usage: BdQueueDrainIngest --kind people|orgs|ab-projects|proponents [--dir <path>]");
}

var inputDir = ReadArg(args, "--dir")
    ?? Path.Combine(@"C:\ProgramData\KorOperations\QueueDrain", kind, "outputs");
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

// Org-side chokepoint (FirmNarrative provider; auto-decomposes via the
// existing IntelExtractorRegistry chain — register the schema extractor).
services.AddSingleton<IIntelExtractor>(_ => new CanonicalSchemaExtractor("FirmNarrative"));
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
    "people"      => new Regex(@"^refresh-person-(\d+)\.json$", RegexOptions.IgnoreCase),
    "orgs"        => new Regex(@"^refresh-org-(\d+)\.json$", RegexOptions.IgnoreCase),
    "ab-projects" => new Regex(@"^refresh-project-(\d+)\.json$", RegexOptions.IgnoreCase),
    "proponents"  => new Regex(@"^refresh-proponent-(\d+)\.json$", RegexOptions.IgnoreCase),
    _             => throw new InvalidOperationException(),
};

var expectedEnvelopeKind = kind switch
{
    "people"      => "person-brief-refresh",
    "orgs"        => "org-brief-refresh",
    "ab-projects" => "project-brief-refresh",
    "proponents"  => "proponent-research",
    _             => throw new InvalidOperationException(),
};

var files = Directory.GetFiles(inputDir, "refresh-*.json");
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
                        log.LogWarning(
                            "Skipping {Name}: brief has no displayName — people ids are batch ordinals and cannot be trusted. Re-run with a PROMPT that echoes the input displayName.",
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

                    if (matches.Count == 0)
                    {
                        log.LogWarning(
                            "Skipping {Name}: no active IntelPerson matches displayName '{Subject}' — research a known person or create the IntelPerson first.",
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
