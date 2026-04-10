#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    internal sealed class SlabPropsRow
    {
        public (byte R, byte G, byte B) Color { get; init; }
        public required TextBox NameTextBox { get; init; }
        public required ComboBox TypeComboBox { get; init; }
        public required TextBox ThicknessTextBox { get; init; }
        public required TextBox SdlTextBox { get; init; }
        public required TextBox LiveTextBox { get; init; }
        public required CheckBox IncludeCheckBox { get; init; }
        public required TextBlock AutoIndicatorTextBlock { get; init; }
        public required FrameworkElement RowContainer { get; init; }
        public required ComboBox GradeComboBox { get; init; }
        public required FrameworkElement GradeContainer { get; init; }
        public required FrameworkElement ThicknessContainer { get; init; }
        public required FrameworkElement SdlContainer { get; init; }
        public required FrameworkElement LiveContainer { get; init; }
        public required string DefaultElementType { get; init; }
    }

    public partial class PdfToSafeWindow : Window
    {
        private string? _loadedFilePath;
        private string? _projectPath;
        private PdfToSafeProject _project = new();
        private ExtractedGeometry? _extractedGeometry;
        private bool _isPopulatingPageSelector;
        private readonly GeometryExclusionState _excl;
        private readonly List<SlabPropsRow> _slabPropsRows;
        private BitmapSource? _renderedBitmap;
        private readonly PdfGeometryAnalysisService _aiService;
        private readonly ILogger<PdfToSafeWindow> _logger;
        private CancellationTokenSource _opCts;
        private readonly PdfViewportController _viewport;

        public PdfToSafeWindow(ILogger<PdfToSafeWindow> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _excl = new GeometryExclusionState();
            _slabPropsRows = new List<SlabPropsRow>();
            _aiService = new PdfGeometryAnalysisService(
                Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY") ?? string.Empty);
            _opCts = new CancellationTokenSource();
            _viewport = new PdfViewportController();
            InitializeComponent();
        }

        private async void LoadPdf_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select structural PDF",
                Filter = "PDF files (*.pdf)|*.pdf",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            _loadedFilePath = dialog.FileName;
            _projectPath = null;
            _project = new PdfToSafeProject();
            _excl.Clear();
            FileNameText.Text = System.IO.Path.GetFileName(_loadedFilePath);

            LoadPdfButton.IsEnabled = false;
            try
            {
                ShowLoading("Analysing PDF...");
                SetStatus("Analysing PDF...", "#E8EAF6", "#3949AB");

                int scale = 100;
                var detectedScale = await Task.Run(
                    () => PdfGeometryExtractor.DetectScale(_loadedFilePath),
                    BeginOperation()).ConfigureAwait(true);
                if (detectedScale.HasValue && detectedScale.Value > 0)
                {
                    scale = detectedScale.Value;
                }
                else if (int.TryParse(ScaleInput.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) && parsed > 0)
                {
                    scale = parsed;
                }

                ScaleInput.Text = scale.ToString(CultureInfo.InvariantCulture);

                // Diagnostic trace — dumps full extraction pipeline to desktop
                var diagPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "pdf_extraction_trace.txt");
                await Task.Run(() => PdfDiagnostic.TraceExtraction(_loadedFilePath!, diagPath, scale, 1)).ConfigureAwait(true);
                _logger.LogInformation("Diagnostic trace written to {Path}", diagPath);

                _extractedGeometry = await ExtractGeometryAsync(_loadedFilePath, scale, 1).ConfigureAwait(true);
                await RefreshFromGeometryAsync(_extractedGeometry, _loadedFilePath, 1, scale, true).ConfigureAwait(true);

                SetStatus(
                    _extractedGeometry.IsVectorPdf
                        ? "Vector PDF detected. Ready for configuration and export."
                        : "Raster or image-only PDF detected. Vector PDF is required for export.",
                    _extractedGeometry.IsVectorPdf ? "#E8F5E9" : "#FFF3E0",
                    _extractedGeometry.IsVectorPdf ? "#2E7D32" : "#E65100");
            }
            catch (OperationCanceledException)
            {
                SetStatus("Operation cancelled.", "#F5F5F5", "#616161");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load PDF {FilePath}", _loadedFilePath);
                SetStatus($"Failed to load PDF: {ex.Message}", "#FFEBEE", "#C62828");
                UpdateExportState();
            }
            finally
            {
                HideLoading();
                LoadPdfButton.IsEnabled = true;
            }
        }

        private async Task<ExtractedGeometry> ExtractGeometryAsync(string filePath, int scale, int pageNumber)
        {
            var (slabMin, lineMin, excludeGridLines) = ReadThresholds();
            var ct = BeginOperation();
            return await Task.Run(
                () => PdfGeometryExtractor.Extract(filePath, scale, pageNumber, slabMin, lineMin, excludeGridLines),
                ct).ConfigureAwait(true);
        }

        private async Task RefreshFromGeometryAsync(ExtractedGeometry geometry, string filePath, int pageNumber, int scale, bool updatePageSelector)
        {
            UpdateDetectionSummary(geometry);
            BuildColorSwatches(geometry);
            BuildSlabPropsRows(geometry);
            await ApplyThicknessHintsAsync(filePath, pageNumber, scale).ConfigureAwait(true);
            UpdatePdfInfo(geometry);

            if (updatePageSelector)
            {
                _isPopulatingPageSelector = true;
                PageSelector.Items.Clear();
                for (int i = 1; i <= geometry.PageCount; i++)
                    PageSelector.Items.Add($"Page {i}");
                PageSelector.SelectedIndex = Math.Max(0, pageNumber - 1);
                _isPopulatingPageSelector = false;
                PageSelectorPanel.Visibility = geometry.PageCount > 1 ? Visibility.Visible : Visibility.Collapsed;
            }

            ScalePanel.Visibility = Visibility.Visible;
            ElementsConfigPanel.Visibility = Visibility.Visible;
            PdfInfoPanel.Visibility = Visibility.Visible;
            AiPanel.Visibility = _aiService.IsConfigured ? Visibility.Visible : Visibility.Collapsed;

            await RenderPreviewAsync(filePath, pageNumber - 1).ConfigureAwait(true);
            UpdateExportState();
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows10.0.10240.0")]
        private async Task RenderPreviewAsync(string filePath, int pageIndex = 0)
        {
            try
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile);
                using var page = pdfDoc.GetPage((uint)pageIndex);
                using var stream = new InMemoryRandomAccessStream();

                await page.RenderToStreamAsync(stream, new PdfPageRenderOptions
                {
                    DestinationWidth = PdfToSafeConstants.PreviewBitmapWidth
                });

                stream.Seek(0);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream.AsStream();
                bitmap.EndInit();
                bitmap.Freeze();

                _renderedBitmap = bitmap;
                double aspect = (double)bitmap.PixelHeight / bitmap.PixelWidth;
                double height = PdfToSafeConstants.PreviewBitmapWidth * aspect;

                PreviewCanvas.Width = PdfToSafeConstants.PreviewBitmapWidth;
                PreviewCanvas.Height = height;
                PreviewImage.Width = PdfToSafeConstants.PreviewBitmapWidth;
                PreviewImage.Height = height;
                PreviewImage.Source = bitmap;

                DrawOverlay();
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                PreviewViewbox.Visibility = Visibility.Visible;
                ZoomToolbar.Visibility = Visibility.Visible;
                PreviewLegend.Visibility = Visibility.Visible;
                AiAnalyseButton.IsEnabled = _aiService.IsConfigured && _renderedBitmap is not null && _slabPropsRows.Count > 0;
                _ = Dispatcher.InvokeAsync(FitToView, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Preview render failed for {FilePath}", filePath);
                PreviewPlaceholder.Visibility = Visibility.Visible;
                PreviewViewbox.Visibility = Visibility.Collapsed;
                PreviewLegend.Visibility = Visibility.Collapsed;
                ZoomToolbar.Visibility = Visibility.Collapsed;
            }
        }

        private void DrawOverlay()
        {
            if (_extractedGeometry is null)
                return;

            var toRemove = PreviewCanvas.Children.OfType<UIElement>().Where(x => x != PreviewImage).ToList();
            foreach (var child in toRemove)
                PreviewCanvas.Children.Remove(child);

            if (PreviewCanvas.Width <= 0 || _extractedGeometry.PageWidthPts <= 0 || _extractedGeometry.PageHeightPts <= 0)
                return;

            var xform = new CoordinateTransformer(
                PreviewCanvas.Width,
                _extractedGeometry.PageWidthPts,
                _extractedGeometry.PageHeightPts,
                _extractedGeometry.ScaleDenominator);

            Point ToCanvas(double xMm, double yMm)
            {
                var (x, y) = xform.ToCanvas(xMm, yMm);
                return new Point(x, y);
            }

            for (int i = 0; i < _extractedGeometry.Slabs.Count; i++)
            {
                bool excluded = _excl.IsSlabExcluded(i, _extractedGeometry.SlabColors);
                string? overrideType = _excl.SlabTypeOverrides.TryGetValue(i, out var sov) ? sov : null;
                bool isIgnore = overrideType is not null && string.Equals(overrideType, "Ignore", StringComparison.OrdinalIgnoreCase);

                Brush stroke = (excluded || isIgnore) ? Brushes.White
                    : overrideType switch
                    {
                        "Column" => Brushes.Yellow,
                        "Beam"   => Brushes.Cyan,
                        _        => Brushes.LimeGreen
                    };
                var shape = new Polyline
                {
                    Stroke = stroke,
                    Fill = (excluded || isIgnore) ? new SolidColorBrush(Color.FromArgb(128, 255, 255, 255)) : Brushes.Transparent,
                    StrokeThickness = (excluded || isIgnore) ? 1.5 : 2.0,
                    Opacity = (excluded || isIgnore) ? 0.7 : 1.0,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = Tuple.Create("slab", i),
                    Points = new PointCollection(_extractedGeometry.Slabs[i].Select(p => ToCanvas(p.X, p.Y)))
                };
                if (_extractedGeometry.Slabs[i].Count > 0)
                {
                    var first = _extractedGeometry.Slabs[i][0];
                    shape.Points.Add(ToCanvas(first.X, first.Y));
                }

                shape.MouseDown += Shape_MouseDown;
                Canvas.SetZIndex(shape, 1);
                PreviewCanvas.Children.Add(shape);
            }

            for (int i = 0; i < _extractedGeometry.Lines.Count; i++)
            {
                bool excluded = _excl.IsLineExcluded(i, _extractedGeometry.LineColors);
                string? overrideType = _excl.LineTypeOverrides.TryGetValue(i, out var lov) ? lov : null;
                bool isIgnore = overrideType is not null && string.Equals(overrideType, "Ignore", StringComparison.OrdinalIgnoreCase);

                Brush stroke = (excluded || isIgnore) ? Brushes.White
                    : overrideType switch
                    {
                        "Slab"   => Brushes.LimeGreen,
                        "Column" => Brushes.Yellow,
                        _        => Brushes.Cyan
                    };
                var linePts = _extractedGeometry.Lines[i];
                var shape = new Polyline
                {
                    Stroke = stroke,
                    StrokeThickness = (excluded || isIgnore) ? 4.0 : 1.5,
                    Opacity = (excluded || isIgnore) ? 0.65 : 1.0,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = Tuple.Create("line", i),
                    Points = new PointCollection(linePts.Select(p => ToCanvas(p.X, p.Y)))
                };
                // Close the polyline if endpoints are near each other (elongated closed shapes
                // classified as beams still need their 4th side drawn)
                if (linePts.Count >= 3 &&
                    PolygonProcessor.Distance(linePts[0], linePts[^1]) < 10.0)
                    shape.Points.Add(ToCanvas(linePts[0].X, linePts[0].Y));
                shape.MouseDown += Shape_MouseDown;
                Canvas.SetZIndex(shape, 1);
                PreviewCanvas.Children.Add(shape);
            }

            for (int i = 0; i < _extractedGeometry.Columns.Count; i++)
            {
                bool excluded = _excl.IsColumnExcluded(i, _extractedGeometry.ColumnColors);
                string? overrideType = _excl.ColumnTypeOverrides.TryGetValue(i, out var cov) ? cov : null;
                bool isIgnore = overrideType is not null && string.Equals(overrideType, "Ignore", StringComparison.OrdinalIgnoreCase);

                Brush fill = (excluded || isIgnore) ? Brushes.LightGray
                    : overrideType switch
                    {
                        "Slab" => Brushes.LimeGreen,
                        "Beam" => Brushes.Cyan,
                        _      => Brushes.Yellow
                    };
                Brush border = (excluded || isIgnore) ? Brushes.Gray
                    : overrideType switch
                    {
                        "Slab" => Brushes.DarkGreen,
                        "Beam" => Brushes.DarkCyan,
                        _      => Brushes.DarkGoldenrod
                    };
                var (x, y) = _extractedGeometry.Columns[i];
                var pt = ToCanvas(x, y);
                var dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = fill,
                    Stroke = border,
                    StrokeThickness = (excluded || isIgnore) ? 0.5 : 1.0,
                    Opacity = (excluded || isIgnore) ? 0.35 : 1.0,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    Tag = Tuple.Create("column", i)
                };
                dot.MouseDown += Shape_MouseDown;
                Canvas.SetLeft(dot, pt.X - 5);
                Canvas.SetTop(dot, pt.Y - 5);
                Canvas.SetZIndex(dot, 2);
                PreviewCanvas.Children.Add(dot);
            }

            bool hasContent = _extractedGeometry.Slabs.Count > 0 || _extractedGeometry.Lines.Count > 0 || _extractedGeometry.Columns.Count > 0;
            PreviewLegend.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
            bool hasOverrides = _excl.HasIndexExclusions || _excl.Colors.Count > 0
                || _excl.SlabTypeOverrides.Count > 0 || _excl.LineTypeOverrides.Count > 0 || _excl.ColumnTypeOverrides.Count > 0;
            ClearExclusionsButton.Visibility = hasOverrides ? Visibility.Visible : Visibility.Collapsed;

            if (_extractedGeometry.Slabs.Count > 0)
                LegendSlabRow.Opacity = Enumerable.Range(0, _extractedGeometry.Slabs.Count).All(i => _excl.IsSlabExcluded(i, _extractedGeometry.SlabColors)) ? 0.35 : 1.0;
            if (_extractedGeometry.Lines.Count > 0)
                LegendLineRow.Opacity = Enumerable.Range(0, _extractedGeometry.Lines.Count).All(i => _excl.IsLineExcluded(i, _extractedGeometry.LineColors)) ? 0.35 : 1.0;
            if (_extractedGeometry.Columns.Count > 0)
                LegendColumnRow.Opacity = Enumerable.Range(0, _extractedGeometry.Columns.Count).All(i => _excl.IsColumnExcluded(i, _extractedGeometry.ColumnColors)) ? 0.35 : 1.0;
        }

        private void Shape_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.Tag is not Tuple<string, int> tag)
                return;

            if (e.ChangedButton == System.Windows.Input.MouseButton.Right)
            {
                ShowElementTypeMenu(fe, tag.Item1, tag.Item2);
                e.Handled = true;
                return;
            }

            switch (tag.Item1)
            {
                case "slab":
                    ToggleSetMembership(_excl.Slabs, tag.Item2);
                    break;
                case "line":
                    ToggleSetMembership(_excl.Lines, tag.Item2);
                    break;
                case "column":
                    ToggleSetMembership(_excl.Columns, tag.Item2);
                    break;
            }

            DrawOverlay();
            e.Handled = true;
        }

        private void ShowElementTypeMenu(FrameworkElement target, string elementKind, int index)
        {
            var overrides = elementKind switch
            {
                "slab" => _excl.SlabTypeOverrides,
                "line" => _excl.LineTypeOverrides,
                "column" => _excl.ColumnTypeOverrides,
                _ => null
            };
            if (overrides is null) return;

            string? current = overrides.TryGetValue(index, out var v) ? v : null;
            var menu = new ContextMenu();

            foreach (var typeName in new[] { "Slab", "Beam", "Column", "Ignore" })
            {
                var item = new MenuItem
                {
                    Header = typeName,
                    IsChecked = string.Equals(current, typeName, StringComparison.OrdinalIgnoreCase),
                    Tag = (overrides, index, typeName)
                };
                item.Click += ElementTypeMenuItem_Click;
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            var reset = new MenuItem
            {
                Header = "Reset to default",
                IsEnabled = current is not null,
                Tag = (overrides, index, "")
            };
            reset.Click += ElementTypeMenuItem_Click;
            menu.Items.Add(reset);

            target.ContextMenu = menu;
            menu.IsOpen = true;
        }

        private void ElementTypeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not ValueTuple<Dictionary<int, string>, int, string> tag)
                return;

            var (overrides, index, typeName) = tag;
            if (string.IsNullOrEmpty(typeName))
                overrides.Remove(index);
            else
                overrides[index] = typeName;

            DrawOverlay();
        }

        private static void ToggleSetMembership(HashSet<int> set, int value)
        {
            if (!set.Add(value)) set.Remove(value);
        }

        private int ParseScale()
            => int.TryParse(ScaleInput.Text, out var s) && s > 0 ? s : 100;

        private void ClearExclusions_Click(object sender, RoutedEventArgs e)
        {
            _excl.Clear();
            foreach (var row in _slabPropsRows)
            {
                if (string.Equals(row.TypeComboBox.SelectedItem as string, "Ignore", StringComparison.OrdinalIgnoreCase))
                {
                    row.TypeComboBox.SelectedItem = row.DefaultElementType;
                    row.IncludeCheckBox.IsChecked = true;
                }
            }

            RebuildExcludedColors();
            DrawOverlay();
        }

        private void LegendSlab_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_extractedGeometry is null || _extractedGeometry.Slabs.Count == 0)
                return;

            bool allHidden = Enumerable.Range(0, _extractedGeometry.Slabs.Count).All(i => _excl.Slabs.Contains(i));
            _excl.Slabs.Clear();
            if (!allHidden)
            {
                for (int i = 0; i < _extractedGeometry.Slabs.Count; i++)
                    _excl.Slabs.Add(i);
            }

            DrawOverlay();
            e.Handled = true;
        }

        private void LegendLine_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_extractedGeometry is null || _extractedGeometry.Lines.Count == 0)
                return;

            bool allHidden = Enumerable.Range(0, _extractedGeometry.Lines.Count).All(i => _excl.Lines.Contains(i));
            _excl.Lines.Clear();
            if (!allHidden)
            {
                for (int i = 0; i < _extractedGeometry.Lines.Count; i++)
                    _excl.Lines.Add(i);
            }

            DrawOverlay();
            e.Handled = true;
        }

        private void LegendColumn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_extractedGeometry is null || _extractedGeometry.Columns.Count == 0)
                return;

            bool allHidden = Enumerable.Range(0, _extractedGeometry.Columns.Count).All(i => _excl.Columns.Contains(i));
            _excl.Columns.Clear();
            if (!allHidden)
            {
                for (int i = 0; i < _extractedGeometry.Columns.Count; i++)
                    _excl.Columns.Add(i);
            }

            DrawOverlay();
            e.Handled = true;
        }

        private void ApplyTransform()
        {
            PreviewCanvas.RenderTransform = _viewport.BuildTransform();
        }

        private void FitToView()
        {
            _viewport.FitToView(PreviewViewbox.ActualWidth, PreviewViewbox.ActualHeight, PreviewCanvas.Width, PreviewCanvas.Height);
            ApplyTransform();
        }

        private void PreviewContainer_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            double factor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            _viewport.ZoomAround(factor, e.GetPosition(PreviewViewbox).X, e.GetPosition(PreviewViewbox).Y);
            ApplyTransform();
            e.Handled = true;
        }

        private void PreviewContainer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left && e.ClickCount == 2)
            {
                FitToView();
                e.Handled = true;
                return;
            }

            if (e.ChangedButton is System.Windows.Input.MouseButton.Right or System.Windows.Input.MouseButton.Middle)
            {
                _viewport.BeginPan(e.GetPosition(PreviewViewbox));
                PreviewViewbox.Cursor = System.Windows.Input.Cursors.SizeAll;
                e.Handled = true;
            }
        }

        private void PreviewContainer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_viewport.UpdatePan(e.GetPosition(PreviewViewbox)))
            {
                ApplyTransform();
                e.Handled = true;
            }
        }

        private void PreviewContainer_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            _viewport.EndPan();
            PreviewViewbox.Cursor = System.Windows.Input.Cursors.Arrow;
            e.Handled = true;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            _viewport.ZoomAround(1.15, PreviewViewbox.ActualWidth / 2.0, PreviewViewbox.ActualHeight / 2.0);
            ApplyTransform();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            _viewport.ZoomAround(1.0 / 1.15, PreviewViewbox.ActualWidth / 2.0, PreviewViewbox.ActualHeight / 2.0);
            ApplyTransform();
        }

        private void FitView_Click(object sender, RoutedEventArgs e) => FitToView();

        private async void PageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingPageSelector || string.IsNullOrWhiteSpace(_loadedFilePath) || PageSelector.SelectedIndex < 0)
                return;

            try
            {
                int scale = ParseScale();
                int pageNumber = PageSelector.SelectedIndex + 1;
                ShowLoading("Loading page...");
                SetStatus("Analysing selected page...", "#E8EAF6", "#3949AB");
                _excl.Clear();
                _extractedGeometry = await ExtractGeometryAsync(_loadedFilePath, scale, pageNumber).ConfigureAwait(true);
                await RefreshFromGeometryAsync(_extractedGeometry, _loadedFilePath, pageNumber, scale, false).ConfigureAwait(true);
                SetStatus(
                    _extractedGeometry.IsVectorPdf ? "Page analysed." : "Selected page is not a vector PDF page.",
                    _extractedGeometry.IsVectorPdf ? "#E8F5E9" : "#FFF3E0",
                    _extractedGeometry.IsVectorPdf ? "#2E7D32" : "#E65100");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to analyse selected page");
                SetStatus($"Failed to analyse selected page: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                HideLoading();
            }
        }

        private async void ReAnalyse_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_loadedFilePath))
                return;

            try
            {
                int scale = ParseScale();
                int pageNumber = PageSelector.SelectedIndex >= 0 ? PageSelector.SelectedIndex + 1 : 1;
                ShowLoading("Re-analysing...");
                SetStatus("Re-analysing with current scale...", "#E8EAF6", "#3949AB");
                _excl.Clear();
                _extractedGeometry = await ExtractGeometryAsync(_loadedFilePath, scale, pageNumber).ConfigureAwait(true);
                await RefreshFromGeometryAsync(_extractedGeometry, _loadedFilePath, pageNumber, scale, false).ConfigureAwait(true);
                DrawOverlay();
                SetStatus("Re-analysis complete.", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Re-analysis failed");
                SetStatus($"Re-analysis failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                HideLoading();
            }
        }

        private (double slabMin, double lineMin, bool excludeGridLines) ReadThresholds() => (1000.0, 200.0, false);

        private void BuildColorSwatches(ExtractedGeometry geo)
        {
            _excl.Colors.Clear();
            AiPanel.Visibility = _aiService.IsConfigured ? Visibility.Visible : Visibility.Collapsed;
            AiAnalyseButton.IsEnabled = _aiService.IsConfigured && _renderedBitmap is not null && geo.IsVectorPdf;
        }

        private void BuildSlabPropsRows(ExtractedGeometry geo)
        {
            ElementsConfigRowsPanel.Children.Clear();
            _slabPropsRows.Clear();

            var allColors = geo.SlabColors
                .Concat(geo.LineColors)
                .Concat(geo.ColumnColors)
                .Distinct()
                .OrderBy(c => c.R).ThenBy(c => c.G).ThenBy(c => c.B)
                .ToList();

            for (int i = 0; i < allColors.Count; i++)
            {
                var color = allColors[i];
                string defaultType = GetElementType(color);
                string defaultName = $"{AutoColorName(color, i)} ({color.R:X2}{color.G:X2}{color.B:X2})";

                var rowGrid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

                var includeCheck = new CheckBox
                {
                    IsChecked = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                var swatch = new Rectangle
                {
                    Width = 16,
                    Height = 16,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B)),
                    Stroke = Brushes.Gray,
                    StrokeThickness = 0.5,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var nameBox = new TextBox
                {
                    Text = defaultName,
                    FontSize = 11,
                    Padding = new Thickness(6, 4, 6, 4),
                    Margin = new Thickness(0, 0, 4, 0)
                };

                var typeCombo = new ComboBox { FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
                typeCombo.Items.Add("Slab");
                typeCombo.Items.Add("Beam");
                typeCombo.Items.Add("Column");
                typeCombo.Items.Add("Ignore");
                typeCombo.Items.Add("Opening");
                typeCombo.SelectedItem = defaultType;

                var gradeCombo = new ComboBox { FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
                foreach (var grade in StructuralMaterialDatabase.SupportedGrades)
                    gradeCombo.Items.Add(grade);
                gradeCombo.SelectedItem = PdfToSafeConstants.DefaultGradeCode;

                var thicknessBox = new TextBox
                {
                    Text = PdfToSafeConstants.DefaultThicknessMm.ToString("0.###", CultureInfo.InvariantCulture),
                    FontSize = 11,
                    Padding = new Thickness(6, 4, 6, 4)
                };
                var autoIndicator = new TextBlock
                {
                    Text = "auto",
                    Margin = new Thickness(4, 0, 0, 0),
                    Foreground = Brushes.DarkOliveGreen,
                    FontSize = 10,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed
                };
                var thicknessHost = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 4, 0)
                };
                thicknessHost.Children.Add(thicknessBox);
                thicknessHost.Children.Add(autoIndicator);

                var sdlBox = new TextBox { Text = "0", FontSize = 11, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 4, 0) };
                var liveBox = new TextBox { Text = "0", FontSize = 11, Padding = new Thickness(6, 4, 6, 4) };

                Grid.SetColumn(includeCheck, 0);
                Grid.SetColumn(swatch, 1);
                Grid.SetColumn(nameBox, 2);
                Grid.SetColumn(typeCombo, 3);
                Grid.SetColumn(gradeCombo, 4);
                Grid.SetColumn(thicknessHost, 5);
                Grid.SetColumn(sdlBox, 6);
                Grid.SetColumn(liveBox, 7);

                rowGrid.Children.Add(includeCheck);
                rowGrid.Children.Add(swatch);
                rowGrid.Children.Add(nameBox);
                rowGrid.Children.Add(typeCombo);
                rowGrid.Children.Add(gradeCombo);
                rowGrid.Children.Add(thicknessHost);
                rowGrid.Children.Add(sdlBox);
                rowGrid.Children.Add(liveBox);
                ElementsConfigRowsPanel.Children.Add(rowGrid);

                var row = new SlabPropsRow
                {
                    Color = color,
                    NameTextBox = nameBox,
                    TypeComboBox = typeCombo,
                    ThicknessTextBox = thicknessBox,
                    SdlTextBox = sdlBox,
                    LiveTextBox = liveBox,
                    IncludeCheckBox = includeCheck,
                    AutoIndicatorTextBlock = autoIndicator,
                    RowContainer = rowGrid,
                    GradeComboBox = gradeCombo,
                    GradeContainer = gradeCombo,
                    ThicknessContainer = thicknessHost,
                    SdlContainer = sdlBox,
                    LiveContainer = liveBox,
                    DefaultElementType = defaultType
                };

                typeCombo.SelectionChanged += (_, _) =>
                {
                    includeCheck.IsChecked = !IsExcludedType(typeCombo.SelectedItem as string);
                    UpdateElementRowUi(row);
                };
                includeCheck.Checked += (_, _) =>
                {
                    if (IsExcludedType(typeCombo.SelectedItem as string))
                        typeCombo.SelectedItem = row.DefaultElementType;
                };
                includeCheck.Unchecked += (_, _) => typeCombo.SelectedItem = "Ignore";

                _slabPropsRows.Add(row);
                UpdateElementRowUi(row, false);
            }
        }

        private void UpdateElementRowUi(SlabPropsRow row, bool redraw = true)
        {
            string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
            bool isSlab = string.Equals(type, "Slab", StringComparison.OrdinalIgnoreCase);
            bool excludedByType = IsExcludedType(type);

            row.GradeContainer.Visibility = isSlab ? Visibility.Visible : Visibility.Collapsed;
            row.ThicknessContainer.Visibility = isSlab ? Visibility.Visible : Visibility.Collapsed;
            row.SdlContainer.Visibility = isSlab ? Visibility.Visible : Visibility.Collapsed;
            row.LiveContainer.Visibility = isSlab ? Visibility.Visible : Visibility.Collapsed;
            row.RowContainer.Opacity = excludedByType ? 0.45 : 1.0;

            if (excludedByType) _excl.Colors.Add(row.Color);
            else _excl.Colors.Remove(row.Color);

            if (redraw)
                DrawOverlay();
        }

        private Dictionary<(byte R, byte G, byte B), SlabColorSettings> BuildSlabColorSettings()
        {
            var map = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>();
            foreach (var row in _slabPropsRows)
            {
                string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
                if (IsExcludedType(type))
                    continue;

                map[row.Color] = new SlabColorSettings
                {
                    ElementType = type,
                    ThicknessMm = ParsePositiveDouble(row.ThicknessTextBox.Text, PdfToSafeConstants.DefaultThicknessMm),
                    SdlKPa = ParseNonNegativeDouble(row.SdlTextBox.Text, 0.0),
                    LiveKPa = ParseNonNegativeDouble(row.LiveTextBox.Text, 0.0),
                    GradeCode = row.GradeComboBox.SelectedItem as string ?? PdfToSafeConstants.DefaultGradeCode
                };
            }

            return map;
        }

        private void RebuildExcludedColors()
        {
            _excl.Colors.Clear();
            foreach (var row in _slabPropsRows)
            {
                string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
                if (IsExcludedType(type))
                    _excl.Colors.Add(row.Color);
            }
        }

        private string GetElementType((byte R, byte G, byte B) color)
        {
            if (_extractedGeometry is null) return "Slab";

            // Count elements per bucket for this color — return the dominant type.
            // A color can appear in multiple buckets (e.g., large burgundy shapes
            // are slabs, small ones are columns). Return the most frequent.
            int slabCount = _extractedGeometry.SlabColors.Count(c => c == color);
            int lineCount = _extractedGeometry.LineColors.Count(c => c == color);
            int colCount  = _extractedGeometry.ColumnColors.Count(c => c == color);

            if (slabCount == 0 && lineCount == 0 && colCount == 0) return "Slab";

            if (colCount >= slabCount && colCount >= lineCount) return "Column";
            if (lineCount >= slabCount) return "Beam";
            return "Slab";
        }

        private static string AutoColorName((byte R, byte G, byte B) color, int index)
        {
            if (color == (0, 255, 255)) return "Cyan";
            if (color == (255, 255, 0)) return "Yellow";
            if (color == (255, 0, 0)) return "Red";
            if (color == (0, 255, 0)) return "Green";
            if (color == (0, 0, 255)) return "Blue";
            if (color == (255, 255, 255)) return "White";
            if (color == (0, 0, 0)) return "Black";
            if (color == (128, 128, 128)) return "Grey";
            if (color == (255, 165, 0)) return "Orange";
            if (color == (128, 0, 128)) return "Purple";
            return $"Color {index + 1}";
        }

        private async Task ApplyThicknessHintsAsync(string filePath, int pageNumber, int scaleDenominator)
        {
            if (_extractedGeometry is null || _slabPropsRows.Count == 0)
                return;

            var hints = await Task.Run(
                () => PdfGeometryExtractor.ExtractThicknessHints(filePath, pageNumber, scaleDenominator, _extractedGeometry),
                BeginOperation()).ConfigureAwait(true);

            int applied = 0;
            foreach (var row in _slabPropsRows)
            {
                row.AutoIndicatorTextBlock.Visibility = Visibility.Collapsed;
                string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
                if (!string.Equals(type, "Slab", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!hints.TryGetValue(row.Color, out var hint))
                    continue;

                string newText = hint.ToString("0.###", CultureInfo.InvariantCulture);
                bool canApply = row.ThicknessTextBox.Tag is null ||
                                (row.ThicknessTextBox.Tag is string prevAuto && string.Equals(row.ThicknessTextBox.Text.Trim(), prevAuto, StringComparison.Ordinal));
                if (!canApply)
                    continue;

                row.ThicknessTextBox.Tag = newText;
                row.ThicknessTextBox.Text = newText;
                row.AutoIndicatorTextBlock.Visibility = Visibility.Visible;
                applied++;
            }

            UpdateThicknessHintStatus(applied, hints.Count);
        }

        private void UpdateThicknessHintStatus(int applied, int detected)
        {
            if (detected == 0)
                ThicknessHintStatus.Text = "No thickness callouts detected.";
            else if (applied > 0)
                ThicknessHintStatus.Text = $"Applied {applied} detected thickness hint{(applied == 1 ? string.Empty : "s")}.";
            else
                ThicknessHintStatus.Text = $"Detected {detected} thickness hint{(detected == 1 ? string.Empty : "s")} but existing values were kept.";

            ThicknessHintStatus.Visibility = Visibility.Visible;
        }

        private async void AiAnalyse_Click(object sender, RoutedEventArgs e)
        {
            if (!_aiService.IsConfigured || _renderedBitmap is null || _slabPropsRows.Count == 0)
            {
                SetAiStatus("AI analysis is unavailable.", "#FFF3E0", "#E65100");
                return;
            }

            var colors = _slabPropsRows.Select(r => r.Color).Distinct().ToList();
            try
            {
                ShowLoading("Analysing colors...");
                SetAiStatus("Analysing colors...", "#E8EAF6", "#3949AB");
                var result = await _aiService.AnalyseColorsAsync(_renderedBitmap, colors, null, BeginOperation()).ConfigureAwait(true);
                if (result is null)
                {
                    SetAiStatus("AI analysis returned no result.", "#FFF3E0", "#E65100");
                    return;
                }

                foreach (var row in _slabPropsRows)
                {
                    string type = row.DefaultElementType;
                    if (result.SlabColors.Contains(row.Color)) type = "Slab";
                    else if (result.BeamColors.Contains(row.Color)) type = "Beam";
                    else if (result.ColumnColors.Contains(row.Color)) type = "Column";

                    row.TypeComboBox.SelectedItem = type;
                    row.IncludeCheckBox.IsChecked = !IsExcludedType(type);
                    UpdateElementRowUi(row, false);
                }

                RebuildExcludedColors();
                DrawOverlay();
                SetAiStatus(result.Summary, "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI analysis failed");
                SetAiStatus($"AI analysis failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                HideLoading();
            }
        }

        private void SetAiStatus(string message, string backgroundHex, string foregroundHex)
        {
            AiStatusText.Text = message;
            AiStatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(backgroundHex));
            AiStatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foregroundHex));
            AiStatusBadge.Visibility = Visibility.Visible;
        }

        private static ExportSettings BuildDefaultExportSettings()
        {
            return new ExportSettings
            {
                DesignCode = DesignCodeOption.CSA_A23_3_19,
                LoadCombCode = "NBC",
                IncludePtLoads = false,
                MeshSizeMm = 500,
                AutoGenerateStrips = false,
                SlabMembraneModifier = 1,
                SlabBendingModifier = 1,
                SlabShearModifier = 1,
                DropPanelThicknessMultiplier = 1.5
            };
        }

        // ── Export ────────────────────────────────────────────────────────────

        private async void ExportF2k_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) return;
            var dlg = new SaveFileDialog { Filter = "SAFE F2K (*.f2k)|*.f2k", FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath ?? "export") + "_SAFE.f2k" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                ShowLoading("Exporting SAFE...");
                var colorSettings = BuildSlabColorSettings();
                var reclassified = PdfGeometryExtractor.ReclassifyByColor(
                    _extractedGeometry, colorSettings,
                    _excl.SlabTypeOverrides.Count > 0 ? _excl.SlabTypeOverrides : null,
                    _excl.LineTypeOverrides.Count > 0 ? _excl.LineTypeOverrides : null,
                    _excl.ColumnTypeOverrides.Count > 0 ? _excl.ColumnTypeOverrides : null);
                var settings = BuildDefaultExportSettings();
                await Task.Run(() =>
                {
                    SafeF2kExporter.Export(
                        reclassified,
                        dlg.FileName,
                        _excl.Slabs.Count > 0 ? _excl.Slabs : null,
                        _excl.Lines.Count > 0 ? _excl.Lines : null,
                        _excl.Columns.Count > 0 ? _excl.Columns : null,
                        _excl.Colors.Count > 0 ? _excl.Colors : null,
                        colorSettings,
                        settings);
                }).ConfigureAwait(true);
                SetStatus($"Exported: {System.IO.Path.GetFileName(dlg.FileName)}", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "F2K export failed");
                SetStatus($"Export failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally { HideLoading(); }
        }

        private async void ExportE2k_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) return;
            var dlg = new SaveFileDialog { Filter = "ETABS E2K (*.e2k)|*.e2k", FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath ?? "export") + "_ETABS.e2k" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                ShowLoading("Exporting ETABS...");
                var colorSettings = BuildSlabColorSettings();
                var reclassified = PdfGeometryExtractor.ReclassifyByColor(
                    _extractedGeometry, colorSettings,
                    _excl.SlabTypeOverrides.Count > 0 ? _excl.SlabTypeOverrides : null,
                    _excl.LineTypeOverrides.Count > 0 ? _excl.LineTypeOverrides : null,
                    _excl.ColumnTypeOverrides.Count > 0 ? _excl.ColumnTypeOverrides : null);
                var settings = BuildDefaultExportSettings();
                var filtered = _excl.FilterGeometry(reclassified);
                await Task.Run(() =>
                {
                    EtabsE2kExporter.Export(dlg.FileName, filtered, colorSettings, settings);
                }).ConfigureAwait(true);
                SetStatus($"Exported: {System.IO.Path.GetFileName(dlg.FileName)}", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "E2K export failed");
                SetStatus($"Export failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally { HideLoading(); }
        }

        private async void ExportDxf_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) return;
            var dlg = new SaveFileDialog { Filter = "DXF (*.dxf)|*.dxf", FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath ?? "export") + ".dxf" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                ShowLoading("Exporting DXF...");
                var colorSettings = BuildSlabColorSettings();
                var reclassified = PdfGeometryExtractor.ReclassifyByColor(
                    _extractedGeometry, colorSettings,
                    _excl.SlabTypeOverrides.Count > 0 ? _excl.SlabTypeOverrides : null,
                    _excl.LineTypeOverrides.Count > 0 ? _excl.LineTypeOverrides : null,
                    _excl.ColumnTypeOverrides.Count > 0 ? _excl.ColumnTypeOverrides : null);
                await Task.Run(() =>
                {
                    DxfExporter.Export(
                        reclassified,
                        dlg.FileName,
                        _excl.Slabs.Count > 0 ? _excl.Slabs : null,
                        _excl.Lines.Count > 0 ? _excl.Lines : null,
                        _excl.Columns.Count > 0 ? _excl.Columns : null,
                        _excl.Colors.Count > 0 ? _excl.Colors : null);
                }).ConfigureAwait(true);
                SetStatus($"Exported: {System.IO.Path.GetFileName(dlg.FileName)}", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DXF export failed");
                SetStatus($"Export failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally { HideLoading(); }
        }

        // ── Project save/load ─────────────────────────────────────────────────

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var project = BuildCurrentProject();
                if (_projectPath is null)
                {
                    var dlg = new SaveFileDialog { Filter = "KOR Project (*.kor)|*.kor", FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath ?? "project") + ".kor" };
                    if (dlg.ShowDialog() != true) return;
                    _projectPath = dlg.FileName;
                }
                project.Save(_projectPath);
                SetStatus($"Project saved: {System.IO.Path.GetFileName(_projectPath)}", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Save project failed");
                SetStatus($"Save failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
        }

        private async void LoadProject_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "KOR Project (*.kor)|*.kor" };
            if (dlg.ShowDialog() != true) return;
            try
            {
                ShowLoading("Loading project...");
                var project = PdfToSafeProject.Load(dlg.FileName);
                if (string.IsNullOrWhiteSpace(project.PdfPath) || !File.Exists(project.PdfPath))
                {
                    SetStatus("PDF file not found. Browse to the PDF first.", "#FFEBEE", "#C62828");
                    HideLoading();
                    return;
                }

                _projectPath = dlg.FileName;
                _project = project;
                _loadedFilePath = project.PdfPath;

                int scale = project.ScaleDenominator > 0 ? project.ScaleDenominator : 100;
                ScaleInput.Text = scale.ToString();
                int pageNumber = Math.Max(1, project.PageNumber);

                var geo = await Task.Run(() => PdfGeometryExtractor.Extract(
                    _loadedFilePath, scale, pageNumber)).ConfigureAwait(true);
                _extractedGeometry = geo;

                FileNameText.Text = System.IO.Path.GetFileName(_loadedFilePath);
                ScalePanel.Visibility = Visibility.Visible;

                if (geo.PageCount > 1)
                {
                    _isPopulatingPageSelector = true;
                    PageSelector.Items.Clear();
                    for (int i = 1; i <= geo.PageCount; i++)
                        PageSelector.Items.Add($"Page {i}");
                    PageSelector.SelectedIndex = pageNumber - 1;
                    _isPopulatingPageSelector = false;
                    PageSelectorPanel.Visibility = Visibility.Visible;
                }

                UpdateDetectionSummary(geo);
                BuildColorSwatches(geo);
                BuildSlabPropsRows(geo);
                await ApplyThicknessHintsAsync(_loadedFilePath, pageNumber, scale).ConfigureAwait(true);
                ApplyProjectMappings(project);
                await RenderPreviewAsync(_loadedFilePath, pageNumber - 1).ConfigureAwait(true);
                UpdatePdfInfo(geo);
                ElementsConfigPanel.Visibility = Visibility.Visible;
                UpdateExportState();
                SetStatus("Project loaded.", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Load project failed");
                SetStatus($"Load failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally { HideLoading(); }
        }

        private PdfToSafeProject BuildCurrentProject()
        {
            var project = new PdfToSafeProject
            {
                PdfPath = _loadedFilePath ?? string.Empty,
                PageNumber = PageSelector.SelectedIndex + 1,
                ScaleDenominator = int.TryParse(ScaleInput.Text, out var s) ? s : 100,
            };

            var colorSettings = BuildSlabColorSettings();
            foreach (var row in _slabPropsRows)
            {
                string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
                var mapping = new ColorMapping
                {
                    ElementType = type,
                    Excluded = IsExcludedType(type),
                    GradeCode = row.GradeComboBox.SelectedItem as string ?? PdfToSafeConstants.DefaultGradeCode,
                };
                if (colorSettings.TryGetValue(row.Color, out var cs))
                {
                    mapping.ThicknessMm = cs.ThicknessMm;
                    mapping.SdlKPa = cs.SdlKPa;
                    mapping.LiveKPa = cs.LiveKPa;
                }
                string hexKey = $"{row.Color.R:X2}{row.Color.G:X2}{row.Color.B:X2}";
                project.ColorMappings[hexKey] = mapping;
            }

            // Persist per-element type overrides
            foreach (var (idx, type) in _excl.SlabTypeOverrides)
                project.ElementTypeOverrides[$"slab_{idx}"] = type;
            foreach (var (idx, type) in _excl.LineTypeOverrides)
                project.ElementTypeOverrides[$"line_{idx}"] = type;
            foreach (var (idx, type) in _excl.ColumnTypeOverrides)
                project.ElementTypeOverrides[$"column_{idx}"] = type;

            project.ExportSettings = BuildDefaultExportSettings();
            return project;
        }

        private void ApplyProjectMappings(PdfToSafeProject project)
        {
            foreach (var row in _slabPropsRows)
            {
                string hexKey = $"{row.Color.R:X2}{row.Color.G:X2}{row.Color.B:X2}";
                if (!project.ColorMappings.TryGetValue(hexKey, out var mapping))
                    continue;

                if (!string.IsNullOrEmpty(mapping.ElementType))
                    row.TypeComboBox.SelectedItem = mapping.ElementType;
                if (mapping.ThicknessMm > 0)
                    row.ThicknessTextBox.Text = mapping.ThicknessMm.ToString("0.###", CultureInfo.InvariantCulture);
                if (mapping.SdlKPa > 0)
                    row.SdlTextBox.Text = mapping.SdlKPa.ToString("0.###", CultureInfo.InvariantCulture);
                if (mapping.LiveKPa > 0)
                    row.LiveTextBox.Text = mapping.LiveKPa.ToString("0.###", CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(mapping.GradeCode))
                    row.GradeComboBox.SelectedItem = mapping.GradeCode;

                row.IncludeCheckBox.IsChecked = !mapping.Excluded;
                UpdateElementRowUi(row, false);
            }

            // Restore per-element type overrides
            _excl.SlabTypeOverrides.Clear();
            _excl.LineTypeOverrides.Clear();
            _excl.ColumnTypeOverrides.Clear();
            foreach (var (key, type) in project.ElementTypeOverrides)
            {
                var parts = key.Split('_', 2);
                if (parts.Length != 2 || !int.TryParse(parts[1], out int idx)) continue;
                switch (parts[0])
                {
                    case "slab":   _excl.SlabTypeOverrides[idx] = type; break;
                    case "line":   _excl.LineTypeOverrides[idx] = type; break;
                    case "column": _excl.ColumnTypeOverrides[idx] = type; break;
                }
            }

            RebuildExcludedColors();
            DrawOverlay();
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void SetStatus(string message, string backgroundHex, string foregroundHex)
        {
            StatusText.Text = message;
            StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(backgroundHex));
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foregroundHex));
            StatusBadge.Visibility = Visibility.Visible;
        }

        private CancellationToken BeginOperation()
        {
            _opCts.Cancel();
            _opCts = new CancellationTokenSource();
            return _opCts.Token;
        }

        private void ShowLoading(string message = "Loading...")
        {
            LoadingText.Text = message;
            LoadingOverlay.Visibility = Visibility.Visible;
        }

        private void HideLoading()
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
        }

        private void UpdateDetectionSummary(ExtractedGeometry geo)
        {
            SlabCountText.Text = $"{geo.Slabs.Count} slabs";
            ColumnCountText.Text = $"{geo.Columns.Count} cols";
            LineCountText.Text = $"{geo.Lines.Count} lines";
            DetectionSummaryPanel.Visibility = geo.IsVectorPdf ? Visibility.Visible : Visibility.Collapsed;
        }

        private void UpdatePdfInfo(ExtractedGeometry geo)
        {
            PageCountText.Text = $"{geo.PageCount} page{(geo.PageCount == 1 ? "" : "s")}";
            PathCountText.Text = $"{geo.Slabs.Count + geo.Lines.Count + geo.Columns.Count} elements";
            PdfInfoPanel.Visibility = Visibility.Visible;
        }

        private void UpdateExportState()
        {
            bool canExport = _extractedGeometry?.IsVectorPdf == true;
            ExportF2kButton.IsEnabled = canExport;
            ExportE2kButton.IsEnabled = canExport;
            ExportDxfButton.IsEnabled = canExport;
        }

        private static bool IsExcludedType(string? type)
            => string.Equals(type, "Ignore", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(type, "Opening", StringComparison.OrdinalIgnoreCase);

        private static double ParsePositiveDouble(string? text, double fallback)
        {
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0)
                return v;
            return fallback;
        }

        private static double ParseNonNegativeDouble(string? text, double fallback)
        {
            if (double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v >= 0)
                return v;
            return fallback;
        }
    }
}
