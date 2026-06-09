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
                await sp.GetRequiredService<IPersonRefreshChokepoint>()
                    .RecordAttemptAsync(id, result, nextRefresh, CancellationToken.None)
                    .ConfigureAwait(false);
                break;
            case "orgs":
                {
                    // Detect honing marker — Sonnet's honing PROMPT may either:
                    //   (a) write `_providerName: "FirmNarrativeHoning"` at root of items[0]
                    //   (b) embed `[providerName: FirmNarrativeHoning]` inside a description /
                    //       narrative text field (some PROMPTs use this pattern)
                    // Detect both. Without this, every honing ingest requires a manual
                    // UPDATE ... SET ProviderName = 'FirmNarrativeHoning' SQL pass and risks
                    // unique-key conflicts (UQ_MajorProjectEnrichment_ProjectProvider) on
                    // projects that already have a honing row from a sibling category.
                    var orgProvider = "FirmNarrative";
                    try
                    {
                        using var pDoc = JsonDocument.Parse(briefJson);
                        if (pDoc.RootElement.TryGetProperty("_providerName", out var pn)
                            && pn.ValueKind == JsonValueKind.String)
                        {
                            var v = pn.GetString();
                            if (!string.IsNullOrWhiteSpace(v)) orgProvider = v;
                        }
                        else if (briefJson.IndexOf("[providerName: FirmNarrativeHoning]",
                                    StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            orgProvider = "FirmNarrativeHoning";
                        }
                    }
                    catch { /* fall back to FirmNarrative */ }

                    await sp.GetRequiredService<IEnrichmentTrackingStore>()
                        .RecordAttemptAsync(id, orgProvider, result, nextRefresh, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                break;
            case "ab-projects":
                {
                    // Same honing-marker detection for project briefs.
                    // Supports both root `_providerName` field AND `[providerName: ProjectBriefHoning]`
                    // marker embedded in description / narrative text.
                    var projProvider = "ProjectBrief";
                    try
                    {
                        using var pDoc = JsonDocument.Parse(briefJson);
                        if (pDoc.RootElement.TryGetProperty("_providerName", out var pn)
                            && pn.ValueKind == JsonValueKind.String)
                        {
                            var v = pn.GetString();
                            if (!string.IsNullOrWhiteSpace(v)) projProvider = v;
                        }
                        else if (briefJson.IndexOf("[providerName: ProjectBriefHoning]",
                                    StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            projProvider = "ProjectBriefHoning";
                        }
                    }
                    catch { /* fall back to ProjectBrief */ }

                    await sp.GetRequiredService<IMajorProjectEnrichmentTrackingStore>()
                        .RecordAttemptAsync(id, projProvider, result, nextRefresh, CancellationToken.None)
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
                    // leaving an orphan canonical with no back-link.
                    var trimmedName = proponentName.Trim();
                    await using (var verifyCon = new Microsoft.Data.SqlClient.SqlConnection(cs))
                    {
                        await verifyCon.OpenAsync().ConfigureAwait(false);
                        await using var verify = new Microsoft.Data.SqlClient.SqlCommand(
                            "SELECT COUNT(*) FROM opportunities.MajorProjectsInventory WHERE Id = @id;", verifyCon);
                        verify.Parameters.AddWithValue("@id", id);
                        var exists = (int)(await verify.ExecuteScalarAsync().ConfigureAwait(false) ?? 0);
                        if (exists == 0)
                        {
                            log.LogWarning("{Name}: MPI Id={Id} does not exist; skipping (no canonical created).", name, id);
                            skipped++;
                            continue;
                        }
                    }

                    // MPI exists. Now safe to resolve/create canonical (auto-resurrects
                    // retired matches). Defaults to Kind=Unknown — a later R95d-style
                    // classifier promotes it.
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
                break;
        }

        var target = Path.Combine(processedDir, name);
        if (File.Exists(target))
        {
            File.Delete(target);
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
return failed > 0 ? 1 : 0;
