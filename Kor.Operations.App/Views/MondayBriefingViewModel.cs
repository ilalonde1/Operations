#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Kor.Operations.App;
using Kor.Operations.Core;
using Kor.Operations.Services;
using Microsoft.Win32;

namespace Kor.Operations.App.Views;

internal sealed class MondayBriefingViewModel : ObservableObject
{
    private readonly MondayBriefingClient _client;
    private readonly MondayBriefingExporter _exporter;
    private readonly MondayBriefingDocxExporter _docxExporter;
    private string _header = "";
    private string _lastUpdated = "";
    private bool _isBusy;
    private string? _errorMessage;

    public MondayBriefingViewModel(
        MondayBriefingClient client,
        MondayBriefingExporter exporter,
        MondayBriefingDocxExporter docxExporter)
    {
        _client = client;
        _exporter = exporter;
        _docxExporter = docxExporter;
        RefreshCommand = new AsyncRelayCommand(_ => RefreshAsync());
        ExportExcelCommand = new AsyncRelayCommand(_ => ExportExcelAsync(), _ => BriefSections.Count > 0 || ActionItems.Count > 0);
        ExportWordCommand = new AsyncRelayCommand(_ => ExportWordAsync(), _ => BriefSections.Count > 0 || ActionItems.Count > 0);
        AcknowledgeCommand = new AsyncRelayCommand(p => AcknowledgeAsync((AlertDto)p!), p => p is AlertDto && !IsBusy);
    }

    public ObservableCollection<AlertDto> ActionItems { get; } = new();

    public ObservableCollection<BriefDto> BriefSections { get; } = new();

    public ObservableCollection<ActionItemGroup> ActionItemGroups { get; } = new();

    public string Header
    {
        get => _header;
        private set => SetField(ref _header, value);
    }

    public string LastUpdated
    {
        get => _lastUpdated;
        private set => SetField(ref _lastUpdated, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(BusyVisibility));
            }
        }
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetField(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(ErrorVisibility));
            }
        }
    }

    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;

    public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage) ? Visibility.Collapsed : Visibility.Visible;

    public ICommand RefreshCommand { get; }

    public ICommand ExportExcelCommand { get; }

    public ICommand ExportWordCommand { get; }

    public ICommand AcknowledgeCommand { get; }

    private async Task RefreshAsync(CancellationToken ct = default)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            if (!_client.IsConfigured)
            {
                ErrorMessage = "MCP server is not configured. Set McpServer.ServiceUrl/Username/Password in App.config.";
                Header = "Monday Briefing - Not configured";
                return;
            }

            var alertsTask = _client.GetActiveAlertsAsync(ct);
            var briefTask = _client.GetLatestBriefAsync(ct);
            await Task.WhenAll(alertsTask, briefTask).ConfigureAwait(true);

            Replace(ActionItems, alertsTask.Result
                .Where(a => a.acknowledgedAt is null)
                .OrderBy(a => a.section)
                .ThenBy(a => SeverityOrder(a.severity))
                .ThenByDescending(a => a.generatedAt));
            Replace(BriefSections, briefTask.Result.OrderBy(b => SectionOrder(b.section)));
            RebuildGroups();

            var weekOf = BriefSections.FirstOrDefault()?.weekOf;
            Header = weekOf is null
                ? "Monday Briefing - No brief yet"
                : $"Monday Briefing - Week of {weekOf:yyyy-MM-dd}";
            LastUpdated = $"Loaded {DateTime.Now:yyyy-MM-dd HH:mm}";
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task ExportExcelAsync()
    {
        var weekOf = BriefSections.FirstOrDefault()?.weekOf ?? MostRecentMonday(DateTime.Today);
        var dlg = new SaveFileDialog
        {
            Filter = "Excel Workbook (*.xlsx)|*.xlsx",
            FileName = $"KOR-Monday-Briefing-{weekOf:yyyy-MM-dd}.xlsx",
            AddExtension = true,
            DefaultExt = ".xlsx",
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            // Off the UI thread — ClosedXML.SaveAs is synchronous I/O; on a
            // big briefing run with ~20 alerts plus 6 brief sections it's
            // a ~50ms write but bigger weeks could stutter the UI without this.
            var path = dlg.FileName;
            var brief = BriefSections.ToList();
            var alerts = ActionItems.ToList();
            await Task.Run(() => _exporter.Export(path, brief, alerts)).ConfigureAwait(true);
            MessageBox.Show("Export completed.", "Monday Briefing — Export To Excel", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Monday Briefing — Export To Excel", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task ExportWordAsync()
    {
        var weekOf = BriefSections.FirstOrDefault()?.weekOf ?? MostRecentMonday(DateTime.Today);
        var dlg = new SaveFileDialog
        {
            Filter = "Word Document (*.docx)|*.docx",
            FileName = $"KOR-Monday-Briefing-{weekOf:yyyy-MM-dd}.docx",
            AddExtension = true,
            DefaultExt = ".docx",
        };

        if (dlg.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var path = dlg.FileName;
            var brief = BriefSections.ToList();
            var alerts = ActionItems.ToList();
            await Task.Run(() => _docxExporter.Export(path, brief, alerts)).ConfigureAwait(true);
            MessageBox.Show("Export completed.", "Monday Briefing — Export To Word", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed:\n{ex.Message}", "Monday Briefing — Export To Word", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task AcknowledgeAsync(AlertDto alert)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _client.AcknowledgeAlertAsync(alert.id, CancellationToken.None).ConfigureAwait(true);
            ActionItems.Remove(alert);
            RebuildGroups();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            MessageBox.Show($"Acknowledge failed:\n{ex.Message}", "Monday Briefing — Acknowledge Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildGroups()
    {
        ActionItemGroups.Clear();
        foreach (var group in ActionItems
            .GroupBy(a => a.section)
            .OrderBy(g => g.Key))
        {
            ActionItemGroups.Add(new ActionItemGroup(group.Key, group.ToList()));
        }
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> rows)
    {
        target.Clear();
        foreach (var row in rows)
        {
            target.Add(row);
        }
    }

    private static DateTime MostRecentMonday(DateTime date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.Date.AddDays(-daysSinceMonday);
    }

    private static int SectionOrder(string section) => section switch
    {
        "FinancialHealth" => 0,
        "PortfolioHealth" => 1,
        "ClientStrategy" => 2,
        "BdMarket" => 3,
        "OperationsTalent" => 4,
        "WatchItems" => 5,
        _ => 99,
    };

    private static int SeverityOrder(string severity) => severity.ToUpperInvariant() switch
    {
        "HIGH" => 0,
        "MEDIUM" => 1,
        "LOW" => 2,
        _ => 99,
    };
}

internal sealed class ActionItemGroup
{
    public ActionItemGroup(string section, IReadOnlyList<AlertDto> items)
    {
        Section = section;
        Items = items;
    }

    public string Section { get; }

    public IReadOnlyList<AlertDto> Items { get; }

    public int Count => Items.Count;

    public string Header => $"{Section} ({Count})";
}
