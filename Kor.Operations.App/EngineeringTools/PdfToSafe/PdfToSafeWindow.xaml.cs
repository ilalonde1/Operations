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
    /// <summary>
    /// Per-colour configuration for the PDF-to-SAFE extraction. A pure data
    /// record — no WPF controls. The UI is driven entirely by the AI bar
    /// (AiChatPanel) and the preview overlay; the old Element Configuration
    /// table has been retired in favour of conversational control.
    /// </summary>
    internal sealed class SlabPropsRow
    {
        public (byte R, byte G, byte B) Color { get; init; }
        public string Name { get; set; } = "";
        public string ElementType { get; set; } = "Slab";
        public string GradeCode { get; set; } = PdfToSafeConstants.DefaultGradeCode;
        public double ThicknessMm { get; set; } = PdfToSafeConstants.DefaultThicknessMm;
        public double SdlKPa { get; set; } = 0.0;
        public double LiveKPa { get; set; } = 0.0;
        public bool Included { get; set; } = true;
        /// <summary>
        /// When an auto thickness hint was applied, records the value so we can
        /// detect whether the user has since overridden it.
        /// </summary>
        public double? AutoThicknessMm { get; set; }
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
            InitializeAiBar();
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

                // Fire-and-forget vision auto-classification. Safe to run in
                // parallel with the user reviewing the preview — all mutations
                // are marshalled back to the UI thread via the AI dispatcher.
                _ = TryVisionAutoClassifyAsync();
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
            PdfInfoPanel.Visibility = Visibility.Visible;

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

            // Remember what the user last touched so the AI can answer
            // "what's this?" without the user having to name the shape.
            _lastFocusedElement = (tag.Item1, tag.Item2);

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
            // Legacy per-colour UI was retired in favour of the AI bar —
            // only the excluded-colour set still needs to be reset here.
            _excl.Colors.Clear();
        }

        private void BuildSlabPropsRows(ExtractedGeometry geo)
        {
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
                _slabPropsRows.Add(new SlabPropsRow
                {
                    Color = color,
                    Name = $"{AutoColorName(color, i)} ({color.R:X2}{color.G:X2}{color.B:X2})",
                    ElementType = defaultType,
                    DefaultElementType = defaultType,
                    GradeCode = PdfToSafeConstants.DefaultGradeCode,
                    ThicknessMm = PdfToSafeConstants.DefaultThicknessMm,
                    SdlKPa = 0,
                    LiveKPa = 0,
                    Included = !IsExcludedType(defaultType)
                });
            }

            RebuildExcludedColors();
        }

        private Dictionary<(byte R, byte G, byte B), SlabColorSettings> BuildSlabColorSettings()
        {
            var map = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>();
            foreach (var row in _slabPropsRows)
            {
                if (IsExcludedType(row.ElementType)) continue;
                map[row.Color] = new SlabColorSettings
                {
                    ElementType = row.ElementType,
                    ThicknessMm = row.ThicknessMm > 0 ? row.ThicknessMm : PdfToSafeConstants.DefaultThicknessMm,
                    SdlKPa = row.SdlKPa >= 0 ? row.SdlKPa : 0,
                    LiveKPa = row.LiveKPa >= 0 ? row.LiveKPa : 0,
                    GradeCode = string.IsNullOrWhiteSpace(row.GradeCode)
                        ? PdfToSafeConstants.DefaultGradeCode : row.GradeCode
                };
            }
            return map;
        }

        private void RebuildExcludedColors()
        {
            _excl.Colors.Clear();
            foreach (var row in _slabPropsRows)
                if (IsExcludedType(row.ElementType))
                    _excl.Colors.Add(row.Color);
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
                if (!string.Equals(row.ElementType, "Slab", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!hints.TryGetValue(row.Color, out var hint))
                    continue;

                // Apply only if the user hasn't overridden the previous auto value
                // (or if this is the first hint pass).
                bool canApply = row.AutoThicknessMm is null
                    || Math.Abs(row.ThicknessMm - row.AutoThicknessMm.Value) < 0.001;
                if (!canApply) continue;

                row.ThicknessMm = hint;
                row.AutoThicknessMm = hint;
                applied++;
            }

            if (applied > 0)
                _logger.LogInformation(
                    "Applied {Applied} thickness hint(s) from {Detected} detected callout(s).",
                    applied, hints.Count);
        }

        /// <summary>
        /// Current export settings. Initialised to the shipped defaults and
        /// mutated in place by the AI's set_export_settings tool handler so
        /// subsequent exports in the same session honour what the user asked for.
        /// </summary>
        private readonly ExportSettings _exportSettings = new ExportSettings
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

        private ExportSettings BuildDefaultExportSettings() => _exportSettings;

        // ── Export ────────────────────────────────────────────────────────────

        private async void ExportF2k_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) return;
            var dlg = new SaveFileDialog
            {
                Filter = "SAFE F2K (*.f2k)|*.f2k",
                FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath ?? "export") + "_SAFE.f2k"
            };
            if (dlg.ShowDialog() != true) return;
            await DoExportF2kAsync(dlg.FileName).ConfigureAwait(true);
        }

        private async void ExportE2k_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) return;
            var dlg = new SaveFileDialog
            {
                Filter = "ETABS E2K (*.e2k)|*.e2k",
                FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath ?? "export") + "_ETABS.e2k"
            };
            if (dlg.ShowDialog() != true) return;
            await DoExportE2kAsync(dlg.FileName).ConfigureAwait(true);
        }

        private async void ExportDxf_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) return;
            var dlg = new SaveFileDialog
            {
                Filter = "DXF (*.dxf)|*.dxf",
                FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath ?? "export") + ".dxf"
            };
            if (dlg.ShowDialog() != true) return;
            await DoExportDxfAsync(dlg.FileName).ConfigureAwait(true);
        }

        /// <summary>
        /// Writes the current model to a SAFE F2K file at <paramref name="outputPath"/>.
        /// No dialogs — caller provides the path. Used by both the button handler
        /// and the AI export_f2k tool. Returns an empty string on success or an
        /// error message on failure so callers can propagate it to the user.
        /// </summary>
        internal async Task<string> DoExportF2kAsync(string outputPath)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf)
                return "No PDF loaded or extraction empty.";
            if (string.IsNullOrWhiteSpace(outputPath))
                return "Output path is required.";

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
                        outputPath,
                        _excl.Slabs.Count > 0 ? _excl.Slabs : null,
                        _excl.Lines.Count > 0 ? _excl.Lines : null,
                        _excl.Columns.Count > 0 ? _excl.Columns : null,
                        _excl.Colors.Count > 0 ? _excl.Colors : null,
                        colorSettings,
                        settings);
                }).ConfigureAwait(true);
                SetStatus($"Exported: {System.IO.Path.GetFileName(outputPath)}", "#E8F5E9", "#2E7D32");
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "F2K export failed");
                SetStatus($"Export failed: {ex.Message}", "#FFEBEE", "#C62828");
                return ex.Message;
            }
            finally { HideLoading(); }
        }

        internal async Task<string> DoExportE2kAsync(string outputPath)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf)
                return "No PDF loaded or extraction empty.";
            if (string.IsNullOrWhiteSpace(outputPath))
                return "Output path is required.";

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
                    EtabsE2kExporter.Export(outputPath, filtered, colorSettings, settings);
                }).ConfigureAwait(true);
                SetStatus($"Exported: {System.IO.Path.GetFileName(outputPath)}", "#E8F5E9", "#2E7D32");
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "E2K export failed");
                SetStatus($"Export failed: {ex.Message}", "#FFEBEE", "#C62828");
                return ex.Message;
            }
            finally { HideLoading(); }
        }

        internal async Task<string> DoExportDxfAsync(string outputPath)
        {
            if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf)
                return "No PDF loaded or extraction empty.";
            if (string.IsNullOrWhiteSpace(outputPath))
                return "Output path is required.";

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
                        outputPath,
                        _excl.Slabs.Count > 0 ? _excl.Slabs : null,
                        _excl.Lines.Count > 0 ? _excl.Lines : null,
                        _excl.Columns.Count > 0 ? _excl.Columns : null,
                        _excl.Colors.Count > 0 ? _excl.Colors : null);
                }).ConfigureAwait(true);
                SetStatus($"Exported: {System.IO.Path.GetFileName(outputPath)}", "#E8F5E9", "#2E7D32");
                return "";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DXF export failed");
                SetStatus($"Export failed: {ex.Message}", "#FFEBEE", "#C62828");
                return ex.Message;
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

            foreach (var row in _slabPropsRows)
            {
                var mapping = new ColorMapping
                {
                    ElementType = row.ElementType,
                    Excluded = IsExcludedType(row.ElementType),
                    GradeCode = string.IsNullOrWhiteSpace(row.GradeCode)
                        ? PdfToSafeConstants.DefaultGradeCode : row.GradeCode,
                    ThicknessMm = row.ThicknessMm,
                    SdlKPa = row.SdlKPa,
                    LiveKPa = row.LiveKPa,
                };
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
                    row.ElementType = mapping.ElementType;
                if (mapping.ThicknessMm > 0) row.ThicknessMm = mapping.ThicknessMm;
                if (mapping.SdlKPa > 0) row.SdlKPa = mapping.SdlKPa;
                if (mapping.LiveKPa > 0) row.LiveKPa = mapping.LiveKPa;
                if (!string.IsNullOrEmpty(mapping.GradeCode))
                    row.GradeCode = mapping.GradeCode;

                row.Included = !mapping.Excluded;
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
            // A real status supersedes any in-progress spinner.
            StatusSpinnerRow.Visibility = Visibility.Collapsed;
            // Copy button is only useful when there's actual text to copy —
            // hide for empty messages and for very short single-line statuses
            // (the user can select-copy those directly with the system cursor).
            StatusCopyButton.Visibility = string.IsNullOrWhiteSpace(message)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }

        /// <summary>
        /// Shows the status badge in "busy" mode: an animated spinner with an
        /// italic label above whatever text is currently in StatusText. Use for
        /// background AI calls that could otherwise leave the user wondering if
        /// anything is happening.
        /// </summary>
        private void SetStatusBusy(string spinnerLabel, string backgroundHex = "#E8EAF6", string foregroundHex = "#3949AB")
        {
            StatusSpinnerLabel.Text = spinnerLabel;
            StatusBadge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(backgroundHex));
            StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(foregroundHex));
            StatusBadge.Visibility = Visibility.Visible;
            StatusSpinnerRow.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// Clears the inline spinner without touching StatusText — lets AI calls
        /// signal "done" without overwriting the final message emitted by the caller.
        /// </summary>
        private void ClearStatusBusy()
        {
            StatusSpinnerRow.Visibility = Visibility.Collapsed;
        }

        private void StatusCopy_Click(object sender, RoutedEventArgs e)
        {
            var text = StatusText.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            try
            {
                Clipboard.SetText(text);
                var original = StatusCopyButton.Content;
                StatusCopyButton.Content = "Copied";
                var timer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (_, __) => { StatusCopyButton.Content = original; timer.Stop(); };
                timer.Start();
            }
            catch
            {
                // Clipboard can be briefly locked by another app — silent no-op
                // matches the behaviour of AiQueryPanel.CopyBtn_Click.
            }
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

    }
}
