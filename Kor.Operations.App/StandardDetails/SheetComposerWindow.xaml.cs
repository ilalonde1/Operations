#nullable enable
using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media.Imaging;

namespace Kor.Operations.StandardDetails;

public partial class SheetComposerWindow : Window
{
    private const double SheetWidthMm = 914.4;
    private const double SheetHeightMm = 609.6;
    private const double TitleBlockWidthMm = 185;
    private const double DefaultPlacementWidthMm = 155;
    private const double MinPlacementWidthMm = 45;
    private const double MaxPlacementWidthMm = 360;
    private const double SnapGridMm = 5;

    private readonly KorStandardsReadRepository _catalogRepository;
    private readonly StandardDetailsRepository _governanceRepository;
    private readonly StandardDetailsSheetComposer _composer;
    private readonly bool _groupSchemaAvailable;
    private readonly long? _selectedGroupId;
    private readonly Guid _actorUserId;
    private readonly string? _selectedDiscipline;
    private readonly string? _selectedKind;
    private readonly ObservableCollection<ComposerDetailDisplayRow> _details = new();
    private readonly ObservableCollection<ComposerPlacementDisplayRow> _placements = new();
    private double _sheetPxPerMm = 1;
    private bool _syncingPlacementFields;

    internal SheetComposerWindow(
        KorStandardsReadRepository catalogRepository,
        StandardDetailsRepository governanceRepository,
        StandardDetailsSheetComposer composer,
        bool groupSchemaAvailable,
        long? selectedGroupId,
        Guid actorUserId,
        string? selectedDiscipline = null,
        string? selectedKind = null)
    {
        _catalogRepository = catalogRepository ?? throw new ArgumentNullException(nameof(catalogRepository));
        _governanceRepository = governanceRepository ?? throw new ArgumentNullException(nameof(governanceRepository));
        _composer = composer ?? throw new ArgumentNullException(nameof(composer));
        _groupSchemaAvailable = groupSchemaAvailable;
        _selectedGroupId = selectedGroupId;
        _actorUserId = actorUserId;
        _selectedDiscipline = string.IsNullOrWhiteSpace(selectedDiscipline) ? null : selectedDiscipline.Trim();
        _selectedKind = string.IsNullOrWhiteSpace(selectedKind) ? null : selectedKind.Trim();
        InitializeComponent();
        DetailsGrid.ItemsSource = _details;
        PlacementsGrid.ItemsSource = _placements;
        PlacementItems.ItemsSource = _placements;
        _placements.CollectionChanged += Placements_CollectionChanged;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateSheetCanvas();
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
            var rows = await _catalogRepository.LoadSheetComposerDetailsAsync(SearchBox.Text?.Trim() ?? string.Empty, _selectedDiscipline, _selectedKind);
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
                    Kind = row.Kind,
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

    private async void Add_Click(object sender, RoutedEventArgs e)
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

        ToggleBusy(true);
        try
        {
            var thumbnail = await LoadDetailThumbnailAsync(detail.DetailNumber);
            var row = new ComposerPlacementDisplayRow
            {
                DetailNumber = detail.DetailNumber,
                Title = detail.Title,
                CanonicalViewName = detail.CanonicalViewName,
                Thumbnail = thumbnail,
                ImageAspect = thumbnail is null || thumbnail.PixelHeight == 0
                    ? 4.0 / 3.0
                    : (double)thumbnail.PixelWidth / thumbnail.PixelHeight
            };

            var size = ComputePlacementSizeMm(row);
            var spot = FindFreeSpot(size.Width, size.Height);
            SetPlacementCenter(row, spot.X, spot.Y, updateText: true);
            _placements.Add(row);
            PlacementsGrid.SelectedItem = row;
            PlacementsGrid.ScrollIntoView(row);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Standard Details - Add Detail", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ToggleBusy(false);
        }
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

    private void SheetHost_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSheetCanvas();
    }

    private void PlacementThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ComposerPlacementDisplayRow row })
        {
            PlacementsGrid.SelectedItem = row;
        }
    }

    private void PlacementThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ComposerPlacementDisplayRow row })
        {
            return;
        }

        var center = ReadPlacementCenter(row);
        var size = ComputePlacementSizeMm(row);
        var x = center.X + PixelsToMm(e.HorizontalChange);
        var y = center.Y + PixelsToMm(e.VerticalChange);
        if (SnapCheckBox.IsChecked == true)
        {
            x = Snap(x);
            y = Snap(y);
        }

        x = Clamp(x, size.Width / 2, SheetWidthMm - (size.Width / 2));
        y = Clamp(y, size.Height / 2, SheetHeightMm - (size.Height / 2));
        SetPlacementCenter(row, x, y, updateText: true);
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ComposerPlacementDisplayRow row })
        {
            return;
        }

        e.Handled = true;
        PlacementsGrid.SelectedItem = row;

        var size = ComputePlacementSizeMm(row);
        var verticalWidthDelta = PixelsToMm(e.VerticalChange) * row.ImageAspect;
        var horizontalWidthDelta = PixelsToMm(e.HorizontalChange);
        var widthDelta = Math.Abs(verticalWidthDelta) > Math.Abs(horizontalWidthDelta)
            ? verticalWidthDelta
            : horizontalWidthDelta;
        var width = Clamp(size.Width + widthDelta, MinPlacementWidthMm, MaxPlacementWidthMm);
        var scale = ClampInt((int)Math.Round(DefaultPlacementWidthMm * 100 / width), 1, 24000);
        var center = ReadPlacementCenter(row);

        _syncingPlacementFields = true;
        try
        {
            row.ScaleText = scale.ToString(CultureInfo.InvariantCulture);
            SetPlacementCenter(row, center.X, center.Y, updateText: true);
        }
        finally
        {
            _syncingPlacementFields = false;
        }

        UpdatePlacementFromFields(row);
    }

    private async Task<BitmapSource?> LoadDetailThumbnailAsync(string detailNumber)
    {
        byte[]? bytes;
        try
        {
            bytes = await _catalogRepository.LoadRenderedImageAsync("detail", detailNumber);
        }
        catch
        {
            return null;
        }

        if (bytes is not { Length: > 0 })
        {
            return null;
        }

        try
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(bytes))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }

            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void Placements_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (ComposerPlacementDisplayRow row in e.OldItems)
            {
                row.PropertyChanged -= Placement_PropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (ComposerPlacementDisplayRow row in e.NewItems)
            {
                row.PropertyChanged += Placement_PropertyChanged;
                UpdatePlacementFromFields(row);
            }
        }

        UpdatePlacementSummary();
    }

    private void Placement_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_syncingPlacementFields || sender is not ComposerPlacementDisplayRow row)
        {
            return;
        }

        if (e.PropertyName is nameof(ComposerPlacementDisplayRow.XmmText)
            or nameof(ComposerPlacementDisplayRow.YmmText)
            or nameof(ComposerPlacementDisplayRow.ScaleText))
        {
            UpdatePlacementFromFields(row);
        }
    }

    private void UpdateSheetCanvas()
    {
        var availableWidth = Math.Max(1, SheetHost.ActualWidth - 4);
        var availableHeight = Math.Max(1, SheetHost.ActualHeight - 4);
        _sheetPxPerMm = Math.Min(availableWidth / SheetWidthMm, availableHeight / SheetHeightMm);

        var width = SheetWidthMm * _sheetPxPerMm;
        var height = SheetHeightMm * _sheetPxPerMm;
        SheetCanvas.Width = width;
        SheetCanvas.Height = height;
        PlacementItems.Width = width;
        PlacementItems.Height = height;

        SheetPaper.Width = width;
        SheetPaper.Height = height;
        Canvas.SetLeft(SheetPaper, 0);
        Canvas.SetTop(SheetPaper, 0);

        var titleWidth = MmToPixels(TitleBlockWidthMm);
        TitleBlockRegion.Width = titleWidth;
        TitleBlockRegion.Height = height;
        Canvas.SetLeft(TitleBlockRegion, width - titleWidth);
        Canvas.SetTop(TitleBlockRegion, 0);

        PositionTitleBlockLine(TitleBlockTopLine, width - titleWidth, height * 0.68, width, height * 0.68);
        PositionTitleBlockLine(TitleBlockMiddleLine, width - titleWidth, height * 0.78, width, height * 0.78);
        PositionTitleBlockLine(TitleBlockBottomLine, width - titleWidth, height * 0.9, width, height * 0.9);

        foreach (var row in _placements)
        {
            UpdatePlacementFromFields(row);
        }
    }

    private static void PositionTitleBlockLine(System.Windows.Shapes.Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
    }

    private void UpdatePlacementFromFields(ComposerPlacementDisplayRow row)
    {
        var center = ReadPlacementCenter(row);
        var size = ComputePlacementSizeMm(row);
        var x = Clamp(center.X, size.Width / 2, SheetWidthMm - (size.Width / 2));
        var y = Clamp(center.Y, size.Height / 2, SheetHeightMm - (size.Height / 2));
        SetPlacementCenter(row, x, y, updateText: x != center.X || y != center.Y);
    }

    private (double X, double Y) ReadPlacementCenter(ComposerPlacementDisplayRow row)
    {
        var x = TryParseDouble(row.XmmText, out var parsedX) ? parsedX : SheetWidthMm / 2;
        var y = TryParseDouble(row.YmmText, out var parsedY) ? parsedY : SheetHeightMm / 2;
        return (x, y);
    }

    private void SetPlacementCenter(ComposerPlacementDisplayRow row, double xMm, double yMm, bool updateText)
    {
        var size = ComputePlacementSizeMm(row);
        xMm = Clamp(xMm, size.Width / 2, SheetWidthMm - (size.Width / 2));
        yMm = Clamp(yMm, size.Height / 2, SheetHeightMm - (size.Height / 2));

        _syncingPlacementFields = true;
        try
        {
            if (updateText)
            {
                row.XmmText = FormatMm(xMm);
                row.YmmText = FormatMm(yMm);
            }

            row.SetCanvasRect(
                MmToPixels(xMm - (size.Width / 2)),
                MmToPixels(yMm - (size.Height / 2)),
                MmToPixels(size.Width),
                MmToPixels(size.Height));
        }
        finally
        {
            _syncingPlacementFields = false;
        }
    }

    private (double Width, double Height) ComputePlacementSizeMm(ComposerPlacementDisplayRow row)
    {
        var scale = TryParseScale(row.ScaleText) ?? 100;
        var width = Clamp(DefaultPlacementWidthMm * 100 / scale, MinPlacementWidthMm, MaxPlacementWidthMm);
        var height = width / Math.Max(0.2, row.ImageAspect);
        if (height > SheetHeightMm * 0.9)
        {
            height = SheetHeightMm * 0.9;
            width = height * row.ImageAspect;
        }

        return (width, height);
    }

    private (double X, double Y) FindFreeSpot(double widthMm, double heightMm)
    {
        var margin = 20;
        var step = 25;
        for (var y = margin + (heightMm / 2); y <= SheetHeightMm - (heightMm / 2); y += step)
        {
            for (var x = margin + (widthMm / 2); x <= SheetWidthMm - TitleBlockWidthMm - (widthMm / 2) - margin; x += step)
            {
                if (!_placements.Any(p => Overlaps(x, y, widthMm, heightMm, p)))
                {
                    return (x, y);
                }
            }
        }

        return (SheetWidthMm / 2, SheetHeightMm / 2);
    }

    private bool Overlaps(double xMm, double yMm, double widthMm, double heightMm, ComposerPlacementDisplayRow row)
    {
        var center = ReadPlacementCenter(row);
        var size = ComputePlacementSizeMm(row);
        var left = xMm - (widthMm / 2);
        var right = xMm + (widthMm / 2);
        var top = yMm - (heightMm / 2);
        var bottom = yMm + (heightMm / 2);
        var rowLeft = center.X - (size.Width / 2);
        var rowRight = center.X + (size.Width / 2);
        var rowTop = center.Y - (size.Height / 2);
        var rowBottom = center.Y + (size.Height / 2);
        return left < rowRight && right > rowLeft && top < rowBottom && bottom > rowTop;
    }

    private double MmToPixels(double mm) => mm * _sheetPxPerMm;

    private double PixelsToMm(double pixels) => pixels / Math.Max(0.0001, _sheetPxPerMm);

    private static double Snap(double value) => Math.Round(value / SnapGridMm) * SnapGridMm;

    private static double Clamp(double value, double min, double max) => Math.Min(Math.Max(value, min), max);

    private static int ClampInt(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    private static string FormatMm(double value) => Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);

    private static bool TryParseDouble(string text, out double value)
        => double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static int? TryParseScale(string text)
    {
        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 1
            ? value
            : null;
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
        DetailsGrid.IsEnabled = !busy;
        PlacementsGrid.IsEnabled = !busy;
        SheetCanvas.IsEnabled = !busy;
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
        public string Kind { get; init; } = string.Empty;
        public string CanonicalViewName { get; init; } = string.Empty;
        public string CurrentSheetText { get; init; } = string.Empty;
        public bool IsAlreadyOnSheet { get; init; }
    }

    private sealed class ComposerPlacementDisplayRow : INotifyPropertyChanged
    {
        private string _xmmText = string.Empty;
        private string _ymmText = string.Empty;
        private string _scaleText = string.Empty;
        private double _canvasLeft;
        private double _canvasTop;
        private double _canvasWidth;
        private double _canvasHeight;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string DetailNumber { get; init; } = string.Empty;
        public string Title { get; init; } = string.Empty;
        public string CanonicalViewName { get; init; } = string.Empty;
        public BitmapSource? Thumbnail { get; init; }
        public double ImageAspect { get; init; } = 4.0 / 3.0;
        public Visibility ThumbnailVisibility => Thumbnail is null ? Visibility.Collapsed : Visibility.Visible;
        public Visibility PlaceholderVisibility => Thumbnail is null ? Visibility.Visible : Visibility.Collapsed;

        public string XmmText
        {
            get => _xmmText;
            set => SetField(ref _xmmText, value ?? string.Empty);
        }

        public string YmmText
        {
            get => _ymmText;
            set => SetField(ref _ymmText, value ?? string.Empty);
        }

        public string ScaleText
        {
            get => _scaleText;
            set => SetField(ref _scaleText, value ?? string.Empty);
        }

        public double CanvasLeft
        {
            get => _canvasLeft;
            private set => SetField(ref _canvasLeft, value);
        }

        public double CanvasTop
        {
            get => _canvasTop;
            private set => SetField(ref _canvasTop, value);
        }

        public double CanvasWidth
        {
            get => _canvasWidth;
            private set => SetField(ref _canvasWidth, value);
        }

        public double CanvasHeight
        {
            get => _canvasHeight;
            private set => SetField(ref _canvasHeight, value);
        }

        public void SetCanvasRect(double left, double top, double width, double height)
        {
            CanvasLeft = left;
            CanvasTop = top;
            CanvasWidth = width;
            CanvasHeight = height;
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
