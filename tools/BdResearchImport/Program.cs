#nullable enable
using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.IndustryEvents;
using Kor.Opportunities.Data.Intel;
using Kor.Opportunities.Data.MajorProjects;
using Kor.Opportunities.Data.ResearchEnvelope;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;

namespace Kor.BdResearchImport;

internal static class Program
{
    private const string Province = "BC";
    private const string DefaultBaseDirectory = @"C:\VIsual Studio Projects";
    private const string ProponentSource = "MajorProjectsInventory.Proponent";
    private const string ArchitectSource = "MajorProjectsInventory.Architect";
    private const string ClientKind = "Client";
    private const string DeveloperKind = "Developer";
    private const string ContractorKind = "Contractor";
    private const string OwnerKind = "Owner";
    private static readonly Regex KorRegex = new(@"\bKOR\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex CanonicalIngestFileSkipRegex = new(@"(progress|summary|README|INSTRUCTIONS)\.", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly HashSet<string> StrictCanonicalKinds = new(StringComparer.Ordinal)
    {
        "Architect",
        "Developer",
        "GC",
        "Designer",
        "Modular",
        "Competitor",
        "Buyer",
        "Government",
        "Subcontractor",
        "Vendor",
        "KorClient",
        "Unknown",
    };

    private const string StrictCanonicalKindList = "{Architect, Developer, GC, Designer, Modular, Competitor, Buyer, Government, Subcontractor, Vendor, KorClient, Unknown}";
    private static readonly string[] DataHoningRecognizedKinds =
    [
        OrgKinds.Architect,
        OrgKinds.Competitor,
        OrgKinds.Developer,
        OrgKinds.GeneralContractor,
        OrgKinds.Subcontractor,
        OrgKinds.Buyer,
        ClientKind,
        OrgKinds.KorClient,
        OrgKinds.KorStructural,
    ];

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ImportOptions.Parse(args);
            var canonicalIngestMode = !string.IsNullOrWhiteSpace(options.IngestCanonicalFolder);
            if ((!options.DryRun || canonicalIngestMode) && string.IsNullOrWhiteSpace(options.OpportunitiesDb))
            {
                Console.Error.WriteLine("Missing connection string. Set KOR_OPPORTUNITIES_OPPORTUNITIESDB or pass --db.");
                return 2;
            }

            var stats = new ImportStats();
            var sw = Stopwatch.StartNew();
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
            };

            var db = options.DryRun && !canonicalIngestMode ? null : options.OpportunitiesDb;
            var orgStore = db is null ? null : new SqlCanonicalOrgStore(db);
            SqlEnrichmentTrackingStore? enrichmentStore = null;
            if (db is not null)
            {
                var fallback = new DefaultIntelExtractor();
                var registry = new IntelExtractorRegistry(IntelExtractorBootstrap.GetDefaultExtractors(), fallback);
                var persistence = new IntelPersistenceService(db);
                enrichmentStore = new SqlEnrichmentTrackingStore(db, registry, persistence);
            }

            var industryEventStore = db is null ? null : new SqlIndustryEventStore(db);
            var displacementBriefStore = db is null ? null : new SqlArchitectDisplacementBriefStore(db);
            var resolver = orgStore is null
                ? null
                : new CanonicalOrgResolver(orgStore, NullLogger<CanonicalOrgResolver>.Instance);

            Console.WriteLine($"BD Research import starting: base={options.BaseDirectory}; dry-run={options.DryRun.ToString().ToLowerInvariant()}; fx-rate={options.FxRate.ToString(CultureInfo.InvariantCulture)}");

            if (canonicalIngestMode)
            {
                return await RunIngestCanonicalAsync(options, orgStore, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            }

            bool Run(string tag) => string.IsNullOrWhiteSpace(options.Only)
                || string.Equals(options.Only, tag, StringComparison.OrdinalIgnoreCase);

            if (Run("contractor")) await ImportContractorResearchAsync(options, orgStore, enrichmentStore, stats, cts.Token).ConfigureAwait(false);
            if (Run("public-sector")) await ImportPublicSectorAsync(options, orgStore, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("indigenous")) await ImportIndigenousDevelopmentAsync(options, orgStore, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("bc-dev")) await ImportBcDevelopmentPipelineAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("la")) await ImportUsMarketAsync(
                options,
                orgStore,
                enrichmentStore,
                resolver,
                stats,
                directoryName: "KOR-LA-Market",
                enrichmentProviderName: "LAMarketResearch",
                projectSource: "LAMarketProjects",
                sourceKeyPrefix: "LAMKT-",
                defaultProvince: "CA",
                includeStateInSourceKey: false,
                ct: cts.Token).ConfigureAwait(false);
            if (Run("pacnw")) await ImportUsMarketAsync(
                options,
                orgStore,
                enrichmentStore,
                resolver,
                stats,
                directoryName: "KOR-PacNW-Market",
                enrichmentProviderName: "PacNWMarketResearch",
                projectSource: "PacNWMarketProjects",
                sourceKeyPrefix: "PACNW-",
                defaultProvince: "WA",
                includeStateInSourceKey: true,
                ct: cts.Token).ConfigureAwait(false);
            if (Run("alberta")) await ImportAlbertaMarketAsync(options, orgStore, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("institutional")) await ImportInstitutionalPipelineAsync(options, orgStore, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("prime-targeting")) await ImportPrimeTargetingAsync(options, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("prime-contacts")) await ImportPrimeContactsAsync(options, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("island-okanagan")) await ImportIslandOkanaganAsync(options, orgStore, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("intel-gathering")) await ImportIntelGatheringAsync(options, orgStore, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("owner-pipelines")) await ImportOwnerPipelinesAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("competitor-profiles")) await ImportCompetitorProfilesAsync(options, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("decision-makers")) await ImportDecisionMakersAsync(options, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("data-honing")) await ImportDataHoningAsync(options, enrichmentStore, stats, cts.Token).ConfigureAwait(false);
            if (Run("registries")) await ImportRegistriesAsync(options, orgStore, enrichmentStore, stats, cts.Token).ConfigureAwait(false);
            if (Run("capital-plans")) await ImportCapitalPlansAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("project-teams")) await ImportProjectTeamsAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("competitor-projects")) await ImportCompetitorProjectsAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("structural-pipeline")) await ImportStructuralPipelineAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("indigenous-projects")) await ImportIndigenousPipelineProjectsAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("indigenous-orgs")) await ImportIndigenousPipelineOrgsAsync(options, orgStore, enrichmentStore, stats, cts.Token).ConfigureAwait(false);
            if (Run("owner-procurement")) await ImportOwnerProcurementAsync(options, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("competitor-signals")) await ImportCompetitorSignalsAsync(options, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("structural-partner-map")) await ImportStructuralPartnerMapAsync(options, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("displacement-briefs")) await ImportDisplacementBriefsAsync(options, displacementBriefStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("sub-consultants")) await ImportSubConsultantsAsync(options, orgStore, enrichmentStore, stats, cts.Token).ConfigureAwait(false);
            if (Run("facility-renewal")) await ImportFacilityRenewalAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("projects-honing")) await ImportProjectsHoningAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("pipeline-seats")) await ImportPipelineSeatsAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("project-reverify")) await ImportProjectReverifyAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("midmarket")) await ImportMidMarketAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("architect-forecast")) await ImportArchitectForecastAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("kor-capability")) await ImportKorCapabilityAsync(options, orgStore, enrichmentStore, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("industry-events")) await ImportIndustryEventsAsync(options, industryEventStore, stats, cts.Token).ConfigureAwait(false);
            if (Run("db-contractors")) await ImportDbContractorsAsync(options, orgStore, enrichmentStore, stats, cts.Token).ConfigureAwait(false);
            if (Run("incumbent-rosters")) await ImportIncumbentRostersAsync(options, orgStore, enrichmentStore, stats, cts.Token).ConfigureAwait(false);
            if (Run("capital-funding-signals")) await ImportCapitalFundingSignalsAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("seismic-pipeline")) await ImportSeismicPipelineAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("island-okanagan-pairing")) await ImportIslandOkanaganPairingAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("lower-mainland-pairing")) await ImportLowerMainlandPairingAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("edmonton-pairing")) await ImportEdmontonPairingAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("bd-tracking")) await ImportBdTrackingAsync(options, resolver, stats, cts.Token).ConfigureAwait(false);
            if (Run("bd-tracking-crosslink")) await ImportBdTrackingCrossLinkAsync(options, stats, cts.Token).ConfigureAwait(false);

            sw.Stop();
            WriteSummary(options, stats, sw.Elapsed);
            return 0;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("BD Research import canceled.");
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"BD Research import failed: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> RunIngestCanonicalAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        if (orgStore is null || resolver is null)
        {
            Console.Error.WriteLine("--ingest-canonical requires a database-backed CanonicalOrgResolver.");
            return 2;
        }

        if (!options.DryRun && enrichmentStore is null)
        {
            Console.Error.WriteLine("--ingest-canonical requires a database-backed enrichment store.");
            return 2;
        }

        var folder = options.IngestCanonicalFolder!;
        if (!Path.IsPathFullyQualified(folder))
        {
            Console.Error.WriteLine("--ingest-canonical requires an absolute folder path.");
            return 2;
        }

        if (!Directory.Exists(folder))
        {
            Console.Error.WriteLine($"--ingest-canonical folder does not exist: {folder}");
            return 2;
        }

        var ingestStats = new CanonicalIngestStats();
        var prefix = options.DryRun ? "[DRY-RUN] " : string.Empty;
        var aggressiveIndex = await BuildAggressiveKeyIndexAsync(options.OpportunitiesDb, ct).ConfigureAwait(false);
        if (!options.Quiet)
        {
            Console.WriteLine($"{prefix}  Aggressive-key canonical index loaded: {aggressiveIndex.Count} keys");
        }

        async Task IngestRecordAsync(CanonicalIngestFileStats fileStats, string relPath, JsonElement record, int index)
        {
            fileStats.RecordCount++;
            if (record.ValueKind != JsonValueKind.Object)
            {
                fileStats.Skipped++;
                AddCount(ingestStats.SkippedByReason, "missing _providerName");
                if (!options.Quiet)
                {
                    Console.WriteLine($"  SKIP {relPath}#{index}: missing _providerName and no --provider fallback");
                }

                return;
            }

            var providerName = String(record, "_providerName") ?? options.IngestCanonicalProviderOverride;
            if (string.IsNullOrWhiteSpace(providerName))
            {
                if (options.StrictCanonicalSchema)
                {
                    ingestStats.StrictViolations++;
                    Console.WriteLine($"  STRICT-ERROR {relPath}#{index}: missing _providerName and no --provider fallback");
                }

                fileStats.Skipped++;
                AddCount(ingestStats.SkippedByReason, "missing _providerName");
                if (!options.Quiet)
                {
                    Console.WriteLine($"  SKIP {relPath}#{index}: missing _providerName and no --provider fallback");
                }

                return;
            }

            var displayName = GetDisplayName(record, out var aliasField);
            if (aliasField is not null)
            {
                fileStats.AliasFallbacks++;
                AddCount(ingestStats.AliasFallbacksByField, aliasField);
                if (options.StrictCanonicalSchema)
                {
                    Console.WriteLine($"  STRICT-ERROR {relPath}#{index} ({providerName}): PROMPT used legacy alias '{aliasField}'; prefer displayName");
                }
                else if (!options.Quiet)
                {
                    Console.WriteLine($"  WARN {relPath}#{index} ({providerName}): PROMPT used legacy alias '{aliasField}'; prefer displayName");
                }

                if (options.StrictCanonicalSchema) ingestStats.StrictViolations++;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                fileStats.Skipped++;
                AddCount(ingestStats.SkippedByReason, "missing displayName");
                if (!options.Quiet)
                {
                    Console.WriteLine($"  SKIP {relPath}#{index} ({providerName}): no displayName field");
                }

                return;
            }

            var kindFieldPresent = record.TryGetProperty("kind", out var kindElement)
                && kindElement.ValueKind is not JsonValueKind.Null and not JsonValueKind.Undefined;
            var kind = String(record, "kind") ?? OrgKinds.Unknown;
            if (options.StrictCanonicalSchema && !kindFieldPresent)
            {
                ingestStats.StrictViolations++;
                Console.WriteLine($"  STRICT-ERROR {relPath}#{index} ({displayName}): missing 'kind' field; canonical schema requires kind {StrictCanonicalKindList}");
            }
            else if (options.StrictCanonicalSchema && !StrictCanonicalKinds.Contains(kind))
            {
                ingestStats.StrictViolations++;
                Console.WriteLine($"  STRICT-ERROR {relPath}#{index} ({displayName}): kind value '{kind}' not in enum {StrictCanonicalKindList}");
            }

            var aggressiveKey = CanonicalOrgResolver.NormalizeAggressiveKey(displayName);
            long? orgId = null;
            var aggressiveHit = false;
            if (aggressiveKey.Length > 0 && aggressiveIndex.TryGetValue(aggressiveKey, out var hitId))
            {
                orgId = hitId;
                aggressiveHit = true;
                ingestStats.AggressiveKeyMatches++;
                if (!options.Quiet)
                {
                    Console.WriteLine($"  AGGRESSIVE-MATCH {relPath}#{index} ({displayName}): -> canonicalOrgId={hitId} via stripped-suffix key '{aggressiveKey}'");
                }
            }

            if (orgId is null)
            {
                var wouldMatch = await AlreadyResolvableAsync(displayName).ConfigureAwait(false);
                if (options.DryRun)
                {
                    orgId = wouldMatch ? 0L : null;
                }
                else
                {
                    orgId = await resolver.ResolveAsync(
                        displayName,
                        kind,
                        source: "research-ingest",
                        ct: ct,
                        allowCreate: true).ConfigureAwait(false);

                    if (!orgId.HasValue)
                    {
                        fileStats.Skipped++;
                        AddCount(ingestStats.SkippedByReason, "resolver-null");
                        if (!options.Quiet)
                        {
                            Console.WriteLine($"  SKIP {relPath}#{index} ({displayName}): resolver returned null");
                        }

                        return;
                    }
                }

                if (wouldMatch)
                {
                    ingestStats.CanonicalOrgMatches++;
                }
                else
                {
                    ingestStats.CanonicalOrgCreates++;
                }
            }
            else
            {
                ingestStats.CanonicalOrgMatches++;
            }

            if (!options.DryRun && aggressiveKey.Length > 0 && orgId.HasValue && !aggressiveIndex.ContainsKey(aggressiveKey))
            {
                aggressiveIndex[aggressiveKey] = orgId.Value;
            }

            if (aggressiveHit && !options.DryRun)
            {
                await orgStore.UpsertAliasAsync(
                    rawName: displayName.Trim(),
                    source: "research-ingest",
                    canonicalOrgId: orgId.Value,
                    confidence: 70,
                    classifiedBy: "auto-aggressive-key",
                    notes: $"Matched via NormalizeAggressiveKey -> '{aggressiveKey}'",
                    ct: ct).ConfigureAwait(false);
            }

            if (!options.DryRun)
            {
                if (enrichmentStore is null)
                {
                    throw new InvalidOperationException("Enrichment store is not available.");
                }

                var json = record.GetRawText();
                var result = new EnrichmentResult(
                    EnrichmentStatuses.Ok,
                    null,
                    json,
                    $"Ingested via --ingest-canonical at {DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");
                var nextRefresh = DateTimeOffset.UtcNow.AddDays(21);
                await enrichmentStore.RecordAttemptAsync(
                    orgId.Value,
                    providerName,
                    result,
                    nextRefresh,
                    ct).ConfigureAwait(false);
                stats.EnrichmentRowsWritten++;
                AddCount(stats.EnrichmentRowsByProvider, providerName);
            }

            fileStats.Ingested++;
            AddCount(ingestStats.IngestedByProvider, providerName);
        }

        async Task<bool> AlreadyResolvableAsync(string displayName)
        {
            var trimmed = displayName.Trim();
            var alias = await orgStore.LookupAliasAsync(trimmed, "research-ingest", ct).ConfigureAwait(false);
            if (alias is not null && alias.CanonicalOrgId.HasValue)
            {
                return true;
            }

            var normalized = CanonicalOrgResolver.NormalizeName(displayName);
            if (normalized.Length == 0)
            {
                return false;
            }

            return (await orgStore.FindByNormalizedNameAsync(normalized, ct).ConfigureAwait(false)).HasValue;
        }

        static string? GetDisplayName(JsonElement record, out string? aliasField)
        {
            var displayName = String(record, "displayName");
            if (!string.IsNullOrWhiteSpace(displayName))
            {
                aliasField = null;
                return displayName;
            }

            foreach (var field in new[] { "orgDisplayName", "organizationName", "firmName" })
            {
                var value = String(record, field);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    aliasField = field;
                    return value;
                }
            }

            aliasField = null;
            return null;
        }

        void WriteCanonicalSummary()
        {
            Console.WriteLine($"Canonical research ingest (dry-run={options.DryRun.ToString().ToLowerInvariant()}) complete.");
            Console.WriteLine($"  Files walked:                  {ingestStats.FilesWalked}");
            Console.WriteLine($"  Files with records:            {ingestStats.FilesWithRecords}");
            Console.WriteLine($"  Files skipped (none parseable): {ingestStats.FilesSkippedNoParseable}");

            Console.WriteLine("  Records ingested by provider:");
            foreach (var (provider, count) in ingestStats.IngestedByProvider.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    {provider}: {count}");
            }

            Console.WriteLine("  Records skipped by reason:");
            foreach (var (reason, count) in ingestStats.SkippedByReason.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    {reason}: {count}");
            }

            Console.WriteLine("  Alias fallback usage:");
            foreach (var (field, count) in ingestStats.AliasFallbacksByField.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    {field}: {count}");
            }

            Console.WriteLine($"  CanonicalOrg creates:          {ingestStats.CanonicalOrgCreates}");
            Console.WriteLine($"  CanonicalOrg matches:          {ingestStats.CanonicalOrgMatches}");
            Console.WriteLine($"  Matched via aggressive-key fallback: {ingestStats.AggressiveKeyMatches}");
            Console.WriteLine($"  Strict-mode violations: {ingestStats.StrictViolations}");
        }

        foreach (var path in Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories)
                     .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            ingestStats.FilesWalked++;

            var relPath = Path.GetRelativePath(folder, path);
            if (CanonicalIngestFileSkipRegex.IsMatch(Path.GetFileName(path)))
            {
                ingestStats.FilesSkippedNoParseable++;
                AddCount(ingestStats.SkippedByReason, "file name-filter match");
                if (!options.Quiet)
                {
                    Console.WriteLine($"{prefix}  SKIP {relPath}: file name-filter match");
                }

                continue;
            }

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                ingestStats.FilesSkippedNoParseable++;
                AddCount(ingestStats.SkippedByReason, "file unparseable");
                if (!options.Quiet)
                {
                    Console.WriteLine($"{prefix}  SKIP {relPath}: file unparseable ({ex.GetType().Name}: {ex.Message})");
                }

                continue;
            }

            var fileStats = new CanonicalIngestFileStats();
            using (doc)
            {
                if (doc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    var index = 0;
                    foreach (var record in doc.RootElement.EnumerateArray())
                    {
                        await IngestRecordAsync(fileStats, relPath, record, index++).ConfigureAwait(false);
                    }
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    await IngestRecordAsync(fileStats, relPath, doc.RootElement, 0).ConfigureAwait(false);
                }
                else
                {
                    AddCount(ingestStats.SkippedByReason, "file unparseable");
                    if (!options.Quiet)
                    {
                        Console.WriteLine($"{prefix}  SKIP {relPath}: root JSON is not an object or array");
                    }
                }
            }

            if (fileStats.RecordCount > 0)
            {
                ingestStats.FilesWithRecords++;
            }
            else
            {
                ingestStats.FilesSkippedNoParseable++;
            }

            Console.WriteLine($"{prefix}  {relPath}: ingested={fileStats.Ingested} skipped={fileStats.Skipped} aliasFallbacks={fileStats.AliasFallbacks}");
        }

        WriteCanonicalSummary();
        return options.StrictCanonicalSchema && ingestStats.StrictViolations > 0 ? 3 : 0;
    }

    private static async Task<Dictionary<string, long>> BuildAggressiveKeyIndexAsync(
        string connectionString,
        CancellationToken ct)
    {
        const string sql = @"
SELECT Id, DisplayName
FROM opportunities.CanonicalOrg
ORDER BY Id;";

        var dict = new Dictionary<string, long>(StringComparer.Ordinal);
        await using var con = new SqlConnection(connectionString);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con);
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            var id = r.GetInt64(0);
            var displayName = r.GetString(1);
            var key = CanonicalOrgResolver.NormalizeAggressiveKey(displayName);
            if (key.Length == 0)
            {
                continue;
            }

            if (!dict.ContainsKey(key))
            {
                dict[key] = id;
            }
        }

        return dict;
    }

    private static async Task ImportContractorResearchAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Contractor-Research", "import-payload.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "contractor");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("contractor: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object
                     && doc.RootElement.TryGetProperty("firms", out var legacyArr)
                     && legacyArr.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"contractor: payload is legacy {{firms:[]}} shape (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = legacyArr;
                source = "legacy {firms:[]}";
            }
            else
            {
                Console.WriteLine(
                    $"contractor: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"contractor: ingesting {itemsArray.GetArrayLength()} firm item(s) via {source}.");

            foreach (var firm in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var firmName = String(firm, "firmName");
                if (string.IsNullOrWhiteSpace(firmName))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await UpsertOrgAsync(
                    orgStore,
                    options,
                    stats,
                    OrgKinds.GeneralContractor,
                    firmName,
                    String(firm, "websiteUrl"),
                    String(firm, "researchNotes"),
                    "ContractorResearch",
                    ct).ConfigureAwait(false);

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "ContractorResearch",
                    firm.GetRawText(),
                    null,
                    firmName,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportPublicSectorAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var dir = Path.Combine(options.BaseDirectory, "KOR-PublicSector-Research");
        if (!Directory.Exists(dir))
        {
            Console.WriteLine($"[WARN] Missing directory: {dir}");
            stats.FilesMissing++;
            return;
        }

        foreach (var path in Directory.GetFiles(dir, "buyer-profile.json", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            ct.ThrowIfCancellationRequested();
            if (!TryLoadJson(path, out var doc))
            {
                stats.FilesMissing++;
                continue;
            }

            using (doc)
            {
                var validation = ResearchEnvelopeValidator.Validate(doc, "public-sector-buyer-profile");
                JsonElement buyer;
                string source;
                if (validation.IsValid && validation.Envelope is { } env
                    && env.Items.ValueKind == JsonValueKind.Array
                    && env.Items.GetArrayLength() >= 1)
                {
                    buyer = env.Items.EnumerateArray().First();
                    source = $"envelope v{env.SchemaVersion}";
                }
                else if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    buyer = doc.RootElement;
                    source = "legacy single-object root";
                }
                else
                {
                    Console.WriteLine($"public-sector: {path} has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                    continue;
                }

                Console.WriteLine($"public-sector: {path} ingesting via {source}.");
                var buyerName = String(buyer, "buyerName") ?? String(buyer, "ownerName");
                if (string.IsNullOrWhiteSpace(buyerName))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await UpsertOrgAsync(
                    orgStore,
                    options,
                    stats,
                    ClientKind,
                    buyerName,
                    String(buyer, "websiteUrl") ?? String(buyer, "capitalPlanUrl"),
                    String(buyer, "korRelevanceReason") ?? String(buyer, "procurementProcess"),
                    "PublicSectorResearch",
                    ct).ConfigureAwait(false);

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "PublicSectorResearch",
                    buyer.GetRawText(),
                    null,
                    buyerName,
                    ct).ConfigureAwait(false);
            }
        }

        var projectsPath = Path.Combine(dir, "projects-payload.json");
        if (!TryLoadJson(projectsPath, out var projectsDoc))
        {
            stats.FilesMissing++;
            return;
        }

        using (projectsDoc)
        {
            foreach (var project in EnumerateArray(projectsDoc.RootElement, "projects"))
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var buyerOrg = String(project, "buyerOrg");
                var proponentId = await ResolveAsync(resolver, options, stats, buyerOrg, OrgKinds.Unknown, ProponentSource, ct).ConfigureAwait(false);
                var record = new MajorProjectRecord(
                    Source: "PublicSectorResearch",
                    SourceKey: "PUBSEC-" + Sha1($"{buyerOrg}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: String(project, "description"),
                    EstimatedCostCad: Money(project, "estimatedCostCad"),
                    EstimatedCostText: CostText(project, "estimatedCostCad"),
                    Sector: String(project, "sector"),
                    SubSector: String(project, "subSector"),
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(project, "region"),
                    MunicipalityName: String(project, "municipality"),
                    ProponentName: buyerOrg,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: null,
                    ArchitectCanonicalOrgId: null,
                    Stage: String(project, "stage"),
                    ProjectStatus: String(project, "stage"),
                    ProjectStage: "PublicCapitalPlan",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: Short(project, "anticipatedTenderYear"),
                    CompletionYear: Short(project, "anticipatedCompletionYear"),
                    ScheduleNotes: null,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: String(project, "sourceUrl"),
                    RawJson: project.GetRawText());

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportIndigenousDevelopmentAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var dir = Path.Combine(options.BaseDirectory, "KOR-Indigenous-Development");
        var devCorpsPath = Path.Combine(dir, "dev-corps-payload.json");
        if (TryLoadJson(devCorpsPath, out var devCorpsDoc))
        {
            using (devCorpsDoc)
            {
                if (TrySelectEnvelopeOrLegacyItems(devCorpsDoc, "indigenous-dev-corps", "indigenous-development", "dev-corps", "orgs", out var orgItems))
                {
                    foreach (var org in orgItems.EnumerateArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        var orgName = String(org, "org_name") ?? String(org, "name");
                        if (string.IsNullOrWhiteSpace(orgName))
                        {
                            stats.OrgRowsSkipped++;
                            continue;
                        }

                        var orgId = await UpsertOrgAsync(
                            orgStore,
                            options,
                            stats,
                            DeveloperKind,
                            orgName,
                            String(org, "website"),
                            String(org, "notes"),
                            "IndigenousDevResearch",
                            ct).ConfigureAwait(false);

                        await WriteEnrichmentAsync(
                            enrichmentStore,
                            options,
                            stats,
                            orgId,
                            "IndigenousDevResearch",
                            org.GetRawText(),
                            null,
                            orgName,
                            ct).ConfigureAwait(false);
                    }
                }
            }
        }
        else
        {
            stats.FilesMissing++;
        }

        var projectsPath = Path.Combine(dir, "projects-payload.json");
        if (TryLoadJson(projectsPath, out var projectsDoc))
        {
            using (projectsDoc)
            {
                if (!TrySelectEnvelopeOrLegacyItems(projectsDoc, "indigenous-dev-projects", "indigenous-development", "projects", "projects", out var projectItems))
                {
                    return;
                }

                foreach (var project in projectItems.EnumerateArray())
                {
                    ct.ThrowIfCancellationRequested();
                    var projectName = String(project, "ProjectName") ?? String(project, "projectName");
                    if (string.IsNullOrWhiteSpace(projectName))
                    {
                        stats.ProjectRowsSkipped++;
                        continue;
                    }

                    var proponentName = String(project, "ProponentName") ?? String(project, "owner");
                    var architectName = String(project, "ArchitectName") ?? String(project, "architect");
                    var structuralEngineer = String(project, "StructuralEngineer") ?? String(project, "structuralEngineer");
                    var proponentId = await ResolveAsync(resolver, options, stats, proponentName, OrgKinds.Unknown, ProponentSource, ct).ConfigureAwait(false);
                    var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                    var record = new MajorProjectRecord(
                        Source: "IndigenousDevProjects",
                        SourceKey: "INDIG-" + Sha1($"{String(project, "IndigenousNames")}|{projectName}"),
                        ProjectName: projectName,
                        ProjectDescription: null,
                        EstimatedCostCad: Money(project, "EstimatedCostCad"),
                        EstimatedCostText: CostText(project, "EstimatedCostCad") ?? String(project, "estimatedCost"),
                        Sector: String(project, "Sector") ?? String(project, "sector"),
                        SubSector: null,
                        ConstructionType: null,
                        ConstructionSubtype: null,
                        ProjectType: null,
                        RegionName: null,
                        MunicipalityName: String(project, "Location") ?? String(project, "city"),
                        ProponentName: proponentName,
                        ProponentCanonicalOrgId: proponentId,
                        ArchitectName: architectName,
                        ArchitectCanonicalOrgId: architectId,
                        Stage: String(project, "Status") ?? String(project, "stage"),
                        ProjectStatus: String(project, "Status") ?? String(project, "stage"),
                        ProjectStage: "Indigenous",
                        ProjectCategoryName: null,
                        PublicFundingInd: null,
                        ProvincialFunding: null,
                        FederalFunding: null,
                        MunicipalFunding: null,
                        OtherPublicFunding: null,
                        GreenBuildingInd: null,
                        IndigenousInd: true,
                        IndigenousNames: String(project, "IndigenousNames") ?? String(project, "owner"),
                        ConstructionJobs: null,
                        OperatingJobs: null,
                        StandardizedStartDate: null,
                        StandardizedCompletionDate: null,
                        StartYear: null,
                        CompletionYear: null,
                        ScheduleNotes: BuildIndigenousScheduleNotes(String(project, "ExpectedTimeline") ?? String(project, "expectedTimeline"), structuralEngineer),
                        Latitude: null,
                        Longitude: null,
                        ProjectWebsite: null,
                        SourceUrl: String(project, "SourceUrl") ?? String(project, "sourceUrl"),
                        RawJson: project.GetRawText());

                    await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
                }
            }
        }
        else
        {
            stats.FilesMissing++;
        }

        var partnerGraphPath = Path.Combine(dir, "partner-graph.json");
        if (TryLoadJson(partnerGraphPath, out var partnerGraphDoc))
        {
            using (partnerGraphDoc)
            {
                if (TrySelectEnvelopeOrLegacyItems(partnerGraphDoc, "indigenous-partner-graph", "indigenous-development", "partner-graph", "firms", out var firmItems))
                {
                    foreach (var firm in firmItems.EnumerateArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        var firmName = String(firm, "firm_name") ?? String(firm, "firmName") ?? String(firm, "name");
                        if (string.IsNullOrWhiteSpace(firmName))
                        {
                            stats.OrgRowsSkipped++;
                            continue;
                        }

                        var orgId = await UpsertOrgAsync(
                            orgStore,
                            options,
                            stats,
                            MapPartnerKind(String(firm, "kind")),
                            firmName,
                            null,
                            null,
                            "IndigenousPartnerGraph",
                            ct).ConfigureAwait(false);

                        await WriteEnrichmentAsync(
                            enrichmentStore,
                            options,
                            stats,
                            orgId,
                            "IndigenousPartnerGraph",
                            firm.GetRawText(),
                            String(firm, "kor_signal"),
                            firmName,
                            ct).ConfigureAwait(false);
                    }
                }
            }
        }
        else
        {
            stats.FilesMissing++;
        }
    }

    private static async Task ImportBcDevelopmentPipelineAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-BC-Development-Pipeline", "projects-payload.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "bc-dev");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("bc-dev: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object
                     && doc.RootElement.TryGetProperty("projects", out var legacyArr)
                     && legacyArr.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"bc-dev: payload is legacy {{projects:[]}} shape (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = legacyArr;
                source = "legacy {projects:[]}";
            }
            else
            {
                Console.WriteLine(
                    $"bc-dev: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"bc-dev: ingesting {itemsArray.GetArrayLength()} project item(s) via {source}.");

            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "ProjectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var proponentName = String(project, "ProponentName");
                var architectName = String(project, "ArchitectName");
                var proponentId = await ResolveAsync(resolver, options, stats, proponentName, OrgKinds.Unknown, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var record = new MajorProjectRecord(
                    Source: "BcDevelopmentPipeline",
                    SourceKey: "DEVPIPE-" + Sha1($"{String(project, "Municipality")}|{projectName}|{String(project, "Address")}"),
                    ProjectName: projectName,
                    ProjectDescription: String(project, "Description"),
                    EstimatedCostCad: Money(project, "EstimatedCostCad"),
                    EstimatedCostText: CostText(project, "EstimatedCostCad"),
                    Sector: String(project, "Sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: null,
                    MunicipalityName: String(project, "Municipality"),
                    ProponentName: proponentName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "Status"),
                    ProjectStatus: String(project, "Status"),
                    ProjectStage: "PreTender",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: null,
                    ScheduleNotes: JoinNotes(
                        ("ApplicationType", String(project, "ApplicationType")),
                        ("Storeys", String(project, "Storeys")),
                        ("UnitsOrFloorArea", String(project, "UnitsOrFloorArea"))),
                    Latitude: Decimal(project, "Latitude"),
                    Longitude: Decimal(project, "Longitude"),
                    ProjectWebsite: null,
                    SourceUrl: String(project, "SourceUrl"),
                    RawJson: project.GetRawText());

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportUsMarketAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        string directoryName,
        string enrichmentProviderName,
        string projectSource,
        string sourceKeyPrefix,
        string defaultProvince,
        bool includeStateInSourceKey,
        CancellationToken ct)
    {
        var dir = Path.Combine(options.BaseDirectory, directoryName);

        var firmsPath = Path.Combine(dir, "firms-payload.json");
        if (TryLoadJson(firmsPath, out var firmsDoc))
        {
            using (firmsDoc)
            {
                if (TrySelectEnvelopeOrLegacyItems(firmsDoc, "us-market-firms", "us-market", "firms", "firms", out var firmItems))
                {
                    foreach (var firm in firmItems.EnumerateArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        var firmName = String(firm, "firmName");
                        if (string.IsNullOrWhiteSpace(firmName))
                        {
                            stats.OrgRowsSkipped++;
                            continue;
                        }

                        var orgId = await UpsertOrgAsync(
                            orgStore,
                            options,
                            stats,
                            MapResearchFirmKind(String(firm, "kind")),
                            firmName,
                            String(firm, "website") ?? String(firm, "websiteUrl"),
                            String(firm, "researchNotes"),
                            enrichmentProviderName,
                            ct).ConfigureAwait(false);

                        await WriteEnrichmentAsync(
                            enrichmentStore,
                            options,
                            stats,
                            orgId,
                            enrichmentProviderName,
                            firm.GetRawText(),
                            null,
                            firmName,
                            ct).ConfigureAwait(false);
                    }
                }
            }
        }
        else
        {
            stats.FilesMissing++;
        }

        var projectsPath = Path.Combine(dir, "projects-payload.json");
        if (!TryLoadJson(projectsPath, out var projectsDoc))
        {
            stats.FilesMissing++;
            return;
        }

        using (projectsDoc)
        {
            var sourceKeysSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TrySelectEnvelopeOrLegacyItems(projectsDoc, "us-market-projects", "us-market", "projects", "projects", out var projectItems))
            {
                return;
            }

            foreach (var project in projectItems.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "ProjectName") ?? String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var municipality = String(project, "Municipality") ?? String(project, "city");
                var rawState = String(project, "State") ?? String(project, "state");
                if (includeStateInSourceKey && string.IsNullOrWhiteSpace(rawState))
                {
                    stats.ProjectRowsSkipped++;
                    Console.WriteLine($"[WARN] {projectSource}: skipping project with blank State; project={projectName}");
                    continue;
                }

                var province = NormalizeProvince(includeStateInSourceKey ? rawState : defaultProvince, defaultProvince);
                var proponentName = String(project, "ProponentName") ?? String(project, "owner");
                var architectName = String(project, "ArchitectName") ?? String(project, "architect");
                var proponentId = await ResolveAsync(resolver, options, stats, proponentName, OrgKinds.Unknown, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var usd = Decimal(project, "EstimatedCostUsd");
                var sourceKeyInput = includeStateInSourceKey
                    ? $"{projectName}|{municipality}|{province}"
                    : $"{projectName}|{municipality}";
                var sourceKey = sourceKeyPrefix + Sha1(sourceKeyInput);
                if (!sourceKeysSeen.Add(sourceKey))
                {
                    stats.SourceKeyCollisions++;
                    Console.WriteLine($"[WARN] {projectSource}: duplicate SourceKey in this run ({sourceKey}); later row may overwrite earlier row. project={projectName}");
                }

                var record = new MajorProjectRecord(
                    Source: projectSource,
                    SourceKey: sourceKey,
                    ProjectName: projectName,
                    ProjectDescription: String(project, "FullDescription"),
                    EstimatedCostCad: UsdToCad(usd, options.FxRate),
                    EstimatedCostText: UsdCostText(usd, options.FxRate),
                    Sector: String(project, "Sector") ?? String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: null,
                    MunicipalityName: municipality,
                    ProponentName: proponentName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "Stage") ?? String(project, "stage"),
                    ProjectStatus: String(project, "Stage") ?? String(project, "stage"),
                    // Pass the raw stage from the research JSON so ProjectStageRouter
                    // can normalize it to a canonical lifecycle stage. Default to
                    // "Planned" when the research didn't surface one — this keeps US
                    // projects visible in the BD-actionable drain flow. The
                    // "USMarketResearch" pipeline tag is retained via the source-key
                    // prefix and provider name; it should NEVER be used as the
                    // lifecycle stage (router would classify it as a tag and leave
                    // canonical stage NULL — the exact bug this fix closes).
                    ProjectStage: String(project, "Stage") ?? String(project, "stage") ?? "Planned",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: null,
                    ScheduleNotes: null,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: String(project, "SourceUrl") ?? String(project, "sourceUrl"),
                    RawJson: project.GetRawText())
                {
                    Province = province,
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportAlbertaMarketAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var dir = Path.Combine(options.BaseDirectory, "KOR-Alberta-Market");
        const string enrichmentProviderName = "AlbertaMarketResearch";

        var firmsPath = Path.Combine(dir, "firms-payload.json");
        if (TryLoadJson(firmsPath, out var firmsDoc))
        {
            using (firmsDoc)
            {
                if (TrySelectEnvelopeOrLegacyItems(firmsDoc, "alberta-firms", "alberta", "firms", "firms", out var firmItems))
                {
                    foreach (var firm in firmItems.EnumerateArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        var firmName = String(firm, "firmName");
                        if (string.IsNullOrWhiteSpace(firmName))
                        {
                            stats.OrgRowsSkipped++;
                            continue;
                        }

                        var orgId = await UpsertOrgAsync(
                            orgStore,
                            options,
                            stats,
                            MapResearchFirmKind(String(firm, "kind")),
                            firmName,
                            String(firm, "website") ?? String(firm, "websiteUrl"),
                            String(firm, "researchNotes"),
                            enrichmentProviderName,
                            ct).ConfigureAwait(false);

                        await WriteEnrichmentAsync(
                            enrichmentStore,
                            options,
                            stats,
                            orgId,
                            enrichmentProviderName,
                            firm.GetRawText(),
                            null,
                            firmName,
                            ct).ConfigureAwait(false);
                    }
                }
            }
        }
        else
        {
            stats.FilesMissing++;
        }

        var projectsPath = Path.Combine(dir, "projects-payload.json");
        if (!TryLoadJson(projectsPath, out var projectsDoc))
        {
            stats.FilesMissing++;
            return;
        }

        using (projectsDoc)
        {
            var sourceKeysSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!TrySelectEnvelopeOrLegacyItems(projectsDoc, "alberta-projects", "alberta", "projects", "projects", out var projectItems))
            {
                return;
            }

            foreach (var project in projectItems.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "ProjectName") ?? String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var municipality = String(project, "Municipality") ?? String(project, "city");
                var sourceKey = "ABMKT-" + Sha1($"{projectName}|{municipality}");
                if (!sourceKeysSeen.Add(sourceKey))
                {
                    stats.SourceKeyCollisions++;
                    Console.WriteLine($"[WARN] AlbertaMarketProjects: duplicate SourceKey in this run ({sourceKey}); later row may overwrite earlier row. project={projectName}");
                }

                var proponentName = String(project, "ProponentName") ?? String(project, "owner");
                var architectName = String(project, "ArchitectName") ?? String(project, "architect");
                var proponentId = await ResolveAsync(resolver, options, stats, proponentName, OrgKinds.Unknown, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);

                var record = new MajorProjectRecord(
                    Source: "AlbertaMarketProjects",
                    SourceKey: sourceKey,
                    ProjectName: projectName,
                    ProjectDescription: String(project, "FullDescription"),
                    EstimatedCostCad: Money(project, "EstimatedCostCad"),
                    EstimatedCostText: CostText(project, "EstimatedCostCad"),
                    Sector: String(project, "Sector") ?? String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: null,
                    MunicipalityName: municipality,
                    ProponentName: proponentName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "Stage") ?? String(project, "stage"),
                    ProjectStatus: String(project, "Stage") ?? String(project, "stage"),
                    // Same fix as ImportUsMarketAsync — pass the raw research stage so
                    // ProjectStageRouter can normalize it. Default to "Planned".
                    // "AlbertaMarketResearch" was a pipeline-tag (not a lifecycle
                    // stage); using it as ProjectStage left canonical stage NULL.
                    ProjectStage: String(project, "Stage") ?? String(project, "stage") ?? "Planned",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: null,
                    ScheduleNotes: null,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: String(project, "SourceUrl") ?? String(project, "sourceUrl"),
                    RawJson: project.GetRawText())
                {
                    Province = "AB",
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportInstitutionalPipelineAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var dir = Path.Combine(options.BaseDirectory, "KOR-Institutional-Pipeline");

        var ownersPath = Path.Combine(dir, "owners-payload.json");
        if (TryLoadJson(ownersPath, out var ownersDoc))
        {
            using (ownersDoc)
            {
                if (TrySelectEnvelopeOrLegacyItems(ownersDoc, "institutional-owners", "institutional", "owners", "owners", out var ownerItems))
                {
                    foreach (var owner in ownerItems.EnumerateArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        var ownerName = String(owner, "ownerName");
                        if (string.IsNullOrWhiteSpace(ownerName))
                        {
                            stats.OrgRowsSkipped++;
                            continue;
                        }

                        var orgId = await UpsertOrgAsync(
                            orgStore,
                            options,
                            stats,
                            OrgKinds.Buyer,
                            ownerName,
                            String(owner, "publishedCapitalPlanUrl") ?? String(owner, "capitalPlanUrl"),
                            String(owner, "korRelevanceReason"),
                            "InstitutionalOwnerResearch",
                            ct).ConfigureAwait(false);

                        await WriteEnrichmentAsync(
                            enrichmentStore,
                            options,
                            stats,
                            orgId,
                            "InstitutionalOwnerResearch",
                            owner.GetRawText(),
                            null,
                            ownerName,
                            ct).ConfigureAwait(false);
                    }
                }
            }
        }
        else
        {
            stats.FilesMissing++;
        }

        var projectsPath = Path.Combine(dir, "projects-payload.json");
        if (!TryLoadJson(projectsPath, out var projectsDoc))
        {
            stats.FilesMissing++;
            return;
        }

        using (projectsDoc)
        {
            if (!TrySelectEnvelopeOrLegacyItems(projectsDoc, "institutional-projects", "institutional", "projects", "projects", out var projectItems))
            {
                return;
            }

            foreach (var project in projectItems.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "ProjectName") ?? String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(project, "OwnerName") ?? String(project, "owner");
                var architectName = String(project, "ArchitectName") ?? String(project, "architect");
                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var sourceCost = Decimal(project, "EstimatedCostCad");
                var currency = String(project, "Currency");
                var costCad = IsUsd(currency)
                    ? UsdToCad(sourceCost, options.FxRate)
                    : sourceCost.HasValue ? decimal.Round(sourceCost.Value, 0) : (decimal?)null;
                var costText = IsUsd(currency) ? UsdCostText(sourceCost, options.FxRate) : CostText(project, "EstimatedCostCad");
                var tenderYear = Short(project, "AnticipatedTenderYear");

                var record = new MajorProjectRecord(
                    Source: "InstitutionalPipelineProjects",
                    SourceKey: "INST-" + Sha1($"{ownerName}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: null,
                    EstimatedCostCad: costCad,
                    EstimatedCostText: costText,
                    Sector: String(project, "Sector") ?? String(project, "sector"),
                    SubSector: String(project, "SubSector") ?? String(project, "subSector"),
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: null,
                    MunicipalityName: String(project, "Municipality") ?? String(project, "city"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "Stage") ?? String(project, "stage"),
                    ProjectStatus: String(project, "Stage") ?? String(project, "stage"),
                    ProjectStage: "InstitutionalPipeline",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: tenderYear,
                    CompletionYear: tenderYear,
                    ScheduleNotes: null,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: String(project, "SourceUrl") ?? String(project, "sourceUrl"),
                    RawJson: project.GetRawText())
                {
                    Province = ProvinceFromMarket(String(project, "Province") ?? String(project, "province"), String(project, "Market") ?? String(project, "market")),
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportPrimeTargetingAsync(
        ImportOptions options,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Prime-Consultant-Strategy", "primes-payload.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "prime-targeting");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("prime-targeting: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object
                     && doc.RootElement.TryGetProperty("primes", out var legacyArr)
                     && legacyArr.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"prime-targeting: payload is legacy {{primes:[]}} shape (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = legacyArr;
                source = "legacy {primes:[]}";
            }
            else
            {
                Console.WriteLine(
                    $"prime-targeting: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"prime-targeting: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var prime in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var firmName = String(prime, "firmName");
                if (string.IsNullOrWhiteSpace(firmName))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await ResolveAsync(resolver, options, stats, firmName, OrgKinds.Architect, "PrimeTargeting", ct).ConfigureAwait(false);
                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "PrimeTargeting",
                    prime.GetRawText(),
                    null,
                    firmName,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportPrimeContactsAsync(
        ImportOptions options,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Prime-DecisionMakers", "people-payload.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "prime-contacts");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("prime-contacts: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Object
                     && doc.RootElement.TryGetProperty("people", out var legacyArr)
                     && legacyArr.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"prime-contacts: payload is legacy {{people:[]}} shape (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = legacyArr;
                source = "legacy {people:[]}";
            }
            else
            {
                Console.WriteLine(
                    $"prime-contacts: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"prime-contacts: ingesting {itemsArray.GetArrayLength()} person item(s) via {source}.");

            var peopleByFirm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var person in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var firmName = String(person, "firmName");
                if (string.IsNullOrWhiteSpace(firmName))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                if (!peopleByFirm.TryGetValue(firmName, out var people))
                {
                    people = new List<string>();
                    peopleByFirm[firmName] = people;
                }

                people.Add(person.GetRawText());
            }

            foreach (var (firmName, people) in peopleByFirm)
            {
                ct.ThrowIfCancellationRequested();
                var orgId = await ResolveAsync(resolver, options, stats, firmName, OrgKinds.Architect, "PrimeContacts", ct).ConfigureAwait(false);
                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "PrimeContacts",
                    "{\"people\":[" + string.Join(",", people) + "]}",
                    null,
                    firmName,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportIslandOkanaganAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var dir = Path.Combine(options.BaseDirectory, "KOR-Island-Okanagan-Ecosystem");

        var orgsPath = Path.Combine(dir, "orgs.json");
        if (TryLoadJson(orgsPath, out var orgsDoc))
        {
            using (orgsDoc)
            {
                if (TrySelectEnvelopeOrLegacyItems(orgsDoc, "island-okanagan-orgs", "island-okanagan", "orgs", null, out var orgItems))
                {
                    foreach (var org in orgItems.EnumerateArray())
                    {
                        ct.ThrowIfCancellationRequested();
                        var name = String(org, "name");
                        if (string.IsNullOrWhiteSpace(name))
                        {
                            stats.OrgRowsSkipped++;
                            continue;
                        }

                        var orgId = await UpsertOrgAsync(
                            orgStore,
                            options,
                            stats,
                            MapIslandKind(String(org, "kind")),
                            name,
                            String(org, "website"),
                            String(org, "korRelevance"),
                            "IslandOkanaganEcosystem",
                            ct).ConfigureAwait(false);

                        await WriteEnrichmentAsync(
                            enrichmentStore,
                            options,
                            stats,
                            orgId,
                            "IslandOkanaganEcosystem",
                            org.GetRawText(),
                            null,
                            name,
                            ct).ConfigureAwait(false);
                    }
                }
            }
        }
        else
        {
            stats.FilesMissing++;
        }

        var projectsPath = Path.Combine(dir, "projects.json");
        if (!TryLoadJson(projectsPath, out var projectsDoc))
        {
            stats.FilesMissing++;
            return;
        }

        using (projectsDoc)
        {
            if (!TrySelectEnvelopeOrLegacyItems(projectsDoc, "island-okanagan-projects", "island-okanagan", "projects", null, out var projectItems))
            {
                return;
            }

            foreach (var project in projectItems.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "name");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(project, "owner");
                var architectName = String(project, "architect");
                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, LeadFirm(architectName), OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var cost = Money(project, "estimatedValue");
                var gc = String(project, "generalContractor");

                var record = new MajorProjectRecord(
                    Source: "IslandOkanaganProjects",
                    SourceKey: "IOECO-" + Sha1($"{ownerName}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: String(project, "description"),
                    EstimatedCostCad: cost,
                    EstimatedCostText: cost.HasValue ? CadCostText(cost.Value) : null,
                    Sector: String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(project, "region"),
                    MunicipalityName: String(project, "city"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "stage"),
                    ProjectStatus: String(project, "stage"),
                    ProjectStage: "IslandOkanaganEcosystem",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: Short(project, "expectedYear"),
                    ScheduleNotes: string.IsNullOrWhiteSpace(gc) ? null : "GC: " + gc,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(project),
                    RawJson: project.GetRawText())
                {
                    Province = ProvinceFromMarket(String(project, "province"), String(project, "region")),
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static string MapIslandKind(string? kind) => kind?.Trim().ToLowerInvariant() switch
    {
        "architect" => OrgKinds.Architect,
        "developer" => OrgKinds.Developer,
        "gc" => OrgKinds.GeneralContractor,
        "competitor" => OrgKinds.Competitor,
        "buyer" => OrgKinds.Buyer,
        _ => OrgKinds.Unknown,
    };

    // The research lists teams like "Parkin Architects (prime) + ZGF Architects".
    // Resolve the lead firm only (text before the first '(', '+', or ';').
    private static string? LeadFirm(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cut = value.IndexOfAny(new[] { '(', '+', ';' });
        var lead = (cut >= 0 ? value[..cut] : value).Trim().TrimEnd(',').Trim();
        return string.IsNullOrWhiteSpace(lead) ? null : lead;
    }

    private static string CadCostText(decimal v)
        => v >= 1_000_000_000m ? "$" + (v / 1_000_000_000m).ToString("0.##", CultureInfo.InvariantCulture) + "B"
         : v >= 1_000_000m ? "$" + (v / 1_000_000m).ToString("0.#", CultureInfo.InvariantCulture) + "M"
         : "$" + v.ToString("N0", CultureInfo.InvariantCulture);

    private static string? FirstSourceUrl(JsonElement element)
    {
        if (element.TryGetProperty("sourceUrls", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var u in arr.EnumerateArray())
            {
                if (u.ValueKind == JsonValueKind.String)
                {
                    var s = u.GetString();
                    if (!string.IsNullOrWhiteSpace(s))
                    {
                        return s.Trim();
                    }
                }
            }
        }

        return null;
    }

    private static bool TrySelectEnvelopeOrLegacyItems(
        JsonDocument doc,
        string expectedKind,
        string logPrefix,
        string fileLabel,
        string? legacyArrayProperty,
        out JsonElement items)
    {
        var validation = ResearchEnvelopeValidator.Validate(doc, expectedKind);
        string source;
        if (validation.IsValid && validation.Envelope is { } env)
        {
            if (env.Items.ValueKind != JsonValueKind.Array)
            {
                Console.WriteLine($"{logPrefix}: {fileLabel} envelope items is not an array; skipping that file.");
                items = default;
                return false;
            }

            items = env.Items;
            source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
        }
        else if (legacyArrayProperty is null && doc.RootElement.ValueKind == JsonValueKind.Array)
        {
            Console.WriteLine(
                $"{logPrefix}: {fileLabel} payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
            items = doc.RootElement;
            source = "legacy flat-array";
        }
        else if (legacyArrayProperty is not null
                 && doc.RootElement.ValueKind == JsonValueKind.Object
                 && doc.RootElement.TryGetProperty(legacyArrayProperty, out var legacyArr)
                 && legacyArr.ValueKind == JsonValueKind.Array)
        {
            Console.WriteLine(
                $"{logPrefix}: {fileLabel} payload is legacy {{{legacyArrayProperty}:[]}} shape (no envelope: {validation.Reason}); ingesting via legacy path.");
            items = legacyArr;
            source = $"legacy {{{legacyArrayProperty}:[]}}";
        }
        else
        {
            Console.WriteLine(
                $"{logPrefix}: {fileLabel} has neither envelope nor legacy shape ({validation.Reason}); skipping that file.");
            items = default;
            return false;
        }

        Console.WriteLine($"{logPrefix}: {fileLabel} ingesting {items.GetArrayLength()} item(s) via {source}.");
        return true;
    }

    private static async Task ImportIntelGatheringAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        // Track A: team-pairing graph (architect + structural engineer + GC + owner
        // per recent award). The structural-engineer-per-project edge is the
        // highest-value datapoint — stored in the new MPI structural/GC columns.
        var teamPath = Path.Combine(options.BaseDirectory, "KOR-Intel-Gathering", "outputs", "team-awards.json");
        if (!TryLoadJson(teamPath, out var teamDoc))
        {
            stats.FilesMissing++;
            return;
        }

        using (teamDoc)
        {
            if (!TrySelectEnvelopeOrLegacyItems(teamDoc, "intel-gathering-team-awards", "intel-gathering", "team-awards", null, out var teamItems))
            {
                return;
            }

            foreach (var t in teamItems.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(t, "project") ?? String(t, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(t, "owner");
                var architectName = String(t, "architect");
                var structuralName = String(t, "structuralEngineer");
                var gcName = String(t, "generalContractor");

                // Prefer the seed Id the research carried through; else resolve by name.
                var architectId = LongOrNull(t, "architectSeedId")
                    ?? await ResolveAsync(resolver, options, stats, LeadFirm(architectName), OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var structuralId = await ResolveAsync(resolver, options, stats, LeadFirm(structuralName), OrgKinds.Competitor, "IntelTeamStructural", ct).ConfigureAwait(false);
                var gcId = await ResolveAsync(resolver, options, stats, LeadFirm(gcName), OrgKinds.GeneralContractor, "IntelTeamGC", ct).ConfigureAwait(false);
                var cost = Money(t, "estimatedValue");

                var record = new MajorProjectRecord(
                    Source: "IntelTeamAwards",
                    SourceKey: "TEAM-" + Sha1($"{architectName}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: null,
                    EstimatedCostCad: cost,
                    EstimatedCostText: cost.HasValue ? CadCostText(cost.Value) : null,
                    Sector: String(t, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: null,
                    MunicipalityName: String(t, "city"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: "Complete",
                    ProjectStatus: "Awarded",
                    ProjectStage: "IntelTeamAwards",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: Short(t, "awardYear"),
                    CompletionYear: null,
                    ScheduleNotes: null,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(t),
                    RawJson: t.GetRawText())
                {
                    Province = ProvinceFromMarket(String(t, "province"), String(t, "city")),
                    StructuralEngineerName = structuralName,
                    StructuralEngineerCanonicalOrgId = structuralId,
                    GeneralContractorName = gcName,
                    GeneralContractorCanonicalOrgId = gcId,
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static long? LongOrNull(JsonElement element, string propertyName)
    {
        var text = String(element, propertyName);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : (long?)null;
    }

    private static async Task<long?> ResolveKorStructuralOrgAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        CancellationToken ct)
    {
        if (options.DryRun)
        {
            if (!options.Quiet)
            {
                Console.WriteLine("[DRY-RUN] KorCapability: planned lookup CanonicalOrg kind=KorStructural/name=KOR Structural");
            }

            return null;
        }

        if (orgStore is null)
        {
            throw new InvalidOperationException("Canonical org store is not available.");
        }

        var byKind = await orgStore.SearchCanonicalOrgsAsync(null, OrgKinds.KorStructural, 10, ct).ConfigureAwait(false);
        var row = byKind.FirstOrDefault()
            ?? (await orgStore.SearchCanonicalOrgsAsync("KOR Structural", null, 10, ct).ConfigureAwait(false))
                .FirstOrDefault(o => string.Equals(o.DisplayName, "KOR Structural", StringComparison.OrdinalIgnoreCase));

        if (row is null)
        {
            throw new InvalidOperationException("Could not find the KOR Structural canonical org row.");
        }

        if (!options.Quiet)
        {
            Console.WriteLine($"[ORG] KorCapability: KOR Structural -> CanonicalOrgId={row.Id}");
        }

        return row.Id;
    }

    private static string[] StringArray(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .ToArray();
    }

    private static string? JoinArray(JsonElement element, string propertyName)
    {
        var values = StringArray(element, propertyName);
        return values.Length == 0 ? null : string.Join("; ", values);
    }

    private static string? StringOrJoinedArray(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            return JoinArray(element, propertyName);
        }

        return String(element, propertyName);
    }

    private static DateOnly? DateOnlyOrNull(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;
    }

    private static JsonElement? RawJsonOrNull(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value;
    }

    private static int CountName(string? name)
        => string.IsNullOrWhiteSpace(name) ? 0 : 1;

    private static string? CleanTeamName(string? name)
    {
        var trimmed = NullIfBlank(name);
        return string.Equals(trimmed, "unknown", StringComparison.OrdinalIgnoreCase) ? null : trimmed;
    }

    private static bool IsUnknownName(string? name)
    {
        var trimmed = NullIfBlank(name);
        return trimmed is null || trimmed.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsConfirmedOrProbable(string? confidence)
    {
        var trimmed = confidence?.Trim();
        return string.Equals(trimmed, "confirmed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "probable", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRetiringProjectVerdict(string? statusVerdict)
    {
        var trimmed = statusVerdict?.Trim();
        return string.Equals(trimmed, "built-complete", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "cancelled", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "not-found", StringComparison.OrdinalIgnoreCase)
            || string.Equals(trimmed, "under-construction", StringComparison.OrdinalIgnoreCase);
    }

    private static void AddSectorSystems(
        Dictionary<string, Dictionary<string, int>> sectorSystemMatrix,
        string? sector,
        IReadOnlyList<string> systems)
    {
        var sectorKey = string.IsNullOrWhiteSpace(sector) ? "Unknown" : sector.Trim();
        if (!sectorSystemMatrix.TryGetValue(sectorKey, out var systemCounts))
        {
            systemCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            sectorSystemMatrix[sectorKey] = systemCounts;
        }

        foreach (var system in systems)
        {
            if (string.IsNullOrWhiteSpace(system))
            {
                continue;
            }

            var key = system.Trim();
            systemCounts.TryGetValue(key, out var current);
            systemCounts[key] = current + 1;
        }
    }

    private static async Task ImportDataHoningAsync(
        ImportOptions options,
        SqlEnrichmentTrackingStore? enrichmentStore,
        ImportStats stats,
        CancellationToken ct)
    {
        // Applies KOR-Data-Honing orgs-enriched.json: fills missing websites and
        // writes the rich enrichment (sectors, key people, notable projects,
        // structural-partner intel, geography corrections, JV/identity dataIssues)
        // keyed by Id. Skips merged-away duplicate-losers. Re-runnable.
        var path = Path.Combine(options.BaseDirectory, "KOR-Data-Honing", "outputs", "orgs-enriched.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "data-honing");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("data-honing: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"data-honing: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"data-honing: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"data-honing: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            var validIds = options.DryRun
                ? null
                : await LoadValidOrgIdsAsync(options.OpportunitiesDb, ct).ConfigureAwait(false);

            foreach (var org in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var id = LongOrNull(org, "id");
                if (id is null)
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                // Skip records whose canonical row was merged away (a dup-loser).
                if (validIds is not null && !validIds.Contains(id.Value))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var displayName = String(org, "displayName") ?? id.Value.ToString(CultureInfo.InvariantCulture);
                var website = String(org, "website");
                if (!options.DryRun && !string.IsNullOrWhiteSpace(website))
                {
                    await UpdateOrgWebsiteAsync(options.OpportunitiesDb, id.Value, website!, ct).ConfigureAwait(false);
                }

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    id,
                    "DataHoning",
                    org.GetRawText(),
                    null,
                    displayName,
                    ct).ConfigureAwait(false);

                var targetKind = DataHoningTargetKind(org);
                if (!options.DryRun && validIds is not null && validIds.Contains(id.Value) && targetKind is not null)
                {
                    var changed = await UpdateOrgKindAsync(options.OpportunitiesDb, id.Value, targetKind, ct).ConfigureAwait(false);
                    if (changed)
                    {
                        stats.OrgsReclassified++;
                        if (string.Equals(targetKind, OrgKinds.Unknown, StringComparison.OrdinalIgnoreCase))
                        {
                            stats.OrgsHidden++;
                        }
                    }
                }
            }
        }
    }

    private static string? DataHoningTargetKind(JsonElement org)
    {
        var dataIssues = StringArray(org, "dataIssues");
        if (dataIssues.Any(i =>
            string.Equals(i, "placeholder-name", StringComparison.OrdinalIgnoreCase)
            || string.Equals(i, "defunct", StringComparison.OrdinalIgnoreCase)))
        {
            return OrgKinds.Unknown;
        }

        var suggestedKind = String(org, "suggestedKind")?.Trim();
        if (string.IsNullOrWhiteSpace(suggestedKind))
        {
            return null;
        }

        foreach (var kind in DataHoningRecognizedKinds)
        {
            if (string.Equals(suggestedKind, kind, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return String(org, "sourceUrl");
    }

    private static async Task<HashSet<long>> LoadValidOrgIdsAsync(string db, CancellationToken ct)
    {
        var ids = new HashSet<long>();
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand("SELECT Id FROM opportunities.CanonicalOrg;", con) { CommandTimeout = 60 };
        await using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        while (await r.ReadAsync(ct).ConfigureAwait(false))
        {
            ids.Add(r.GetInt64(0));
        }

        return ids;
    }

    private static async Task UpdateOrgWebsiteAsync(string db, long id, string website, CancellationToken ct)
    {
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "UPDATE opportunities.CanonicalOrg SET Website = COALESCE(Website, @w), UpdatedAtUtc = sysdatetimeoffset() WHERE Id = @id;",
            con);
        cmd.Parameters.Add("@w", SqlDbType.NVarChar, 500).Value = website;
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static async Task<bool> UpdateOrgKindAsync(string db, long id, string kind, CancellationToken ct)
    {
        await using var con = new SqlConnection(db);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(
            "UPDATE opportunities.CanonicalOrg SET Kind = @kind, UpdatedAtUtc = sysdatetimeoffset() WHERE Id = @id AND Kind <> @kind;",
            con);
        cmd.Parameters.Add("@kind", SqlDbType.NVarChar, 40).Value = kind;
        cmd.Parameters.Add("@id", SqlDbType.BigInt).Value = id;
        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static async Task ImportOwnerPipelinesAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        // Intel-Gathering Track B: institutional owner capital pipelines (projects
        // 1-3 yrs ahead of the RFP) -> MajorProjectsInventory.
        var path = !string.IsNullOrWhiteSpace(options.PipelinesFile)
            ? options.PipelinesFile!
            : Path.Combine(options.BaseDirectory, "KOR-Intel-Gathering", "outputs", "owner-pipelines.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "owner-pipelines");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("owner-pipelines: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"owner-pipelines: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"owner-pipelines: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"owner-pipelines: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var p in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(p, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(p, "owner");
                var architectName = String(p, "plannedArchitect");
                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, LeadFirm(architectName), OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var cost = Money(p, "estimatedValue");

                var record = new MajorProjectRecord(
                    Source: "OwnerPipelineProjects",
                    SourceKey: "OWNPIPE-" + Sha1($"{ownerName}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: String(p, "description"),
                    EstimatedCostCad: cost,
                    EstimatedCostText: cost.HasValue ? CadCostText(cost.Value) : null,
                    Sector: String(p, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: null,
                    MunicipalityName: String(p, "city"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(p, "stage"),
                    ProjectStatus: String(p, "stage"),
                    ProjectStage: "OwnerPipeline",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: Short(p, "expectedYear"),
                    ScheduleNotes: null,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(p),
                    RawJson: p.GetRawText())
                {
                    Province = ProvinceFromMarket(String(p, "province"), String(p, "city")),
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportCompetitorProfilesAsync(
        ImportOptions options,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        // Intel-Gathering Track D: structural-competitor deep-profiles (go-to
        // architect partners, recent wins, strongholds, exploitable gaps).
        var path = Path.Combine(options.BaseDirectory, "KOR-Intel-Gathering", "outputs", "competitor-profiles.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            // Try envelope first (new path).
            var validation = ResearchEnvelopeValidator.Validate(doc, "competitor-profiles");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine(
                        "competitor-profiles: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                // Legacy fallback. Log so we can track when legacy callers
                // stop coming in (and eventually delete the fallback).
                Console.WriteLine(
                    $"competitor-profiles: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"competitor-profiles: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"competitor-profiles: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var c in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var name = String(c, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var id = LongOrNull(c, "id");
                var orgId = id is > 0
                    ? id
                    : await ResolveAsync(resolver, options, stats, name, OrgKinds.Competitor, "CompetitorProfile", ct).ConfigureAwait(false);

                await WriteEnrichmentAsync(enrichmentStore, options, stats, orgId, "CompetitorProfile", c.GetRawText(), null, name, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportDecisionMakersAsync(
        ImportOptions options,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        // Intel-Gathering Track C: decision-maker / people layer, grouped per org
        // into one enrichment row (mirrors PrimeContacts).
        var path = Path.Combine(options.BaseDirectory, "KOR-Intel-Gathering", "outputs", "people.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "decision-makers");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine(
                        "decision-makers: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"decision-makers: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"decision-makers: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"decision-makers: ingesting {itemsArray.GetArrayLength()} person item(s) via {source}.");

            var groups = new Dictionary<string, (long? OrgId, string? Name, string Kind, List<string> People)>(StringComparer.OrdinalIgnoreCase);
            foreach (var person in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var orgName = String(person, "orgName");
                var orgId = LongOrNull(person, "orgId");
                var key = orgId is > 0 ? "id:" + orgId.Value.ToString(CultureInfo.InvariantCulture) : orgName;
                if (string.IsNullOrWhiteSpace(key))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                if (!groups.TryGetValue(key, out var g))
                {
                    g = (orgId, orgName, MapIslandKind(String(person, "orgKind")), new List<string>());
                    groups[key] = g;
                }

                g.People.Add(person.GetRawText());
            }

            foreach (var g in groups.Values)
            {
                ct.ThrowIfCancellationRequested();
                var orgId = g.OrgId is > 0
                    ? g.OrgId
                    : await ResolveAsync(resolver, options, stats, g.Name, g.Kind, "DecisionMakers", ct).ConfigureAwait(false);

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "DecisionMakers",
                    "{\"people\":[" + string.Join(",", g.People) + "]}",
                    null,
                    g.Name ?? "(org)",
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportRegistriesAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Registry-Graph", "outputs", "firms.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "registries");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("registries: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"registries: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"registries: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"registries: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var firm in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var name = String(firm, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await UpsertOrgAsync(
                    orgStore,
                    options,
                    stats,
                    MapIslandKind(String(firm, "kind")),
                    name,
                    String(firm, "website"),
                    notes: null,
                    source: "Registries",
                    ct).ConfigureAwait(false);

                var principals = new List<string>();
                if (firm.TryGetProperty("principals", out var principalsArray) && principalsArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var principal in principalsArray.EnumerateArray())
                    {
                        principals.Add(principal.GetRawText());
                    }
                }

                if (principals.Count == 0)
                {
                    continue;
                }

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "Registry",
                    "{\"people\":[" + string.Join(",", principals) + "]}",
                    null,
                    name,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportOwnerProcurementAsync(
        ImportOptions options,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Owner-Procurement", "outputs", "owner-procurement.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "owner-procurement");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("owner-procurement: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"owner-procurement: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"owner-procurement: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"owner-procurement: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var element in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var orgName = String(element, "orgName") ?? String(element, "ownerName");
                if (string.IsNullOrWhiteSpace(orgName))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await ResolveAsync(resolver, options, stats, orgName, "Buyer", "OwnerProcurement", ct).ConfigureAwait(false);
                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "ProcurementProfile",
                    element.GetRawText(),
                    null,
                    orgName,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportCompetitorSignalsAsync(
        ImportOptions options,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Competitor-Signals", "outputs", "competitor-signals.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "competitor-signals");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("competitor-signals: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"competitor-signals: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"competitor-signals: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"competitor-signals: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var element in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var orgName = String(element, "orgName");
                if (string.IsNullOrWhiteSpace(orgName))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await ResolveAsync(resolver, options, stats, orgName, "Competitor", "CompetitorSignals", ct).ConfigureAwait(false);
                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "CompetitorSignals",
                    element.GetRawText(),
                    null,
                    orgName,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportStructuralPartnerMapAsync(
        ImportOptions options,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Structural-Partner-Map", "outputs", "structural-partner-map.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "structural-partner-map");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("structural-partner-map: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"structural-partner-map: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"structural-partner-map: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"structural-partner-map: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var element in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var orgName = String(element, "orgName");
                if (string.IsNullOrWhiteSpace(orgName))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await ResolveAsync(resolver, options, stats, orgName, "Architect", "StructuralPartnerMap", ct).ConfigureAwait(false);
                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "StructuralPartnerMap",
                    element.GetRawText(),
                    null,
                    orgName,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportDisplacementBriefsAsync(
        ImportOptions options,
        SqlArchitectDisplacementBriefStore? briefStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Structural-Partner-Map", "outputs", "displacement-briefs.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "displacement-briefs");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("displacement-briefs: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"displacement-briefs: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"displacement-briefs: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"displacement-briefs: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            var written = 0;
            var skippedNoArchitect = 0;
            var skippedLowConfidence = 0;

            foreach (var element in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();

                // architectId is the primary key. The Sonnet brief writes it
                // verbatim from structural-partner-map.json; we trust it but
                // verify the canonical row still exists. If it's been merged
                // into a survivor, fall back to resolving by architectName so
                // a merge does not silently drop the brief.
                var architectId = LongOrNull(element, "architectId");
                var architectName = String(element, "architectName");

                if (!architectId.HasValue && string.IsNullOrWhiteSpace(architectName))
                {
                    skippedNoArchitect++;
                    continue;
                }

                if (!architectId.HasValue && !string.IsNullOrWhiteSpace(architectName))
                {
                    architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, "DisplacementBrief", ct).ConfigureAwait(false);
                }

                if (!architectId.HasValue)
                {
                    skippedNoArchitect++;
                    continue;
                }

                // Hard rule from the Sonnet prompt: confidenceScore < 0.3
                // briefs are written as "skipped" entries. Defensive guard
                // here in case any slipped through.
                var confidence = Decimal(element, "confidenceScore");
                if (confidence.HasValue && confidence.Value < 0.30m)
                {
                    skippedLowConfidence++;
                    continue;
                }

                var market = String(element, "market");
                var korPriority = String(element, "korPriority");
                var generatedAt = DateTimeOffset.UtcNow;
                if (element.TryGetProperty("_meta", out var metaEl) && metaEl.ValueKind == JsonValueKind.Object)
                {
                    var raw = String(metaEl, "generatedAt");
                    if (!string.IsNullOrWhiteSpace(raw)
                        && DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var parsed))
                    {
                        generatedAt = parsed;
                    }
                }

                if (options.DryRun)
                {
                    if (!options.Quiet)
                    {
                        Console.WriteLine($"[DRY-RUN] DisplacementBrief: architectId={architectId} priority={korPriority} confidence={confidence}");
                    }
                    written++;
                    continue;
                }

                if (briefStore is null)
                {
                    throw new InvalidOperationException("Displacement brief store is not available.");
                }

                await briefStore.UpsertAsync(
                    architectCanonicalOrgId: architectId.Value,
                    market: market,
                    korPriority: korPriority,
                    confidenceScore: confidence,
                    briefJson: element.GetRawText(),
                    generatedAtUtc: generatedAt,
                    ct: ct).ConfigureAwait(false);

                written++;
                if (!options.Quiet)
                {
                    Console.WriteLine($"[WRITE ] DisplacementBrief: architectId={architectId} priority={korPriority} confidence={confidence}");
                }
            }

            Console.WriteLine($"Displacement briefs: written={written}; skippedNoArchitect={skippedNoArchitect}; skippedLowConfidence={skippedLowConfidence}");
        }
    }

    private static async Task ImportSubConsultantsAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-SubConsultants", "outputs", "subconsultant-firms.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "sub-consultants");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("sub-consultants: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"sub-consultants: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"sub-consultants: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"sub-consultants: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var element in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var name = String(element, "name") ?? String(element, "firmName");
                if (string.IsNullOrWhiteSpace(name))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await UpsertOrgAsync(
                    orgStore,
                    options,
                    stats,
                    "Subcontractor",
                    name,
                    String(element, "website"),
                    $"Discipline: {String(element, "discipline")}",
                    "SubConsultants",
                    ct).ConfigureAwait(false);

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "SubConsultant",
                    element.GetRawText(),
                    null,
                    name,
                    ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportFacilityRenewalAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Facility-Renewal", "outputs", "renewal-pipeline.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "facility-renewal");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("facility-renewal: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"facility-renewal: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"facility-renewal: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"facility-renewal: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var element in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var facilityName = String(element, "facilityName") ?? String(element, "projectName");
                if (string.IsNullOrWhiteSpace(facilityName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(element, "owner");
                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var record = new MajorProjectRecord(
                    Source: "FacilityRenewal",
                    SourceKey: "RENEWAL-" + Sha1($"{ownerName}|{facilityName}"),
                    ProjectName: facilityName,
                    ProjectDescription: null,
                    EstimatedCostCad: Money(element, "estCostCad"),
                    EstimatedCostText: CostText(element, "estCostCad"),
                    Sector: String(element, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(element, "market"),
                    MunicipalityName: String(element, "city"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: null,
                    ArchitectCanonicalOrgId: null,
                    Stage: "identified",
                    ProjectStatus: "identified",
                    ProjectStage: "FacilityRenewal",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: null,
                    ScheduleNotes: JoinNotes(("Condition", String(element, "conditionSignal")), ("Need", String(element, "renewalNeed")), ("Timeline", String(element, "estTimeline"))),
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(element),
                    RawJson: element.GetRawText())
                {
                    Province = ProvinceFromResearchMarket(String(element, "market")),
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportCapitalPlansAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Capital-Plans", "outputs", "capital-pipeline.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "capital-plans");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("capital-plans: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"capital-plans: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"capital-plans: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"capital-plans: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            var sourceKeysSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(project, "owner");
                var sourceKey = "CAPPLAN-" + Slug(ownerName) + "-" + Slug(projectName);
                if (!sourceKeysSeen.Add(sourceKey))
                {
                    stats.SourceKeyCollisions++;
                    Console.WriteLine($"[WARN] CapitalPlans: duplicate SourceKey in this run ({sourceKey}); later row may overwrite earlier row. project={projectName}");
                }

                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var record = new MajorProjectRecord(
                    Source: "CapitalPlans",
                    SourceKey: sourceKey,
                    ProjectName: projectName,
                    ProjectDescription: String(project, "notes"),
                    EstimatedCostCad: Money(project, "budgetCad"),
                    EstimatedCostText: CostText(project, "budgetCad"),
                    Sector: String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(project, "market"),
                    MunicipalityName: null,
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: null,
                    ArchitectCanonicalOrgId: null,
                    Stage: String(project, "status"),
                    ProjectStatus: String(project, "status"),
                    ProjectStage: "CapitalPlan",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: null,
                    ScheduleNotes: JoinNotes(("Timeline", String(project, "timeline")), ("Notes", String(project, "notes"))),
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: String(project, "sourceUrl"),
                    RawJson: project.GetRawText())
                {
                    // m123 root-cause fix: capital plans span BC AND AB owners
                    // (Alberta Infrastructure etc.) — the file-level BC constant
                    // mislabeled 38 Alberta rows. Derive from the market field.
                    Province = ProvinceFromResearchMarket(String(project, "market")),
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportProjectsHoningAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Data-Honing", "outputs", "projects-enriched.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "projects-honing");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("projects-honing: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"projects-honing: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"projects-honing: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"projects-honing: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var id = LongOrNull(project, "id");
                if (id is not > 0)
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var projectName = String(project, "projectName") ?? String(project, "ProjectName");
                var architectName = String(project, "architectName") ?? String(project, "ArchitectName");
                var structuralName = String(project, "structuralEngineerName") ?? String(project, "StructuralEngineerName");
                var gcName = String(project, "generalContractorName") ?? String(project, "GeneralContractorName");
                var proponentName = String(project, "proponentName") ?? String(project, "ProponentName");

                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, "ProjectsHoning", ct).ConfigureAwait(false);
                var structuralId = await ResolveAsync(resolver, options, stats, structuralName, OrgKinds.Competitor, "ProjectsHoning", ct).ConfigureAwait(false);
                var gcId = await ResolveAsync(resolver, options, stats, gcName, OrgKinds.GeneralContractor, "ProjectsHoning", ct).ConfigureAwait(false);
                var proponentId = await ResolveAsync(resolver, options, stats, proponentName, OrgKinds.Buyer, "ProjectsHoning", ct).ConfigureAwait(false);

                var updated = await UpdateMajorProjectFromHoningAsync(
                    options,
                    id.Value,
                    architectName,
                    architectId,
                    structuralName,
                    structuralId,
                    gcName,
                    gcId,
                    proponentName,
                    proponentId,
                    String(project, "stage") ?? String(project, "Stage"),
                    Short(project, "completionYear") ?? Short(project, "CompletionYear"),
                    ct).ConfigureAwait(false);

                if (updated)
                {
                    IncrementProjectSource(stats, "ProjectsHoning");
                    if (!options.Quiet)
                    {
                        Console.WriteLine(options.DryRun
                            ? $"[DRY-RUN] ProjectsHoning: planned MPI update Id={id.Value}; project={projectName ?? "(unnamed)"}"
                            : $"[MPI] ProjectsHoning: updated Id={id.Value}; project={projectName ?? "(unnamed)"}");
                    }
                }
                else
                {
                    stats.ProjectRowsSkipped++;
                    if (!options.Quiet)
                    {
                        Console.WriteLine($"[WARN] ProjectsHoning: skipped missing MPI Id={id.Value}; project={projectName ?? "(unnamed)"}");
                    }
                }
            }
        }
    }

    private static async Task ImportMidMarketAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-MidMarket-Pipeline", "outputs", "midmarket-pipeline.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "midmarket");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("midmarket: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"midmarket: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"midmarket: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"midmarket: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "name") ?? String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(project, "owner");
                var architectName = String(project, "architect");
                var structuralName = String(project, "structuralEngineer");
                var municipality = String(project, "municipality") ?? String(project, "city");
                var ownerId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var structuralId = await ResolveAsync(resolver, options, stats, structuralName, OrgKinds.Competitor, "MidMarketStructural", ct).ConfigureAwait(false);

                var record = new MajorProjectRecord(
                    Source: "MidMarketPipeline",
                    SourceKey: Sha1($"{projectName}|{municipality}"),
                    ProjectName: projectName,
                    ProjectDescription: String(project, "notableScope"),
                    EstimatedCostCad: LongOrNull(project, "estValueCad"),
                    EstimatedCostText: CostText(project, "estValueCad"),
                    Sector: String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(project, "market"),
                    MunicipalityName: municipality,
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: ownerId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "stage"),
                    ProjectStatus: String(project, "stage"),
                    ProjectStage: String(project, "stage"),
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: null,
                    ScheduleNotes: null,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(project),
                    RawJson: project.GetRawText())
                {
                    Province = NormalizeProvince(String(project, "market"), Province),
                    StructuralEngineerName = structuralName,
                    StructuralEngineerCanonicalOrgId = structuralId,
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportArchitectForecastAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Architect-Forecast", "outputs", "architect-forecast.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "architect-forecast");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("architect-forecast: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"architect-forecast: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"architect-forecast: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"architect-forecast: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            var stamped = 0;
            var skipped = 0;
            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var id = LongOrNull(project, "id");
                var architectName = String(project, "likelyArchitect") ?? String(project, "architectName");
                var confidence = String(project, "architectConfidence");
                if (id is not > 0 || !IsConfirmedOrProbable(confidence) || IsUnknownName(architectName))
                {
                    skipped++;
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var updated = await UpdateMajorProjectArchitectForecastAsync(options, id.Value, architectName, architectId, ct).ConfigureAwait(false);
                if (updated)
                {
                    stamped++;
                    IncrementProjectSource(stats, "ArchitectForecast");
                    if (!options.Quiet)
                    {
                        Console.WriteLine(options.DryRun
                            ? $"[DRY-RUN] ArchitectForecast: planned MPI architect update Id={id.Value}; project={String(project, "projectName") ?? "(unnamed)"}"
                            : $"[MPI] ArchitectForecast: updated Id={id.Value}; project={String(project, "projectName") ?? "(unnamed)"}");
                    }
                }
                else
                {
                    skipped++;
                    stats.ProjectRowsSkipped++;
                    if (!options.Quiet)
                    {
                        Console.WriteLine($"[WARN] ArchitectForecast: skipped MPI Id={id.Value}; architect already set or row missing.");
                    }
                }
            }

            Console.WriteLine($"[architect-forecast] stamped={stamped}; skipped={skipped}");
        }
    }

    private static async Task ImportPipelineSeatsAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Pipeline-Seats", "outputs", "project-seats.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "pipeline-seats");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("pipeline-seats: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"pipeline-seats: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"pipeline-seats: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"pipeline-seats: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            var seatStatusCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var orgsResolved = 0;
            var projectsStamped = 0;
            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var id = LongOrNull(project, "id");
                if (id is not > 0)
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var projectName = String(project, "projectName");
                var architectName = CleanTeamName(String(project, "architect"));
                var structuralName = CleanTeamName(String(project, "structuralEngineer"));
                var gcName = CleanTeamName(String(project, "generalContractor"));
                var seatStatus = String(project, "seatStatus") ?? String(project, "structuralSeatStatus");
                AddCount(seatStatusCounts, string.IsNullOrWhiteSpace(seatStatus) ? "(blank)" : seatStatus.Trim());

                orgsResolved += CountName(architectName) + CountName(structuralName) + CountName(gcName);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var structuralId = await ResolveAsync(resolver, options, stats, structuralName, OrgKinds.Competitor, "PipelineSeatsStructural", ct).ConfigureAwait(false);
                var gcId = await ResolveAsync(resolver, options, stats, gcName, OrgKinds.GeneralContractor, "PipelineSeatsGC", ct).ConfigureAwait(false);

                var updated = await UpdateMajorProjectSeatAsync(
                    options,
                    id.Value,
                    architectName,
                    architectId,
                    structuralName,
                    structuralId,
                    gcName,
                    gcId,
                    seatStatus,
                    String(project, "korOpening"),
                    String(project, "confidence"),
                    ct).ConfigureAwait(false);

                if (updated)
                {
                    projectsStamped++;
                    IncrementProjectSource(stats, "PipelineSeats");
                    if (!options.Quiet)
                    {
                        Console.WriteLine(options.DryRun
                            ? $"[DRY-RUN] PipelineSeats: planned MPI seat update Id={id.Value}; project={projectName ?? "(unnamed)"}"
                            : $"[MPI] PipelineSeats: updated Id={id.Value}; project={projectName ?? "(unnamed)"}");
                    }
                }
                else
                {
                    stats.ProjectRowsSkipped++;
                    if (!options.Quiet)
                    {
                        Console.WriteLine($"[WARN] PipelineSeats: skipped missing MPI Id={id.Value}; project={projectName ?? "(unnamed)"}");
                    }
                }
            }

            Console.WriteLine($"[pipeline-seats] projects stamped={projectsStamped}; orgs resolved={orgsResolved}");
            foreach (var (status, count) in seatStatusCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[pipeline-seats] seatStatus {status}: {count}");
            }
        }
    }

    private static async Task ImportProjectReverifyAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Project-Reverify", "outputs", "project-status.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "project-reverify");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("project-reverify: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"project-reverify: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"project-reverify: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"project-reverify: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            var verdictCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var updated = 0;
            var retired = 0;
            var kept = 0;
            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var id = LongOrNull(project, "id");
                if (id is not > 0)
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var verdict = String(project, "statusVerdict") ?? String(project, "verdict");
                var verdictKey = string.IsNullOrWhiteSpace(verdict) ? "(blank)" : verdict.Trim();
                AddCount(verdictCounts, verdictKey);

                var architectName = CleanTeamName(String(project, "architectUpdate"));
                var structuralName = CleanTeamName(String(project, "structuralUpdate"));
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, "ProjectReverifyArchitect", ct).ConfigureAwait(false);
                var structuralId = await ResolveAsync(resolver, options, stats, structuralName, OrgKinds.Competitor, "ProjectReverifyStructural", ct).ConfigureAwait(false);
                var retire = IsRetiringProjectVerdict(verdict);

                var rowUpdated = await UpdateMajorProjectReverifyAsync(
                    options,
                    id.Value,
                    verdict,
                    retire,
                    architectName,
                    architectId,
                    structuralName,
                    structuralId,
                    ct).ConfigureAwait(false);

                if (rowUpdated)
                {
                    updated++;
                    if (retire)
                    {
                        retired++;
                    }
                    else
                    {
                        kept++;
                    }

                    IncrementProjectSource(stats, "ProjectReverify");
                    if (!options.Quiet)
                    {
                        Console.WriteLine(options.DryRun
                            ? $"[DRY-RUN] ProjectReverify: planned MPI reverify Id={id.Value}; verdict={verdictKey}"
                            : $"[MPI] ProjectReverify: updated Id={id.Value}; verdict={verdictKey}");
                    }
                }
                else
                {
                    stats.ProjectRowsSkipped++;
                    if (!options.Quiet)
                    {
                        Console.WriteLine($"[WARN] ProjectReverify: skipped missing MPI Id={id.Value}; verdict={verdictKey}");
                    }
                }
            }

            Console.WriteLine($"[project-reverify] updated={updated}; retired={retired}; kept={kept}");
            foreach (var (verdict, count) in verdictCounts.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"[project-reverify] verdict {verdict}: {count}");
            }
        }
    }

    private static async Task ImportIndustryEventsAsync(
        ImportOptions options,
        SqlIndustryEventStore? store,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Industry-Events", "outputs", "industry-events.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "industry-events");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("industry-events: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"industry-events: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"industry-events: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"industry-events: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            var upserted = 0;
            foreach (var item in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var name = String(item, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    if (!options.Quiet)
                    {
                        Console.WriteLine("[WARN] IndustryEvents: skipped row with blank name.");
                    }

                    continue;
                }

                var startDateText = String(item, "startDate");
                var record = new IndustryEventRecord(
                    SourceKey: Sha1($"{name}|{startDateText}"),
                    Name: name,
                    Organizer: String(item, "organizer"),
                    EventType: String(item, "eventType"),
                    StartDate: DateOnlyOrNull(startDateText),
                    EndDate: DateOnlyOrNull(String(item, "endDate")),
                    Recurrence: String(item, "recurrence"),
                    City: String(item, "city"),
                    Market: String(item, "market"),
                    Format: String(item, "format"),
                    SectorsThemes: JoinArray(item, "sectorsThemes"),
                    Audience: String(item, "audience"),
                    TargetsPresent: StringOrJoinedArray(item, "targetsPresent"),
                    RegistrationUrl: String(item, "registrationUrl"),
                    CostNote: String(item, "costNote"),
                    KorRelevance: String(item, "korRelevance"),
                    SourceNote: String(item, "sourceNote"));

                upserted++;
                if (options.DryRun)
                {
                    if (!options.Quiet)
                    {
                        Console.WriteLine($"[DRY-RUN] IndustryEvents: planned upsert {record.SourceKey}; event={name}");
                    }

                    continue;
                }

                if (store is null)
                {
                    throw new InvalidOperationException("Industry event store is not available.");
                }

                await store.UpsertAsync(record, ct).ConfigureAwait(false);
                if (!options.Quiet)
                {
                    Console.WriteLine($"[EVENT] IndustryEvents: upserted {record.SourceKey}; event={name}");
                }
            }

            Console.WriteLine($"[industry-events] upserted={upserted}");
        }
    }

    private static async Task ImportKorCapabilityAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var projectsPath = Path.Combine(options.BaseDirectory, "KOR-Capability-Corpus", "outputs", "kor-capability-corpus.json");
        var rosterPath = Path.Combine(options.BaseDirectory, "KOR-Capability-Corpus", "outputs", "kor-roster.json");
        if (!TryLoadJson(projectsPath, out var projectsDoc))
        {
            stats.FilesMissing++;
            return;
        }

        if (!TryLoadJson(rosterPath, out var rosterDoc))
        {
            projectsDoc.Dispose();
            stats.FilesMissing++;
            return;
        }

        using (projectsDoc)
        using (rosterDoc)
        {
            if (!TrySelectEnvelopeOrLegacyItems(projectsDoc, "kor-capability-corpus", "kor-capability", "corpus", null, out var projectItems))
            {
                return;
            }

            if (!TrySelectEnvelopeOrLegacyItems(rosterDoc, "kor-roster", "kor-capability", "roster", null, out var rosterItems))
            {
                return;
            }

            var korOrgId = await ResolveKorStructuralOrgAsync(options, orgStore, ct).ConfigureAwait(false);
            var projects = new List<KorCapabilityProject>();
            var roster = new List<KorCapabilityRosterMember>();
            var sectorSystemMatrix = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
            var orgsResolved = 0;

            foreach (var project in projectItems.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var owner = String(project, "owner");
                var architect = String(project, "architect");
                var generalContractor = String(project, "generalContractor");
                orgsResolved += CountName(owner) + CountName(architect) + CountName(generalContractor);
                await ResolveAsync(resolver, options, stats, architect, OrgKinds.Architect, "KorCapabilityArchitect", ct).ConfigureAwait(false);
                await ResolveAsync(resolver, options, stats, generalContractor, OrgKinds.GeneralContractor, "KorCapabilityGC", ct).ConfigureAwait(false);
                await ResolveAsync(resolver, options, stats, owner, OrgKinds.Buyer, "KorCapabilityOwner", ct).ConfigureAwait(false);

                var systems = StringArray(project, "structuralSystems");
                AddSectorSystems(sectorSystemMatrix, String(project, "sector"), systems);

                projects.Add(new KorCapabilityProject(
                    projectName,
                    String(project, "city"),
                    String(project, "market"),
                    Short(project, "completionYear"),
                    String(project, "sector"),
                    systems,
                    owner,
                    architect,
                    generalContractor,
                    RawJsonOrNull(project, "scale"),
                    String(project, "notableFeatures"),
                    StringArray(project, "awards"),
                    String(project, "creditedFirmName"),
                    Decimal(project, "systemConfidence"),
                    StringArray(project, "sourceUrls")));
            }

            var pEngCount = 0;
            foreach (var person in rosterItems.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var credentials = StringArray(person, "credentials");
                if (credentials.Any(c => c.Contains("P.Eng", StringComparison.OrdinalIgnoreCase)))
                {
                    pEngCount++;
                }

                roster.Add(new KorCapabilityRosterMember(
                    String(person, "name"),
                    String(person, "title"),
                    credentials,
                    String(person, "specialty"),
                    String(person, "era"),
                    String(person, "creditedFirmName"),
                    String(person, "notes")));
            }

            var payload = JsonSerializer.Serialize(new
            {
                projects,
                roster,
                sectorSystemMatrix,
                pEngCount,
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            await WriteEnrichmentAsync(
                enrichmentStore,
                options,
                stats,
                korOrgId,
                "KorCapability",
                payload,
                null,
                "KOR Structural",
                ct).ConfigureAwait(false);

            Console.WriteLine($"[kor-capability] projects={projects.Count}; roster={roster.Count}; orgs resolved/created={orgsResolved}");
        }
    }

    private static async Task ImportProjectTeamsAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Project-Teams", "outputs", "project-teams.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "project-teams");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("project-teams: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"project-teams: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"project-teams: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"project-teams: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                // Round 60g (2026-06-03): accept both base-schema field names
                // (owner/architect/structuralEngineer/generalContractor) and the
                // backfill-schema variants (proponentName/architectName/
                // structuralName/generalContractorName) used by the overnight
                // MPI NULL-team-backfill Sonnet session. Fall back to the
                // original key when the backfill key isn't present.
                var ownerName = String(project, "owner") ?? String(project, "proponentName");
                var architectName = String(project, "architect") ?? String(project, "architectName");
                var structuralName = String(project, "structuralEngineer") ?? String(project, "structuralName");
                var gcName = String(project, "generalContractor") ?? String(project, "generalContractorName");

                // Round 60g: if the entry includes mpiId, repoint the existing
                // MPI row's FK columns directly (only when currently NULL — never
                // overwrite existing attributions). Skip the full upsert path
                // so we don't insert a duplicate MPI row.
                var mpiId = LongOrNull(project, "mpiId");
                if (mpiId.HasValue && mpiId.Value > 0)
                {
                    var architectIdForBackfill = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                    var structuralIdForBackfill = await ResolveAsync(resolver, options, stats, structuralName, OrgKinds.Competitor, "ProjectTeamsStructural", ct).ConfigureAwait(false);
                    var gcIdForBackfill = await ResolveAsync(resolver, options, stats, gcName, OrgKinds.GeneralContractor, "ProjectTeamsGC", ct).ConfigureAwait(false);
                    await BackfillMpiTeamAsync(options, stats, mpiId.Value, architectIdForBackfill, structuralIdForBackfill, gcIdForBackfill, architectName, structuralName, gcName, ct).ConfigureAwait(false);
                    continue;
                }

                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var structuralId = await ResolveAsync(resolver, options, stats, structuralName, OrgKinds.Competitor, "ProjectTeamsStructural", ct).ConfigureAwait(false);
                var gcId = await ResolveAsync(resolver, options, stats, gcName, OrgKinds.GeneralContractor, "ProjectTeamsGC", ct).ConfigureAwait(false);

                var record = new MajorProjectRecord(
                    Source: "project-teams",
                    SourceKey: "PTEAM-" + Sha1($"{ownerName}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: null,
                    EstimatedCostCad: Money(project, "estCostCad"),
                    EstimatedCostText: CostText(project, "estCostCad"),
                    Sector: String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(project, "market"),
                    MunicipalityName: String(project, "city"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "status"),
                    ProjectStatus: String(project, "status"),
                    ProjectStage: "project-teams",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: Short(project, "completionYear"),
                    ScheduleNotes: JoinNotes(("MechanicalEngineer", String(project, "mechanicalEngineer")), ("ElectricalEngineer", String(project, "electricalEngineer"))),
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(project),
                    RawJson: project.GetRawText())
                {
                    Province = ProvinceFromResearchMarket(String(project, "market")),
                    StructuralEngineerName = structuralName,
                    StructuralEngineerCanonicalOrgId = structuralId,
                    GeneralContractorName = gcName,
                    GeneralContractorCanonicalOrgId = gcId,
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportCompetitorProjectsAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Competitor-Portfolios", "outputs", "competitor-projects.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "competitor-projects");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("competitor-projects: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"competitor-projects: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"competitor-projects: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"competitor-projects: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var structuralName = String(project, "structuralEngineer");
                var architectName = String(project, "architect");
                var ownerName = String(project, "owner");
                var structuralId = await ResolveAsync(resolver, options, stats, structuralName, OrgKinds.Competitor, "CompetitorProjectsStructural", ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);

                var record = new MajorProjectRecord(
                    Source: "competitor-projects",
                    SourceKey: "CPROJ-" + Sha1($"{structuralName}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: null,
                    EstimatedCostCad: null,
                    EstimatedCostText: null,
                    Sector: String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(project, "market"),
                    MunicipalityName: String(project, "city"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: null,
                    ProjectStatus: null,
                    ProjectStage: "competitor-projects",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: Short(project, "completionYear"),
                    ScheduleNotes: null,
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(project),
                    RawJson: project.GetRawText())
                {
                    Province = ProvinceFromResearchMarket(String(project, "market")),
                    StructuralEngineerName = structuralName,
                    StructuralEngineerCanonicalOrgId = structuralId,
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportStructuralPipelineAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Seismic-MassTimber", "outputs", "structural-pipeline.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "structural-pipeline");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("structural-pipeline: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"structural-pipeline: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"structural-pipeline: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"structural-pipeline: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(project, "owner");
                var architectName = String(project, "architect");
                var structuralName = String(project, "structuralEngineer");
                var proponentId = await ResolveAsync(resolver, options, stats, ownerName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                var structuralId = await ResolveAsync(resolver, options, stats, structuralName, OrgKinds.Competitor, "StructuralPipelineStructural", ct).ConfigureAwait(false);

                var record = new MajorProjectRecord(
                    Source: "structural-pipeline",
                    SourceKey: "STRUCPIPE-" + Sha1($"{ownerName}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: null,
                    EstimatedCostCad: Money(project, "estCostCad"),
                    EstimatedCostText: CostText(project, "estCostCad"),
                    Sector: String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(project, "market"),
                    MunicipalityName: String(project, "city"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "status"),
                    ProjectStatus: String(project, "status"),
                    ProjectStage: "structural-pipeline",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: Short(project, "completionYear"),
                    ScheduleNotes: JoinNotes(("Segment", String(project, "segment")), ("Program", String(project, "program"))),
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(project),
                    RawJson: project.GetRawText())
                {
                    Province = ProvinceFromResearchMarket(String(project, "market")),
                    StructuralEngineerName = structuralName,
                    StructuralEngineerCanonicalOrgId = structuralId,
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportIndigenousPipelineProjectsAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Indigenous-Pipeline", "outputs", "indigenous-projects.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "indigenous-projects");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("indigenous-projects: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"indigenous-projects: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"indigenous-projects: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"indigenous-projects: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var proponentName = String(project, "proponent");
                var architectName = String(project, "architect");
                var proponentId = await ResolveAsync(resolver, options, stats, proponentName, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);

                var record = new MajorProjectRecord(
                    Source: "indigenous-projects",
                    SourceKey: "INDIGP-" + Sha1($"{proponentName}|{projectName}"),
                    ProjectName: projectName,
                    ProjectDescription: null,
                    EstimatedCostCad: Money(project, "estCostCad"),
                    EstimatedCostText: CostText(project, "estCostCad"),
                    Sector: String(project, "sector"),
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: String(project, "market"),
                    MunicipalityName: String(project, "city"),
                    ProponentName: proponentName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "status"),
                    ProjectStatus: String(project, "status"),
                    ProjectStage: "indigenous-projects",
                    ProjectCategoryName: null,
                    PublicFundingInd: null,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: true,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: null,
                    ScheduleNotes: JoinNotes(("Funding", String(project, "funding")), ("Timeline", String(project, "timeline")), ("Partner", String(project, "developerPartner"))),
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(project),
                    RawJson: project.GetRawText())
                {
                    Province = ProvinceFromResearchMarket(String(project, "market")),
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task ImportIndigenousPipelineOrgsAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Indigenous-Pipeline", "outputs", "indigenous-orgs.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "indigenous-orgs");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("indigenous-orgs: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"indigenous-orgs: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"indigenous-orgs: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"indigenous-orgs: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var org in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var name = String(org, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var orgId = await UpsertOrgAsync(
                    orgStore,
                    options,
                    stats,
                    MapIslandKind(String(org, "kind")),
                    name,
                    String(org, "website"),
                    $"Nation: {String(org, "nation")}",
                    "IndigenousPipeline",
                    ct).ConfigureAwait(false);

                var people = new List<string>();
                if (org.TryGetProperty("keyPeople", out var peopleArray) && peopleArray.ValueKind == JsonValueKind.Array)
                {
                    foreach (var person in peopleArray.EnumerateArray())
                    {
                        people.Add(person.GetRawText());
                    }
                }

                if (people.Count == 0)
                {
                    continue;
                }

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "IndigenousDev",
                    "{\"people\":[" + string.Join(",", people) + "]}",
                    null,
                    name,
                    ct).ConfigureAwait(false);
            }
        }
    }

    // === KOR-DesignBuild-Contractors (db-contractors.json) ===========================
    // Each row is a GC / design-builder active on institutional buildings under
    // alternative delivery (DB / CMAR / PDB / IPD). Upserts as a CanonicalOrg
    // (Kind=GC) and stashes the full research blob as a CanonicalOrgEnrichment
    // record so the Org Brief surfaces it.
    private static async Task ImportDbContractorsAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-DesignBuild-Contractors", "outputs", "db-contractors.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "db-contractors");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("db-contractors: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"db-contractors: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"db-contractors: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"db-contractors: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var contractor in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var name = String(contractor, "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var hq = String(contractor, "hq");
                var notes = string.IsNullOrWhiteSpace(hq) ? null : "HQ: " + hq;

                var orgId = await UpsertOrgAsync(
                    orgStore,
                    options,
                    stats,
                    OrgKinds.GeneralContractor,
                    name,
                    null,
                    notes,
                    "DesignBuildContractors",
                    ct).ConfigureAwait(false);

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "DesignBuildContractors",
                    contractor.GetRawText(),
                    String(contractor, "fitNotes"),
                    name,
                    ct).ConfigureAwait(false);
            }
        }
    }

    // === KOR-Incumbent-Rosters (incumbent-rosters.json) ==============================
    // Each row is a standing-offer / on-call / SoR / prequal roster held by a
    // structural firm at a target public owner. Upserts the owner as Buyer +
    // stashes the full blob so the owner's Org Brief surfaces the incumbent +
    // renewal-window intel.
    private static async Task ImportIncumbentRostersAsync(
        ImportOptions options,
        SqlCanonicalOrgStore? orgStore,
        SqlEnrichmentTrackingStore? enrichmentStore,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Incumbent-Rosters", "outputs", "incumbent-rosters.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "incumbent-rosters");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("incumbent-rosters: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"incumbent-rosters: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"incumbent-rosters: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"incumbent-rosters: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var roster in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var owner = String(roster, "owner");
                if (string.IsNullOrWhiteSpace(owner))
                {
                    stats.OrgRowsSkipped++;
                    continue;
                }

                var market = String(roster, "market");
                var notes = string.IsNullOrWhiteSpace(market) ? null : "Market: " + market;

                var orgId = await UpsertOrgAsync(
                    orgStore,
                    options,
                    stats,
                    OrgKinds.Buyer,
                    owner,
                    null,
                    notes,
                    "IncumbentRosters",
                    ct).ConfigureAwait(false);

                await WriteEnrichmentAsync(
                    enrichmentStore,
                    options,
                    stats,
                    orgId,
                    "IncumbentRosters",
                    roster.GetRawText(),
                    String(roster, "opportunityTiming"),
                    owner,
                    ct).ConfigureAwait(false);
            }
        }
    }

    // === KOR-Capital-Funding-Signals (capital-funding-signals.json) =================
    // Each row is a publicly-funded but not-yet-tendered building project. Upserts
    // as a MajorProjectsInventory row keyed by Sha1(name|municipality), tagged
    // ProjectStage=CapitalPlan so it shows in the Forward Pipeline panel.
    private static async Task ImportCapitalFundingSignalsAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var path = Path.Combine(options.BaseDirectory, "KOR-Capital-Funding-Signals", "outputs", "capital-funding-signals.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            var validation = ResearchEnvelopeValidator.Validate(doc, "capital-funding-signals");
            JsonElement itemsArray;
            string source;
            if (validation.IsValid && validation.Envelope is { } env)
            {
                if (env.Items.ValueKind != JsonValueKind.Array)
                {
                    Console.WriteLine("capital-funding-signals: envelope items is not an array; skipping.");
                    return;
                }
                itemsArray = env.Items;
                source = $"envelope v{env.SchemaVersion} generated {env.GeneratedAtUtc:yyyy-MM-dd}";
            }
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                Console.WriteLine(
                    $"capital-funding-signals: payload is legacy flat-array (no envelope: {validation.Reason}); ingesting via legacy path.");
                itemsArray = doc.RootElement;
                source = "legacy flat-array";
            }
            else
            {
                Console.WriteLine(
                    $"capital-funding-signals: payload has neither envelope nor legacy shape ({validation.Reason}); skipping.");
                return;
            }

            Console.WriteLine($"capital-funding-signals: ingesting {itemsArray.GetArrayLength()} item(s) via {source}.");

            foreach (var project in itemsArray.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "name") ?? String(project, "projectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var owner = String(project, "owner") ?? String(project, "ownerName");
                var architect = String(project, "architectSelected");
                var market = String(project, "market");
                var municipality = String(project, "municipality") ?? String(project, "city");
                var sector = String(project, "sector");
                var stage = String(project, "stage");
                var notableScope = String(project, "notableScope") ?? String(project, "detail");
                var fundingSource = String(project, "fundingSource");
                var announcedDate = String(project, "announcedDate") ?? String(project, "occurredAt");
                var expectedProcurementWindow = String(project, "expectedProcurementWindow");

                var ownerId = await ResolveAsync(resolver, options, stats, owner, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architect, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);

                var scheduleParts = new List<string>();
                if (!string.IsNullOrWhiteSpace(fundingSource)) scheduleParts.Add("Funding: " + fundingSource);
                if (!string.IsNullOrWhiteSpace(announcedDate)) scheduleParts.Add("Announced " + announcedDate);
                if (!string.IsNullOrWhiteSpace(expectedProcurementWindow)) scheduleParts.Add("Procurement " + expectedProcurementWindow);

                var record = new MajorProjectRecord(
                    Source: "CapitalFundingSignals",
                    SourceKey: Sha1($"CapitalFundingSignals|{projectName}|{municipality}"),
                    ProjectName: projectName,
                    ProjectDescription: notableScope,
                    EstimatedCostCad: LongOrNull(project, "fundingAmountCad"),
                    EstimatedCostText: String(project, "estimatedCost"),
                    Sector: sector,
                    SubSector: null,
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: market,
                    MunicipalityName: municipality,
                    ProponentName: owner,
                    ProponentCanonicalOrgId: ownerId,
                    ArchitectName: architect,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: stage,
                    ProjectStatus: stage,
                    ProjectStage: "CapitalPlan",
                    ProjectCategoryName: null,
                    PublicFundingInd: true,
                    ProvincialFunding: null,
                    FederalFunding: null,
                    MunicipalFunding: null,
                    OtherPublicFunding: null,
                    GreenBuildingInd: null,
                    IndigenousInd: null,
                    IndigenousNames: null,
                    ConstructionJobs: null,
                    OperatingJobs: null,
                    StandardizedStartDate: null,
                    StandardizedCompletionDate: null,
                    StartYear: null,
                    CompletionYear: null,
                    ScheduleNotes: scheduleParts.Count == 0 ? null : string.Join(" | ", scheduleParts),
                    Latitude: null,
                    Longitude: null,
                    ProjectWebsite: null,
                    SourceUrl: FirstSourceUrl(project),
                    RawJson: project.GetRawText())
                {
                    Province = NormalizeProvince(market, string.Empty),
                };

                await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
            }
        }
    }

    // === KOR-Seismic-Pipeline (seismic-pipeline.jsonl) ==============================
    // One JSON object per line. Each row is a publicly-funded seismic-upgrade
    // building project (school / hospital / post-sec / civic / housing) in BC, WA,
    // or OR for the 2026-2031 window. Upserts each as a MajorProjectsInventory row
    // tagged ProjectStage="Seismic" so the Forward Pipeline view can filter to
    // KOR's most distinctively winnable scope. Owner / architect / structural names
    // resolve to canonical orgs via the existing CanonicalOrgResolver.
    private static async Task ImportSeismicPipelineAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var jsonlPath = Path.Combine(options.BaseDirectory, "KOR-Seismic-Pipeline", "outputs", "seismic-pipeline.jsonl");
        var envelopePath = Path.ChangeExtension(jsonlPath, ".json");
        string[]? lines = null;

        if (File.Exists(envelopePath) && TryLoadJson(envelopePath, out var envelopeDoc))
        {
            using (envelopeDoc)
            {
                var validation = ResearchEnvelopeValidator.Validate(envelopeDoc, "seismic-pipeline");
                if (validation.IsValid && validation.Envelope is { } env && env.Items.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"seismic-pipeline: envelope v{env.SchemaVersion} ingesting {env.Items.GetArrayLength()} item(s).");
                    lines = env.Items.EnumerateArray().Select(item => item.GetRawText()).ToArray();
                }
                else
                {
                    Console.WriteLine($"seismic-pipeline: envelope path exists but invalid ({validation.Reason}); falling through to JSONL legacy.");
                }
            }
        }

        if (lines is null)
        {
            if (!File.Exists(jsonlPath))
            {
                Console.WriteLine($"[WARN] Missing payload: {jsonlPath}");
                stats.FilesMissing++;
                return;
            }

            Console.WriteLine($"[FILE] {jsonlPath}");
            lines = File.ReadAllLines(jsonlPath);
            Console.WriteLine($"seismic-pipeline: legacy JSONL ingesting {lines.Length} item(s).");
        }

        foreach (var raw in lines)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(raw, JsonOptions);
            var project = doc.RootElement;

            var projectName = String(project, "projectName");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                stats.ProjectRowsSkipped++;
                continue;
            }

            var ownerOrg = String(project, "ownerOrg") ?? String(project, "owner");
            var architectOrg = String(project, "architectOrg") ?? String(project, "architect");
            var structuralOrg = String(project, "structuralOrg") ?? String(project, "structuralEngineer");
            var program = String(project, "program");
            var region = String(project, "region");
            var addressOrCity = String(project, "addressOrCity") ?? String(project, "city");
            var facilityType = String(project, "facilityType");
            var stage = String(project, "stage");
            var scopeNotes = String(project, "scopeNotes");
            var notes = String(project, "notes");
            var fundingSource = String(project, "fundingSource");
            var expectedRfp = String(project, "expectedRfpWindow");
            var fundingYear = Short(project, "fundingYear");

            var ownerId = await ResolveAsync(resolver, options, stats, ownerOrg, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
            var architectId = await ResolveAsync(resolver, options, stats, architectOrg, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
            var structuralId = await ResolveAsync(resolver, options, stats, structuralOrg, OrgKinds.Competitor, "SeismicPipeline.Structural", ct).ConfigureAwait(false);

            var scheduleParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(program)) scheduleParts.Add("Program: " + program);
            if (fundingYear.HasValue) scheduleParts.Add("Funded " + fundingYear.Value);
            if (!string.IsNullOrWhiteSpace(fundingSource)) scheduleParts.Add("Funding: " + fundingSource);
            if (!string.IsNullOrWhiteSpace(expectedRfp)) scheduleParts.Add("RFP " + expectedRfp);
            if (!string.IsNullOrWhiteSpace(notes)) scheduleParts.Add(notes);

            var description = string.IsNullOrWhiteSpace(scopeNotes) ? notes : scopeNotes;

            var record = new MajorProjectRecord(
                Source: "SeismicPipeline",
                SourceKey: Sha1($"SeismicPipeline|{projectName}|{addressOrCity}"),
                ProjectName: projectName,
                ProjectDescription: description,
                EstimatedCostCad: LongOrNull(project, "fundingValueCad"),
                EstimatedCostText: null,
                Sector: facilityType,
                SubSector: null,
                ConstructionType: null,
                ConstructionSubtype: null,
                ProjectType: null,
                RegionName: region,
                MunicipalityName: addressOrCity,
                ProponentName: ownerOrg,
                ProponentCanonicalOrgId: ownerId,
                ArchitectName: architectOrg,
                ArchitectCanonicalOrgId: architectId,
                Stage: stage,
                ProjectStatus: stage,
                ProjectStage: "Seismic",
                ProjectCategoryName: program,
                PublicFundingInd: true,
                ProvincialFunding: ProvincialFlag(fundingSource),
                FederalFunding: FederalFlag(fundingSource),
                MunicipalFunding: MunicipalFlag(fundingSource),
                OtherPublicFunding: null,
                GreenBuildingInd: null,
                IndigenousInd: null,
                IndigenousNames: null,
                ConstructionJobs: null,
                OperatingJobs: null,
                StandardizedStartDate: null,
                StandardizedCompletionDate: null,
                StartYear: fundingYear,
                CompletionYear: null,
                ScheduleNotes: scheduleParts.Count == 0 ? null : string.Join(" | ", scheduleParts),
                Latitude: null,
                Longitude: null,
                ProjectWebsite: null,
                SourceUrl: FirstSourceUrl(project),
                RawJson: project.GetRawText())
            {
                Province = ProvinceFromSeismicRegion(region),
                StructuralEngineerName = structuralOrg,
                StructuralEngineerCanonicalOrgId = structuralId,
            };

            await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
        }
    }

    // === KOR-Island-Okanagan-Pairing (pairings.jsonl) ================================
    // Round 2 of the Island/Okanagan ecosystem mapping (Round 1 produced the org +
    // project anchor lists at ..\KOR-Island-Okanagan-Ecosystem). One JSON object per
    // line. Each row is a verified architect / structural-engineer pairing on a
    // specific Island or Okanagan public building project, 2021-2026. The killer
    // intel: every row has BOTH architectOrg AND structuralOrg confirmed from a
    // primary source URL, so the StructuralPartnerMap dossier section and the
    // Competitor Watch panel can finally answer "who pairs with whom".
    //
    // Upserts as MajorProjectsInventory with ProjectStage="IslandOkanaganPairing"
    // so the Forward Pipeline view can isolate this dataset. Owner / architect /
    // structural / GC names all resolve through CanonicalOrgResolver so the
    // network graph hangs together.
    // Round 41: thin wrappers for the three Pairing sessions. Share one body
    // since the JSONL schemas are identical (year / projectName / region /
    // sector / owner / valueCad / stage / architectOrg / structuralOrg /
    // mepConsultants / gcOrConstructionManager / sourceUrls / notes). Only
    // the input directory, the Source label, and the Province differ.
    private static Task ImportIslandOkanaganPairingAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
        => ImportPairingAsync(options, resolver, stats,
            directoryName: "KOR-Island-Okanagan-Pairing",
            sourceLabel: "IslandOkanaganPairing",
            // Island + Okanagan are all BC; ProvinceFromSeismicRegion would
            // also return "BC" for these regions so the behaviour matches
            // the pre-Round-41 single-handler implementation.
            province: "BC",
            ct: ct);

    private static Task ImportLowerMainlandPairingAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
        => ImportPairingAsync(options, resolver, stats,
            directoryName: "KOR-LowerMainland-Pairing",
            sourceLabel: "LowerMainlandPairing",
            province: "BC",
            ct: ct);

    private static Task ImportEdmontonPairingAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
        => ImportPairingAsync(options, resolver, stats,
            directoryName: "KOR-Edmonton-Pairing",
            sourceLabel: "EdmontonPairing",
            province: "AB",
            ct: ct);

    private static async Task ImportPairingAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        string directoryName,
        string sourceLabel,
        string province,
        CancellationToken ct)
    {
        var jsonlPath = Path.Combine(options.BaseDirectory, directoryName, "outputs", "pairings.jsonl");
        var envelopePath = Path.ChangeExtension(jsonlPath, ".json");
        var logPrefix = $"pairing:{sourceLabel}";
        string[]? lines = null;

        if (File.Exists(envelopePath) && TryLoadJson(envelopePath, out var envelopeDoc))
        {
            using (envelopeDoc)
            {
                var validation = ResearchEnvelopeValidator.Validate(envelopeDoc, "pairing");
                if (validation.IsValid && validation.Envelope is { } env && env.Items.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"{logPrefix}: envelope v{env.SchemaVersion} ingesting {env.Items.GetArrayLength()} item(s).");
                    lines = env.Items.EnumerateArray().Select(item => item.GetRawText()).ToArray();
                }
                else
                {
                    Console.WriteLine($"{logPrefix}: envelope path exists but invalid ({validation.Reason}); falling through to JSONL legacy.");
                }
            }
        }

        if (lines is null)
        {
            if (!File.Exists(jsonlPath))
            {
                Console.WriteLine($"[WARN] Missing payload: {jsonlPath}");
                stats.FilesMissing++;
                return;
            }

            Console.WriteLine($"[FILE] {jsonlPath}");
            lines = File.ReadAllLines(jsonlPath);
            Console.WriteLine($"{logPrefix}: legacy JSONL ingesting {lines.Length} item(s).");
        }

        foreach (var raw in lines)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            using var doc = JsonDocument.Parse(raw, JsonOptions);
            var project = doc.RootElement;

            var projectName = String(project, "projectName");
            if (string.IsNullOrWhiteSpace(projectName))
            {
                stats.ProjectRowsSkipped++;
                continue;
            }

            var owner = String(project, "owner");
            var architectOrg = String(project, "architectOrg") ?? String(project, "architectName");
            var structuralOrg = String(project, "structuralOrg") ?? String(project, "structuralPartner");
            var gcOrg = String(project, "gcOrConstructionManager");
            var region = String(project, "region");
            var sector = String(project, "sector");
            var stage = String(project, "stage");
            var notes = String(project, "notes");
            var mep = String(project, "mepConsultants");
            var year = Short(project, "year");

            var ownerId = await ResolveAsync(resolver, options, stats, owner, OrgKinds.Buyer, ProponentSource, ct).ConfigureAwait(false);
            var architectId = await ResolveAsync(resolver, options, stats, architectOrg, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
            var structuralId = await ResolveAsync(resolver, options, stats, structuralOrg, OrgKinds.Competitor, $"{sourceLabel}.Structural", ct).ConfigureAwait(false);
            var gcId = await ResolveAsync(resolver, options, stats, gcOrg, OrgKinds.GeneralContractor, $"{sourceLabel}.Gc", ct).ConfigureAwait(false);

            var scheduleParts = new List<string>();
            if (year.HasValue) scheduleParts.Add(year.Value.ToString());
            if (!string.IsNullOrWhiteSpace(mep)) scheduleParts.Add("MEP: " + mep);
            if (!string.IsNullOrWhiteSpace(notes)) scheduleParts.Add(notes);

            // year on a "Completed" row is completion year; on Construction /
            // Design / RFP rows it's "year of record" for the team-assembly
            // event. Map both ways so the Forward Pipeline view sorts sensibly.
            short? completionYear = null;
            short? startYear = null;
            if (year.HasValue)
            {
                if (string.Equals(stage, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    completionYear = year;
                }
                else
                {
                    startYear = year;
                }
            }

            var record = new MajorProjectRecord(
                Source: sourceLabel,
                SourceKey: Sha1($"{sourceLabel}|{projectName}|{region}"),
                ProjectName: projectName,
                ProjectDescription: notes,
                EstimatedCostCad: LongOrNull(project, "valueCad"),
                EstimatedCostText: null,
                Sector: sector,
                SubSector: null,
                ConstructionType: null,
                ConstructionSubtype: null,
                ProjectType: null,
                RegionName: region,
                MunicipalityName: null,
                ProponentName: owner,
                ProponentCanonicalOrgId: ownerId,
                ArchitectName: architectOrg,
                ArchitectCanonicalOrgId: architectId,
                Stage: stage,
                ProjectStatus: stage,
                ProjectStage: sourceLabel,
                ProjectCategoryName: null,
                PublicFundingInd: true,
                ProvincialFunding: null,
                FederalFunding: null,
                MunicipalFunding: null,
                OtherPublicFunding: null,
                GreenBuildingInd: null,
                IndigenousInd: null,
                IndigenousNames: null,
                ConstructionJobs: null,
                OperatingJobs: null,
                StandardizedStartDate: null,
                StandardizedCompletionDate: null,
                StartYear: startYear,
                CompletionYear: completionYear,
                ScheduleNotes: scheduleParts.Count == 0 ? null : string.Join(" | ", scheduleParts),
                Latitude: null,
                Longitude: null,
                ProjectWebsite: null,
                SourceUrl: FirstSourceUrl(project),
                RawJson: project.GetRawText())
            {
                Province = province,
                StructuralEngineerName = structuralOrg,
                StructuralEngineerCanonicalOrgId = structuralId,
                GeneralContractorName = gcOrg,
                GeneralContractorCanonicalOrgId = gcId,
            };

            await UpsertMajorProjectAsync(options, stats, record, ct).ConfigureAwait(false);
        }
    }

    // === BD Tracking Spreadsheet (bd-tracking.jsonl) =================================
    // Source: docs/KOR Structural BD Tracking 2026.xlsx, 6 regional sheets, extracted
    // to tools/BdTrackingImport/inputs/bd-tracking.jsonl by extract.py. Each row is one
    // BD touchpoint between a KOR initiator (Omar/Conor/Islam/Jim/Rory/Ian) and a
    // contact at an external firm (the "Company" column).
    //
    // Import groups rows by (Initiator, Company, Region) and upserts ONE CrmEngagement
    // per group; each spreadsheet row becomes one CrmActivity under that engagement;
    // Proposals$ sum into ProposalsSubmittedCad/AcceptedCad on the engagement. USA rows
    // (USD) convert at 1.36 (DASHBOARD sheet's $US Conv. rate). Companies resolve via
    // CanonicalOrgResolver, which currently uses NormalizeName (strict) — Round 35a's
    // NormalizeForFuzzyMatch helper exists but is NOT wired into the resolver, so
    // typos like "Concord Pacifid" still create a separate canonical here. The next
    // data-honing pass is the de-facto fuzzy reconciler (it consolidates via aliases).
    //
    // Idempotency (Round 37a T1.003): re-running this importer deletes the
    // BdTracking-owned child rows (Activities + Contacts where CreatedBy ends in
    // "(BdTrackingImport)") on each engagement before re-inserting from spreadsheet
    // truth. Manually-added activities/contacts (different CreatedBy) survive.
    private static async Task ImportBdTrackingAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        var jsonlPath = Path.Combine(options.BaseDirectory, "Operations", "tools", "BdTrackingImport", "inputs", "bd-tracking.jsonl");
        var envelopePath = Path.ChangeExtension(jsonlPath, ".json");
        string[]? lines = null;

        if (File.Exists(envelopePath) && TryLoadJson(envelopePath, out var envelopeDoc))
        {
            using (envelopeDoc)
            {
                var validation = ResearchEnvelopeValidator.Validate(envelopeDoc, "bd-tracking");
                if (validation.IsValid && validation.Envelope is { } env && env.Items.ValueKind == JsonValueKind.Array)
                {
                    Console.WriteLine($"bd-tracking: envelope v{env.SchemaVersion} ingesting {env.Items.GetArrayLength()} item(s).");
                    lines = env.Items.EnumerateArray().Select(item => item.GetRawText()).ToArray();
                }
                else
                {
                    Console.WriteLine($"bd-tracking: envelope path exists but invalid ({validation.Reason}); falling through to JSONL legacy.");
                }
            }
        }

        if (lines is null)
        {
            if (!File.Exists(jsonlPath))
            {
                Console.WriteLine($"[WARN] Missing payload: {jsonlPath}");
                stats.FilesMissing++;
                return;
            }

            Console.WriteLine($"[FILE] {jsonlPath}");
            lines = File.ReadAllLines(jsonlPath);
            Console.WriteLine($"bd-tracking: legacy JSONL ingesting {lines.Length} item(s).");
        }

        // Parse all rows into typed records.
        var rows = new List<BdTrackingRow>();
        foreach (var raw in lines)
        {
            if (string.IsNullOrWhiteSpace(raw)) continue;
            using var doc = JsonDocument.Parse(raw, JsonOptions);
            rows.Add(BdTrackingRow.FromJson(doc.RootElement));
        }
        Console.WriteLine($"[BD] parsed {rows.Count} rows");

        // Group by normalized (Initiator, Company, Region). Each group becomes one
        // engagement with N activities under it.
        var groups = rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Company))
            .GroupBy(r => new
            {
                Initiator = NormalizeInitiator(r.Initiator),
                Company = (r.Company ?? string.Empty).Trim(),
                Region = r.Region ?? string.Empty,
            })
            .ToList();

        Console.WriteLine($"[BD] grouped into {groups.Count} engagements");

        var skipped = 0;
        var engagementsCreated = 0;
        var engagementsUpdated = 0;
        var activitiesInserted = 0;
        var contactsInserted = 0;
        var unresolvedCompanies = new List<string>();
        var reconciliation = new List<string>
        {
            "Region,Initiator,Company,BuyerCanonicalOrgId,EngagementId,Activities,ProposalsSubmittedCad,ProposalsAcceptedCad,Status",
        };

        foreach (var g in groups)
        {
            ct.ThrowIfCancellationRequested();

            var initiator = g.Key.Initiator;
            var company = g.Key.Company;
            var region = g.Key.Region;
            var groupRows = g.OrderBy(r => r.Date ?? DateTimeOffset.MinValue).ToList();

            // Resolve Company -> CanonicalOrg. We use Unknown kind for genuinely new
            // canonicals because BD-tracking companies are a mix of Developers /
            // Architects / Buyers / GCs and the spreadsheet doesn't tag them. The
            // resolver matches existing canonicals by NormalizedName first
            // (preserving their existing Kind), so this default only affects
            // truly new firms — the next data-honing pass reclassifies them
            // based on actual research.
            var buyerId = await ResolveAsync(resolver, options, stats, company, OrgKinds.Unknown, "BdTracking.Company", ct).ConfigureAwait(false);
            if (buyerId is null && !options.DryRun)
            {
                unresolvedCompanies.Add(company);
            }

            // Roll up Submitted/Accepted across rows in this group (USA -> CAD).
            decimal? submitted = null;
            decimal? accepted = null;
            foreach (var r in groupRows)
            {
                if (r.ProposalsSubmittedCad.HasValue)
                {
                    var v = r.Currency == "USD" ? r.ProposalsSubmittedCad.Value * 1.36m : r.ProposalsSubmittedCad.Value;
                    submitted = (submitted ?? 0) + v;
                }
                if (r.ProposalsAcceptedCad.HasValue)
                {
                    var v = r.Currency == "USD" ? r.ProposalsAcceptedCad.Value * 1.36m : r.ProposalsAcceptedCad.Value;
                    accepted = (accepted ?? 0) + v;
                }
            }

            // Collect freeform "Potential Projects" text from any rows that have it.
            var potentialBag = groupRows
                .Select(r => r.PotentialProjects)
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct()
                .ToList();
            var potentialProjects = potentialBag.Count == 0 ? null : string.Join(" | ", potentialBag);

            // Upsert engagement.
            long engagementId = 0;
            string status;
            if (options.DryRun)
            {
                Console.WriteLine($"[DRY-RUN] BdTracking engagement: initiator={initiator}; company={company}; region={region}; activities={groupRows.Count}; submitted={submitted ?? 0:N0}; accepted={accepted ?? 0:N0}");
                status = "DryRun";
            }
            else
            {
                var upsert = await UpsertBdTrackingEngagementAsync(options.OpportunitiesDb!, initiator, region, buyerId, submitted, accepted, potentialProjects, ct).ConfigureAwait(false);
                engagementId = upsert.Id;
                if (upsert.WasInsert) engagementsCreated++;
                else engagementsUpdated++;
                status = upsert.WasInsert ? "Created" : "Updated";

                // Round 37a (T1.003): re-run idempotency. Delete BdTracking-owned
                // child rows (activities then contacts to respect the FK) before
                // re-inserting from spreadsheet truth. Manually-added rows with a
                // different CreatedBy survive. No-ops on first run.
                if (!upsert.WasInsert)
                {
                    await DeleteBdTrackingChildrenAsync(options.OpportunitiesDb!, engagementId, ct).ConfigureAwait(false);
                }

                // Contact: if any row has a Contact name, upsert one (de-dup by display name).
                var contactByName = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (var r in groupRows)
                {
                    if (string.IsNullOrWhiteSpace(r.Contact)) continue;
                    if (contactByName.ContainsKey(r.Contact)) continue;
                    var contactId = await UpsertBdTrackingContactAsync(options.OpportunitiesDb!, engagementId, r.Contact, r.ContactInfo, initiator, ct).ConfigureAwait(false);
                    contactByName[r.Contact] = contactId;
                    contactsInserted++;
                }

                // Activities (one per row).
                foreach (var r in groupRows)
                {
                    long? contactId = null;
                    if (!string.IsNullOrWhiteSpace(r.Contact) && contactByName.TryGetValue(r.Contact, out var c)) contactId = c;
                    await InsertBdTrackingActivityAsync(options.OpportunitiesDb!, engagementId, r, contactId, initiator, ct).ConfigureAwait(false);
                    activitiesInserted++;
                }
            }

            reconciliation.Add(string.Join(',', new[]
            {
                CsvEscape(region),
                CsvEscape(initiator),
                CsvEscape(company),
                buyerId?.ToString() ?? "",
                engagementId == 0 ? "" : engagementId.ToString(),
                groupRows.Count.ToString(),
                submitted?.ToString("F2") ?? "",
                accepted?.ToString("F2") ?? "",
                status,
            }));
        }

        // Skipped rows summary
        foreach (var r in rows.Where(r => string.IsNullOrWhiteSpace(r.Company)))
        {
            skipped++;
            stats.ProjectRowsSkipped++;
        }

        // Write the reconciliation CSV alongside the input so Ian can review.
        var outDir = Path.Combine(options.BaseDirectory, "Operations", "tools", "BdTrackingImport", "outputs");
        Directory.CreateDirectory(outDir);
        var reconPath = Path.Combine(outDir, options.DryRun ? "reconciliation-dryrun.csv" : "reconciliation.csv");
        File.WriteAllLines(reconPath, reconciliation);
        Console.WriteLine($"[BD] reconciliation written to {reconPath}");

        Console.WriteLine($"[BD] groups={groups.Count}; activities-rows={rows.Count - skipped}; skipped={skipped}");
        Console.WriteLine($"[BD] engagements-created={engagementsCreated}; engagements-updated={engagementsUpdated}; activities-inserted={activitiesInserted}; contacts-inserted={contactsInserted}");
        if (unresolvedCompanies.Count > 0)
        {
            Console.WriteLine($"[BD] unresolved companies (no canonical match — auto-created): {unresolvedCompanies.Count}");
            foreach (var c in unresolvedCompanies.Take(10)) Console.WriteLine($"       - {c}");
        }
    }

    // === BD Tracking Cross-Link =====================================================
    // Reads CrmEngagements with non-empty PotentialProjects (the freeform "what
    // they're working on / what we discussed" column from the spreadsheet),
    // splits the text into candidate project phrases, fuzzy-matches each against
    // MajorProjectsInventory project names within the engagement's region's
    // province, and inserts opportunities.CrmEngagementProjectLink rows for
    // high-confidence matches. Lower-confidence matches go to an uncertain CSV
    // for human review.
    //
    // Matching algorithm: token-set Jaccard with stopword removal + substring
    // boost. High confidence = Jaccard >= 0.60 OR exact substring containment;
    // medium = Jaccard 0.40-0.60.
    private static async Task ImportBdTrackingCrossLinkAsync(
        ImportOptions options,
        ImportStats stats,
        CancellationToken ct)
    {
        if (options.DryRun)
        {
            Console.WriteLine("[BD-CROSSLINK] dry-run only prints planned links; skipping DB scan in dry-run mode.");
        }

        if (string.IsNullOrWhiteSpace(options.OpportunitiesDb))
        {
            Console.Error.WriteLine("[BD-CROSSLINK] no connection string; skipped.");
            return;
        }

        await using var con = new SqlConnection(options.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);

        // Load engagements with PotentialProjects + Region.
        var engagements = new List<(long Id, string Region, string Text)>();
        const string loadEngagementsSql = @"
SELECT Id, Region, PotentialProjects
FROM   opportunities.CrmEngagements
WHERE  PotentialProjects IS NOT NULL
  AND  Region IS NOT NULL;";
        await using (var cmd = new SqlCommand(loadEngagementsSql, con))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                engagements.Add((r.GetInt64(0), r.GetString(1), r.GetString(2)));
            }
        }
        Console.WriteLine($"[BD-CROSSLINK] loaded {engagements.Count} engagements with PotentialProjects text");

        // Load MPI projects (id + name + province) into memory by province. The
        // BC + AB sets are big-ish (~2-3k each) so we tokenize once per project
        // for cheap repeated Jaccard scoring downstream.
        var mpiByProvince = new Dictionary<string, List<(long Id, string Name, HashSet<string> Tokens)>>(StringComparer.OrdinalIgnoreCase);
        const string loadMpiSql = @"
SELECT Id, ProjectName, Province
FROM   opportunities.MajorProjectsInventory
WHERE  Province IS NOT NULL
  AND  RetiredAtUtc IS NULL
  AND  ProjectName IS NOT NULL;";
        await using (var cmd = new SqlCommand(loadMpiSql, con))
        await using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await r.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = r.GetInt64(0);
                var name = r.GetString(1);
                var prov = r.GetString(2);
                if (!mpiByProvince.TryGetValue(prov, out var list))
                {
                    list = new List<(long, string, HashSet<string>)>();
                    mpiByProvince[prov] = list;
                }
                list.Add((id, name, TokenizeName(name)));
            }
        }
        Console.WriteLine($"[BD-CROSSLINK] loaded MPI by province: {string.Join(", ", mpiByProvince.Select(kv => $"{kv.Key}={kv.Value.Count}"))}");

        var highConfidenceLinks = new List<(long EngagementId, long MpiId, decimal Confidence, string MatchedText)>();
        var uncertainCandidates = new List<string> { "EngagementId,Region,Phrase,BestMpiId,BestMpiName,Jaccard,Reason" };

        foreach (var eng in engagements)
        {
            ct.ThrowIfCancellationRequested();
            var province = BdTrackingRegionToProvince(eng.Region);
            if (string.IsNullOrEmpty(province) || !mpiByProvince.TryGetValue(province, out var candidates))
            {
                // No matching province (USA / Eastern Canada — MPI is BC + AB only).
                continue;
            }

            foreach (var phrase in SplitPotentialProjectsPhrases(eng.Text))
            {
                var phraseTokens = TokenizeName(phrase);
                if (phraseTokens.Count == 0) continue;

                double bestScore = 0;
                long bestId = 0;
                string bestName = "";
                foreach (var (mpiId, mpiName, mpiTokens) in candidates)
                {
                    var score = ScoreMatch(phrase, phraseTokens, mpiName, mpiTokens);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestId = mpiId;
                        bestName = mpiName;
                    }
                }

                if (bestScore >= 0.60)
                {
                    highConfidenceLinks.Add((eng.Id, bestId, (decimal)(bestScore * 100), phrase));
                }
                else if (bestScore >= 0.40)
                {
                    uncertainCandidates.Add($"{eng.Id},{CsvEscape(eng.Region)},{CsvEscape(phrase)},{bestId},{CsvEscape(bestName)},{bestScore:F2},MediumConfidence");
                }
            }
        }

        Console.WriteLine($"[BD-CROSSLINK] high-confidence links found: {highConfidenceLinks.Count}");
        Console.WriteLine($"[BD-CROSSLINK] uncertain candidates (40-60% match): {uncertainCandidates.Count - 1}");

        if (options.DryRun)
        {
            foreach (var l in highConfidenceLinks.Take(20))
            {
                Console.WriteLine($"[DRY-RUN] crosslink: engagement={l.EngagementId} -> mpi={l.MpiId}; confidence={l.Confidence:F0}; phrase=\"{l.MatchedText}\"");
            }
        }
        else
        {
            const string upsertSql = @"
IF NOT EXISTS (SELECT 1 FROM opportunities.CrmEngagementProjectLink
               WHERE EngagementId = @eid AND MajorProjectsInventoryId = @mpi)
BEGIN
    INSERT INTO opportunities.CrmEngagementProjectLink
        (EngagementId, MajorProjectsInventoryId, Confidence, MatchedText, MatchedBy)
    VALUES (@eid, @mpi, @conf, @text, N'BdTrackingCrossLink');
END;";
            var inserted = 0;
            var raced = 0;
            foreach (var l in highConfidenceLinks)
            {
                ct.ThrowIfCancellationRequested();
                await using var cmd = new SqlCommand(upsertSql, con);
                cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = l.EngagementId;
                cmd.Parameters.Add("@mpi", System.Data.SqlDbType.BigInt).Value = l.MpiId;
                cmd.Parameters.Add("@conf", System.Data.SqlDbType.Decimal).Value = l.Confidence;
                cmd.Parameters.Add("@text", System.Data.SqlDbType.NVarChar, 500).Value = (object?)l.MatchedText ?? DBNull.Value;
                try
                {
                    await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
                    inserted++;
                }
                catch (SqlException sqlex) when (sqlex.Number is 2627 or 2601)
                {
                    // Round 37b (T4.001): the IF-NOT-EXISTS/INSERT pattern is
                    // not atomic — a parallel cross-link run can sneak its row
                    // in between the check and the insert. The unique index on
                    // (EngagementId, MajorProjectsInventoryId) raises 2627/2601;
                    // both mean "the row already exists" — skip and continue.
                    raced++;
                }
            }
            Console.WriteLine($"[BD-CROSSLINK] high-confidence links inserted: {inserted}; skipped (already existed / raced): {raced}");
        }

        var outDir = Path.Combine(options.BaseDirectory, "Operations", "tools", "BdTrackingImport", "outputs");
        Directory.CreateDirectory(outDir);
        File.WriteAllLines(Path.Combine(outDir, "crosslink-uncertain.csv"), uncertainCandidates);
        var hcLines = new List<string> { "EngagementId,MpiId,Confidence,MatchedText" };
        hcLines.AddRange(highConfidenceLinks.Select(l => $"{l.EngagementId},{l.MpiId},{l.Confidence:F0},{CsvEscape(l.MatchedText)}"));
        File.WriteAllLines(Path.Combine(outDir, "crosslink-matched.csv"), hcLines);
        Console.WriteLine($"[BD-CROSSLINK] reports written to {outDir}");
    }

    private static readonly HashSet<string> Stopwords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a","an","the","of","and","or","for","to","in","at","on","by","with","from",
        "new","existing","project","building","centre","center","place"
    };

    private static HashSet<string> TokenizeName(string name)
    {
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sb = new System.Text.StringBuilder();
        foreach (var ch in name.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
            else
            {
                if (sb.Length > 0)
                {
                    var tok = sb.ToString();
                    if (tok.Length >= 3 && !Stopwords.Contains(tok)) tokens.Add(tok);
                    sb.Clear();
                }
            }
        }
        if (sb.Length > 0)
        {
            var tok = sb.ToString();
            if (tok.Length >= 3 && !Stopwords.Contains(tok)) tokens.Add(tok);
        }
        return tokens;
    }

    private static double ScoreMatch(string phrase, HashSet<string> phraseTokens, string name, HashSet<string> nameTokens)
    {
        if (phraseTokens.Count == 0 || nameTokens.Count == 0) return 0;

        // Substring containment bonus: if the entire phrase (no whitespace) appears
        // inside the name (or vice-versa), boost. Minimum length is 12 chars to
        // avoid generic single-word matches like "surrey" / "towers" firing on
        // any MPI project that happens to contain those words.
        var phraseFlat = string.Concat(phrase.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        var nameFlat = string.Concat(name.Where(char.IsLetterOrDigit)).ToLowerInvariant();
        if (phraseFlat.Length >= 12 && (nameFlat.Contains(phraseFlat) || phraseFlat.Contains(nameFlat)))
        {
            return 1.0;
        }

        // Jaccard on the token sets — but require at least 2 significant tokens
        // in common for the match to count (single-token "tower" / "school" /
        // "centre" Jaccard hits otherwise dominate).
        var intersect = phraseTokens.Intersect(nameTokens).Count();
        if (intersect < 2) return 0;
        var union = phraseTokens.Union(nameTokens).Count();
        return union == 0 ? 0 : (double)intersect / union;
    }

    private static IEnumerable<string> SplitPotentialProjectsPhrases(string text)
    {
        // Split on ", ", " | ", ";", " and " (each common in spreadsheet entries).
        var pieces = text.Split(new[] { ", ", " | ", "; ", "; ", " and " }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in pieces)
        {
            var trimmed = p.Trim();
            if (trimmed.Length >= 4) yield return trimmed;
        }
    }

    private static string BdTrackingRegionToProvince(string region) => region switch
    {
        "Vancouver/LowerMainland" => "BC",
        "VancouverIsland" => "BC",
        "Okanagan-BcInterior" => "BC",
        "Alberta" => "AB",
        // USA and EasternCanada have no MPI rows; return "" to skip the match.
        _ => string.Empty,
    };

    // Initiator names from the spreadsheet have typos and casing inconsistency
    // ("Omar ALcazar", "Conor Murtagh"). Normalize to a stable first-name token
    // for CrmEngagement.OwnerStaffId.
    private static string NormalizeInitiator(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown";
        var first = raw.Trim().Split(' ', 2)[0];
        // Title-case: "ALcazar" -> ignore (we only use the first name token anyway)
        if (first.Length == 0) return "Unknown";
        return char.ToUpperInvariant(first[0]) + first[1..].ToLowerInvariant();
    }

    private static string CsvEscape(string? s)
    {
        if (s is null) return "";
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    private sealed class BdTrackingRow
    {
        public string Region { get; set; } = "";
        public int RowNumber { get; set; }
        public DateTimeOffset? Date { get; set; }
        public string? Initiator { get; set; }
        public string? Contact { get; set; }
        public string? Company { get; set; }
        public string? ContactInfo { get; set; }
        public string? Location { get; set; }
        public string? Type { get; set; }
        public string? PotentialProjects { get; set; }
        public decimal? ProposalsSubmittedCad { get; set; }
        public decimal? ProposalsAcceptedCad { get; set; }
        public string? Notes { get; set; }
        public string Currency { get; set; } = "CAD";

        public static BdTrackingRow FromJson(JsonElement e)
        {
            return new BdTrackingRow
            {
                Region = e.GetProperty("region").GetString() ?? "",
                RowNumber = e.TryGetProperty("rowNumber", out var rn) && rn.ValueKind == JsonValueKind.Number ? rn.GetInt32() : 0,
                Date = TryDate(e, "date"),
                Initiator = TryStr(e, "initiator"),
                Contact = TryStr(e, "contact"),
                Company = TryStr(e, "company"),
                ContactInfo = TryStr(e, "contactInfo"),
                Location = TryStr(e, "location"),
                Type = TryStr(e, "type"),
                PotentialProjects = TryStr(e, "potentialProjects"),
                ProposalsSubmittedCad = TryDec(e, "proposalsSubmittedCad"),
                ProposalsAcceptedCad = TryDec(e, "proposalsAcceptedCad"),
                Notes = TryStr(e, "notes"),
                Currency = TryStr(e, "currency") ?? "CAD",
            };
        }

        private static string? TryStr(JsonElement e, string n)
            => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        private static decimal? TryDec(JsonElement e, string n)
        {
            if (!e.TryGetProperty(n, out var v) || v.ValueKind != JsonValueKind.Number) return null;
            return v.GetDecimal();
        }
        private static DateTimeOffset? TryDate(JsonElement e, string n)
        {
            var s = TryStr(e, n);
            return DateTimeOffset.TryParse(s, out var d) ? d : null;
        }
    }

    private record EngagementUpsertResult(long Id, bool WasInsert);

    private static async Task<EngagementUpsertResult> UpsertBdTrackingEngagementAsync(
        string connStr, string initiator, string region, long? buyerId, decimal? submitted, decimal? accepted, string? potentialProjects, CancellationToken ct)
    {
        // Natural key: (OwnerStaffId, Region, BuyerCanonicalOrgId) where
        // OpportunityId IS NULL (only BD-tracking engagements; existing
        // opportunity-linked engagements stay in their own lane).
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);

        const string lookupSql = @"
SELECT TOP 1 Id
FROM   opportunities.CrmEngagements
WHERE  OpportunityId IS NULL
  AND  OwnerStaffId = @init
  AND  Region = @region
  AND  (@buyer IS NULL OR BuyerCanonicalOrgId = @buyer);";

        long? existing = null;
        await using (var cmd = new SqlCommand(lookupSql, con))
        {
            cmd.Parameters.Add("@init", System.Data.SqlDbType.NVarChar, 20).Value = initiator;
            cmd.Parameters.Add("@region", System.Data.SqlDbType.NVarChar, 40).Value = region;
            cmd.Parameters.Add("@buyer", System.Data.SqlDbType.BigInt).Value = (object?)buyerId ?? DBNull.Value;
            var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r is long lid) existing = lid;
            else if (r is int iid) existing = iid;
        }

        if (existing is long eId)
        {
            const string updateSql = @"
UPDATE opportunities.CrmEngagements
SET    BuyerCanonicalOrgId    = COALESCE(BuyerCanonicalOrgId, @buyer),
       ProposalsSubmittedCad  = @submitted,
       ProposalsAcceptedCad   = @accepted,
       PotentialProjects      = @potential,
       UpdatedAtUtc           = SYSDATETIMEOFFSET(),
       UpdatedBy              = @actor
WHERE  Id = @id;";
            await using var cmd = new SqlCommand(updateSql, con);
            cmd.Parameters.Add("@id", System.Data.SqlDbType.BigInt).Value = eId;
            cmd.Parameters.Add("@buyer", System.Data.SqlDbType.BigInt).Value = (object?)buyerId ?? DBNull.Value;
            cmd.Parameters.Add("@submitted", System.Data.SqlDbType.Decimal).Value = (object?)submitted ?? DBNull.Value;
            cmd.Parameters.Add("@accepted", System.Data.SqlDbType.Decimal).Value = (object?)accepted ?? DBNull.Value;
            cmd.Parameters.Add("@potential", System.Data.SqlDbType.NVarChar, -1).Value = (object?)potentialProjects ?? DBNull.Value;
            cmd.Parameters.Add("@actor", System.Data.SqlDbType.NVarChar, 150).Value = $"{initiator} (BdTrackingImport)";
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
            return new EngagementUpsertResult(eId, false);
        }

        const string insertSql = @"
INSERT INTO opportunities.CrmEngagements
    (OpportunityId, Stage, OwnerStaffId, BuyerCanonicalOrgId, Region,
     ProposalsSubmittedCad, ProposalsAcceptedCad, PotentialProjects,
     CreatedBy, UpdatedBy)
OUTPUT INSERTED.Id
VALUES
    (NULL, 1, @init, @buyer, @region,
     @submitted, @accepted, @potential,
     @actor, @actor);";
        await using (var cmd = new SqlCommand(insertSql, con))
        {
            cmd.Parameters.Add("@init", System.Data.SqlDbType.NVarChar, 20).Value = initiator;
            cmd.Parameters.Add("@buyer", System.Data.SqlDbType.BigInt).Value = (object?)buyerId ?? DBNull.Value;
            cmd.Parameters.Add("@region", System.Data.SqlDbType.NVarChar, 40).Value = region;
            cmd.Parameters.Add("@submitted", System.Data.SqlDbType.Decimal).Value = (object?)submitted ?? DBNull.Value;
            cmd.Parameters.Add("@accepted", System.Data.SqlDbType.Decimal).Value = (object?)accepted ?? DBNull.Value;
            cmd.Parameters.Add("@potential", System.Data.SqlDbType.NVarChar, -1).Value = (object?)potentialProjects ?? DBNull.Value;
            cmd.Parameters.Add("@actor", System.Data.SqlDbType.NVarChar, 150).Value = $"{initiator} (BdTrackingImport)";
            var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            var newId = r is long lid ? lid : Convert.ToInt64(r);
            return new EngagementUpsertResult(newId, true);
        }
    }

    private static async Task<long> UpsertBdTrackingContactAsync(
        string connStr, long engagementId, string displayName, string? contactInfo, string actor, CancellationToken ct)
    {
        // Split contactInfo into email vs phone heuristically (most are emails).
        string? email = null, phone = null;
        if (!string.IsNullOrWhiteSpace(contactInfo))
        {
            if (contactInfo.Contains('@')) email = contactInfo.Trim();
            else phone = contactInfo.Trim();
        }

        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);

        const string lookupSql = "SELECT TOP 1 Id FROM opportunities.CrmContacts WHERE EngagementId = @eid AND DisplayName = @name;";
        await using (var cmd = new SqlCommand(lookupSql, con))
        {
            cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = engagementId;
            cmd.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 200).Value = displayName;
            var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (r is long lid) return lid;
            if (r is int iid) return iid;
        }

        const string insertSql = @"
INSERT INTO opportunities.CrmContacts (EngagementId, DisplayName, Email, Phone, IsPrimary, CreatedBy, UpdatedBy)
OUTPUT INSERTED.Id
VALUES (@eid, @name, @email, @phone, 0, @actor, @actor);";
        await using (var cmd = new SqlCommand(insertSql, con))
        {
            cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = engagementId;
            cmd.Parameters.Add("@name", System.Data.SqlDbType.NVarChar, 200).Value = displayName;
            cmd.Parameters.Add("@email", System.Data.SqlDbType.NVarChar, 300).Value = (object?)email ?? DBNull.Value;
            cmd.Parameters.Add("@phone", System.Data.SqlDbType.NVarChar, 60).Value = (object?)phone ?? DBNull.Value;
            cmd.Parameters.Add("@actor", System.Data.SqlDbType.NVarChar, 150).Value = $"{actor} (BdTrackingImport)";
            var r = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
            return r is long lid ? lid : Convert.ToInt64(r);
        }
    }

    private static async Task InsertBdTrackingActivityAsync(
        string connStr, long engagementId, BdTrackingRow row, long? contactId, string actor, CancellationToken ct)
    {
        var activityType = MapBdTrackingActivityType(row.Type);
        var subject = BuildBdTrackingSubject(row);
        var body = row.Notes;
        var occurredAt = row.Date ?? DateTimeOffset.UtcNow;

        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);
        const string sql = @"
INSERT INTO opportunities.CrmActivities (EngagementId, ActivityType, OccurredAtUtc, Subject, Body, ContactId, CreatedBy)
VALUES (@eid, @type, @occurred, @subject, @body, @contact, @actor);";
        await using var cmd = new SqlCommand(sql, con);
        cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = engagementId;
        cmd.Parameters.Add("@type", System.Data.SqlDbType.Int).Value = activityType;
        cmd.Parameters.Add("@occurred", System.Data.SqlDbType.DateTimeOffset).Value = occurredAt;
        cmd.Parameters.Add("@subject", System.Data.SqlDbType.NVarChar, 300).Value = subject;
        cmd.Parameters.Add("@body", System.Data.SqlDbType.NVarChar, -1).Value = (object?)body ?? DBNull.Value;
        cmd.Parameters.Add("@contact", System.Data.SqlDbType.BigInt).Value = (object?)contactId ?? DBNull.Value;
        cmd.Parameters.Add("@actor", System.Data.SqlDbType.NVarChar, 150).Value = $"{actor} (BdTrackingImport)";
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static int MapBdTrackingActivityType(string? type) => (type?.Trim().ToLowerInvariant()) switch
    {
        "meeting" => 3,
        "phone call" or "phone" or "call" => 2,
        "email" => 4,
        "rfp" => 6,
        "event" or "presentation" => 99,
        "note" => 1,
        _ => 99,
    };

    // Round 37a (T1.003): wipe BdTracking-created children for one engagement
    // so re-runs replace, not append. Activities first (they FK to contacts).
    // Filter on CreatedBy LIKE '%(BdTrackingImport)' so any future manually-
    // added activity/contact with a different actor display survives.
    private static async Task DeleteBdTrackingChildrenAsync(string connStr, long engagementId, CancellationToken ct)
    {
        await using var con = new SqlConnection(connStr);
        await con.OpenAsync(ct).ConfigureAwait(false);

        const string deleteActivitiesSql = @"
DELETE FROM opportunities.CrmActivities
WHERE  EngagementId = @eid
  AND  CreatedBy LIKE '%(BdTrackingImport)';";
        await using (var cmd = new SqlCommand(deleteActivitiesSql, con))
        {
            cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = engagementId;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }

        const string deleteContactsSql = @"
DELETE FROM opportunities.CrmContacts
WHERE  EngagementId = @eid
  AND  CreatedBy LIKE '%(BdTrackingImport)';";
        await using (var cmd = new SqlCommand(deleteContactsSql, con))
        {
            cmd.Parameters.Add("@eid", System.Data.SqlDbType.BigInt).Value = engagementId;
            await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }
    }

    private static string BuildBdTrackingSubject(BdTrackingRow row)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(row.Type)) parts.Add(row.Type);
        if (!string.IsNullOrWhiteSpace(row.Contact)) parts.Add(row.Contact!);
        if (!string.IsNullOrWhiteSpace(row.PotentialProjects)) parts.Add(Truncate(row.PotentialProjects!, 80));
        return parts.Count == 0 ? "(BD touchpoint)" : string.Join(" — ", parts);
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // Seismic-pipeline regions are sub-regional labels (LowerMainland, GreaterVictoria,
    // Okanagan, etc.) rather than 2-letter codes; the existing NormalizeProvince
    // would truncate "LowerMainland" to "LO". Map by prefix instead — WA-* / OR-*
    // are the cross-border programs, everything else is BC.
    private static string ProvinceFromSeismicRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region)) return string.Empty;
        var r = region.Trim();
        if (r.StartsWith("WA-", StringComparison.OrdinalIgnoreCase) || r.Equals("WA", StringComparison.OrdinalIgnoreCase)) return "WA";
        if (r.StartsWith("OR-", StringComparison.OrdinalIgnoreCase) || r.Equals("OR", StringComparison.OrdinalIgnoreCase)) return "OR";
        return "BC";
    }

    private static bool? ProvincialFlag(string? fundingSource)
    {
        if (string.IsNullOrWhiteSpace(fundingSource)) return null;
        var f = fundingSource.ToLowerInvariant();
        return f.Contains("province") || f.Contains("provincial") || f.Contains("joint") || f.Contains("mixed");
    }

    private static bool? FederalFlag(string? fundingSource)
    {
        if (string.IsNullOrWhiteSpace(fundingSource)) return null;
        var f = fundingSource.ToLowerInvariant();
        return f.Contains("federal") || f.Contains("joint") || f.Contains("mixed");
    }

    private static bool? MunicipalFlag(string? fundingSource)
    {
        if (string.IsNullOrWhiteSpace(fundingSource)) return null;
        var f = fundingSource.ToLowerInvariant();
        return f.Contains("municipal") || f.Contains("district") || f.Contains("mixed");
    }

    private static async Task<long?> UpsertOrgAsync(
        SqlCanonicalOrgStore? store,
        ImportOptions options,
        ImportStats stats,
        string kind,
        string displayName,
        string? website,
        string? notes,
        string source,
        CancellationToken ct)
    {
        stats.OrgsUpserted++;
        AddCount(stats.OrgsBySource, source);
        if (options.DryRun)
        {
            if (!options.Quiet)
            {
                Console.WriteLine($"[DRY-RUN] {source}: planned CanonicalOrg upsert kind={kind}; name={displayName}");
            }

            return null;
        }

        if (store is null)
        {
            throw new InvalidOperationException("Canonical org store is not available.");
        }

        var id = await store.UpsertCanonicalOrgAsync(kind, displayName, null, website, notes, ct).ConfigureAwait(false);
        if (!options.Quiet)
        {
            Console.WriteLine($"[ORG] {source}: {displayName} -> CanonicalOrgId={id}");
        }

        return id;
    }

    private static async Task WriteEnrichmentAsync(
        SqlEnrichmentTrackingStore? store,
        ImportOptions options,
        ImportStats stats,
        long? canonicalOrgId,
        string providerName,
        string resultJson,
        string? notes,
        string displayName,
        CancellationToken ct)
    {
        stats.EnrichmentRowsWritten++;
        AddCount(stats.EnrichmentRowsByProvider, providerName);
        if (options.DryRun)
        {
            if (!options.Quiet)
            {
                Console.WriteLine($"[DRY-RUN] {providerName}: planned enrichment upsert for {displayName}");
            }

            return;
        }

        if (store is null || !canonicalOrgId.HasValue)
        {
            throw new InvalidOperationException("Enrichment store or CanonicalOrgId is not available.");
        }

        await store.RecordAttemptAsync(
            canonicalOrgId.Value,
            providerName,
            new EnrichmentResult(EnrichmentStatuses.Ok, null, resultJson, notes),
            DateTimeOffset.UtcNow.AddDays(365),
            ct).ConfigureAwait(false);
        if (!options.Quiet)
        {
            Console.WriteLine($"[ENRICH] {providerName}: CanonicalOrgId={canonicalOrgId.Value}");
        }
    }

    private static async Task<long?> ResolveAsync(
        CanonicalOrgResolver? resolver,
        ImportOptions options,
        ImportStats stats,
        string? name,
        string kind,
        string source,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        if (source == ProponentSource)
        {
            stats.ProponentsResolved++;
        }
        else if (source == ArchitectSource)
        {
            stats.ArchitectsResolved++;
        }

        if (options.DryRun)
        {
            if (!options.Quiet)
            {
                Console.WriteLine($"[DRY-RUN] {source}: planned resolve/create kind={kind}; name={name.Trim()}");
            }

            return null;
        }

        if (resolver is null)
        {
            throw new InvalidOperationException("Canonical org resolver is not available.");
        }

        var id = await resolver.ResolveAsync(
            name,
            kind,
            source,
            ct,
            allowCreate: true,
            minConfidenceForCreate: 70).ConfigureAwait(false);
        if (!options.Quiet)
        {
            Console.WriteLine($"[RESOLVE] {source}: {name.Trim()} -> {id?.ToString(CultureInfo.InvariantCulture) ?? "(unresolved)"}");
        }

        return id;
    }

    // Round 60g (2026-06-03): backfill team FKs onto an EXISTING MPI row by
    // primary key. Only fills NULL columns - never overwrites existing
    // attributions. Used by the project-teams backfill path when an entry
    // includes mpiId. The Sonnet overnight session sourced rows from
    // SELECT * FROM opportunities.MajorProjectsInventory WHERE FK IS NULL,
    // so mpiId is authoritative.
    private static async Task BackfillMpiTeamAsync(
        ImportOptions options,
        ImportStats stats,
        long mpiId,
        long? architectId,
        long? structuralId,
        long? gcId,
        string? architectName,
        string? structuralName,
        string? gcName,
        CancellationToken ct)
    {
        if (architectId is null && structuralId is null && gcId is null)
        {
            stats.ProjectRowsSkipped++;
            if (!options.Quiet)
            {
                Console.WriteLine($"[SKIP] project-teams mpiId={mpiId}: no resolvable team IDs");
            }
            return;
        }

        if (options.DryRun)
        {
            if (!options.Quiet)
            {
                Console.WriteLine($"[DRY-RUN] project-teams mpiId={mpiId}: arch={architectId?.ToString() ?? "-"}; se={structuralId?.ToString() ?? "-"}; gc={gcId?.ToString() ?? "-"}");
            }
            return;
        }

        const string sql = @"
UPDATE opportunities.MajorProjectsInventory
SET
    ArchitectCanonicalOrgId           = COALESCE(ArchitectCanonicalOrgId,           @archId),
    StructuralEngineerCanonicalOrgId  = COALESCE(StructuralEngineerCanonicalOrgId,  @seId),
    GeneralContractorCanonicalOrgId   = COALESCE(GeneralContractorCanonicalOrgId,   @gcId),
    ArchitectName                     = CASE WHEN ArchitectCanonicalOrgId IS NULL AND @archName IS NOT NULL THEN @archName ELSE ArchitectName END,
    StructuralEngineerName            = CASE WHEN StructuralEngineerCanonicalOrgId IS NULL AND @seName IS NOT NULL THEN @seName ELSE StructuralEngineerName END,
    GeneralContractorName             = CASE WHEN GeneralContractorCanonicalOrgId IS NULL AND @gcName IS NOT NULL THEN @gcName ELSE GeneralContractorName END,
    UpdatedAtUtc                      = sysdatetimeoffset()
WHERE Id = @mpiId;";

        await using var con = new Microsoft.Data.SqlClient.SqlConnection(options.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, con) { CommandTimeout = 30 };
        cmd.Parameters.Add("@mpiId", System.Data.SqlDbType.BigInt).Value = mpiId;
        cmd.Parameters.Add("@archId", System.Data.SqlDbType.BigInt).Value = (object?)architectId ?? DBNull.Value;
        cmd.Parameters.Add("@seId", System.Data.SqlDbType.BigInt).Value = (object?)structuralId ?? DBNull.Value;
        cmd.Parameters.Add("@gcId", System.Data.SqlDbType.BigInt).Value = (object?)gcId ?? DBNull.Value;
        cmd.Parameters.Add("@archName", System.Data.SqlDbType.NVarChar, 400).Value = string.IsNullOrWhiteSpace(architectName) ? DBNull.Value : (object)architectName;
        cmd.Parameters.Add("@seName", System.Data.SqlDbType.NVarChar, 400).Value = string.IsNullOrWhiteSpace(structuralName) ? DBNull.Value : (object)structuralName;
        cmd.Parameters.Add("@gcName", System.Data.SqlDbType.NVarChar, 400).Value = string.IsNullOrWhiteSpace(gcName) ? DBNull.Value : (object)gcName;
        var rows = await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);

        if (rows == 0)
        {
            if (!options.Quiet)
            {
                Console.WriteLine($"[WARN] project-teams mpiId={mpiId}: row not found in opportunities.MajorProjectsInventory");
            }
            return;
        }

        IncrementProjectSource(stats, "project-teams-backfill");
        if (!options.Quiet)
        {
            Console.WriteLine($"[BACKFILL] mpiId={mpiId}: arch={architectId?.ToString() ?? "-"}; se={structuralId?.ToString() ?? "-"}; gc={gcId?.ToString() ?? "-"}");
        }
    }

    private static async Task UpsertMajorProjectAsync(ImportOptions options, ImportStats stats, MajorProjectRecord r, CancellationToken ct)
    {
        IncrementProjectSource(stats, r.Source);

        if (options.DryRun)
        {
            if (!options.Quiet)
            {
                Console.WriteLine($"[DRY-RUN] {r.Source}: planned MPI upsert {r.SourceKey}; project={r.ProjectName}");
            }

            return;
        }

        const string sql = @"
SET XACT_ABORT ON;

DECLARE @inserted table (Id bigint NOT NULL);
DECLARE @nameMatchedId bigint;

BEGIN TRAN;

UPDATE opportunities.MajorProjectsInventory WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
SET
    LastSeenAtUtc = sysdatetimeoffset(),
    UpdatedAtUtc = sysdatetimeoffset(),
    ExternalProjectId = @externalProjectId,
    ProjectName = @projectName,
    ProjectDescription = @projectDescription,
    EstimatedCostCad = @estimatedCostCad,
    EstimatedCostText = @estimatedCostText,
    Sector = @sector,
    SubSector = @subSector,
    ConstructionType = @constructionType,
    ConstructionSubtype = @constructionSubtype,
    ProjectType = @projectType,
    RegionName = @regionName,
    MunicipalityName = @municipalityName,
    ProponentName = @proponentName,
    ProponentCanonicalOrgId = @proponentCanonicalOrgId,
    ArchitectName = @architectName,
    ArchitectCanonicalOrgId = @architectCanonicalOrgId,
    Stage = @stage,
    ProjectStatus = @projectStatus,
    ProjectStage = @projectStage,
    KorPipelineTag = @korPipelineTag,
    ProjectCategoryName = @projectCategoryName,
    PublicFundingInd = @publicFundingInd,
    ProvincialFunding = @provincialFunding,
    FederalFunding = @federalFunding,
    MunicipalFunding = @municipalFunding,
    OtherPublicFunding = @otherPublicFunding,
    GreenBuildingInd = @greenBuildingInd,
    IndigenousInd = @indigenousInd,
    IndigenousNames = @indigenousNames,
    ConstructionJobs = @constructionJobs,
    OperatingJobs = @operatingJobs,
    StandardizedStartDate = @standardizedStartDate,
    StandardizedCompletionDate = @standardizedCompletionDate,
    StartYear = @startYear,
    CompletionYear = @completionYear,
    ScheduleNotes = @scheduleNotes,
    Latitude = @latitude,
    Longitude = @longitude,
    ProjectWebsite = @projectWebsite,
    SourceUrl = @sourceUrl,
    IssueYear = @issueYear,
    IssueQuarter = @issueQuarter,
    StructuralEngineerName = @structuralEngineerName,
    StructuralEngineerCanonicalOrgId = @structuralEngineerCanonicalOrgId,
    GeneralContractorName = @generalContractorName,
    GeneralContractorCanonicalOrgId = @generalContractorCanonicalOrgId,
    RawJson = @rawJson
WHERE Province = @province
  AND SourceKey = @sourceKey;

IF @@ROWCOUNT = 0
BEGIN
    -- C9 guard: the same project arriving via a different SourceKey must not fork
    -- a duplicate row. Match active rows on normalized name + compatible
    -- municipality; generic names known to repeat across distinct projects are
    -- exempt from the check.
    IF LOWER(LTRIM(RTRIM(@projectName))) NOT IN
        (N'condominium development', N'residential condominium', N'mixed-use development',
         N'condo development', N'apartment building')
    BEGIN
        SELECT TOP (1) @nameMatchedId = Id
        FROM opportunities.MajorProjectsInventory WITH (UPDLOCK, HOLDLOCK, ROWLOCK)
        WHERE RetiredAtUtc IS NULL
          AND Province = @province
          AND LOWER(LTRIM(RTRIM(ProjectName))) = LOWER(LTRIM(RTRIM(@projectName)))
          AND (MunicipalityName IS NULL OR LTRIM(RTRIM(MunicipalityName)) = N''
               OR @municipalityName IS NULL OR LTRIM(RTRIM(@municipalityName)) = N''
               OR LOWER(LTRIM(RTRIM(MunicipalityName))) = LOWER(LTRIM(RTRIM(@municipalityName))))
        ORDER BY Id;
    END;

    IF @nameMatchedId IS NOT NULL
    BEGIN
        -- Same project under another SourceKey: refresh seen timestamps and
        -- COALESCE-fill gaps only; never overwrite existing non-null values.
        UPDATE opportunities.MajorProjectsInventory
        SET
            LastSeenAtUtc = sysdatetimeoffset(),
            UpdatedAtUtc = sysdatetimeoffset(),
            ExternalProjectId = COALESCE(ExternalProjectId, @externalProjectId),
            ProjectDescription = COALESCE(ProjectDescription, @projectDescription),
            EstimatedCostCad = COALESCE(EstimatedCostCad, @estimatedCostCad),
            EstimatedCostText = COALESCE(EstimatedCostText, @estimatedCostText),
            Sector = COALESCE(Sector, @sector),
            SubSector = COALESCE(SubSector, @subSector),
            ConstructionType = COALESCE(ConstructionType, @constructionType),
            ConstructionSubtype = COALESCE(ConstructionSubtype, @constructionSubtype),
            ProjectType = COALESCE(ProjectType, @projectType),
            RegionName = COALESCE(RegionName, @regionName),
            MunicipalityName = COALESCE(MunicipalityName, @municipalityName),
            ProponentName = COALESCE(ProponentName, @proponentName),
            ProponentCanonicalOrgId = COALESCE(ProponentCanonicalOrgId, @proponentCanonicalOrgId),
            ArchitectName = COALESCE(ArchitectName, @architectName),
            ArchitectCanonicalOrgId = COALESCE(ArchitectCanonicalOrgId, @architectCanonicalOrgId),
            Stage = COALESCE(Stage, @stage),
            ProjectStatus = COALESCE(ProjectStatus, @projectStatus),
            ProjectStage = COALESCE(ProjectStage, @projectStage),
            KorPipelineTag = COALESCE(KorPipelineTag, @korPipelineTag),
            ProjectCategoryName = COALESCE(ProjectCategoryName, @projectCategoryName),
            PublicFundingInd = COALESCE(PublicFundingInd, @publicFundingInd),
            ProvincialFunding = COALESCE(ProvincialFunding, @provincialFunding),
            FederalFunding = COALESCE(FederalFunding, @federalFunding),
            MunicipalFunding = COALESCE(MunicipalFunding, @municipalFunding),
            OtherPublicFunding = COALESCE(OtherPublicFunding, @otherPublicFunding),
            GreenBuildingInd = COALESCE(GreenBuildingInd, @greenBuildingInd),
            IndigenousInd = COALESCE(IndigenousInd, @indigenousInd),
            IndigenousNames = COALESCE(IndigenousNames, @indigenousNames),
            ConstructionJobs = COALESCE(ConstructionJobs, @constructionJobs),
            OperatingJobs = COALESCE(OperatingJobs, @operatingJobs),
            StandardizedStartDate = COALESCE(StandardizedStartDate, @standardizedStartDate),
            StandardizedCompletionDate = COALESCE(StandardizedCompletionDate, @standardizedCompletionDate),
            StartYear = COALESCE(StartYear, @startYear),
            CompletionYear = COALESCE(CompletionYear, @completionYear),
            ScheduleNotes = COALESCE(ScheduleNotes, @scheduleNotes),
            Latitude = COALESCE(Latitude, @latitude),
            Longitude = COALESCE(Longitude, @longitude),
            ProjectWebsite = COALESCE(ProjectWebsite, @projectWebsite),
            SourceUrl = COALESCE(SourceUrl, @sourceUrl),
            IssueYear = COALESCE(IssueYear, @issueYear),
            IssueQuarter = COALESCE(IssueQuarter, @issueQuarter),
            StructuralEngineerName = COALESCE(StructuralEngineerName, @structuralEngineerName),
            StructuralEngineerCanonicalOrgId = COALESCE(StructuralEngineerCanonicalOrgId, @structuralEngineerCanonicalOrgId),
            GeneralContractorName = COALESCE(GeneralContractorName, @generalContractorName),
            GeneralContractorCanonicalOrgId = COALESCE(GeneralContractorCanonicalOrgId, @generalContractorCanonicalOrgId),
            RawJson = COALESCE(RawJson, @rawJson)
        WHERE Id = @nameMatchedId;
    END
    ELSE
    BEGIN
        INSERT INTO opportunities.MajorProjectsInventory
            (Province, SourceKey, ExternalProjectId, ProjectName, ProjectDescription, EstimatedCostCad,
             EstimatedCostText, Sector, SubSector, ConstructionType, ConstructionSubtype, ProjectType,
             RegionName, MunicipalityName, ProponentName, ProponentCanonicalOrgId, ArchitectName,
             ArchitectCanonicalOrgId, Stage, ProjectStatus, ProjectStage, KorPipelineTag, ProjectCategoryName,
             PublicFundingInd, ProvincialFunding, FederalFunding, MunicipalFunding, OtherPublicFunding,
             GreenBuildingInd, IndigenousInd, IndigenousNames, ConstructionJobs, OperatingJobs,
             StandardizedStartDate, StandardizedCompletionDate, StartYear, CompletionYear,
             ScheduleNotes, Latitude, Longitude, ProjectWebsite, SourceUrl, IssueYear,
             IssueQuarter, StructuralEngineerName, StructuralEngineerCanonicalOrgId,
             GeneralContractorName, GeneralContractorCanonicalOrgId, RawJson)
        OUTPUT inserted.Id INTO @inserted
        VALUES
            (@province, @sourceKey, @externalProjectId, @projectName, @projectDescription, @estimatedCostCad,
             @estimatedCostText, @sector, @subSector, @constructionType, @constructionSubtype, @projectType,
             @regionName, @municipalityName, @proponentName, @proponentCanonicalOrgId, @architectName,
             @architectCanonicalOrgId, @stage, @projectStatus, @projectStage, @korPipelineTag, @projectCategoryName,
             @publicFundingInd, @provincialFunding, @federalFunding, @municipalFunding, @otherPublicFunding,
             @greenBuildingInd, @indigenousInd, @indigenousNames, @constructionJobs, @operatingJobs,
             @standardizedStartDate, @standardizedCompletionDate, @startYear, @completionYear,
             @scheduleNotes, @latitude, @longitude, @projectWebsite, @sourceUrl, @issueYear,
             @issueQuarter, @structuralEngineerName, @structuralEngineerCanonicalOrgId,
             @generalContractorName, @generalContractorCanonicalOrgId, @rawJson);
    END;
END;

COMMIT TRAN;

SELECT
    CASE WHEN EXISTS (SELECT 1 FROM @inserted) THEN 1
         WHEN @nameMatchedId IS NOT NULL THEN 2
         ELSE 0 END AS Outcome,
    @nameMatchedId AS NameMatchedId;";

        await using var con = new SqlConnection(options.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        AddParams(cmd, r);
        long? nameMatchedId = null;
        await using (var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            await reader.ReadAsync(ct).ConfigureAwait(false);
            var outcome = Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture);
            if (outcome == 2)
            {
                nameMatchedId = reader.GetInt64(1);
            }
        }

        if (nameMatchedId.HasValue)
        {
            stats.MpiNameMatchDedups++;
            Console.WriteLine($"[MPI] {r.Source}: name-matched existing MPI {nameMatchedId.Value}; project={r.ProjectName} (new SourceKey {r.SourceKey} not inserted)");
        }
        else if (!options.Quiet)
        {
            Console.WriteLine($"[MPI] {r.Source}: {r.SourceKey}; project={r.ProjectName}");
        }
    }

    private static async Task<bool> UpdateMajorProjectFromHoningAsync(
        ImportOptions options,
        long id,
        string? architectName,
        long? architectCanonicalOrgId,
        string? structuralEngineerName,
        long? structuralEngineerCanonicalOrgId,
        string? generalContractorName,
        long? generalContractorCanonicalOrgId,
        string? proponentName,
        long? proponentCanonicalOrgId,
        string? stage,
        short? completionYear,
        CancellationToken ct)
    {
        if (options.DryRun)
        {
            return true;
        }

        const string sql = @"
UPDATE opportunities.MajorProjectsInventory WITH (UPDLOCK, ROWLOCK)
SET
    ArchitectName = COALESCE(@architectName, ArchitectName),
    StructuralEngineerName = COALESCE(@structuralEngineerName, StructuralEngineerName),
    GeneralContractorName = COALESCE(@generalContractorName, GeneralContractorName),
    ProponentName = COALESCE(@proponentName, ProponentName),
    Stage = COALESCE(@stage, Stage),
    CompletionYear = COALESCE(@completionYear, CompletionYear),
    ArchitectCanonicalOrgId = COALESCE(ArchitectCanonicalOrgId, @architectCanonicalOrgId),
    StructuralEngineerCanonicalOrgId = COALESCE(StructuralEngineerCanonicalOrgId, @structuralEngineerCanonicalOrgId),
    GeneralContractorCanonicalOrgId = COALESCE(GeneralContractorCanonicalOrgId, @generalContractorCanonicalOrgId),
    ProponentCanonicalOrgId = COALESCE(ProponentCanonicalOrgId, @proponentCanonicalOrgId),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id;";

        await using var con = new SqlConnection(options.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        AddLong(cmd, "@id", id);
        AddString(cmd, "@architectName", architectName, 500);
        AddLong(cmd, "@architectCanonicalOrgId", architectCanonicalOrgId);
        AddString(cmd, "@structuralEngineerName", structuralEngineerName, 500);
        AddLong(cmd, "@structuralEngineerCanonicalOrgId", structuralEngineerCanonicalOrgId);
        AddString(cmd, "@generalContractorName", generalContractorName, 500);
        AddLong(cmd, "@generalContractorCanonicalOrgId", generalContractorCanonicalOrgId);
        AddString(cmd, "@proponentName", proponentName, 500);
        AddLong(cmd, "@proponentCanonicalOrgId", proponentCanonicalOrgId);
        AddString(cmd, "@stage", stage, 50);
        AddShort(cmd, "@completionYear", completionYear);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static async Task<bool> UpdateMajorProjectSeatAsync(
        ImportOptions options,
        long id,
        string? architectName,
        long? architectCanonicalOrgId,
        string? structuralEngineerName,
        long? structuralEngineerCanonicalOrgId,
        string? generalContractorName,
        long? generalContractorCanonicalOrgId,
        string? seatStatus,
        string? korSeatOpening,
        string? seatConfidence,
        CancellationToken ct)
    {
        if (options.DryRun)
        {
            return true;
        }

        const string sql = @"
UPDATE opportunities.MajorProjectsInventory WITH (UPDLOCK, ROWLOCK)
SET
    ArchitectName = COALESCE(ArchitectName, @architectName),
    ArchitectCanonicalOrgId = COALESCE(ArchitectCanonicalOrgId, @architectCanonicalOrgId),
    StructuralEngineerName = COALESCE(StructuralEngineerName, @structuralEngineerName),
    StructuralEngineerCanonicalOrgId = COALESCE(StructuralEngineerCanonicalOrgId, @structuralEngineerCanonicalOrgId),
    GeneralContractorName = COALESCE(GeneralContractorName, @generalContractorName),
    GeneralContractorCanonicalOrgId = COALESCE(GeneralContractorCanonicalOrgId, @generalContractorCanonicalOrgId),
    SeatStatus = @seatStatus,
    KorSeatOpening = @korSeatOpening,
    SeatConfidence = @seatConfidence,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id;";

        await using var con = new SqlConnection(options.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        AddLong(cmd, "@id", id);
        AddString(cmd, "@architectName", architectName, 500);
        AddLong(cmd, "@architectCanonicalOrgId", architectCanonicalOrgId);
        AddString(cmd, "@structuralEngineerName", structuralEngineerName, 500);
        AddLong(cmd, "@structuralEngineerCanonicalOrgId", structuralEngineerCanonicalOrgId);
        AddString(cmd, "@generalContractorName", generalContractorName, 500);
        AddLong(cmd, "@generalContractorCanonicalOrgId", generalContractorCanonicalOrgId);
        AddString(cmd, "@seatStatus", seatStatus, 20);
        AddString(cmd, "@korSeatOpening", korSeatOpening, 500);
        AddString(cmd, "@seatConfidence", seatConfidence, 20);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static async Task<bool> UpdateMajorProjectReverifyAsync(
        ImportOptions options,
        long id,
        string? statusVerdict,
        bool retire,
        string? architectName,
        long? architectCanonicalOrgId,
        string? structuralEngineerName,
        long? structuralEngineerCanonicalOrgId,
        CancellationToken ct)
    {
        if (options.DryRun)
        {
            return true;
        }

        const string sql = @"
UPDATE opportunities.MajorProjectsInventory WITH (UPDLOCK, ROWLOCK)
SET
    Stage = @statusVerdict,
    LastVerifiedAtUtc = sysdatetimeoffset(),
    RetiredAtUtc = CASE WHEN @retire = 1 THEN sysdatetimeoffset() ELSE RetiredAtUtc END,
    RetiredReason = CASE WHEN @retire = 1 THEN LEFT(N'Re-verify: ' + COALESCE(@statusVerdict, N'(blank)'), 200) ELSE RetiredReason END,
    ArchitectName = COALESCE(ArchitectName, @architectName),
    ArchitectCanonicalOrgId = COALESCE(ArchitectCanonicalOrgId, @architectCanonicalOrgId),
    StructuralEngineerName = COALESCE(StructuralEngineerName, @structuralEngineerName),
    StructuralEngineerCanonicalOrgId = COALESCE(StructuralEngineerCanonicalOrgId, @structuralEngineerCanonicalOrgId),
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id;";

        await using var con = new SqlConnection(options.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        AddLong(cmd, "@id", id);
        AddString(cmd, "@statusVerdict", statusVerdict, 50);
        AddBool(cmd, "@retire", retire);
        AddString(cmd, "@architectName", architectName, 500);
        AddLong(cmd, "@architectCanonicalOrgId", architectCanonicalOrgId);
        AddString(cmd, "@structuralEngineerName", structuralEngineerName, 500);
        AddLong(cmd, "@structuralEngineerCanonicalOrgId", structuralEngineerCanonicalOrgId);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static async Task<bool> UpdateMajorProjectArchitectForecastAsync(
        ImportOptions options,
        long id,
        string? architectName,
        long? architectCanonicalOrgId,
        CancellationToken ct)
    {
        if (options.DryRun)
        {
            return true;
        }

        const string sql = @"
UPDATE opportunities.MajorProjectsInventory WITH (UPDLOCK, ROWLOCK)
SET
    ArchitectName = @architectName,
    ArchitectCanonicalOrgId = @architectCanonicalOrgId,
    UpdatedAtUtc = sysdatetimeoffset()
WHERE Id = @id
  AND ArchitectCanonicalOrgId IS NULL;";

        await using var con = new SqlConnection(options.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        AddLong(cmd, "@id", id);
        AddString(cmd, "@architectName", architectName, 500);
        AddLong(cmd, "@architectCanonicalOrgId", architectCanonicalOrgId);

        return await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
    }

    private static void IncrementProjectSource(ImportStats stats, string source)
    {
        stats.ProjectUpsertsBySource.TryGetValue(source, out var count);
        stats.ProjectUpsertsBySource[source] = count + 1;
    }

    private static void AddParams(SqlCommand cmd, MajorProjectRecord r)
    {
        AddString(cmd, "@province", r.Province, 2);
        AddString(cmd, "@sourceKey", r.SourceKey, 200);
        AddString(cmd, "@externalProjectId", null, 50);
        AddString(cmd, "@projectName", r.ProjectName, 500);
        AddString(cmd, "@projectDescription", r.ProjectDescription, -1);
        AddDecimal(cmd, "@estimatedCostCad", r.EstimatedCostCad, 18, 0);
        AddString(cmd, "@estimatedCostText", r.EstimatedCostText, 200);
        AddString(cmd, "@sector", r.Sector, 100);
        AddString(cmd, "@subSector", r.SubSector, 100);
        AddString(cmd, "@constructionType", r.ConstructionType, 100);
        AddString(cmd, "@constructionSubtype", r.ConstructionSubtype, 100);
        AddString(cmd, "@projectType", r.ProjectType, 100);
        AddString(cmd, "@regionName", r.RegionName, 200);
        AddString(cmd, "@municipalityName", r.MunicipalityName, 200);
        AddString(cmd, "@proponentName", r.ProponentName, 500);
        AddLong(cmd, "@proponentCanonicalOrgId", r.ProponentCanonicalOrgId);
        AddString(cmd, "@architectName", r.ArchitectName, 500);
        AddLong(cmd, "@architectCanonicalOrgId", r.ArchitectCanonicalOrgId);
        AddString(cmd, "@stage", r.Stage, 50);
        AddString(cmd, "@projectStatus", r.ProjectStatus, 100);
        ProjectStageRouter.Route(r.ProjectStage, out var routedStage, out var routedTag, out _);
        AddString(cmd, "@projectStage", routedStage, 100);
        AddString(cmd, "@korPipelineTag", routedTag, 80);
        AddString(cmd, "@projectCategoryName", r.ProjectCategoryName, 200);
        AddBool(cmd, "@publicFundingInd", r.PublicFundingInd);
        AddBool(cmd, "@provincialFunding", r.ProvincialFunding);
        AddBool(cmd, "@federalFunding", r.FederalFunding);
        AddBool(cmd, "@municipalFunding", r.MunicipalFunding);
        AddBool(cmd, "@otherPublicFunding", r.OtherPublicFunding);
        AddBool(cmd, "@greenBuildingInd", r.GreenBuildingInd);
        AddBool(cmd, "@indigenousInd", r.IndigenousInd);
        AddString(cmd, "@indigenousNames", r.IndigenousNames, 500);
        AddInt(cmd, "@constructionJobs", r.ConstructionJobs);
        AddInt(cmd, "@operatingJobs", r.OperatingJobs);
        AddString(cmd, "@standardizedStartDate", r.StandardizedStartDate, 20);
        AddString(cmd, "@standardizedCompletionDate", r.StandardizedCompletionDate, 20);
        AddShort(cmd, "@startYear", r.StartYear);
        AddShort(cmd, "@completionYear", r.CompletionYear);
        AddString(cmd, "@scheduleNotes", r.ScheduleNotes, 1000);
        AddDecimal(cmd, "@latitude", r.Latitude, 9, 6);
        AddDecimal(cmd, "@longitude", r.Longitude, 9, 6);
        AddString(cmd, "@projectWebsite", r.ProjectWebsite, 1000);
        AddString(cmd, "@sourceUrl", r.SourceUrl, 1000);
        AddShort(cmd, "@issueYear", null);
        AddByte(cmd, "@issueQuarter", null);
        AddString(cmd, "@structuralEngineerName", r.StructuralEngineerName, 500);
        AddLong(cmd, "@structuralEngineerCanonicalOrgId", r.StructuralEngineerCanonicalOrgId);
        AddString(cmd, "@generalContractorName", r.GeneralContractorName, 500);
        AddLong(cmd, "@generalContractorCanonicalOrgId", r.GeneralContractorCanonicalOrgId);
        AddString(cmd, "@rawJson", r.RawJson, -1);
    }

    private static bool TryLoadJson(string path, out JsonDocument document)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"[WARN] Missing payload: {path}");
            document = null!;
            return false;
        }

        document = JsonDocument.Parse(File.ReadAllText(path), JsonOptions);
        Console.WriteLine($"[FILE] {path}");
        return true;
    }

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement root, string propertyName)
    {
        if (root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                yield return item;
            }
        }
    }

    private static string MapPartnerKind(string? value)
    {
        var normalized = NormalizeToken(value);
        var researchKind = MapResearchFirmKind(value);
        if (researchKind != OrgKinds.Unknown) return researchKind;
        if (normalized.Length == 0) return OrgKinds.Unknown;
        if (normalized.Contains("client", StringComparison.Ordinal) || normalized.Contains("buyer", StringComparison.Ordinal) || normalized.Contains("public", StringComparison.Ordinal)) return ClientKind;
        if (normalized.Contains("developer", StringComparison.Ordinal) || normalized.Contains("development", StringComparison.Ordinal) || normalized.Contains("devcorp", StringComparison.Ordinal)) return DeveloperKind;
        if (normalized.Contains("owner", StringComparison.Ordinal) || normalized.Contains("proponent", StringComparison.Ordinal)) return OwnerKind;
        if (normalized.Contains("vendor", StringComparison.Ordinal) || normalized.Contains("supplier", StringComparison.Ordinal) || normalized.Contains("partner", StringComparison.Ordinal)) return OrgKinds.Vendor;
        if (normalized.Contains("contractor", StringComparison.Ordinal)) return ContractorKind;
        return OrgKinds.Unknown;
    }

    private static string MapResearchFirmKind(string? value)
    {
        var normalized = NormalizeToken(value);
        if (normalized.Length == 0) return OrgKinds.Unknown;
        if (normalized.Contains("competitor", StringComparison.Ordinal)) return OrgKinds.Competitor;
        if (normalized is "gc" || normalized.Contains("generalcontractor", StringComparison.Ordinal) || normalized.Contains("contractor", StringComparison.Ordinal)) return OrgKinds.GeneralContractor;
        if (normalized.Contains("architect", StringComparison.Ordinal) || normalized.Contains("architecture", StringComparison.Ordinal)) return OrgKinds.Architect;
        if (normalized.Contains("developer", StringComparison.Ordinal) || normalized.Contains("development", StringComparison.Ordinal)) return OrgKinds.Developer;
        return OrgKinds.Unknown;
    }

    private static string NormalizeProvince(string? value, string fallback)
    {
        return ProvinceNormalizer.Normalize(value, fallback);
    }

    private static string NormalizeToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var sb = new StringBuilder(value.Length);
        foreach (var ch in value.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch))
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    private static string Slug(string? value)
    {
        var slug = NormalizeToken(value);
        return slug.Length == 0 ? "unknown" : slug;
    }

    private static string? BuildIndigenousScheduleNotes(string? timeline, string? structuralEngineer)
    {
        var notes = new List<(string Label, string? Value)> { ("Timeline", timeline) };
        if (!string.IsNullOrWhiteSpace(structuralEngineer) && KorRegex.IsMatch(structuralEngineer))
        {
            notes.Add(("StructuralEngineer", structuralEngineer));
        }

        return JoinNotes(notes.ToArray());
    }

    private static string? JoinNotes(params (string Label, string? Value)[] parts)
    {
        var values = parts
            .Where(p => !string.IsNullOrWhiteSpace(p.Value))
            .Select(p => $"{p.Label}: {p.Value!.Trim()}")
            .ToList();
        return values.Count == 0 ? null : string.Join("; ", values);
    }

    private static string Sha1(string value)
    {
        var hash = SHA1.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash);
    }

    private static string? String(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => NullIfBlank(value.GetString()),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => NullIfBlank(value.ToString()),
        };
    }

    private static decimal? Money(JsonElement element, string propertyName)
    {
        var value = Decimal(element, propertyName);
        return value.HasValue ? decimal.Round(value.Value, 0) : null;
    }

    private static decimal? UsdToCad(decimal? usd, decimal fxRate)
        => usd.HasValue ? decimal.Round(usd.Value * fxRate, 0) : null;

    private static string? UsdCostText(decimal? usd, decimal fxRate)
        => usd.HasValue
            ? $"USD {usd.Value.ToString("N0", CultureInfo.InvariantCulture)} @{fxRate.ToString(CultureInfo.InvariantCulture)} ({DateTime.UtcNow:yyyy-MM-dd})"
            : null;

    private static bool IsUsd(string? currency)
        => string.Equals(currency?.Trim(), "USD", StringComparison.OrdinalIgnoreCase);

    private static string ProvinceFromMarket(string? province, string? market)
    {
        if (!string.IsNullOrWhiteSpace(province))
        {
            return NormalizeProvince(province, Province);
        }

        var normalized = NormalizeToken(market);
        if (normalized.Contains("alberta", StringComparison.Ordinal) || normalized == "ab") return "AB";
        if (normalized.Contains("britishcolumbia", StringComparison.Ordinal) || normalized.Contains("vancouver", StringComparison.Ordinal) || normalized == "bc") return "BC";
        if (normalized.Contains("california", StringComparison.Ordinal) || normalized.Contains("losangeles", StringComparison.Ordinal) || normalized == "la") return "CA";
        if (normalized.Contains("washington", StringComparison.Ordinal) || normalized.Contains("seattle", StringComparison.Ordinal) || normalized.Contains("pacnw", StringComparison.Ordinal) || normalized.Contains("pacificnorthwest", StringComparison.Ordinal)) return "WA";
        if (normalized.Contains("oregon", StringComparison.Ordinal) || normalized.Contains("portland", StringComparison.Ordinal)) return "OR";
        return Province;
    }

    private static string ProvinceFromResearchMarket(string? market)
    {
        var normalized = NormalizeToken(market);
        return normalized.Contains("alberta", StringComparison.Ordinal)
            || normalized.Contains("calgary", StringComparison.Ordinal)
            || normalized.Contains("edmonton", StringComparison.Ordinal)
            // "Other AB" / "Central AB" style markets normalize to *ab; no BC
            // research market ends in "ab" (m123 root-cause: CapitalPlans rows
            // with market='Other AB' were stamped BC).
            || normalized.EndsWith("ab", StringComparison.Ordinal)
                ? "AB"
                : "BC";
    }

    private static void AddCount(Dictionary<string, int> counts, string key)
    {
        counts.TryGetValue(key, out var current);
        counts[key] = current + 1;
    }

    private static decimal? Decimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var d))
        {
            return d;
        }

        var text = String(element, propertyName);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var normalized = text.Replace("$", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal).Trim();
        return decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? CostText(JsonElement element, string propertyName)
        => String(element, propertyName);

    private static short? Short(JsonElement element, string propertyName)
    {
        var text = String(element, propertyName);
        if (string.IsNullOrWhiteSpace(text)) return null;
        return short.TryParse(text.Replace(",", "", StringComparison.Ordinal).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static string? NullIfBlank(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static void AddString(SqlCommand cmd, string name, string? value, int size)
    {
        var parameter = cmd.Parameters.Add(name, SqlDbType.NVarChar, size);
        if (string.IsNullOrWhiteSpace(value))
        {
            parameter.Value = DBNull.Value;
            return;
        }

        var trimmed = value.Trim();
        parameter.Value = size > 0 && trimmed.Length > size
            ? trimmed.Substring(0, size)
            : trimmed;
    }

    private static void AddDecimal(SqlCommand cmd, string name, decimal? value, byte precision, byte scale)
    {
        var parameter = cmd.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = precision;
        parameter.Scale = scale;
        parameter.Value = value.HasValue ? (object)value.Value : DBNull.Value;
    }

    private static void AddLong(SqlCommand cmd, string name, long? value)
        => cmd.Parameters.Add(name, SqlDbType.BigInt).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void AddInt(SqlCommand cmd, string name, int? value)
        => cmd.Parameters.Add(name, SqlDbType.Int).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void AddShort(SqlCommand cmd, string name, short? value)
        => cmd.Parameters.Add(name, SqlDbType.SmallInt).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void AddByte(SqlCommand cmd, string name, byte? value)
        => cmd.Parameters.Add(name, SqlDbType.TinyInt).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void AddBool(SqlCommand cmd, string name, bool? value)
        => cmd.Parameters.Add(name, SqlDbType.Bit).Value = value.HasValue ? (object)value.Value : DBNull.Value;

    private static void WriteSummary(ImportOptions options, ImportStats stats, TimeSpan elapsed)
    {
        Console.WriteLine($"BD Research import (dry-run={options.DryRun.ToString().ToLowerInvariant()}) complete in {elapsed}.");
        Console.WriteLine($"  Orgs upserted:             {stats.OrgsUpserted}");
        Console.WriteLine($"  Enrichment rows written:   {stats.EnrichmentRowsWritten}");
        Console.WriteLine($"  Project rows skipped:      {stats.ProjectRowsSkipped}");
        Console.WriteLine($"  Org rows skipped:          {stats.OrgRowsSkipped}");
        Console.WriteLine($"  Proponents resolved:       {stats.ProponentsResolved}");
        Console.WriteLine($"  Architects resolved:       {stats.ArchitectsResolved}");
        Console.WriteLine($"  Missing payloads:          {stats.FilesMissing}");
        Console.WriteLine($"  SourceKey collisions:      {stats.SourceKeyCollisions}");
        Console.WriteLine($"  MPI name-match dedups:     {stats.MpiNameMatchDedups}");
        Console.WriteLine($"  Orgs reclassified:         {stats.OrgsReclassified}");
        Console.WriteLine($"  Orgs hidden:               {stats.OrgsHidden}");
        Console.WriteLine("  Orgs upserted per source:");
        foreach (var (source, count) in stats.OrgsBySource.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"    {source}: {count}");
        }

        Console.WriteLine("  Enrichment rows per provider:");
        foreach (var (provider, count) in stats.EnrichmentRowsByProvider.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"    {provider}: {count}");
        }

        Console.WriteLine("  Projects upserted per source:");
        foreach (var (source, count) in stats.ProjectUpsertsBySource.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine($"    {source}: {count}");
        }
    }

    private sealed record ImportOptions(
        string BaseDirectory,
        string OpportunitiesDb,
        bool DryRun,
        bool Quiet,
        decimal FxRate,
        string? Only,
        string? PipelinesFile,
        string? IngestCanonicalFolder,
        string? IngestCanonicalProviderOverride,
        bool StrictCanonicalSchema)
    {
        public static ImportOptions Parse(string[] args)
        {
            var baseDir = DefaultBaseDirectory;
            var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB") ?? string.Empty;
            var dryRun = false;
            var quiet = false;
            var fxRate = 1.36m;
            string? only = null;
            string? pipelinesFile = null;
            string? ingestCanonicalFolder = null;
            string? ingestCanonicalProviderOverride = null;
            var strictCanonicalSchema = false;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--base":
                        baseDir = RequireValue(args, ref i, "--base");
                        break;
                    case "--db":
                        db = RequireValue(args, ref i, "--db");
                        break;
                    case "--dry-run":
                        dryRun = true;
                        break;
                    case "--quiet":
                        quiet = true;
                        break;
                    case "--fx-rate":
                        fxRate = ParseFxRate(RequireValue(args, ref i, "--fx-rate"));
                        break;
                    case "--only":
                        only = RequireValue(args, ref i, "--only");
                        break;
                    case "--pipelines-file":
                        pipelinesFile = RequireValue(args, ref i, "--pipelines-file");
                        break;
                    case "--ingest-canonical":
                        ingestCanonicalFolder = RequireValue(args, ref i, "--ingest-canonical");
                        break;
                    case "--provider":
                        ingestCanonicalProviderOverride = RequireValue(args, ref i, "--provider");
                        break;
                    case "--strict":
                        strictCanonicalSchema = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'.");
                }
            }

            return new ImportOptions(
                baseDir,
                db,
                dryRun,
                quiet,
                fxRate,
                only,
                pipelinesFile,
                ingestCanonicalFolder,
                ingestCanonicalProviderOverride,
                strictCanonicalSchema);
        }

        private static decimal ParseFxRate(string value)
        {
            if (decimal.TryParse(value, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed)
                && parsed > 0)
            {
                return parsed;
            }

            throw new ArgumentException("--fx-rate requires a positive decimal value.");
        }

        private static string RequireValue(string[] args, ref int i, string name)
        {
            if (i + 1 >= args.Length)
            {
                throw new ArgumentException($"{name} requires a value.");
            }

            i++;
            return args[i];
        }
    }

    private sealed class ImportStats
    {
        public int OrgsUpserted { get; set; }
        public int EnrichmentRowsWritten { get; set; }
        public int ProjectRowsSkipped { get; set; }
        public int OrgRowsSkipped { get; set; }
        public int FilesMissing { get; set; }
        public int ProponentsResolved { get; set; }
        public int ArchitectsResolved { get; set; }
        public int SourceKeyCollisions { get; set; }
        public int MpiNameMatchDedups { get; set; }
        public int OrgsReclassified { get; set; }
        public int OrgsHidden { get; set; }
        public Dictionary<string, int> OrgsBySource { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> EnrichmentRowsByProvider { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ProjectUpsertsBySource { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CanonicalIngestStats
    {
        public int FilesWalked { get; set; }
        public int FilesWithRecords { get; set; }
        public int FilesSkippedNoParseable { get; set; }
        public int CanonicalOrgCreates { get; set; }
        public int CanonicalOrgMatches { get; set; }
        public int AggressiveKeyMatches { get; set; }
        public int StrictViolations { get; set; }
        public Dictionary<string, int> IngestedByProvider { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> SkippedByReason { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> AliasFallbacksByField { get; } = new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CanonicalIngestFileStats
    {
        public int RecordCount { get; set; }
        public int Ingested { get; set; }
        public int Skipped { get; set; }
        public int AliasFallbacks { get; set; }
    }

    private sealed record MajorProjectRecord(
        string Source,
        string SourceKey,
        string ProjectName,
        string? ProjectDescription,
        decimal? EstimatedCostCad,
        string? EstimatedCostText,
        string? Sector,
        string? SubSector,
        string? ConstructionType,
        string? ConstructionSubtype,
        string? ProjectType,
        string? RegionName,
        string? MunicipalityName,
        string? ProponentName,
        long? ProponentCanonicalOrgId,
        string? ArchitectName,
        long? ArchitectCanonicalOrgId,
        string? Stage,
        string? ProjectStatus,
        string? ProjectStage,
        string? ProjectCategoryName,
        bool? PublicFundingInd,
        bool? ProvincialFunding,
        bool? FederalFunding,
        bool? MunicipalFunding,
        bool? OtherPublicFunding,
        bool? GreenBuildingInd,
        bool? IndigenousInd,
        string? IndigenousNames,
        int? ConstructionJobs,
        int? OperatingJobs,
        string? StandardizedStartDate,
        string? StandardizedCompletionDate,
        short? StartYear,
        short? CompletionYear,
        string? ScheduleNotes,
        decimal? Latitude,
        decimal? Longitude,
        string? ProjectWebsite,
        string? SourceUrl,
        string RawJson)
    {
        public string Province { get; init; } = Program.Province;
        public string? StructuralEngineerName { get; init; }
        public long? StructuralEngineerCanonicalOrgId { get; init; }
        public string? GeneralContractorName { get; init; }
        public long? GeneralContractorCanonicalOrgId { get; init; }
    }

    private sealed record KorCapabilityProject(
        string ProjectName,
        string? City,
        string? Market,
        short? CompletionYear,
        string? Sector,
        IReadOnlyList<string> StructuralSystems,
        string? Owner,
        string? Architect,
        string? GeneralContractor,
        JsonElement? Scale,
        string? NotableFeatures,
        IReadOnlyList<string> Awards,
        string? CreditedFirmName,
        decimal? SystemConfidence,
        IReadOnlyList<string> SourceUrls);

    private sealed record KorCapabilityRosterMember(
        string? Name,
        string? Title,
        IReadOnlyList<string> Credentials,
        string? Specialty,
        string? Era,
        string? CreditedFirmName,
        string? Notes);
}
