#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.App.Crm;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.Awards;
using Kor.Opportunities.Data.MajorProjects;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Opportunities;

public sealed class OrgDossierViewModel : ObservableObject, IAiContextProvider
{
    private static readonly Regex CamelBoundary = new("(?<=[a-z0-9])(?=[A-Z])", RegexOptions.Compiled);

    private readonly ICanonicalOrgStore _canonicalStore;
    private readonly IEnrichmentTrackingStore _enrichmentStore;
    private readonly IMajorProjectsInventoryStore _majorProjectsStore;
    private readonly IVendorAnalyticsStore _vendorAnalyticsStore;
    private readonly IDeltekClientContextService _deltekService;
    private readonly ILogger<OrgDossierViewModel> _logger;

    private long? _canonicalOrgId;
    private string _displayName = "";
    private string _kind = "";
    private string? _website;
    private string? _clendorClientId;
    private DossierDeltekSnapshot? _deltekSnapshot;
    private string? _notes;
    private string _statusMessage = "Ready.";
    private decimal _lifetimeValue;
    private int _lifetimeCount;
    private IReadOnlyList<ContextDossierSection> _sectionsContextSnapshot = Array.Empty<ContextDossierSection>();
    private IReadOnlyList<DossierProjectRow> _projectsContextSnapshot = Array.Empty<DossierProjectRow>();
    private IReadOnlyList<AwardListing> _recentWinsContextSnapshot = Array.Empty<AwardListing>();

    public OrgDossierViewModel(
        ICanonicalOrgStore canonicalStore,
        IEnrichmentTrackingStore enrichmentStore,
        IMajorProjectsInventoryStore majorProjectsStore,
        IVendorAnalyticsStore vendorAnalyticsStore,
        IDeltekClientContextService deltekService,
        ILogger<OrgDossierViewModel> logger)
    {
        _canonicalStore = canonicalStore ?? throw new ArgumentNullException(nameof(canonicalStore));
        _enrichmentStore = enrichmentStore ?? throw new ArgumentNullException(nameof(enrichmentStore));
        _majorProjectsStore = majorProjectsStore ?? throw new ArgumentNullException(nameof(majorProjectsStore));
        _vendorAnalyticsStore = vendorAnalyticsStore ?? throw new ArgumentNullException(nameof(vendorAnalyticsStore));
        _deltekService = deltekService ?? throw new ArgumentNullException(nameof(deltekService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public ObservableCollection<DossierSection> Sections { get; } = new();
    public ObservableCollection<DossierProjectRow> Projects { get; } = new();
    public ObservableCollection<AwardListing> RecentWins { get; } = new();

    public string DisplayName
    {
        get => _displayName;
        private set => SetField(ref _displayName, value);
    }

    public string Kind
    {
        get => _kind;
        private set => SetField(ref _kind, value);
    }

    public string? Website
    {
        get => _website;
        private set
        {
            if (SetField(ref _website, value))
            {
                OnPropertyChanged(nameof(HasWebsite));
            }
        }
    }

    public string? ClendorClientId
    {
        get => _clendorClientId;
        private set
        {
            if (SetField(ref _clendorClientId, value))
            {
                OnPropertyChanged(nameof(HasClendorClientId));
            }
        }
    }

    public DossierDeltekSnapshot? DeltekSnapshot
    {
        get => _deltekSnapshot;
        private set
        {
            if (SetField(ref _deltekSnapshot, value))
            {
                OnPropertyChanged(nameof(HasDeltekSnapshot));
            }
        }
    }

    public string? Notes
    {
        get => _notes;
        private set
        {
            if (SetField(ref _notes, value))
            {
                OnPropertyChanged(nameof(HasNotes));
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetField(ref _statusMessage, value);
    }

    public decimal LifetimeValue
    {
        get => _lifetimeValue;
        private set
        {
            if (SetField(ref _lifetimeValue, value))
            {
                OnPropertyChanged(nameof(HasAwards));
            }
        }
    }

    public int LifetimeCount
    {
        get => _lifetimeCount;
        private set
        {
            if (SetField(ref _lifetimeCount, value))
            {
                OnPropertyChanged(nameof(HasAwards));
            }
        }
    }

    public bool HeaderLoaded => _canonicalOrgId.HasValue;
    public bool HasWebsite => !string.IsNullOrWhiteSpace(Website);
    public bool HasClendorClientId => !string.IsNullOrWhiteSpace(ClendorClientId);
    public bool HasDeltekSnapshot => _deltekSnapshot is not null;
    public bool HasNotes => !string.IsNullOrWhiteSpace(Notes);
    public bool HasAwards => LifetimeCount > 0;
    public int ProjectCount => Projects.Count;
    public decimal ProjectTotalValue => Projects.Where(p => p.EstimatedCostCad.HasValue).Sum(p => p.EstimatedCostCad!.Value);
    public string ProjectFootprintHeader => $"{ProjectCount:N0} linked projects - {ProjectTotalValue:C0}";

    public async Task LoadAsync(long canonicalOrgId, CancellationToken ct)
    {
        _canonicalOrgId = null;
        _sectionsContextSnapshot = Array.Empty<ContextDossierSection>();
        _projectsContextSnapshot = Array.Empty<DossierProjectRow>();
        _recentWinsContextSnapshot = Array.Empty<AwardListing>();
        OnPropertyChanged(nameof(HeaderLoaded));
        StatusMessage = "Loading dossier...";

        try
        {
            var org = await _canonicalStore.GetCanonicalOrgAsync(canonicalOrgId, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (org is null)
            {
                StatusMessage = $"CanonicalOrg {canonicalOrgId} was not found.";
                return;
            }

            _canonicalOrgId = canonicalOrgId;
            OnPropertyChanged(nameof(HeaderLoaded));
            DisplayName = org.DisplayName;
            Kind = org.Kind;
            Website = org.Website;
            ClendorClientId = org.ClendorClientId;
            DeltekSnapshot = null;
            OnPropertyChanged(nameof(HasDeltekSnapshot));
            Notes = org.Notes;

            var enrichments = await _enrichmentStore.ListByOrgAsync(canonicalOrgId, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            Sections.Clear();
            foreach (var enrichment in enrichments)
            {
                var section = BuildSection(enrichment);
                if (section.Fields.Count > 0)
                {
                    Sections.Add(section);
                }
            }

            var projects = await _majorProjectsStore.ListByCanonicalOrgAsync(canonicalOrgId, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            Projects.Clear();
            foreach (var p in projects)
            {
                Projects.Add(new DossierProjectRow(p, canonicalOrgId));
            }
            OnPropertyChanged(nameof(ProjectCount));
            OnPropertyChanged(nameof(ProjectTotalValue));
            OnPropertyChanged(nameof(ProjectFootprintHeader));

            var awards = await _vendorAnalyticsStore.GetCompetitorProfileAsync(DisplayName, ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            LifetimeValue = awards.LifetimeValue;
            LifetimeCount = awards.LifetimeCount;
            RecentWins.Clear();
            foreach (var win in awards.RecentWins)
            {
                RecentWins.Add(win);
            }

            _sectionsContextSnapshot = Sections
                .Select(s => new ContextDossierSection(s.ProviderName, s.Fields.ToArray()))
                .ToArray();
            _projectsContextSnapshot = Projects.ToArray();
            _recentWinsContextSnapshot = RecentWins.ToArray();

            StatusMessage = $"Loaded {Sections.Count:N0} dossiers, {Projects.Count:N0} projects, {LifetimeCount:N0} award wins.";

            var deltekTrigger = string.IsNullOrWhiteSpace(ClendorClientId)
                ? null
                : ClendorClientId;
            if (deltekTrigger is not null)
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var intel = await _deltekService.LoadAsync(deltekTrigger, CancellationToken.None).ConfigureAwait(false);
                        if (intel is null)
                        {
                            SetDeltekSnapshotIfCurrent(canonicalOrgId, deltekTrigger, new DossierDeltekSnapshot(
                                ClientId: deltekTrigger,
                                ClientName: "(no Clendor row)",
                                ProjectCount: 0,
                                LifetimeFee: 0m,
                                LatestProjectStart: null,
                                LatestProjectName: null,
                                RecentProjects: Array.Empty<DossierDeltekProject>(),
                                ContactCount: 0,
                                ArOutstanding: 0m,
                                Ar90Plus: 0m,
                                DegradedSections: false,
                                ErrorMessage: "IDeltekClientContextService.LoadAsync returned null - Clendor has no row for this ClientId on the App's ODBC connection."));
                            return;
                        }

                        var projects = new List<DossierDeltekProject>();
                        foreach (var p in intel.Projects)
                        {
                            if (projects.Count >= 5)
                            {
                                break;
                            }

                            projects.Add(new DossierDeltekProject(p.Wbs1, p.Name, p.OpenDate, p.Status, p.Fee, p.FeeBilled));
                        }

                        SetDeltekSnapshotIfCurrent(canonicalOrgId, deltekTrigger, new DossierDeltekSnapshot(
                            ClientId: intel.ClientId,
                            ClientName: intel.ClientName,
                            ProjectCount: intel.ProjectCount,
                            LifetimeFee: intel.LifetimeFee,
                            LatestProjectStart: intel.LatestProjectStart,
                            LatestProjectName: intel.LatestProjectName,
                            RecentProjects: projects,
                            ContactCount: intel.Contacts.Count,
                            ArOutstanding: intel.Ar?.TotalOutstanding ?? 0m,
                            Ar90Plus: intel.Ar?.Outstanding90Plus ?? 0m,
                            DegradedSections: intel.HasDegradedSections,
                            ErrorMessage: null));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Org dossier Deltek snapshot failed for {ClendorClientId}.", deltekTrigger);
                        SetDeltekSnapshotIfCurrent(canonicalOrgId, deltekTrigger, new DossierDeltekSnapshot(
                            ClientId: deltekTrigger,
                            ClientName: "",
                            ProjectCount: 0,
                            LifetimeFee: 0m,
                            LatestProjectStart: null,
                            LatestProjectName: null,
                            RecentProjects: Array.Empty<DossierDeltekProject>(),
                            ContactCount: 0,
                            ArOutstanding: 0m,
                            Ar90Plus: 0m,
                            DegradedSections: false,
                            ErrorMessage: ex.GetType().Name + ": " + ex.Message));
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Org dossier load failed for CanonicalOrgId {CanonicalOrgId}.", canonicalOrgId);
            StatusMessage = $"Load failed: {ex.GetType().Name}: {ex.Message}";
            throw;
        }
    }

    private void SetDeltekSnapshotIfCurrent(long canonicalOrgId, string clendorClientId, DossierDeltekSnapshot snapshot)
    {
        if (_canonicalOrgId != canonicalOrgId)
        {
            return;
        }

        if (!string.Equals(ClendorClientId, clendorClientId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        DeltekSnapshot = snapshot;
    }

    // Round 56: paginated-search-envelope keys to suppress when rendering a
    // dossier. BcRegistry's payload is `{ total, pageSize, page, firstIndex,
    // lastIndex, next, results: [...] }` — the user sees the plumbing, not
    // the data. We keep `total` (informative count) but drop the rest.
    private static readonly HashSet<string> PaginationNoiseKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "pageSize", "page", "firstIndex", "lastIndex", "next", "previous",
        "offset", "limit", "hasMore",
    };

    private static DossierSection BuildSection(EnrichmentTrackingRow row)
    {
        // Round 56: ProviderName comes in as a camelCase database value
        // ("CompetitorSignals", "DataHoning", "BcRegistry"). Humanize it
        // to the same Title Case the field labels already use.
        var section = new DossierSection(
            HumanizeLabel(row.ProviderName),
            row.LastRefreshAtUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        if (string.IsNullOrWhiteSpace(row.ResultJson))
        {
            return section;
        }

        try
        {
            using var doc = JsonDocument.Parse(row.ResultJson);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var property in doc.RootElement.EnumerateObject())
                {
                    if (PaginationNoiseKeys.Contains(property.Name))
                    {
                        continue;
                    }
                    AddProperty(section.Fields, property.Name, property.Value);
                }
            }
            else
            {
                var value = RenderScalar(doc.RootElement);
                if (!string.IsNullOrWhiteSpace(value))
                {
                    section.Fields.Add(new DossierField("Result", value));
                }
            }
        }
        catch (JsonException)
        {
            section.Fields.Add(new DossierField("Result", "Provider returned a non-JSON payload."));
        }

        return section;
    }

    private static void AddProperty(ObservableCollection<DossierField> fields, string name, JsonElement value)
    {
        if (IsEmpty(value))
        {
            return;
        }

        var label = HumanizeLabel(name);
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var child in value.EnumerateObject())
                {
                    if (IsEmpty(child.Value))
                    {
                        continue;
                    }

                    var rendered = RenderScalar(child.Value);
                    if (!string.IsNullOrWhiteSpace(rendered))
                    {
                        fields.Add(new DossierField($"{label} - {HumanizeLabel(child.Name)}", rendered));
                    }
                }
                break;
            case JsonValueKind.Array:
                AddArray(fields, label, value);
                break;
            default:
                var scalar = RenderScalar(value);
                if (!string.IsNullOrWhiteSpace(scalar))
                {
                    fields.Add(new DossierField(label, scalar));
                }
                break;
        }
    }

    private static void AddArray(ObservableCollection<DossierField> fields, string label, JsonElement array)
    {
        var items = array.EnumerateArray().Where(e => !IsEmpty(e)).ToList();
        if (items.Count == 0)
        {
            return;
        }

        if (items.All(e => e.ValueKind != JsonValueKind.Object && e.ValueKind != JsonValueKind.Array))
        {
            var values = items.Select(RenderScalar).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (values.Count > 0)
            {
                fields.Add(new DossierField(label, string.Join(", ", values)));
            }
            return;
        }

        // Round 56: collapse arrays of objects when SummarizeObject produces
        // the same string for every entry — BcRegistry's results array did
        // this (10 rows all rendered as "registration.registries.ca", which
        // the user reported as ss9 noise). One labeled line is enough.
        var summaries = items.Select(item => item.ValueKind == JsonValueKind.Object
                ? SummarizeObject(item)
                : RenderScalar(item) ?? string.Empty)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();
        if (summaries.Count == 0)
        {
            return;
        }

        var distinct = summaries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count == 1)
        {
            fields.Add(new DossierField(label, $"{summaries.Count} entries — {distinct[0]}"));
            return;
        }

        var i = 1;
        foreach (var summary in summaries)
        {
            fields.Add(new DossierField($"{label} {i}", summary));
            i++;
        }
    }

    private static string SummarizeObject(JsonElement obj)
    {
        var nameKeys = new[]
        {
            "name", "title", "headline", "firmName", "firm_name", "orgName", "org_name",
            "buyerName", "buyer_name", "projectName", "project_name",
        };
        var detailKeys = new[] { "role", "position", "jobTitle", "job_title", "type", "kind", "date", "year" };
        var values = new List<string>();

        foreach (var key in nameKeys.Concat(detailKeys))
        {
            if (obj.TryGetProperty(key, out var value))
            {
                var rendered = RenderScalar(value);
                if (!string.IsNullOrWhiteSpace(rendered))
                {
                    values.Add(rendered);
                }
            }
        }

        if (values.Count == 0)
        {
            foreach (var property in obj.EnumerateObject().Take(4))
            {
                var rendered = RenderScalar(property.Value);
                if (!string.IsNullOrWhiteSpace(rendered))
                {
                    values.Add($"{HumanizeLabel(property.Name)}: {rendered}");
                }
            }
        }

        return string.Join(" - ", values.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private static bool IsEmpty(JsonElement value)
        => value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
           || (value.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(value.GetString()))
           || (value.ValueKind == JsonValueKind.Array && !value.EnumerateArray().Any(e => !IsEmpty(e)))
           || (value.ValueKind == JsonValueKind.Object && !value.EnumerateObject().Any(p => !IsEmpty(p.Value)));

    private static string? RenderScalar(JsonElement value)
        => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString()?.Trim(),
            JsonValueKind.Number => value.GetRawText(),
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Object => SummarizeObject(value),
            JsonValueKind.Array => string.Join(", ", value.EnumerateArray().Select(RenderScalar).Where(v => !string.IsNullOrWhiteSpace(v))),
            _ => null,
        };

    private static string HumanizeLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            return "";
        }

        var spaced = label.Replace("_", " ", StringComparison.Ordinal).Replace("-", " ", StringComparison.Ordinal);
        spaced = CamelBoundary.Replace(spaced, " ");
        var tokens = spaced.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(' ', tokens.Select(t => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(t.ToLowerInvariant())));
    }

    public string ProviderName => "Org Dossier";
    public bool HasData => HeaderLoaded;

    public string BuildContext()
    {
        var sections = _sectionsContextSnapshot;
        var projects = _projectsContextSnapshot;
        var sb = new StringBuilder();
        sb.AppendLine($"Org dossier: {DisplayName}");
        sb.AppendLine($"Kind: {Kind}");
        if (!string.IsNullOrWhiteSpace(Website)) sb.AppendLine($"Website: {Website}");
        if (!string.IsNullOrWhiteSpace(ClendorClientId)) sb.AppendLine($"ClendorClientId: {ClendorClientId}");
        sb.AppendLine($"Research providers: {string.Join(", ", sections.Select(s => s.ProviderName))}");
        sb.AppendLine($"Linked major projects: {projects.Count:N0}");
        sb.AppendLine($"Lifetime award value: {LifetimeValue:C0} across {LifetimeCount:N0} wins.");
        return sb.ToString();
    }

    public string BuildLocalContext()
    {
        var sections = _sectionsContextSnapshot;
        var projects = _projectsContextSnapshot;
        var wins = _recentWinsContextSnapshot;
        var sb = new StringBuilder();
        sb.AppendLine($"Org dossier: {DisplayName}");
        sb.AppendLine($"Kind: {Kind}");
        if (!string.IsNullOrWhiteSpace(Notes)) sb.AppendLine($"Notes: {Notes}");

        foreach (var section in sections)
        {
            sb.AppendLine();
            sb.AppendLine($"Research dossier - {section.ProviderName}:");
            foreach (var field in section.Fields)
            {
                sb.AppendLine($"  {field.Label}: {field.Value}");
            }
        }

        if (projects.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Major project footprint:");
            foreach (var p in projects)
            {
                sb.AppendLine($"  [{p.Role}] {p.ProjectName} ({p.Province}, {p.StageDisplay}, {p.CostDisplay}); {p.MunicipalityName}; completion {p.CompletionYear?.ToString(CultureInfo.InvariantCulture) ?? "-"}");
            }
        }

        if (wins.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Recent award wins:");
            foreach (var win in wins.Take(15))
            {
                var awardedAt = win.AwardedAtUtc?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
                var value = win.ContractValue?.ToString("C0", CultureInfo.CurrentCulture) ?? "-";
                sb.AppendLine($"  {awardedAt} - {win.Title}; buyer {win.AwardingOrganization}; value {value}");
            }
        }

        return sb.ToString();
    }

    private sealed record ContextDossierSection(string ProviderName, IReadOnlyList<DossierField> Fields);
}

public sealed record DossierSection(string ProviderName, string? RefreshedAt)
{
    public ObservableCollection<DossierField> Fields { get; } = new();
}

public sealed record DossierField(string Label, string Value);

public sealed record DossierDeltekSnapshot(
    string ClientId,
    string ClientName,
    int ProjectCount,
    decimal LifetimeFee,
    DateTime? LatestProjectStart,
    string? LatestProjectName,
    IReadOnlyList<DossierDeltekProject> RecentProjects,
    int ContactCount,
    decimal ArOutstanding,
    decimal Ar90Plus,
    bool DegradedSections,
    string? ErrorMessage)
{
    public bool HasProjects => ProjectCount > 0;
    public bool HasLatestProject => !string.IsNullOrWhiteSpace(LatestProjectName) || LatestProjectStart.HasValue;
    public bool HasRecentProjects => RecentProjects.Count > 0;
    public bool HasArOutstanding => ArOutstanding > 0m;
    public bool HasNoError => string.IsNullOrWhiteSpace(ErrorMessage);
    public bool HasError => !HasNoError;
}

public sealed record DossierDeltekProject(
    string Wbs1,
    string Name,
    DateTime? OpenDate,
    string? Status,
    decimal Fee,
    decimal FeeBilled);

public sealed class DossierProjectRow
{
    public DossierProjectRow(MajorProjectRow project, long canonicalOrgId)
    {
        Project = project;
        var isDeveloper = project.ProponentCanonicalOrgId == canonicalOrgId;
        var isArchitect = project.ArchitectCanonicalOrgId == canonicalOrgId;
        Role = (isDeveloper, isArchitect) switch
        {
            (true, true) => "Developer + Architect",
            (true, false) => "Developer",
            (false, true) => "Architect",
            _ => "Linked",
        };
    }

    public MajorProjectRow Project { get; }
    public string Role { get; }
    public string ProjectName => Project.ProjectName;
    public string Province => Project.Province;
    public string StageDisplay => Project.StageDisplay;
    public string CostDisplay => Project.CostDisplay;
    public decimal? EstimatedCostCad => Project.EstimatedCostCad;
    public string? MunicipalityName => Project.MunicipalityName;
    public short? CompletionYear => Project.CompletionYear;
    public string? SourceUrl => Project.SourceUrl;
    public bool HasAbsoluteSourceUrl => Uri.TryCreate(SourceUrl, UriKind.Absolute, out _);
    public bool HasNoAbsoluteSourceUrl => !HasAbsoluteSourceUrl;
}
