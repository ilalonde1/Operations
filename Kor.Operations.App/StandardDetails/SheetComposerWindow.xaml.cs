#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Kor.Operations.StandardDetails;

public partial class SheetComposerWindow : Window
{
    private readonly KorStandardsReadRepository _catalogRepository;
    private readonly StandardDetailsRepository _governanceRepository;
    private readonly StandardDetailsSheetComposer _composer;
    private readonly bool _groupSchemaAvailable;
    private readonly long? _selectedGroupId;
    private readonly Guid _actorUserId;
    private readonly ObservableCollection<ComposerDetailDisplayRow> _details = new();
    private readonly ObservableCollection<ComposerPlacementDisplayRow> _placements = new();

    internal SheetComposerWindow(
        KorStandardsReadRepository catalogRepository,
        StandardDetailsRepository governanceRepository,
        StandardDetailsSheetComposer composer,
        bool groupSchemaAvailable,
        long? selectedGroupId,
        Guid actorUserId)
    {
        _catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
        _governanceRepository = governanceRepository ?? throw new ArgumentNullException(nameof(governanceRepository));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _groupSchemaAvailable = groupSchemaAvailable;
        _selectedGroupId = selectedGroupId;
        _actorUserId = actorUserId;
        InitializeComponent();
        DetailsGrid.ItemsSource = _details;
        PlacementsGrid.ItemsSource = _placements;
        _placements.CollectionChanged += (_, _) => UpdatePlacementSummary();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadDetailsAsync();
        UpdatePlacementSummary();
    }

    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        await LoadDetailsAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadDetailsAsync();
    }

    private async Task LoadDetailsAsync()
    {
        ToggleBusy(true);
        try
        {
            var rows = await _catalogRepository.LoadSheetComposerDetailsAsync(SearchBox.Text?.Trim() ?? string.Empty);
            var occupied = await _composer.LoadOccupiedDetailsAsync(TimeSpan.FromMinutes(2));

            _details.Clear();
            foreach (var row in rows)
            {
                var occupancy = FindOccupancy(row, occupied);
                _details.Add(new ComposerDetailDisplayRow
                {
                    DetailNumber = row.DetailNumber,
                    Title = row.Title,
                    Discipline = row.Discipline,
                    CanonicalViewName = row.CanonicalViewName,
                    CurrentSheetText = occupancy is null ? "" : $"{occupancy.SheetNumber} - {occupancy.SheetName}",
                    IsAlreadyOnSheet = occupancy is not null
                });
            }

            SummaryText.Text = $"{_details.Count} approved detail(s) loaded. Already-sheeted details stay visible but cannot be added.";
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Sheet composer unavailable.";
            MessageBox.Show(this, ex.Message, "Standard Details - Sheet Composer", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (DetailsGrid.SelectedItem is not ComposerDetailDisplayRow detail)
        {
            MessageBox.Show(this, "Select a detail first.", "Standard Details - Sheet Composer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (detail.IsAlreadyOnSheet)
        {
            MessageBox.Show(this, $"{detail.DetailNumber} is already committed to {detail.CurrentSheetText}.", "Standard Details - Sheet Composer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (_placements.Any(x => string.Equals(x.DetailNumber, detail.DetailNumber, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"{detail.DetailNumber} is already on this composed sheet.", "Standard Details - Sheet Composer", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var index = _placements.Count;
        _placements.Add(new ComposerPlacementDisplayRow
        {
            DetailNumber = detail.DetailNumber,
            Title = detail.Title,
            CanonicalViewName = detail.CanonicalViewName,
            XmmText = (100 + (index % 3) * 150).ToString(CultureInfo.InvariantCulture),
            YmmText = (100 + (index / 3) * 95).ToString(CultureInfo.InvariantCulture)
        });
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (PlacementsGrid.SelectedItem is ComposerPlacementDisplayRow row)
        {
            _placements.Remove(row);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        SheetComposerRequest request;
        try
        {
            request = BuildRequest();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Standard Details - Sheet Composer", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ToggleBusy(true);
        try
        {
            var result = await _composer.ComposeAsync(
                request,
                _governanceRepository,
                _groupSchemaAvailable,
                _selectedGroupId,
                _actorUserId,
                TimeSpan.FromMinutes(10));

            SummaryText.Text = $"Created {result.SheetNumber} - {result.SheetName} with {result.PlacementCount} detail(s). Opening PDF...";
            try
            {
                await _composer.OpenSheetPdfAsync(result.SheetNumber, TimeSpan.FromMinutes(5));
            }
            catch (Exception openEx)
            {
                MessageBox.Show(
                    this,
                    $"Created {result.SheetNumber} - {result.SheetName}, but the PDF could not be opened.{Environment.NewLine}{openEx.Message}",
                    "Standard Details - Open PDF Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Standard Details - Sheet Composer Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private SheetComposerRequest BuildRequest()
    {
        var sheetNumber = SheetNumberBox.Text.Trim();
        var sheetName = SheetNameBox.Text.Trim();
        var likeSheet = LikeSheetBox.Text.Trim();
        var placements = _placements.Select(ToPlacement).ToList();
        return new SheetComposerRequest(sheetNumber, sheetName, likeSheet, placements);
    }

    private async void OpenPdf_Click(object sender, RoutedEventArgs e)
    {
        var sheetNumber = SheetNumberBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(sheetNumber))
        {
            MessageBox.Show(this, "Enter a sheet number first.", "Standard Details - Open PDF", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ToggleBusy(true);
        try
        {
            SummaryText.Text = $"Opening PDF for {sheetNumber}...";
            await _composer.OpenSheetPdfAsync(sheetNumber, TimeSpan.FromMinutes(5));
            SummaryText.Text = $"Opened PDF for {sheetNumber}.";
        }
        catch (Exception ex)
        {
            SummaryText.Text = "PDF export unavailable.";
            MessageBox.Show(this, ex.Message, "Standard Details - Open PDF Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ToggleBusy(false);
        }
    }

    private static SheetComposerPlacement ToPlacement(ComposerPlacementDisplayRow row)
    {
        if (!double.TryParse(row.XmmText, NumberStyles.Float, CultureInfo.InvariantCulture, out var x))
        {
            throw new InvalidOperationException($"{row.DetailNumber}: X mm must be a number.");
        }

        if (!double.TryParse(row.YmmText, NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            throw new InvalidOperationException($"{row.DetailNumber}: Y mm must be a number.");
        }

        int? scale = null;
        var scaleText = row.ScaleText?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(scaleText))
        {
            if (!int.TryParse(scaleText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed < 1 || parsed > 24000)
            {
                throw new InvalidOperationException($"{row.DetailNumber}: Scale must be blank or an integer from 1 to 24000.");
            }

            scale = parsed;
        }

        return new SheetComposerPlacement(row.DetailNumber, row.CanonicalViewName, x, y, scale);
    }

    private static SheetComposerOccupiedDetail? FindOccupancy(
        SheetComposerDetailRow detail,
        System.Collections.Generic.IReadOnlyDictionary<string, SheetComposerOccupiedDetail> occupied)
    {
        if (occupied.TryGetValue(detail.DetailNumber, out var byNumber))
        {
            return byNumber;
        }

        return occupied.Values.FirstOrDefault(x => string.Equals(x.ViewName, detail.CanonicalViewName, StringComparison.OrdinalIgnoreCase));
    }

    private void ToggleBusy(bool busy)
    {
        SearchBox.IsEnabled = !busy;
        AddButton.IsEnabled = !busy;
        OpenPdfButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
    }

    private void UpdatePlacementSummary()
    {
        PlacementSummaryText.Text = $"{_placements.Count} detail(s) selected";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private sealed class ComposerDetailDisplayRow
    {
        public string DetailNumber { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string Discipline { get; init; } = string.Empty;
        public string CanonicalViewName { get; init; } = string.Empty;
        public string CurrentSheetText { get; init; } = string.Empty;
        public bool IsAlreadyOnSheet { get; init; }
    }

    private sealed class ComposerPlacementDisplayRow
    {
        public string DetailNumber { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string CanonicalViewName { get; init; } = string.Empty;
        public string XmmText { get; set; } = string.Empty;
        public string YmmText { get; set; } = string.Empty;
        public string ScaleText { get; set; } = string.Empty;
    }
}
