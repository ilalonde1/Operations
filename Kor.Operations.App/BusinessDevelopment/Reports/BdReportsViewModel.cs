#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Kor.Opportunities.Data.BdReports;
using Kor.Opportunities.Data.BdReports.Generators;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.BusinessDevelopment.Reports;

/// <summary>One pursuit-dashboard card: a sector's verdict counts.</summary>
public sealed record SectorCardVm(
    string Key,
    string Title,
    int PursueUrgent,
    int Pursue,
    int Monitor,
    int Discover,
    int Dead,
    int Duplicate,
    int NotHoned,
    int Total,
    int FreshCount,
    int AgingCount,
    int StaleCount)
{
    public string PursueDisplay => Pursue.ToString(CultureInfo.InvariantCulture);
    public string MonitorDisplay => Monitor.ToString(CultureInfo.InvariantCulture);
    public string DiscoverDisplay => Discover.ToString(CultureInfo.InvariantCulture);
    public string NotHonedDisplay => NotHoned.ToString(CultureInfo.InvariantCulture);
    public string TotalDisplay => Total.ToString(CultureInfo.InvariantCulture);

    /// <summary>BD-UI-Plan decision 6: green &lt;30d, yellow 30-90d, red 90+d.</summary>
    public string FreshnessDisplay =>
        $"{FreshCount} fresh · {AgingCount} aging · {StaleCount} stale";

    /// <summary>
    /// Full labeled breakdown for the compact one-row sector item's tooltip
    /// (2026-06-12 UX pass: the row itself shows number-only mini-pills).
    /// </summary>
    public string SummaryTooltip =>
        $"{Total} projects — {PursueUrgent} URGENT · {Pursue} PURSUE · {Monitor} MONITOR · {Discover} DISCOVER · {NotHoned} not honed\n{FreshnessDisplay}";

    public bool HasStale => StaleCount > 0;
    public bool HasAging => AgingCount > 0 && StaleCount == 0;
}

/// <summary>One cross-cutting analytical report entry in the left-hand list.</summary>
public sealed record AnalyticalReportVm(string Key, string Title, string Description);

public sealed class BdReportsViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    private readonly IBdReportService _reportService;
    private readonly ILogger<BdReportsViewModel>? _logger;

    private SectorCardVm? _selectedSector;
    private bool _isBusy;
    private string _statusMessage = "Ready.";
    private string? _previewHtml;
    private BdReportDocument? _currentDocument;
    private string? _currentDocumentKey;
    private int _currentRecordCount;

    // Generation sequence: a build that finished after a newer one started
    // must not commit its document/status over the newer one (adversarial
    // review M2 — the window cancels superseded CTSs, but a build whose
    // awaits had already completed would still have committed).
    private int _generationSeq;

    public BdReportsViewModel(IBdReportService reportService, ILogger<BdReportsViewModel>? logger = null)
    {
        _reportService = reportService ?? throw new ArgumentNullException(nameof(reportService));
        _logger = logger;
    }

    public string ProviderName => "BD Reports";
    public bool HasData => Sectors.Count > 0;

    public string BuildContext()
    {
        var perSector = string.Join("; ", Sectors.Select(s =>
            $"{s.Title}: {s.PursueUrgent + s.Pursue} pursue ({s.PursueUrgent} urgent), {s.Monitor} monitor, {s.Discover} discover, {s.NotHoned} not honed"));
        var selected = SelectedSector is { } sel ? $" Currently viewing: {sel.Title}." : string.Empty;
        return $"BD Reports dashboard — {perSector}.{selected}";
    }

    public string BuildLocalContext() => BuildContext();

    public ObservableCollection<SectorCardVm> Sectors { get; } = new();

    public IReadOnlyList<AnalyticalReportVm> AnalyticalReports { get; } = new[]
    {
        new AnalyticalReportVm("call-sheet", "Weekly Call Sheet", "Cross-sector URGENT + PURSUE pool — what to act on this week."),
        new AnalyticalReportVm("exec-overview", "Executive Overview", "Cross-sector headline, heatmap, and strategic synthesis."),
        new AnalyticalReportVm("architect-frequency", "Architect Frequency", "Warm-intro priority list from the structured org graph."),
        new AnalyticalReportVm("competitor-intel", "Competitor Intelligence", "Rival footprint heatmap — where they're entrenched, where to flip."),
        new AnalyticalReportVm("strategic-relationships", "Strategic Relationships", "Ten compounding targets with contacts and 12-month plans."),
        new AnalyticalReportVm("prime-consultant", "Prime Consultants", "Who leads each pursuit team — approach before the RFP issues."),
        new AnalyticalReportVm("pursuit-dossier", "Pursuit Dossiers", "Every live pursuit with its resolved team graph — architect route-in, incumbent SE, gaps."),
        new AnalyticalReportVm("awards", "Awards", "Upcoming AEC industry award programs KOR could enter for recognition."),
        new AnalyticalReportVm("visual-teaming-graph", "Teaming Heat-Graph", "Interactive map of orgs on live pursuits — heat = priority, edges = who teams with whom."),
        new AnalyticalReportVm("visual-priority-treemap", "Priority Treemap", "Every priority org tiled by importance, colored by heat — where the leverage is."),
        new AnalyticalReportVm("visual-attack-cards", "Opportunity Attack Cards", "One card per live pursuit — the team + KOR's path-in (warm / displace / open seat)."),
    };

    private AnalyticalReportVm? _selectedAnalytical;
    public AnalyticalReportVm? SelectedAnalytical
    {
        get => _selectedAnalytical;
        set { if (!ReferenceEquals(_selectedAnalytical, value)) { _selectedAnalytical = value; OnPropertyChanged(); } }
    }

    /// <summary>Dispatches to the analytical generator for the selected entry.</summary>
    public Task<string?> BuildAnalyticalPreviewAsync(CancellationToken ct) => SelectedAnalytical?.Key switch
    {
        "call-sheet" => BuildCallSheetPreviewAsync(ct),
        "exec-overview" => BuildExecSummaryPreviewAsync(ct),
        "architect-frequency" => BuildArchitectFrequencyPreviewAsync(ct),
        "competitor-intel" => BuildCompetitorIntelPreviewAsync(ct),
        "strategic-relationships" => BuildStrategicRelationshipsPreviewAsync(ct),
        "prime-consultant" => BuildPrimeConsultantPreviewAsync(ct),
        "pursuit-dossier" => BuildPursuitDossierPreviewAsync(ct),
        "awards" => BuildAwardsPreviewAsync(ct),
        "visual-teaming-graph" => BuildVisualPreviewAsync("visual-teaming-graph", "Teaming Heat-Graph", _reportService.GetTeamingGraphHtmlAsync, ct),
        "visual-priority-treemap" => BuildVisualPreviewAsync("visual-priority-treemap", "Priority Treemap", _reportService.GetPriorityTreemapHtmlAsync, ct),
        "visual-attack-cards" => BuildVisualPreviewAsync("visual-attack-cards", "Opportunity Attack Cards", _reportService.GetAttackCardsHtmlAsync, ct),
        _ => Task.FromResult<string?>(null),
    };

    public SectorCardVm? SelectedSector
    {
        get => _selectedSector;
        set { if (!ReferenceEquals(_selectedSector, value)) { _selectedSector = value; OnPropertyChanged(); } }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); OnPropertyChanged(nameof(CanExportDocx)); } }
    }

    public bool IsIdle => !IsBusy;

    public string StatusMessage
    {
        get => _statusMessage;
        set { if (_statusMessage != value) { _statusMessage = value; OnPropertyChanged(); } }
    }

    /// <summary>HTML of the last generated preview; the DOCX export renders from the SAME document.</summary>
    public string? PreviewHtml
    {
        get => _previewHtml;
        private set { if (_previewHtml != value) { _previewHtml = value; OnPropertyChanged(); OnPropertyChanged(nameof(CanExportDocx)); } }
    }

    // A "visual" report (heat-graph / treemap / cards) renders straight from the
    // service HTML — there is no BdReportDocument behind it, so it has no DOCX
    // document model. Every block-model report sets _currentDocument non-null;
    // the visuals null it out. So: PreviewHtml present + no document == visual.
    // The window exports visuals by screenshotting the WebView2 instead of
    // rendering a document.
    public bool CurrentIsVisual => _currentDocument is null && PreviewHtml is not null;

    public bool CanExportDocx => (_currentDocument is not null || CurrentIsVisual) && !IsBusy;

    public async Task LoadAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage ="Loading sector summaries…";
        try
        {
            var summaries = await _reportService.GetSectorSummariesAsync(ct).ConfigureAwait(true);
            Sectors.Clear();
            foreach (var s in summaries)
            {
                Sectors.Add(new SectorCardVm(
                    s.SectorKey, s.SectorTitle, s.PursueUrgent, s.Pursue, s.Monitor,
                    s.Discover, s.Dead, s.Duplicate, s.NoVerdict, s.Total,
                    s.FreshCount, s.AgingCount, s.StaleCount));
            }

            OnPropertyChanged(nameof(HasData));
            StatusMessage = $"{Sectors.Count} sectors loaded.";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Load cancelled.";
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: sector summary load failed.");
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>
    /// Builds the report for the selected sector from live data and returns
    /// the HTML preview. The document is retained so a subsequent DOCX export
    /// matches the preview one-to-one.
    /// </summary>
    public async Task<string?> BuildPreviewAsync(CancellationToken ct)
    {
        if (SelectedSector is not { } sector)
        {
            return null;
        }

        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage =$"Generating {sector.Title} report…";
        try
        {
            var definition = SectorReportDefinitionCatalog.All.Single(d => d.Key == sector.Key);
            var prose = SectorReportProseCatalog.For(sector.Key);
            var rows = await _reportService.GetSectorPursuitsAsync(sector.Key, ct).ConfigureAwait(true);
            var marketSignals = await _reportService.GetSectorIntelSignalsAsync(sector.Key, ct).ConfigureAwait(true);

            var document = SectorReportGenerator.Build(definition, prose, rows, DateTimeOffset.UtcNow, marketSignals);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey =sector.Key;
            SelectedAnalytical = null;
            _currentRecordCount = rows.Count(r => r.Verdict is not null);
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"{sector.Title}: {_currentRecordCount} honed of {rows.Count} projects. Preview matches the DOCX export.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: preview generation failed for {Sector}.", sector.Key);
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>
    /// Builds the cross-cutting weekly call sheet (all sectors, URGENT +
    /// PURSUE pool) and returns the HTML preview; DOCX export then matches it.
    /// </summary>
    public async Task<string?> BuildCallSheetPreviewAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage ="Generating weekly call sheet…";
        try
        {
            var pool = await _reportService.GetCallSheetPoolAsync(ct).ConfigureAwait(true);
            var summaries = await _reportService.GetSectorSummariesAsync(ct).ConfigureAwait(true);

            var document = CallSheetReportGenerator.Build(pool, summaries, DateTimeOffset.UtcNow);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey ="call-sheet";
            _currentRecordCount = pool.Count;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Call sheet: {pool.Count(r => r.IsUrgent)} URGENT + {pool.Count(r => !r.IsUrgent)} PURSUE across all sectors.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: call sheet generation failed.");
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>Builds the cross-sector executive overview; DOCX export then matches it.</summary>
    public async Task<string?> BuildExecSummaryPreviewAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage ="Generating executive overview…";
        try
        {
            var headline = await _reportService.GetExecHeadlineAsync(ct).ConfigureAwait(true);
            var summaries = await _reportService.GetSectorSummariesAsync(ct).ConfigureAwait(true);
            var pool = await _reportService.GetCallSheetPoolAsync(ct).ConfigureAwait(true);

            var document = ExecSummaryReportGenerator.Build(headline, summaries, pool, DateTimeOffset.UtcNow);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey ="exec-overview";
            _currentRecordCount = headline.HonedCount;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Executive overview: {headline.HonedCount:N0} honed of {headline.TotalActiveMpis:N0} active MPIs, ~${headline.HonedCostCad / 1_000_000_000m:F1}B pipeline.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: executive overview generation failed.");
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>Builds the architect warm-intro priority report; DOCX export then matches it.</summary>
    public async Task<string?> BuildArchitectFrequencyPreviewAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage ="Generating architect frequency report…";
        try
        {
            var architects = await _reportService.GetArchitectLeverageAsync(60, ct).ConfigureAwait(true);

            var document = ArchitectFrequencyReportGenerator.Build(architects, DateTimeOffset.UtcNow);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey ="architect-frequency";
            _currentRecordCount = architects.Count;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Architect frequency: {architects.Count} ranked architects, top linked {architects.FirstOrDefault()?.ActiveProjects ?? 0} active projects.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: architect frequency generation failed.");
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>Builds the competitor footprint report; DOCX export then matches it.</summary>
    public async Task<string?> BuildCompetitorIntelPreviewAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage ="Generating competitor intelligence report…";
        try
        {
            var competitors = await _reportService.GetCompetitorFootprintAsync(25, ct).ConfigureAwait(true);

            var document = CompetitorIntelReportGenerator.Build(competitors, DateTimeOffset.UtcNow);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey ="competitor-intel";
            _currentRecordCount = competitors.Count;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Competitor intelligence: {competitors.Count} tracked rivals, top footprint {competitors.FirstOrDefault()?.Total ?? 0} refs.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: competitor intelligence generation failed.");
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>Builds the ten-target strategic relationships deep-dive; DOCX export then matches it.</summary>
    public async Task<string?> BuildStrategicRelationshipsPreviewAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage ="Generating strategic relationships deep-dive…";
        try
        {
            var targets = new List<(StrategicTargetDefinition, IReadOnlyList<string>)>();
            foreach (var def in StrategicTargetCatalog.All)
            {
                var projects = await _reportService.GetProjectsMentioningAsync(def.LikePattern, 12, ct).ConfigureAwait(true);
                targets.Add((def, projects));
            }

            var document = StrategicRelationshipsReportGenerator.Build(targets, DateTimeOffset.UtcNow);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey ="strategic-relationships";
            _currentRecordCount = targets.Count;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Strategic relationships: {targets.Count} targets, {targets.Sum(t => t.Item2.Count)} live pipeline matches.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: strategic relationships generation failed.");
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>Builds the prime consultant identification report; DOCX export then matches it.</summary>
    public async Task<string?> BuildPrimeConsultantPreviewAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage ="Generating prime consultant report…";
        try
        {
            var projects = await _reportService.GetPrimeConsultantsAsync(ct).ConfigureAwait(true);

            var document = PrimeConsultantReportGenerator.Build(projects, DateTimeOffset.UtcNow);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey ="prime-consultant";
            _currentRecordCount = projects.Count;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Prime consultants: {projects.Count} researched, {projects.Count(p => p.HasPrime)} identified, {projects.Count(p => p.KnownToKor)} KOR-aligned.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: prime consultant generation failed.");
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>Builds the pursuit dossiers report (live relationship graph); DOCX export then matches it.</summary>
    public async Task<string?> BuildPursuitDossierPreviewAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage = "Generating pursuit dossiers…";
        try
        {
            var pursuits = await _reportService.GetPursuitDossiersAsync(ct).ConfigureAwait(true);

            var document = PursuitDossierReportGenerator.Build(pursuits, DateTimeOffset.UtcNow);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey = "pursuit-dossier";
            _currentRecordCount = pursuits.Count;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Pursuit dossiers: {pursuits.Count} pursuits, {pursuits.Count(p => p.HasArchitectEdge)} with architect edge, {pursuits.Count(p => !p.HasArchitectEdge)} graph gaps.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: pursuit dossier generation failed.");
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            // Only the newest generation owns the busy flag — a superseded
            // build finishing late must not flip it off mid-generation.
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    public async Task<string?> BuildAwardsPreviewAsync(CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage = "Generating awards finder report...";
        try
        {
            var awards = await _reportService.GetUpcomingAwardProgramsAsync(80, ct).ConfigureAwait(true);

            var document = AwardProgramsReportGenerator.Build(awards, DateTimeOffset.UtcNow);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = document;
            _currentDocumentKey = "awards";
            _currentRecordCount = awards.Count;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Awards finder: {awards.Count} upcoming programs, {awards.Count(a => a.SubmissionDeadline is not null)} with posted deadlines.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: awards finder generation failed.");
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>
    /// Builds one of the live BD visuals (teaming heat-graph / priority treemap /
    /// attack cards). The HTML is produced by the report service from live queries
    /// and shown straight in the WebView2 preview; there is no block-model document,
    /// so export goes through the window's screenshot path (see CurrentIsVisual).
    /// </summary>
    private async Task<string?> BuildVisualPreviewAsync(
        string key, string title, Func<CancellationToken, Task<string>> htmlFactory, CancellationToken ct)
    {
        var generation = ++_generationSeq;
        IsBusy = true;
        StatusMessage = $"Generating {title} from the live BD graph…";
        try
        {
            var html = await htmlFactory(ct).ConfigureAwait(true);
            ct.ThrowIfCancellationRequested();
            if (generation != _generationSeq)
            {
                return null; // superseded by a newer selection
            }

            _currentDocument = null;   // visual: no block-model document behind it
            _currentDocumentKey = key;
            _currentRecordCount = 0;
            SelectedSector = null;
            PreviewHtml = html;

            LogGenerateBestEffort("html");
            StatusMessage = $"{title}: live from the BD graph. Export captures the rendered visual.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            if (generation == _generationSeq)
            {
                StatusMessage = "Generation cancelled.";
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "BD Reports: visual generation failed for {Key}.", key);
            StatusMessage = $"Generation failed: {ex.Message}";
            return null;
        }
        finally
        {
            if (generation == _generationSeq)
            {
                IsBusy = false;
            }

            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>Renders the previewed document to DOCX bytes (same content as the preview).</summary>
    public byte[] RenderCurrentDocx()
    {
        var document = _currentDocument
            ?? throw new InvalidOperationException("Generate a preview before exporting.");
        return DocxBuilder.Render(document);
    }

    public string SuggestedDocxFileName =>
        $"KOR-{_currentDocumentKey ?? "report"}-BD-Report-{DateTime.Now:yyyy-MM-dd}.docx";

    /// <summary>Audit-log a DOCX export of the current document (call after a successful save).</summary>
    public void LogDocxExportBestEffort(string? notes = null) => LogGenerateBestEffort("docx", notes);

    // Fire-and-forget by design: BdReportAuditLog is compliance telemetry and
    // must never block or fail report generation.
    private void LogGenerateBestEffort(string format, string? notes = null)
    {
        var category = _currentDocumentKey;
        if (category is null)
        {
            return;
        }

        var count = _currentRecordCount;
        _ = Task.Run(async () =>
        {
            try
            {
                await _reportService.LogReportGeneratedAsync(
                    category, format, Environment.UserName, count, notes, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BD Reports: audit log write failed for {Category}/{Format}.", category, format);
            }
        });
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
