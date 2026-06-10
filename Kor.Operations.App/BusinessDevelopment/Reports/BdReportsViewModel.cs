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
    int Total)
{
    public string PursueDisplay => Pursue.ToString(CultureInfo.InvariantCulture);
    public string MonitorDisplay => Monitor.ToString(CultureInfo.InvariantCulture);
    public string DiscoverDisplay => Discover.ToString(CultureInfo.InvariantCulture);
    public string NotHonedDisplay => NotHoned.ToString(CultureInfo.InvariantCulture);
    public string TotalDisplay => Total.ToString(CultureInfo.InvariantCulture);
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
                    s.Discover, s.Dead, s.Duplicate, s.NoVerdict, s.Total));
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
            PreviewHtml = HtmlPreviewBuilder.Render(document);

            var honed = rows.Count(r => r.Verdict is not null);
            StatusMessage = $"{sector.Title}: {honed} honed of {rows.Count} projects. Preview matches the DOCX export.";
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

    /// <summary>Renders the previewed document to DOCX bytes (same content as the preview).</summary>
    public byte[] RenderCurrentDocx()
    {
        var document = _currentDocument
            ?? throw new InvalidOperationException("Generate a preview before exporting.");
        return DocxBuilder.Render(document);
    }

    public string SuggestedDocxFileName =>
        $"KOR-{SelectedSector?.Key ?? "report"}-BD-Report-{DateTime.Now:yyyy-MM-dd}.docx";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
