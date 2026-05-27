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
            if (!options.DryRun && string.IsNullOrWhiteSpace(options.OpportunitiesDb))
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

            var db = options.DryRun ? null : options.OpportunitiesDb;
            var orgStore = db is null ? null : new SqlCanonicalOrgStore(db);
            var enrichmentStore = db is null ? null : new SqlEnrichmentTrackingStore(db);
            var resolver = orgStore is null
                ? null
                : new CanonicalOrgResolver(orgStore, NullLogger<CanonicalOrgResolver>.Instance);

            Console.WriteLine($"BD Research import starting: base={options.BaseDirectory}; dry-run={options.DryRun.ToString().ToLowerInvariant()}; fx-rate={options.FxRate.ToString(CultureInfo.InvariantCulture)}");

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
            foreach (var firm in EnumerateArray(doc.RootElement, "firms"))
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
                var buyer = doc.RootElement;
                var buyerName = String(buyer, "buyerName");
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
                    String(buyer, "websiteUrl"),
                    String(buyer, "korRelevanceReason"),
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
                foreach (var org in EnumerateArray(devCorpsDoc.RootElement, "orgs"))
                {
                    ct.ThrowIfCancellationRequested();
                    var orgName = String(org, "org_name");
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
        else
        {
            stats.FilesMissing++;
        }

        var projectsPath = Path.Combine(dir, "projects-payload.json");
        if (TryLoadJson(projectsPath, out var projectsDoc))
        {
            using (projectsDoc)
            {
                foreach (var project in EnumerateArray(projectsDoc.RootElement, "projects"))
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
                    var structuralEngineer = String(project, "StructuralEngineer");
                    var proponentId = await ResolveAsync(resolver, options, stats, proponentName, OrgKinds.Unknown, ProponentSource, ct).ConfigureAwait(false);
                    var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);
                    var record = new MajorProjectRecord(
                        Source: "IndigenousDevProjects",
                        SourceKey: "INDIG-" + Sha1($"{String(project, "IndigenousNames")}|{projectName}"),
                        ProjectName: projectName,
                        ProjectDescription: null,
                        EstimatedCostCad: Money(project, "EstimatedCostCad"),
                        EstimatedCostText: CostText(project, "EstimatedCostCad"),
                        Sector: String(project, "Sector"),
                        SubSector: null,
                        ConstructionType: null,
                        ConstructionSubtype: null,
                        ProjectType: null,
                        RegionName: null,
                        MunicipalityName: String(project, "Location"),
                        ProponentName: proponentName,
                        ProponentCanonicalOrgId: proponentId,
                        ArchitectName: architectName,
                        ArchitectCanonicalOrgId: architectId,
                        Stage: String(project, "Status"),
                        ProjectStatus: String(project, "Status"),
                        ProjectStage: "Indigenous",
                        ProjectCategoryName: null,
                        PublicFundingInd: null,
                        ProvincialFunding: null,
                        FederalFunding: null,
                        MunicipalFunding: null,
                        OtherPublicFunding: null,
                        GreenBuildingInd: null,
                        IndigenousInd: true,
                        IndigenousNames: String(project, "IndigenousNames"),
                        ConstructionJobs: null,
                        OperatingJobs: null,
                        StandardizedStartDate: null,
                        StandardizedCompletionDate: null,
                        StartYear: null,
                        CompletionYear: null,
                        ScheduleNotes: BuildIndigenousScheduleNotes(String(project, "ExpectedTimeline"), structuralEngineer),
                        Latitude: null,
                        Longitude: null,
                        ProjectWebsite: null,
                        SourceUrl: String(project, "SourceUrl"),
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
                foreach (var firm in EnumerateArray(partnerGraphDoc.RootElement, "firms"))
                {
                    ct.ThrowIfCancellationRequested();
                    var firmName = String(firm, "firm_name");
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
            foreach (var project in EnumerateArray(doc.RootElement, "projects"))
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
                foreach (var firm in EnumerateArray(firmsDoc.RootElement, "firms"))
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
                        String(firm, "website"),
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
            foreach (var project in EnumerateArray(projectsDoc.RootElement, "projects"))
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "ProjectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var municipality = String(project, "Municipality");
                var rawState = String(project, "State");
                if (includeStateInSourceKey && string.IsNullOrWhiteSpace(rawState))
                {
                    stats.ProjectRowsSkipped++;
                    Console.WriteLine($"[WARN] {projectSource}: skipping project with blank State; project={projectName}");
                    continue;
                }

                var province = NormalizeProvince(includeStateInSourceKey ? rawState : defaultProvince, defaultProvince);
                var proponentName = String(project, "ProponentName");
                var architectName = String(project, "ArchitectName");
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
                    Sector: String(project, "Sector"),
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
                    Stage: String(project, "Stage"),
                    ProjectStatus: String(project, "Stage"),
                    ProjectStage: "USMarketResearch",
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
                    SourceUrl: String(project, "SourceUrl"),
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
                foreach (var firm in EnumerateArray(firmsDoc.RootElement, "firms"))
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
                        String(firm, "website"),
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
            foreach (var project in EnumerateArray(projectsDoc.RootElement, "projects"))
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "ProjectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var municipality = String(project, "Municipality");
                var sourceKey = "ABMKT-" + Sha1($"{projectName}|{municipality}");
                if (!sourceKeysSeen.Add(sourceKey))
                {
                    stats.SourceKeyCollisions++;
                    Console.WriteLine($"[WARN] AlbertaMarketProjects: duplicate SourceKey in this run ({sourceKey}); later row may overwrite earlier row. project={projectName}");
                }

                var proponentName = String(project, "ProponentName");
                var architectName = String(project, "ArchitectName");
                var proponentId = await ResolveAsync(resolver, options, stats, proponentName, OrgKinds.Unknown, ProponentSource, ct).ConfigureAwait(false);
                var architectId = await ResolveAsync(resolver, options, stats, architectName, OrgKinds.Architect, ArchitectSource, ct).ConfigureAwait(false);

                var record = new MajorProjectRecord(
                    Source: "AlbertaMarketProjects",
                    SourceKey: sourceKey,
                    ProjectName: projectName,
                    ProjectDescription: String(project, "FullDescription"),
                    EstimatedCostCad: Money(project, "EstimatedCostCad"),
                    EstimatedCostText: CostText(project, "EstimatedCostCad"),
                    Sector: String(project, "Sector"),
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
                    Stage: String(project, "Stage"),
                    ProjectStatus: String(project, "Stage"),
                    ProjectStage: "AlbertaMarketResearch",
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
                    SourceUrl: String(project, "SourceUrl"),
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
                foreach (var owner in EnumerateArray(ownersDoc.RootElement, "owners"))
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
                        String(owner, "publishedCapitalPlanUrl"),
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
            foreach (var project in EnumerateArray(projectsDoc.RootElement, "projects"))
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(project, "ProjectName");
                if (string.IsNullOrWhiteSpace(projectName))
                {
                    stats.ProjectRowsSkipped++;
                    continue;
                }

                var ownerName = String(project, "OwnerName");
                var architectName = String(project, "ArchitectName");
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
                    Sector: String(project, "Sector"),
                    SubSector: String(project, "SubSector"),
                    ConstructionType: null,
                    ConstructionSubtype: null,
                    ProjectType: null,
                    RegionName: null,
                    MunicipalityName: String(project, "Municipality"),
                    ProponentName: ownerName,
                    ProponentCanonicalOrgId: proponentId,
                    ArchitectName: architectName,
                    ArchitectCanonicalOrgId: architectId,
                    Stage: String(project, "Stage"),
                    ProjectStatus: String(project, "Stage"),
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
                    SourceUrl: String(project, "SourceUrl"),
                    RawJson: project.GetRawText())
                {
                    Province = ProvinceFromMarket(String(project, "Province"), String(project, "Market")),
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
            foreach (var prime in EnumerateArray(doc.RootElement, "primes"))
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
            var peopleByFirm = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var person in EnumerateArray(doc.RootElement, "people"))
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
                if (orgsDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var org in orgsDoc.RootElement.EnumerateArray())
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
            if (projectsDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var project in projectsDoc.RootElement.EnumerateArray())
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
            if (teamDoc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var t in teamDoc.RootElement.EnumerateArray())
            {
                ct.ThrowIfCancellationRequested();
                var projectName = String(t, "project");
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
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var validIds = options.DryRun
                ? null
                : await LoadValidOrgIdsAsync(options.OpportunitiesDb, ct).ConfigureAwait(false);

            foreach (var org in doc.RootElement.EnumerateArray())
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
            }
        }
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

    private static async Task ImportOwnerPipelinesAsync(
        ImportOptions options,
        CanonicalOrgResolver? resolver,
        ImportStats stats,
        CancellationToken ct)
    {
        // Intel-Gathering Track B: institutional owner capital pipelines (projects
        // 1-3 yrs ahead of the RFP) -> MajorProjectsInventory.
        var path = Path.Combine(options.BaseDirectory, "KOR-Intel-Gathering", "outputs", "owner-pipelines.json");
        if (!TryLoadJson(path, out var doc))
        {
            stats.FilesMissing++;
            return;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var p in doc.RootElement.EnumerateArray())
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
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var c in doc.RootElement.EnumerateArray())
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
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            var groups = new Dictionary<string, (long? OrgId, string? Name, string Kind, List<string> People)>(StringComparer.OrdinalIgnoreCase);
            foreach (var person in doc.RootElement.EnumerateArray())
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

    private static async Task UpsertMajorProjectAsync(ImportOptions options, ImportStats stats, MajorProjectRecord r, CancellationToken ct)
    {
        stats.ProjectUpsertsBySource.TryGetValue(r.Source, out var count);
        stats.ProjectUpsertsBySource[r.Source] = count + 1;

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
    INSERT INTO opportunities.MajorProjectsInventory
        (Province, SourceKey, ExternalProjectId, ProjectName, ProjectDescription, EstimatedCostCad,
         EstimatedCostText, Sector, SubSector, ConstructionType, ConstructionSubtype, ProjectType,
         RegionName, MunicipalityName, ProponentName, ProponentCanonicalOrgId, ArchitectName,
         ArchitectCanonicalOrgId, Stage, ProjectStatus, ProjectStage, ProjectCategoryName,
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
         @architectCanonicalOrgId, @stage, @projectStatus, @projectStage, @projectCategoryName,
         @publicFundingInd, @provincialFunding, @federalFunding, @municipalFunding, @otherPublicFunding,
         @greenBuildingInd, @indigenousInd, @indigenousNames, @constructionJobs, @operatingJobs,
         @standardizedStartDate, @standardizedCompletionDate, @startYear, @completionYear,
         @scheduleNotes, @latitude, @longitude, @projectWebsite, @sourceUrl, @issueYear,
         @issueQuarter, @structuralEngineerName, @structuralEngineerCanonicalOrgId,
         @generalContractorName, @generalContractorCanonicalOrgId, @rawJson);
END;

COMMIT TRAN;

SELECT CASE WHEN EXISTS (SELECT 1 FROM @inserted) THEN 1 ELSE 0 END;";

        await using var con = new SqlConnection(options.OpportunitiesDb);
        await con.OpenAsync(ct).ConfigureAwait(false);
        await using var cmd = new SqlCommand(sql, con) { CommandTimeout = 60 };
        AddParams(cmd, r);
        await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false);
        if (!options.Quiet)
        {
            Console.WriteLine($"[MPI] {r.Source}: {r.SourceKey}; project={r.ProjectName}");
        }
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
        AddString(cmd, "@projectStage", r.ProjectStage, 100);
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
        var trimmed = NullIfBlank(value) ?? fallback;
        trimmed = trimmed.ToUpperInvariant();
        return trimmed.Length <= 2 ? trimmed : trimmed[..2];
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

    private sealed record ImportOptions(string BaseDirectory, string OpportunitiesDb, bool DryRun, bool Quiet, decimal FxRate, string? Only)
    {
        public static ImportOptions Parse(string[] args)
        {
            var baseDir = DefaultBaseDirectory;
            var db = Environment.GetEnvironmentVariable("KOR_OPPORTUNITIES_OPPORTUNITIESDB") ?? string.Empty;
            var dryRun = false;
            var quiet = false;
            var fxRate = 1.36m;
            string? only = null;

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
                    default:
                        throw new ArgumentException($"Unknown argument '{args[i]}'.");
                }
            }

            return new ImportOptions(baseDir, db, dryRun, quiet, fxRate, only);
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
        public Dictionary<string, int> OrgsBySource { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> EnrichmentRowsByProvider { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> ProjectUpsertsBySource { get; } = new(StringComparer.OrdinalIgnoreCase);
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
}
