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
if (kind is not ("people" or "orgs" or "ab-projects"))
{
    return Fail("Usage: BdQueueDrainIngest --kind people|orgs|ab-projects [--dir <path>]");
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

await using var sp = services.BuildServiceProvider();
var log = sp.GetRequiredService<ILogger<Program>>();

var idPattern = kind switch
{
    "people"      => new Regex(@"^refresh-person-(\d+)\.json$", RegexOptions.IgnoreCase),
    "orgs"        => new Regex(@"^refresh-org-(\d+)\.json$", RegexOptions.IgnoreCase),
    "ab-projects" => new Regex(@"^refresh-project-(\d+)\.json$", RegexOptions.IgnoreCase),
    _             => throw new InvalidOperationException(),
};

var expectedEnvelopeKind = kind switch
{
    "people"      => "person-brief-refresh",
    "orgs"        => "org-brief-refresh",
    "ab-projects" => "project-brief-refresh",
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
                await sp.GetRequiredService<IEnrichmentTrackingStore>()
                    .RecordAttemptAsync(id, "FirmNarrative", result, nextRefresh, CancellationToken.None)
                    .ConfigureAwait(false);
                break;
            case "ab-projects":
                await sp.GetRequiredService<IMajorProjectEnrichmentTrackingStore>()
                    .RecordAttemptAsync(id, "ProjectBrief", result, nextRefresh, CancellationToken.None)
                    .ConfigureAwait(false);
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
