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

            await LoadPdfCoreAsync(dialog.FileName, fireVisionAutoClassify: true).ConfigureAwait(true);
        }

        /// <summary>
        /// Core PDF-load pipeline used by both the Browse button and the
        /// Auto-Import one-click flow. Owns the state reset, extraction,
        /// refresh, status set, and (optionally) the fire-and-forget vision
        /// auto-classify kickoff.
        /// </summary>
        internal async Task LoadPdfCoreAsync(string pdfPath, bool fireVisionAutoClassify)
        {
            _loadedFilePath = pdfPath;
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

                _projectSettingsLoaded = false; // fresh PDF → firm defaults apply
                _extractedGeometry = await ExtractGeometryAsync(_loadedFilePath, scale, 1).ConfigureAwait(true);
                await RefreshFromGeometryAsync(_extractedGeometry, _loadedFilePath, 1, scale, true).ConfigureAwait(true);

                SetStatus(
                    _extractedGeometry.IsVectorPdf
                        ? "Vector PDF detected. Ready for configuration and export."
                        : "Raster or image-only PDF detected. Vector PDF is required for export.",
                    _extractedGeometry.IsVectorPdf ? "#E8F5E9" : "#FFF3E0",
                    _extractedGeometry.IsVectorPdf ? "#2E7D32" : "#E65100");

                // Vision auto-classification. By default fire-and-forget so
                // the user can continue reviewing the preview; Auto-Import
                // passes false here and awaits the call itself.
                if (fireVisionAutoClassify)
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

        /// <summary>
        /// When true, the preview canvas renders the reclassified model (what
        /// SAFE will see on import) instead of the raw Bluebeam source.
        /// Toggled by the "Preview export" button on the zoom toolbar.
        /// </summary>
        private bool _previewingExport;

        private void PreviewToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null) return;
            _previewingExport = !_previewingExport;
            PreviewToggleButton.Content = _previewingExport ? "View source" : "Preview export";
            PreviewBanner.Visibility = _previewingExport ? Visibility.Visible : Visibility.Collapsed;
            if (_previewingExport)
                DrawExportPreview();
            else
                DrawOverlay();
        }

        private void DrawOverlay()
        {
            // While previewing, redirect every redraw request through the
            // preview renderer so UI state changes (overrides, exclusions)
            // update the preview instead of clobbering it.
            if (_previewingExport)
            {
                DrawExportPreview();
                return;
            }

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

            // Capture colour settings ONCE so each shape's stroke can reflect its
            // predicted post-reclassification type rather than its raw bucket.
            // A burgundy slab polygon flagged as "Column" but too elongated to be
            // a real column will be auto-routed to wall reduction by
            // PdfGeometryExtractor.ReclassifyByColor — the user should see that
            // route in the default preview, not just after clicking "Preview
            // export."
            var overlayColourSettings = BuildSlabColorSettings();

            for (int i = 0; i < _extractedGeometry.Slabs.Count; i++)
            {
                bool excluded = _excl.IsSlabExcluded(i, _extractedGeometry.SlabColors);
                string? overrideType = _excl.SlabTypeOverrides.TryGetValue(i, out var sov) ? sov : null;
                bool isIgnore = overrideType is not null && string.Equals(overrideType, "Ignore", StringComparison.OrdinalIgnoreCase);

                string predictedType = PredictSlabExportType(i, _extractedGeometry, overlayColourSettings, overrideType);

                Brush stroke = (excluded || isIgnore) ? Brushes.White
                    : predictedType switch
                    {
                        "Wall"   => new SolidColorBrush(Color.FromRgb(220, 38, 38)), // matches wallBrush in DrawExportPreview
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

                // Predict the post-reclassification type so when the AI (or the
                // user) sets a colour's type to Slab / Wall / Column / Beam,
                // the line stroke immediately reflects what the exporter will
                // produce — no need to click "Preview export".
                string predictedLineType = PredictLineExportType(i, _extractedGeometry, overlayColourSettings, overrideType);

                Brush stroke = (excluded || isIgnore) ? Brushes.White
                    : predictedLineType switch
                    {
                        "Wall"   => new SolidColorBrush(Color.FromRgb(220, 38, 38)),
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

        /// <summary>
        /// Renders the reclassified geometry — exactly what the F2K export will
        /// contain — on top of the preview bitmap. SAFE-style visuals with
        /// section labels so engineers can A/B against the source view and
        /// catch issues before SAFE import.
        /// </summary>
        private void DrawExportPreview()
        {
            if (_extractedGeometry is null) return;

            var toRemove = PreviewCanvas.Children.OfType<UIElement>().Where(x => x != PreviewImage).ToList();
            foreach (var child in toRemove)
                PreviewCanvas.Children.Remove(child);

            if (PreviewCanvas.Width <= 0 || _extractedGeometry.PageWidthPts <= 0 || _extractedGeometry.PageHeightPts <= 0)
                return;

            // Build the exact same inputs the export pipeline uses.
            var colorSettings = BuildSlabColorSettings();
            var reclassified = PdfGeometryExtractor.ReclassifyByColor(
                _extractedGeometry, colorSettings,
                _excl.SlabTypeOverrides.Count > 0 ? _excl.SlabTypeOverrides : null,
                _excl.LineTypeOverrides.Count > 0 ? _excl.LineTypeOverrides : null,
                _excl.ColumnTypeOverrides.Count > 0 ? _excl.ColumnTypeOverrides : null);

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

            // Slab brushes mimic SAFE's default slab shading — translucent
            // fill so the user can still see the bitmap underneath.
            var slabFill   = new SolidColorBrush(Color.FromArgb(90, 74, 144, 226));
            var slabStroke = new SolidColorBrush(Color.FromRgb(37, 99, 175));
            var wallBrush  = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            var beamBrush  = new SolidColorBrush(Color.FromRgb(14, 165, 233));
            var columnFill = new SolidColorBrush(Color.FromRgb(234, 179, 8));
            var columnStroke = new SolidColorBrush(Color.FromRgb(161, 98, 7));
            var labelBrush = new SolidColorBrush(Color.FromArgb(220, 15, 23, 42));

            // Pre-compute text-annotation resolutions once; used by the Line
            // label code (beam sections from PDF text callouts) and the
            // Column label code (authoritative section from PDF text).
            var annRes = AnnotationResolver.Resolve(reclassified);

            // ── Slabs (filled polygons) ─────────────────────────────────
            for (int i = 0; i < reclassified.Slabs.Count; i++)
            {
                var pts = reclassified.Slabs[i];
                if (pts.Count < 3) continue;
                var poly = new System.Windows.Shapes.Polygon
                {
                    Stroke = slabStroke,
                    Fill   = slabFill,
                    StrokeThickness = 1.5,
                    Points = new PointCollection(pts.Select(p => ToCanvas(p.X, p.Y)))
                };
                Canvas.SetZIndex(poly, 1);
                PreviewCanvas.Children.Add(poly);
            }

            // Resolve line-bucket text matches here so we can label beam
            // lines with their text-derived section too (walls use the hint
            // from the reclassifier; plain beams use PDF text callouts).
            // Annotation resolution was computed above (annRes).

            // ── Lines (walls / beams / hairlines) ────────────────────────
            for (int i = 0; i < reclassified.Lines.Count; i++)
            {
                var pts = reclassified.Lines[i];
                if (pts.Count < 2) continue;
                var hint = i < reclassified.LineSectionHints.Count ? reclassified.LineSectionHints[i] : null;
                bool isWall = hint.HasValue;

                var polyline = new Polyline
                {
                    Stroke = isWall ? wallBrush : beamBrush,
                    StrokeThickness = isWall ? 4.0 : 1.5,
                    Opacity = isWall ? 0.95 : 0.8,
                    Points = new PointCollection(pts.Select(p => ToCanvas(p.X, p.Y)))
                };
                Canvas.SetZIndex(polyline, 2);
                PreviewCanvas.Children.Add(polyline);

                // Pick the best label source: hint (wall) > text annotation (beam) > none.
                string? sectionLabel = null;
                if (isWall)
                {
                    var (w, d) = hint!.Value;
                    sectionLabel = $"W{(int)Math.Round(w)}x{(int)Math.Round(d)}";
                }
                else if (i < annRes.LineSectionMm.Length && annRes.LineSectionMm[i].HasValue)
                {
                    var (w, d) = annRes.LineSectionMm[i]!.Value;
                    sectionLabel = $"B{(int)Math.Round(w)}x{(int)Math.Round(d)}";
                }

                if (sectionLabel is not null)
                {
                    var midMm = ((pts[0].X + pts[^1].X) / 2.0, (pts[0].Y + pts[^1].Y) / 2.0);
                    var midCanvas = ToCanvas(midMm.Item1, midMm.Item2);
                    var label = BuildSectionLabel(sectionLabel, labelBrush);
                    Canvas.SetLeft(label, midCanvas.X + 6);
                    Canvas.SetTop(label, midCanvas.Y - 9);
                    Canvas.SetZIndex(label, 4);
                    PreviewCanvas.Children.Add(label);
                }
            }

            // ── Columns (sized rectangles, SAFE-style) ───────────────────
            for (int i = 0; i < reclassified.Columns.Count; i++)
            {
                var (x, y) = reclassified.Columns[i];
                // Prefer text-derived column section over bbox if available.
                var (w, d) = i < annRes.ColumnSectionMm.Length && annRes.ColumnSectionMm[i].HasValue
                    ? annRes.ColumnSectionMm[i]!.Value
                    : (i < reclassified.ColumnSizes.Count ? reclassified.ColumnSizes[i] : (400d, 400d));
                var halfW = Math.Max(1.0, w / 2.0);
                var halfD = Math.Max(1.0, d / 2.0);
                var tl = ToCanvas(x - halfW, y + halfD);
                var br = ToCanvas(x + halfW, y - halfD);
                double rectW = Math.Max(4, Math.Abs(br.X - tl.X));
                double rectH = Math.Max(4, Math.Abs(br.Y - tl.Y));

                var rect = new System.Windows.Shapes.Rectangle
                {
                    Width = rectW,
                    Height = rectH,
                    Fill = columnFill,
                    Stroke = columnStroke,
                    StrokeThickness = 1.0
                };
                Canvas.SetLeft(rect, Math.Min(tl.X, br.X));
                Canvas.SetTop(rect, Math.Min(tl.Y, br.Y));
                Canvas.SetZIndex(rect, 3);
                PreviewCanvas.Children.Add(rect);

                var centerCanvas = ToCanvas(x, y);
                // Match the section name that the .f2k writer will emit so
                // the preview label and the SAFE model agree (Batch 44b).
                var (snappedSecName, _, _) = F2kModelPrep.SnapColumnSection(w, d);
                var label = BuildSectionLabel(snappedSecName, labelBrush);
                Canvas.SetLeft(label, centerCanvas.X + rectW / 2.0 + 2);
                Canvas.SetTop(label, centerCanvas.Y - 8);
                Canvas.SetZIndex(label, 5);
                PreviewCanvas.Children.Add(label);
            }

            // Summary counts overlay (top-left of the canvas).
            var summary = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 248, 250, 252)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(203, 213, 225)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(10, 6, 10, 6)
            };
            int wallCount = 0;
            for (int i = 0; i < reclassified.LineSectionHints.Count; i++)
                if (reclassified.LineSectionHints[i].HasValue) wallCount++;
            int plainLineCount = reclassified.Lines.Count - wallCount;
            summary.Child = new TextBlock
            {
                Text = $"Export preview:  {reclassified.Slabs.Count} slab(s) · " +
                       $"{reclassified.Columns.Count} column(s) · " +
                       $"{wallCount} wall(s) · {plainLineCount} line(s)",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
            };
            Canvas.SetLeft(summary, 12);
            Canvas.SetTop(summary, 12);
            Canvas.SetZIndex(summary, 10);
            PreviewCanvas.Children.Add(summary);
        }

        private static Border BuildSectionLabel(string text, Brush foreground)
        {
            return new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 148, 163, 184)),
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(2),
                Padding = new Thickness(3, 0, 3, 0),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = foreground
                }
            };
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

            foreach (var typeName in new[] { "Slab", "Beam", "Column", "Wall", "Opening", "Ignore" })
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
            catch (OperationCanceledException)
            {
                // Normal: user switched pages or re-analysed before the previous
                // operation finished. Silently discard — the new operation is running.
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
            catch (OperationCanceledException)
            {
                // Normal: user triggered another operation before this one finished.
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

        private async void RerunVision_Click(object sender, RoutedEventArgs e)
        {
            // Manual rerun for the auto-vision pass. Closes Gap #2 from the
            // 2026-05-09 PdfToSafe testing pass: the auto-pass currently
            // fires once on PDF load; switching pages or wanting a second
            // try previously had no entry point. The gates inside
            // TryVisionAutoClassifyAsync now surface a banner if anything
            // prevents the run (Batch 42), so this button can be a thin
            // pass-through without its own validation.
            try
            {
                await TryVisionAutoClassifyAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Manual vision rerun failed.");
                SetStatus($"Vision rerun failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
        }

        private (double slabMin, double lineMin, bool excludeGridLines) ReadThresholds() => (1000.0, 200.0, false);

        private void BuildColorSwatches(ExtractedGeometry geo)
        {
            // Legacy per-colour UI was retired in favour of the AI bar —
            // only the excluded-colour set still needs to be reset here.
            _excl.Colors.Clear();
        }

        private void RefreshColorPropsGrid()
        {
            if (ColorPropsGrid is null) return;
            ColorPropsGrid.ItemsSource = null;
            ColorPropsGrid.ItemsSource = _slabPropsRows;
        }

        private void ColorPropsGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            // After any cell edit, refresh the exclusion state and overlay
            // so the preview immediately reflects the engineer's change.
            Dispatcher.InvokeAsync(() =>
            {
                RebuildExcludedColors();
                DrawOverlay();
            }, System.Windows.Threading.DispatcherPriority.Render);
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
                // Seed new rows with firm defaults rather than shipped
                // constants so engineers don't retype their standard values
                // (grade, thickness, SDL, LIVE) on every PDF.
                _slabPropsRows.Add(new SlabPropsRow
                {
                    Color = color,
                    Name = $"{AutoColorName(color, i)} ({color.R:X2}{color.G:X2}{color.B:X2})",
                    ElementType = defaultType,
                    DefaultElementType = defaultType,
                    GradeCode = _firmDefaults.DefaultGradeCode,
                    ThicknessMm = _firmDefaults.DefaultSlabThicknessMm > 0
                        ? _firmDefaults.DefaultSlabThicknessMm
                        : PdfToSafeConstants.DefaultThicknessMm,
                    SdlKPa = _firmDefaults.DefaultSdlKPa,
                    LiveKPa = _firmDefaults.DefaultLiveKPa,
                    Included = !IsExcludedType(defaultType)
                });
            }

            RebuildExcludedColors();
            RefreshColorPropsGrid();
        }

        private Dictionary<(byte R, byte G, byte B), SlabColorSettings> BuildSlabColorSettings()
        {
            var map = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>();
            foreach (var row in _slabPropsRows)
            {
                if (!row.Included || IsExcludedType(row.ElementType)) continue;
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
                if (!row.Included || IsExcludedType(row.ElementType))
                    _excl.Colors.Add(row.Color);
        }

        /// <summary>
        /// Predicts what bucket a raw slab will land in once
        /// <see cref="PdfGeometryExtractor.ReclassifyByColor"/> runs at export
        /// time. Lets the default overlay stroke a burgundy "Column" polygon
        /// in WALL red when it'll be auto-routed to wall reduction (e.g. an
        /// elevator core C-shape) — so the user sees what the exporter does
        /// without having to click "Preview export". MIRROR of the slab
        /// branch of ReclassifyByColor; keep in sync when that changes.
        /// </summary>
        private static string PredictSlabExportType(
            int slabIdx,
            ExtractedGeometry geo,
            IReadOnlyDictionary<(byte R, byte G, byte B), SlabColorSettings> colorSettings,
            string? overrideType)
        {
            if (!string.IsNullOrWhiteSpace(overrideType))
                return overrideType!;

            var color = slabIdx < geo.SlabColors.Count ? geo.SlabColors[slabIdx] : ((byte)0, (byte)0, (byte)0);
            if (colorSettings is null || !colorSettings.TryGetValue(color, out var cs))
                return "Slab";

            string type = cs.ElementType;
            if (!string.Equals(type, "Column", StringComparison.OrdinalIgnoreCase))
                return type;

            // Column-guardrail check: a slab polygon too big or too elongated
            // to be a real column gets routed to wall reduction.
            var pts = geo.Slabs[slabIdx];
            if (pts is null || pts.Count == 0) return type;
            double minX = pts.Min(p => p.X), maxX = pts.Max(p => p.X);
            double minY = pts.Min(p => p.Y), maxY = pts.Max(p => p.Y);
            double w = maxX - minX, d = maxY - minY;
            double minDim = Math.Min(w, d), maxDim = Math.Max(w, d);
            const double columnMaxSideMm = 2000.0;
            const double columnMaxAspect = 2.5;
            bool columnSectionIsSane =
                maxDim <= columnMaxSideMm &&
                (minDim <= 0 || maxDim <= columnMaxAspect * minDim);
            return columnSectionIsSane ? "Column" : "Wall";
        }

        /// <summary>
        /// Mirror of the lines branch of <see cref="PdfGeometryExtractor.ReclassifyByColor"/>
        /// for stroke-colour prediction in the default overlay. Lines themselves
        /// are not size-reduced, so this is simpler than the slab predictor —
        /// the colour setting (or per-element override) dictates the bucket
        /// directly: Slab lines are chained into a slab polygon, Column lines
        /// collapse to a point, others stay as linear elements.
        /// </summary>
        private static string PredictLineExportType(
            int lineIdx,
            ExtractedGeometry geo,
            IReadOnlyDictionary<(byte R, byte G, byte B), SlabColorSettings> colorSettings,
            string? overrideType)
        {
            if (!string.IsNullOrWhiteSpace(overrideType))
                return overrideType!;
            var color = lineIdx < geo.LineColors.Count ? geo.LineColors[lineIdx] : ((byte)0, (byte)0, (byte)0);
            if (colorSettings is null || !colorSettings.TryGetValue(color, out var cs))
                return "Beam";
            return cs.ElementType;
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
        /// Per-user firm defaults loaded from %AppData%\KorOperations\
        /// pdftosafe_defaults.json. Applied to <see cref="_exportSettings"/>
        /// on construction and used as the fallback for <see cref="SlabPropsRow"/>
        /// seed values (thickness, grade, SDL, LIVE, wall depth).
        /// </summary>
        private readonly FirmDefaults _firmDefaults = FirmDefaults.Load();

        /// <summary>
        /// Current export settings. Initialised from <see cref="_firmDefaults"/>
        /// and mutated in place by the AI's set_export_settings tool handler so
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

        /// <summary>
        /// Builds a filtered <see cref="ExtractedGeometry"/> that reflects only
        /// the shapes that will actually be exported (post per-element and per-
        /// color exclusions). Used by both OAPI and file-export validators so
        /// excluded bad geometry doesn't block export.
        /// </summary>
        private ExtractedGeometry BuildFilteredGeometryForValidation(ExtractedGeometry reclassified)
        {
            var exColors = _excl.Colors.Count > 0 ? _excl.Colors : null;
            var g = new ExtractedGeometry { IsVectorPdf = true };
            for (int i = 0; i < reclassified.Slabs.Count; i++)
            {
                if (_excl.Slabs.Contains(i)) continue;
                if (exColors != null && i < reclassified.SlabColors.Count && exColors.Contains(reclassified.SlabColors[i])) continue;
                g.Slabs.Add(reclassified.Slabs[i]);
                g.SlabColors.Add(i < reclassified.SlabColors.Count ? reclassified.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
            }
            for (int i = 0; i < reclassified.Columns.Count; i++)
            {
                if (_excl.Columns.Contains(i)) continue;
                if (exColors != null && i < reclassified.ColumnColors.Count && exColors.Contains(reclassified.ColumnColors[i])) continue;
                g.Columns.Add(reclassified.Columns[i]);
                g.ColumnColors.Add(i < reclassified.ColumnColors.Count ? reclassified.ColumnColors[i] : ((byte)0, (byte)0, (byte)0));
                g.ColumnSizes.Add(i < reclassified.ColumnSizes.Count ? reclassified.ColumnSizes[i] : (0, 0));
            }
            for (int i = 0; i < reclassified.Lines.Count; i++)
            {
                if (_excl.Lines.Contains(i)) continue;
                if (exColors != null && i < reclassified.LineColors.Count && exColors.Contains(reclassified.LineColors[i])) continue;
                g.Lines.Add(reclassified.Lines[i]);
                g.LineColors.Add(i < reclassified.LineColors.Count ? reclassified.LineColors[i] : ((byte)0, (byte)0, (byte)0));
                g.LineSectionHints.Add(i < reclassified.LineSectionHints.Count ? reclassified.LineSectionHints[i] : null);
            }
            return g;
        }

        /// <summary>Set to true when a project is loaded so that BuildDefaultExportSettings
        /// does NOT overwrite the restored settings with firm defaults.</summary>
        private bool _projectSettingsLoaded;

        private ExportSettings BuildDefaultExportSettings()
        {
            // If a project was loaded, its export settings are already in
            // _exportSettings — don't overwrite them with firm defaults.
            // Otherwise, apply firm defaults lazily so editing the defaults
            // file between exports takes effect without an app restart.
            if (!_projectSettingsLoaded)
                _firmDefaults.ApplyTo(_exportSettings);
            return _exportSettings;
        }

        private async void SafeApiExport_Click(object sender, RoutedEventArgs e)
        {
            SafeApiExportButton.IsEnabled = false;
            LoadPdfButton.IsEnabled = false;
            try
            {
                // ── Step 1: ensure a vector PDF is loaded ─────────────────
                bool needsLoad =
                    _extractedGeometry is null ||
                    !_extractedGeometry.IsVectorPdf ||
                    string.IsNullOrEmpty(_loadedFilePath);

                if (needsLoad)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select structural PDF",
                        Filter = "PDF files (*.pdf)|*.pdf",
                        Multiselect = false
                    };
                    if (dlg.ShowDialog() != true) return;
                    await LoadPdfCoreAsync(dlg.FileName, fireVisionAutoClassify: false).ConfigureAwait(true);
                    if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf)
                    {
                        SetStatus("Could not extract vector geometry from the selected PDF.", "#FEE2E2", "#991B1B");
                        return;
                    }
                }

                // ── Step 2: AI auto-classify (if configured) ──────────────
                // Runs the vision model to assign wall/column/slab types to
                // each colour, then applies the corrections. Skipped silently
                // if no API key is set — engineer gets the raw extraction.
                if (_aiBarInitialized && _appAiService?.IsConfigured == true)
                {
                    SetStatusBusy("AI classifying markups…");
                    await TryVisionAutoClassifyAsync().ConfigureAwait(true);
                }

                // ── Step 3: OAPI export ───────────────────────────────────
                string pdfPath = _loadedFilePath ?? "export";
                string destFdb = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(pdfPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    System.IO.Path.GetFileNameWithoutExtension(pdfPath) + "_SAFE.fdb");

                SetStatusBusy("Exporting to SAFE — launching SAFE may take 10–30 s…");
                if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("No vector geometry available after loading.", "#FEE2E2", "#991B1B");
                    return;
                }
                var colorSettings = BuildSlabColorSettings();
                var reclassified = PdfGeometryExtractor.ReclassifyByColor(
                    _extractedGeometry, colorSettings,
                    _excl.SlabTypeOverrides.Count > 0 ? _excl.SlabTypeOverrides : null,
                    _excl.LineTypeOverrides.Count > 0 ? _excl.LineTypeOverrides : null,
                    _excl.ColumnTypeOverrides.Count > 0 ? _excl.ColumnTypeOverrides : null);

                // Resolve text annotations (e.g., "S-250", "B300x600", "C500x500"
                // labels near elements) → per-element thickness/section overrides.
                // Then layer AI-supplied vision overrides on top — vision wins
                // when both have a value for the same element index.
                var annRes = AnnotationOverrideMerger.Merge(
                    AnnotationResolver.Resolve(reclassified),
                    _excl.SlabThicknessOverridesMm,
                    _excl.ColumnSectionOverridesMm,
                    _excl.LineSectionOverridesMm);

                // Apply user-level exclusions per object kind, carrying
                // annotation arrays in parallel.
                var keptSlabs = new List<IReadOnlyList<(double X, double Y)>>();
                var keptSlabColors = new List<(byte R, byte G, byte B)>();
                var keptSlabThick = new List<double?>();
                // Color-level exclusions (Ignore/Opening types mapped to _excl.Colors).
                var exColors = _excl.Colors.Count > 0 ? _excl.Colors : null;
                for (int i = 0; i < reclassified.Slabs.Count; i++)
                {
                    if (_excl.Slabs.Contains(i)) continue;
                    if (exColors != null && i < reclassified.SlabColors.Count && exColors.Contains(reclassified.SlabColors[i])) continue;
                    keptSlabs.Add(reclassified.Slabs[i]);
                    keptSlabColors.Add(i < reclassified.SlabColors.Count ? reclassified.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
                    keptSlabThick.Add(i < annRes.SlabThicknessMm.Length ? annRes.SlabThicknessMm[i] : null);
                }

                var keptColumns   = new List<(double X, double Y)>();
                var keptColumnSz  = new List<(double WidthMm, double DepthMm)>();
                var keptColSec    = new List<(double WidthMm, double DepthMm)?>();
                for (int i = 0; i < reclassified.Columns.Count; i++)
                {
                    if (_excl.Columns.Contains(i)) continue;
                    if (exColors != null && i < reclassified.ColumnColors.Count && exColors.Contains(reclassified.ColumnColors[i])) continue;
                    keptColumns.Add(reclassified.Columns[i]);
                    keptColumnSz.Add(i < reclassified.ColumnSizes.Count ? reclassified.ColumnSizes[i] : (400.0, 400.0));
                    keptColSec.Add(i < annRes.ColumnSectionMm.Length ? annRes.ColumnSectionMm[i] : null);
                }

                var keptLines = new List<IReadOnlyList<(double X, double Y)>>();
                var keptHints = new List<(double WidthMm, double DepthMm)?>();
                var keptLineSec = new List<(double WidthMm, double DepthMm)?>();
                for (int i = 0; i < reclassified.Lines.Count; i++)
                {
                    if (_excl.Lines.Contains(i)) continue;
                    if (exColors != null && i < reclassified.LineColors.Count && exColors.Contains(reclassified.LineColors[i])) continue;
                    keptLines.Add(reclassified.Lines[i]);
                    keptHints.Add(i < reclassified.LineSectionHints.Count ? reclassified.LineSectionHints[i] : null);
                    keptLineSec.Add(i < annRes.LineSectionMm.Length ? annRes.LineSectionMm[i] : null);
                }

                if (keptSlabs.Count == 0 && keptColumns.Count == 0 && keptLines.Count == 0)
                {
                    SetStatus("Nothing to export (all objects excluded).", "#FFF3E0", "#E65100");
                    return;
                }

                // Pre-flight validation on the KEPT geometry (post-exclusion).
                // Same validator the F2K path uses — catches degenerate slabs,
                // zero thickness, unsupported columns, etc. before launching SAFE.
                var keptGeo = new ExtractedGeometry { IsVectorPdf = true };
                foreach (var s in keptSlabs) keptGeo.Slabs.Add(s.ToList());
                foreach (var c in keptSlabColors) keptGeo.SlabColors.Add(c);
                foreach (var c in keptColumns) { keptGeo.Columns.Add(c); keptGeo.ColumnColors.Add((0,0,0)); }
                foreach (var (w, d) in keptColumnSz) keptGeo.ColumnSizes.Add((w, d));
                foreach (var l in keptLines) keptGeo.Lines.Add(l.ToList());
                foreach (var h in keptHints) keptGeo.LineSectionHints.Add(h);
                var settings = BuildDefaultExportSettings();
                var validation = ExportValidator.Validate(keptGeo, colorSettings, settings);
                if (validation.HasErrors)
                {
                    SetStatus(FormatValidationReport(validation), "#FEE2E2", "#991B1B");
                    return;
                }

                string? overridePath = string.IsNullOrWhiteSpace(_firmDefaults.SafeExePath) ? null : _firmDefaults.SafeExePath;
                var input = new SafeApiExporter.ExportInput
                {
                    Slabs              = keptSlabs,
                    SlabColors         = keptSlabColors,
                    Columns            = keptColumns,
                    ColumnSizes        = keptColumnSz,
                    Lines              = keptLines,
                    LineSectionHints   = keptHints,
                    AnnotatedSlabThicknesses = keptSlabThick,
                    AnnotatedLineSections    = keptLineSec,
                    AnnotatedColumnSections  = keptColSec,
                    DropPanelCandidates      = reclassified.DropPanelCandidates,
                    DropPanelThicknessMultiplier = settings.DropPanelThicknessMultiplier,
                    SlabMembraneModifier = settings.SlabMembraneModifier,
                    SlabBendingModifier  = settings.SlabBendingModifier,
                    SlabShearModifier    = settings.SlabShearModifier,
                    ColorSettings      = colorSettings,
                    DefaultGradeCode   = _firmDefaults.DefaultGradeCode,
                    DesignCode         = settings.DesignCode,
                    DefaultThicknessMm = _firmDefaults.DefaultSlabThicknessMm > 0 ? _firmDefaults.DefaultSlabThicknessMm : PdfToSafeConstants.DefaultThicknessMm,
                    DefaultWallDepthMm = _firmDefaults.DefaultWallDepthMm > 0    ? _firmDefaults.DefaultWallDepthMm    : 1000.0,
                    ColumnHeightMm     = 3000.0,
                    DestFdbPath        = destFdb,
                    IsImperial         = string.Equals(_firmDefaults.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase),
                    SafeExePathOverride = overridePath,
                };
                var result = await SafeApiExporter.ExportFullModelAsync(input).ConfigureAwait(true);

                if (result.Success)
                    SetStatus(result.Message, "#E8F5E9", "#2E7D32");
                else
                    SetStatus("SAFE export failed — " + result.Message, "#FDECEA", "#B71C1C");
            }
            catch (Exception ex)
            {
                SetStatus($"SAFE export crashed — {ex.GetType().Name}: {ex.Message}", "#FDECEA", "#B71C1C");
            }
            finally
            {
                SafeApiExportButton.IsEnabled = true;
                LoadPdfButton.IsEnabled = true;
            }
        }

        private async void EtabsApiExport_Click(object sender, RoutedEventArgs e)
        {
            EtabsApiExportButton.IsEnabled = false;
            LoadPdfButton.IsEnabled = false;
            try
            {
                // Step 1: ensure PDF loaded
                bool needsLoad =
                    _extractedGeometry is null ||
                    !_extractedGeometry.IsVectorPdf ||
                    string.IsNullOrEmpty(_loadedFilePath);

                if (needsLoad)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Select structural PDF",
                        Filter = "PDF files (*.pdf)|*.pdf",
                        Multiselect = false
                    };
                    if (dlg.ShowDialog() != true) return;
                    await LoadPdfCoreAsync(dlg.FileName, fireVisionAutoClassify: false).ConfigureAwait(true);
                    if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf)
                    {
                        SetStatus("Could not extract vector geometry from the selected PDF.", "#FEE2E2", "#991B1B");
                        return;
                    }
                }

                // Step 2: AI auto-classify
                if (_aiBarInitialized && _appAiService?.IsConfigured == true)
                {
                    SetStatusBusy("AI classifying markups…");
                    await TryVisionAutoClassifyAsync().ConfigureAwait(true);
                }

                // Step 3: ETABS OAPI export
                if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("No vector geometry available.", "#FEE2E2", "#991B1B");
                    return;
                }

                string pdfPath = _loadedFilePath ?? "export";
                string destEdb = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(pdfPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    System.IO.Path.GetFileNameWithoutExtension(pdfPath) + "_ETABS.edb");

                SetStatusBusy("Exporting to ETABS — launching ETABS may take 10–30 s…");

                var colorSettings = BuildSlabColorSettings();
                var reclassified = PdfGeometryExtractor.ReclassifyByColor(
                    _extractedGeometry, colorSettings,
                    _excl.SlabTypeOverrides.Count > 0 ? _excl.SlabTypeOverrides : null,
                    _excl.LineTypeOverrides.Count > 0 ? _excl.LineTypeOverrides : null,
                    _excl.ColumnTypeOverrides.Count > 0 ? _excl.ColumnTypeOverrides : null);

                // Resolve text annotations (same as SAFE path), then layer AI overrides.
                var annResE = AnnotationOverrideMerger.Merge(
                    AnnotationResolver.Resolve(reclassified),
                    _excl.SlabThicknessOverridesMm,
                    _excl.ColumnSectionOverridesMm,
                    _excl.LineSectionOverridesMm);

                var exColorsE = _excl.Colors.Count > 0 ? _excl.Colors : null;
                var keptSlabs = new List<IReadOnlyList<(double X, double Y)>>();
                var keptSlabColors = new List<(byte R, byte G, byte B)>();
                var keptSlabThick = new List<double?>();
                for (int i = 0; i < reclassified.Slabs.Count; i++)
                {
                    if (_excl.Slabs.Contains(i)) continue;
                    if (exColorsE != null && i < reclassified.SlabColors.Count && exColorsE.Contains(reclassified.SlabColors[i])) continue;
                    keptSlabs.Add(reclassified.Slabs[i]);
                    keptSlabColors.Add(i < reclassified.SlabColors.Count ? reclassified.SlabColors[i] : ((byte)0, (byte)0, (byte)0));
                    keptSlabThick.Add(i < annResE.SlabThicknessMm.Length ? annResE.SlabThicknessMm[i] : null);
                }

                var keptColumns = new List<(double X, double Y)>();
                var keptColumnSz = new List<(double WidthMm, double DepthMm)>();
                var keptColSec = new List<(double WidthMm, double DepthMm)?>();
                for (int i = 0; i < reclassified.Columns.Count; i++)
                {
                    if (_excl.Columns.Contains(i)) continue;
                    if (exColorsE != null && i < reclassified.ColumnColors.Count && exColorsE.Contains(reclassified.ColumnColors[i])) continue;
                    keptColumns.Add(reclassified.Columns[i]);
                    keptColumnSz.Add(i < reclassified.ColumnSizes.Count ? reclassified.ColumnSizes[i] : (400.0, 400.0));
                    keptColSec.Add(i < annResE.ColumnSectionMm.Length ? annResE.ColumnSectionMm[i] : null);
                }

                var keptLines = new List<IReadOnlyList<(double X, double Y)>>();
                var keptHints = new List<(double WidthMm, double DepthMm)?>();
                var keptLineSec = new List<(double WidthMm, double DepthMm)?>();
                for (int i = 0; i < reclassified.Lines.Count; i++)
                {
                    if (_excl.Lines.Contains(i)) continue;
                    if (exColorsE != null && i < reclassified.LineColors.Count && exColorsE.Contains(reclassified.LineColors[i])) continue;
                    keptLines.Add(reclassified.Lines[i]);
                    keptHints.Add(i < reclassified.LineSectionHints.Count ? reclassified.LineSectionHints[i] : null);
                    keptLineSec.Add(i < annResE.LineSectionMm.Length ? annResE.LineSectionMm[i] : null);
                }

                if (keptSlabs.Count == 0 && keptColumns.Count == 0 && keptLines.Count == 0)
                {
                    SetStatus("Nothing to export (all objects excluded).", "#FFF3E0", "#E65100");
                    return;
                }

                // Pre-flight validation (same as SAFE path).
                {
                    var vGeo = new ExtractedGeometry { IsVectorPdf = true };
                    foreach (var s in keptSlabs) vGeo.Slabs.Add(s.ToList());
                    foreach (var c in keptSlabColors) vGeo.SlabColors.Add(c);
                    foreach (var c in keptColumns) { vGeo.Columns.Add(c); vGeo.ColumnColors.Add((0,0,0)); }
                    foreach (var (w, d) in keptColumnSz) vGeo.ColumnSizes.Add((w, d));
                    foreach (var l in keptLines) vGeo.Lines.Add(l.ToList());
                    foreach (var h in keptHints) vGeo.LineSectionHints.Add(h);
                    var settingsE = BuildDefaultExportSettings();
                    var validation = ExportValidator.Validate(vGeo, colorSettings, settingsE);
                    if (validation.HasErrors) { SetStatus(FormatValidationReport(validation), "#FEE2E2", "#991B1B"); return; }
                }

                var esE = BuildDefaultExportSettings();
                var input = new SafeApiExporter.ExportInput
                {
                    Slabs              = keptSlabs,
                    SlabColors         = keptSlabColors,
                    Columns            = keptColumns,
                    ColumnSizes        = keptColumnSz,
                    Lines              = keptLines,
                    LineSectionHints   = keptHints,
                    AnnotatedSlabThicknesses = keptSlabThick,
                    AnnotatedLineSections    = keptLineSec,
                    AnnotatedColumnSections  = keptColSec,
                    DropPanelCandidates = reclassified.DropPanelCandidates,
                    DropPanelThicknessMultiplier = esE.DropPanelThicknessMultiplier,
                    SlabMembraneModifier = esE.SlabMembraneModifier,
                    SlabBendingModifier  = esE.SlabBendingModifier,
                    SlabShearModifier    = esE.SlabShearModifier,
                    ColorSettings      = colorSettings,
                    DefaultGradeCode   = _firmDefaults.DefaultGradeCode,
                    DesignCode         = esE.DesignCode,
                    DefaultThicknessMm = _firmDefaults.DefaultSlabThicknessMm > 0 ? _firmDefaults.DefaultSlabThicknessMm : PdfToSafeConstants.DefaultThicknessMm,
                    DefaultWallDepthMm = _firmDefaults.DefaultWallDepthMm > 0    ? _firmDefaults.DefaultWallDepthMm    : 1000.0,
                    ColumnHeightMm     = 3000.0,
                    DestFdbPath        = destEdb,
                    IsImperial         = string.Equals(_firmDefaults.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase),
                    SafeExePathOverride = string.IsNullOrWhiteSpace(_firmDefaults.EtabsExePath) ? null : _firmDefaults.EtabsExePath,
                };
                var result = await EtabsApiExporter.ExportFullModelAsync(input).ConfigureAwait(true);

                if (result.Success)
                    SetStatus(result.Message, "#E8F5E9", "#2E7D32");
                else
                    SetStatus("ETABS export failed — " + result.Message, "#FDECEA", "#B71C1C");
            }
            catch (Exception ex)
            {
                SetStatus($"ETABS export crashed — {ex.GetType().Name}: {ex.Message}", "#FDECEA", "#B71C1C");
            }
            finally
            {
                EtabsApiExportButton.IsEnabled = true;
                LoadPdfButton.IsEnabled = true;
            }
        }

        private async void Sap2000ApiExport_Click(object sender, RoutedEventArgs e)
        {
            Sap2000ApiExportButton.IsEnabled = false;
            LoadPdfButton.IsEnabled = false;
            try
            {
                bool needsLoad = _extractedGeometry is null || !_extractedGeometry.IsVectorPdf || string.IsNullOrEmpty(_loadedFilePath);
                if (needsLoad)
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select structural PDF", Filter = "PDF files (*.pdf)|*.pdf" };
                    if (dlg.ShowDialog() != true) return;
                    await LoadPdfCoreAsync(dlg.FileName, fireVisionAutoClassify: false).ConfigureAwait(true);
                    if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) { SetStatus("Could not extract vector geometry.", "#FEE2E2", "#991B1B"); return; }
                }
                if (_aiBarInitialized && _appAiService?.IsConfigured == true) { SetStatusBusy("AI classifying markups…"); await TryVisionAutoClassifyAsync().ConfigureAwait(true); }
                if (_extractedGeometry is null || !_extractedGeometry.IsVectorPdf) { SetStatus("No vector geometry available.", "#FEE2E2", "#991B1B"); return; }

                string pdfPath = _loadedFilePath ?? "export";
                string dest = System.IO.Path.Combine(
                    System.IO.Path.GetDirectoryName(pdfPath) ?? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    System.IO.Path.GetFileNameWithoutExtension(pdfPath) + "_SAP2000.sdb");
                SetStatusBusy("Exporting to SAP2000 — launching SAP2000 may take 10–30 s…");

                var colorSettings = BuildSlabColorSettings();
                var reclassified = PdfGeometryExtractor.ReclassifyByColor(_extractedGeometry, colorSettings,
                    _excl.SlabTypeOverrides.Count > 0 ? _excl.SlabTypeOverrides : null,
                    _excl.LineTypeOverrides.Count > 0 ? _excl.LineTypeOverrides : null,
                    _excl.ColumnTypeOverrides.Count > 0 ? _excl.ColumnTypeOverrides : null);

                // Resolve text annotations (same as SAFE/ETABS path), then layer AI overrides.
                var annResS = AnnotationOverrideMerger.Merge(
                    AnnotationResolver.Resolve(reclassified),
                    _excl.SlabThicknessOverridesMm,
                    _excl.ColumnSectionOverridesMm,
                    _excl.LineSectionOverridesMm);

                var exColorsS = _excl.Colors.Count > 0 ? _excl.Colors : null;
                var keptSlabs = new List<IReadOnlyList<(double X, double Y)>>(); var keptSlabColors = new List<(byte R, byte G, byte B)>(); var keptSlabThick = new List<double?>();
                for (int i = 0; i < reclassified.Slabs.Count; i++) { if (_excl.Slabs.Contains(i)) continue; if (exColorsS != null && i < reclassified.SlabColors.Count && exColorsS.Contains(reclassified.SlabColors[i])) continue; keptSlabs.Add(reclassified.Slabs[i]); keptSlabColors.Add(i < reclassified.SlabColors.Count ? reclassified.SlabColors[i] : ((byte)0,(byte)0,(byte)0)); keptSlabThick.Add(i < annResS.SlabThicknessMm.Length ? annResS.SlabThicknessMm[i] : null); }
                var keptColumns = new List<(double X, double Y)>(); var keptColumnSz = new List<(double,double)>(); var keptColSec = new List<(double WidthMm, double DepthMm)?>();
                for (int i = 0; i < reclassified.Columns.Count; i++) { if (_excl.Columns.Contains(i)) continue; if (exColorsS != null && i < reclassified.ColumnColors.Count && exColorsS.Contains(reclassified.ColumnColors[i])) continue; keptColumns.Add(reclassified.Columns[i]); keptColumnSz.Add(i < reclassified.ColumnSizes.Count ? reclassified.ColumnSizes[i] : (400.0,400.0)); keptColSec.Add(i < annResS.ColumnSectionMm.Length ? annResS.ColumnSectionMm[i] : null); }
                var keptLines = new List<IReadOnlyList<(double X, double Y)>>(); var keptHints = new List<(double,double)?>(); var keptLineSec = new List<(double WidthMm, double DepthMm)?>();
                for (int i = 0; i < reclassified.Lines.Count; i++) { if (_excl.Lines.Contains(i)) continue; if (exColorsS != null && i < reclassified.LineColors.Count && exColorsS.Contains(reclassified.LineColors[i])) continue; keptLines.Add(reclassified.Lines[i]); keptHints.Add(i < reclassified.LineSectionHints.Count ? reclassified.LineSectionHints[i] : null); keptLineSec.Add(i < annResS.LineSectionMm.Length ? annResS.LineSectionMm[i] : null); }

                if (keptSlabs.Count == 0 && keptColumns.Count == 0 && keptLines.Count == 0) { SetStatus("Nothing to export.", "#FFF3E0", "#E65100"); return; }

                // Pre-flight validation (same as SAFE/ETABS path).
                {
                    var vGeo = new ExtractedGeometry { IsVectorPdf = true };
                    foreach (var s in keptSlabs) vGeo.Slabs.Add(s.ToList());
                    foreach (var c in keptSlabColors) vGeo.SlabColors.Add(c);
                    foreach (var c in keptColumns) { vGeo.Columns.Add(c); vGeo.ColumnColors.Add((0,0,0)); }
                    foreach (var (w, d) in keptColumnSz) vGeo.ColumnSizes.Add((w, d));
                    foreach (var l in keptLines) vGeo.Lines.Add(l.ToList());
                    foreach (var h in keptHints) vGeo.LineSectionHints.Add(h);
                    var settingsS = BuildDefaultExportSettings();
                    var validation = ExportValidator.Validate(vGeo, colorSettings, settingsS);
                    if (validation.HasErrors) { SetStatus(FormatValidationReport(validation), "#FEE2E2", "#991B1B"); return; }
                }

                var esS = BuildDefaultExportSettings();
                var input = new SafeApiExporter.ExportInput
                {
                    Slabs = keptSlabs, SlabColors = keptSlabColors, Columns = keptColumns, ColumnSizes = keptColumnSz,
                    Lines = keptLines, LineSectionHints = keptHints, ColorSettings = colorSettings,
                    AnnotatedSlabThicknesses = keptSlabThick,
                    AnnotatedLineSections    = keptLineSec,
                    AnnotatedColumnSections  = keptColSec,
                    DropPanelCandidates = reclassified.DropPanelCandidates,
                    DropPanelThicknessMultiplier = esS.DropPanelThicknessMultiplier,
                    SlabMembraneModifier = esS.SlabMembraneModifier,
                    SlabBendingModifier  = esS.SlabBendingModifier,
                    SlabShearModifier    = esS.SlabShearModifier,
                    DefaultGradeCode = _firmDefaults.DefaultGradeCode,
                    DesignCode = esS.DesignCode,
                    DefaultThicknessMm = _firmDefaults.DefaultSlabThicknessMm > 0 ? _firmDefaults.DefaultSlabThicknessMm : PdfToSafeConstants.DefaultThicknessMm,
                    DefaultWallDepthMm = _firmDefaults.DefaultWallDepthMm > 0 ? _firmDefaults.DefaultWallDepthMm : 1000.0,
                    ColumnHeightMm = 3000.0, DestFdbPath = dest,
                    IsImperial = string.Equals(_firmDefaults.UnitSystem, "Imperial", StringComparison.OrdinalIgnoreCase),
                    SafeExePathOverride = string.IsNullOrWhiteSpace(_firmDefaults.Sap2000ExePath) ? null : _firmDefaults.Sap2000ExePath,
                };
                var result = await Sap2000ApiExporter.ExportFullModelAsync(input).ConfigureAwait(true);
                if (result.Success) SetStatus(result.Message, "#E8F5E9", "#2E7D32");
                else SetStatus("SAP2000 export failed — " + result.Message, "#FDECEA", "#B71C1C");
            }
            catch (Exception ex) { SetStatus($"SAP2000 export crashed — {ex.GetType().Name}: {ex.Message}", "#FDECEA", "#B71C1C"); }
            finally { Sap2000ApiExportButton.IsEnabled = true; LoadPdfButton.IsEnabled = true; }
        }

        private void FirmDefaults_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new FirmDefaultsDialog(_firmDefaults) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                _firmDefaults.ApplyTo(_exportSettings);
                SetStatus("Firm defaults saved.", "#E8F5E9", "#2E7D32");
            }
        }

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

                // Validate the geometry the export will ACTUALLY use (post-
                // exclusion), not the full reclassified model. This matches
                // the OAPI paths: excluded bad geometry no longer blocks export.
                var validation = ExportValidator.Validate(
                    BuildFilteredGeometryForValidation(reclassified), colorSettings, settings);
                if (validation.HasErrors)
                {
                    var report = FormatValidationReport(validation);
                    SetStatus(report, "#FEE2E2", "#991B1B");
                    return "Export blocked by " + validation.ErrorCount + " error(s).";
                }

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
                SetStatus(BuildExportStatus(outputPath, validation), "#E8F5E9", "#2E7D32");
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

        private static string FormatValidationReport(ValidationResult r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Export blocked — ")
              .Append(r.ErrorCount).Append(" error(s)");
            if (r.WarningCount > 0) sb.Append(", ").Append(r.WarningCount).Append(" warning(s)");
            sb.AppendLine(":");
            foreach (var issue in r.Issues)
            {
                sb.Append(issue.Severity == ValidationSeverity.Error ? "  ✗ " : "  ⚠ ")
                  .AppendLine(issue.Message);
            }
            return sb.ToString().TrimEnd();
        }

        private static string BuildExportStatus(string outputPath, ValidationResult r)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("Exported: ").Append(System.IO.Path.GetFileName(outputPath));
            if (r.WarningCount > 0)
            {
                sb.AppendLine().Append(r.WarningCount).AppendLine(" warning(s):");
                foreach (var issue in r.Issues.Where(i => i.Severity == ValidationSeverity.Warning))
                    sb.Append("  ⚠ ").AppendLine(issue.Message);
            }
            return sb.ToString().TrimEnd();
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
                var validation = ExportValidator.Validate(BuildFilteredGeometryForValidation(reclassified), colorSettings, settings);
                if (validation.HasErrors)
                {
                    SetStatus(FormatValidationReport(validation), "#FEE2E2", "#991B1B");
                    return "Export blocked by " + validation.ErrorCount + " error(s).";
                }
                // Apply both per-element and per-color exclusions (FilterGeometry only handles color).
                var filtered = new ExtractedGeometry
                {
                    PageWidthPts = reclassified.PageWidthPts, PageHeightPts = reclassified.PageHeightPts,
                    ScaleDenominator = reclassified.ScaleDenominator, PageCount = reclassified.PageCount,
                    RawPathCount = reclassified.RawPathCount, IsVectorPdf = reclassified.IsVectorPdf,
                    TextAnnotations = reclassified.TextAnnotations, DropPanelCandidates = reclassified.DropPanelCandidates,
                };
                var exClr = _excl.Colors.Count > 0 ? _excl.Colors : null;
                for (int i = 0; i < reclassified.Slabs.Count; i++)
                {
                    if (_excl.Slabs.Contains(i)) continue;
                    if (exClr != null && i < reclassified.SlabColors.Count && exClr.Contains(reclassified.SlabColors[i])) continue;
                    filtered.Slabs.Add(reclassified.Slabs[i]); filtered.SlabColors.Add(i < reclassified.SlabColors.Count ? reclassified.SlabColors[i] : ((byte)0,(byte)0,(byte)0));
                }
                for (int i = 0; i < reclassified.Columns.Count; i++)
                {
                    if (_excl.Columns.Contains(i)) continue;
                    if (exClr != null && i < reclassified.ColumnColors.Count && exClr.Contains(reclassified.ColumnColors[i])) continue;
                    filtered.Columns.Add(reclassified.Columns[i]); filtered.ColumnColors.Add(i < reclassified.ColumnColors.Count ? reclassified.ColumnColors[i] : ((byte)0,(byte)0,(byte)0));
                    filtered.ColumnSizes.Add(i < reclassified.ColumnSizes.Count ? reclassified.ColumnSizes[i] : (400.0, 400.0));
                }
                for (int i = 0; i < reclassified.Lines.Count; i++)
                {
                    if (_excl.Lines.Contains(i)) continue;
                    if (exClr != null && i < reclassified.LineColors.Count && exClr.Contains(reclassified.LineColors[i])) continue;
                    filtered.Lines.Add(reclassified.Lines[i]); filtered.LineColors.Add(i < reclassified.LineColors.Count ? reclassified.LineColors[i] : ((byte)0,(byte)0,(byte)0));
                    filtered.LineSectionHints.Add(i < reclassified.LineSectionHints.Count ? reclassified.LineSectionHints[i] : null);
                }
                await Task.Run(() =>
                {
                    EtabsE2kExporter.Export(outputPath, filtered, colorSettings, settings);
                }).ConfigureAwait(true);
                SetStatus(BuildExportStatus(outputPath, validation), "#E8F5E9", "#2E7D32");
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
                var validation = ExportValidator.Validate(BuildFilteredGeometryForValidation(reclassified), colorSettings, BuildDefaultExportSettings());
                if (validation.HasErrors)
                {
                    SetStatus(FormatValidationReport(validation), "#FEE2E2", "#991B1B");
                    return "Export blocked by " + validation.ErrorCount + " error(s).";
                }
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
                SetStatus(BuildExportStatus(outputPath, validation), "#E8F5E9", "#2E7D32");
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

        private bool _isSaving;
        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (_isSaving) return;
            _isSaving = true;
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
            finally { _isSaving = false; }
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

                _isPopulatingPageSelector = true;
                PageSelector.Items.Clear();
                if (geo.PageCount > 1)
                {
                    for (int i = 1; i <= geo.PageCount; i++)
                        PageSelector.Items.Add($"Page {i}");
                    PageSelector.SelectedIndex = pageNumber - 1;
                    PageSelectorPanel.Visibility = Visibility.Visible;
                }
                else
                {
                    PageSelectorPanel.Visibility = Visibility.Collapsed;
                }
                _isPopulatingPageSelector = false;

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
                PageNumber = Math.Max(1, PageSelector.SelectedIndex + 1),
                ScaleDenominator = int.TryParse(ScaleInput.Text, out var s) ? s : 100,
            };

            foreach (var row in _slabPropsRows)
            {
                var mapping = new ColorMapping
                {
                    ElementType = row.ElementType,
                    Excluded = !row.Included || IsExcludedType(row.ElementType),
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

            // Persist left-click exclusions (individual shape toggles).
            project.ExcludedSlabs.AddRange(_excl.Slabs);
            project.ExcludedLines.AddRange(_excl.Lines);
            project.ExcludedColumns.AddRange(_excl.Columns);

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
                // Zero is a valid load value (engineer intentionally set no SDL/LIVE).
                // Previous code skipped zero → fell back to defaults → lost the choice.
                row.SdlKPa  = mapping.SdlKPa;
                row.LiveKPa = mapping.LiveKPa;
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

            // Restore left-click exclusions.
            _excl.Slabs.Clear();
            _excl.Lines.Clear();
            _excl.Columns.Clear();
            foreach (int idx in project.ExcludedSlabs)   _excl.Slabs.Add(idx);
            foreach (int idx in project.ExcludedLines)   _excl.Lines.Add(idx);
            foreach (int idx in project.ExcludedColumns) _excl.Columns.Add(idx);

            // Restore export settings (mesh size, stiffness modifiers, design
            // code, combo code, etc.). Without this, saved settings were lost
            // on reload — the tool reverted to firm defaults every time.
            if (project.ExportSettings is not null)
            {
                _exportSettings.DesignCode              = project.ExportSettings.DesignCode;
                _exportSettings.LoadCombCode             = project.ExportSettings.LoadCombCode;
                _exportSettings.MeshSizeMm               = project.ExportSettings.MeshSizeMm;
                _exportSettings.SlabMembraneModifier      = project.ExportSettings.SlabMembraneModifier;
                _exportSettings.SlabBendingModifier       = project.ExportSettings.SlabBendingModifier;
                _exportSettings.SlabShearModifier         = project.ExportSettings.SlabShearModifier;
                _exportSettings.DropPanelThicknessMultiplier = project.ExportSettings.DropPanelThicknessMultiplier;
                _exportSettings.AutoGenerateStrips        = project.ExportSettings.AutoGenerateStrips;
                _exportSettings.StripSpacingMm            = project.ExportSettings.StripSpacingMm;
                _exportSettings.StripAAlongX              = project.ExportSettings.StripAAlongX;
                _exportSettings.IncludePtLoads            = project.ExportSettings.IncludePtLoads;
            }

            _projectSettingsLoaded = true;
            RebuildExcludedColors();
            RefreshColorPropsGrid();
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
            // Clear stale body text and hide the copy button so old content
            // doesn't linger behind the spinner during long operations.
            StatusText.Text = string.Empty;
            StatusCopyButton.Visibility = Visibility.Collapsed;
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
            _opCts.Dispose();
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
