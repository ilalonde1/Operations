#nullable enable
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Kor.Opportunities.Core.Models;
using Kor.Opportunities.Data.HistoricalOpportunities;
using Microsoft.Extensions.Logging;

namespace Kor.Operations.App.Opportunities;

public sealed class HistoricalOpportunityDetailViewModel : INotifyPropertyChanged, Kor.Operations.Services.IAiContextProvider
{
    // BD-Audit-2026-06-09 M16: expose Historical Opportunity Detail state to AI.
    public string ProviderName => "BD Historical Opportunity Detail";
    public bool HasData => _detail is not null;
    public string BuildContext()
        => _detail is null
            ? "BD Historical Opportunity Detail — nothing loaded."
            : $"BD Historical Opportunity Detail — {_detail.Name} (key {_detail.OpportunityKey}, id {_detail.Id}); buyer: {_detail.BuyerName}"
              + (string.IsNullOrWhiteSpace(_detail.ProjectProvince) ? "" : $"; province: {_detail.ProjectProvince}")
              + (string.IsNullOrWhiteSpace(_detail.HistoricalStatus) ? "" : $"; status: {_detail.HistoricalStatus}")
              + (_detail.EstimatedValue is { } est ? $"; est. value: {est:C0}" : "")
              + (string.IsNullOrWhiteSpace(_detail.AwardedToOrganization) ? "" : $"; awarded to: {_detail.AwardedToOrganization}")
              + $"; documents: {DocumentSummary}.";
    public string BuildLocalContext() => BuildContext();

    private readonly ICompetitionInfoQueryStore _opportunityStore;
    private readonly IHistoricalOpportunityDocumentStore _documentStore;
    private readonly ILogger<HistoricalOpportunityDetailViewModel> _logger;

    public HistoricalOpportunityDetailViewModel(
        ICompetitionInfoQueryStore opportunityStore,
        IHistoricalOpportunityDocumentStore documentStore,
        ILogger<HistoricalOpportunityDetailViewModel> logger)
    {
        _opportunityStore = opportunityStore;
        _documentStore = documentStore;
        _logger = logger;
        Documents = new ObservableCollection<HistoricalOpportunityDocumentListing>();
        OpenDocumentCommand = new RelayCommand(p => OpenDocument(p as HistoricalOpportunityDocumentListing));
    }

    public ObservableCollection<HistoricalOpportunityDocumentListing> Documents { get; }

    private HistoricalOpportunityDetail? _detail;
    public HistoricalOpportunityDetail? Detail
    {
        get => _detail;
        set
        {
            _detail = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasDocuments));
            OnPropertyChanged(nameof(DocumentSummary));
        }
    }

    private string _statusText = "Loading";
    public string StatusText { get => _statusText; set { _statusText = value; OnPropertyChanged(); } }

    public bool HasDocuments => Documents.Count > 0;

    public string DocumentSummary
    {
        get
        {
            var total = Documents.Count;
            var downloaded = 0;
            foreach (var d in Documents)
            {
                if (!string.IsNullOrWhiteSpace(d.LocalPath)) downloaded++;
            }

            return $"{downloaded} of {total} downloaded";
        }
    }

    public ICommand OpenDocumentCommand { get; }

    public async Task LoadAsync(long historicalOpportunityId)
    {
        try
        {
            var detail = await _opportunityStore.GetDetailAsync(historicalOpportunityId, CancellationToken.None)
                .ConfigureAwait(true);
            Detail = detail;
            if (detail is null)
            {
                StatusText = "Opportunity not found.";
                return;
            }

            var docs = await _documentStore.ListByOpportunityAsync(historicalOpportunityId, CancellationToken.None)
                .ConfigureAwait(true);
            Documents.Clear();
            foreach (var d in docs) Documents.Add(d);
            OnPropertyChanged(nameof(HasDocuments));
            OnPropertyChanged(nameof(DocumentSummary));
            StatusText = docs.Count == 0 ? "No documents discovered." : $"{docs.Count} document(s).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading opportunity detail failed for {Id}.", historicalOpportunityId);
            StatusText = "Loading failed: " + ex.Message;
        }
    }

    private void OpenDocument(HistoricalOpportunityDocumentListing? doc)
    {
        if (doc is null || string.IsNullOrWhiteSpace(doc.LocalPath)) return;

        try
        {
            var path = ResolveDocumentPath(doc.LocalPath!);
            if (path is null)
            {
                StatusText = $"File not found: {doc.LocalPath}";
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open document {Path}.", doc.LocalPath);
            StatusText = "Open failed: " + ex.Message;
        }
    }

    /// <summary>
    /// The worker writes documents to C:\OpsArchive\... on KOR-APP01. When the
    /// WPF app runs on a different machine, that local path won't resolve —
    /// fall back to the equivalent UNC path on KOR-APP01.
    /// </summary>
    private static string? ResolveDocumentPath(string localPath)
    {
        if (File.Exists(localPath)) return localPath;

        // Translate "C:\OpsArchive\..." → "\\KOR-APP01\C$\OpsArchive\..."
        if (localPath.StartsWith(@"C:\OpsArchive", StringComparison.OrdinalIgnoreCase))
        {
            var unc = @"\\KOR-APP01\C$" + localPath.Substring(2);
            if (File.Exists(unc)) return unc;
        }
        return null;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));

    private sealed class RelayCommand : ICommand
    {
        private readonly Action<object?> _exec;

        public RelayCommand(Action<object?> exec) { _exec = exec; }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => _exec(parameter);

        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}
