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

    public bool HasStale => StaleCount > 0;
    public bool HasAging => AgingCount > 0 && StaleCount == 0;
}

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

    public SectorCardVm? SelectedSector
    {
        get => _selectedSector;
        set { if (!ReferenceEquals(_selectedSector, value)) { _selectedSector = value; OnPropertyChanged(); } }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set { if (_isBusy != value) { _isBusy = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIdle)); } }
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

    public bool CanExportDocx => _currentDocument is not null && !IsBusy;

    public async Task LoadAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = "Loading sector summaries…";
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
            IsBusy = false;
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

        IsBusy = true;
        StatusMessage = $"Generating {sector.Title} report…";
        try
        {
            var definition = SectorReportDefinitionCatalog.All.Single(d => d.Key == sector.Key);
            var prose = SectorReportProseCatalog.For(sector.Key);
            var rows = await _reportService.GetSectorPursuitsAsync(sector.Key, ct).ConfigureAwait(true);

            var document = SectorReportGenerator.Build(definition, prose, rows, DateTimeOffset.UtcNow);
            _currentDocument = document;
            _currentDocumentKey = sector.Key;
            _currentRecordCount = rows.Count(r => r.Verdict is not null);
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"{sector.Title}: {_currentRecordCount} honed of {rows.Count} projects. Preview matches the DOCX export.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Generation cancelled.";
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
            IsBusy = false;
            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>
    /// Builds the cross-cutting weekly call sheet (all sectors, URGENT +
    /// PURSUE pool) and returns the HTML preview; DOCX export then matches it.
    /// </summary>
    public async Task<string?> BuildCallSheetPreviewAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = "Generating weekly call sheet…";
        try
        {
            var pool = await _reportService.GetCallSheetPoolAsync(ct).ConfigureAwait(true);
            var summaries = await _reportService.GetSectorSummariesAsync(ct).ConfigureAwait(true);

            var document = CallSheetReportGenerator.Build(pool, summaries, DateTimeOffset.UtcNow);
            _currentDocument = document;
            _currentDocumentKey = "call-sheet";
            _currentRecordCount = pool.Count;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Call sheet: {pool.Count(r => r.IsUrgent)} URGENT + {pool.Count(r => !r.IsUrgent)} PURSUE across all sectors.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Generation cancelled.";
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
            IsBusy = false;
            OnPropertyChanged(nameof(CanExportDocx));
        }
    }

    /// <summary>Builds the cross-sector executive overview; DOCX export then matches it.</summary>
    public async Task<string?> BuildExecSummaryPreviewAsync(CancellationToken ct)
    {
        IsBusy = true;
        StatusMessage = "Generating executive overview…";
        try
        {
            var headline = await _reportService.GetExecHeadlineAsync(ct).ConfigureAwait(true);
            var summaries = await _reportService.GetSectorSummariesAsync(ct).ConfigureAwait(true);
            var pool = await _reportService.GetCallSheetPoolAsync(ct).ConfigureAwait(true);

            var document = ExecSummaryReportGenerator.Build(headline, summaries, pool, DateTimeOffset.UtcNow);
            _currentDocument = document;
            _currentDocumentKey = "exec-overview";
            _currentRecordCount = headline.HonedCount;
            SelectedSector = null;
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            LogGenerateBestEffort("html");
            StatusMessage = $"Executive overview: {headline.HonedCount:N0} honed of {headline.TotalActiveMpis:N0} active MPIs, ~${headline.HonedCostCad / 1_000_000_000m:F1}B pipeline.";
            return PreviewHtml;
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Generation cancelled.";
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
            IsBusy = false;
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
