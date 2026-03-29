#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
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
        private readonly GeometryExclusionState _excl = new();
        private readonly List<SlabPropsRow> _slabPropsRows = new();
        private readonly System.Collections.ObjectModel.ObservableCollection<StoryDefinition> _stories = new();
        private (byte R, byte G, byte B)? _soloColor = null;
        private BitmapSource? _renderedBitmap;
        private readonly PdfGeometryAnalysisService _aiService;
        private bool _scaleCalibMode = false;
        private System.Windows.Point? _calibPt1 = null;
        private System.Windows.Point? _calibPt2 = null;
        private readonly PdfViewportController _viewport = new();

        public PdfToSafeWindow()
        {
            InitializeComponent();
            _aiService = new PdfGeometryAnalysisService(
                Environment.GetEnvironmentVariable("KOR_ANTHROPIC_KEY") ?? "");
            StoriesGrid.ItemsSource = _stories;
            _stories.CollectionChanged += (_, _) => UpdateStoriesUi();
            UpdateStoriesUi();
            UpdateWorkflowState();
        }

        private void UpdateStoriesUi()
        {
            if (RemoveStoryButton is not null)
                RemoveStoryButton.IsEnabled = _stories.Count > 1;
        }

        private void BuildStoriesFromProject()
        {
            _stories.Clear();

            var sourceStories = _project.Stories.Count > 0
                ? _project.Stories
                : new List<StoryDefinition>
                {
                    new()
                    {
                        Name = "Story 1",
                        PageNumber = Math.Max(1, _project.PageNumber),
                        ElevationMm = 0.0
                    }
                };

            int maxPage = _extractedGeometry?.PageCount ?? int.MaxValue;
            foreach (var story in sourceStories)
            {
                _stories.Add(new StoryDefinition
                {
                    Name = string.IsNullOrWhiteSpace(story.Name) ? $"Story {_stories.Count + 1}" : story.Name,
                    PageNumber = Math.Max(1, Math.Min(story.PageNumber, maxPage)),
                    ElevationMm = story.ElevationMm
                });
            }

            if (_stories.Count == 0)
            {
                _stories.Add(new StoryDefinition
                {
                    Name = "Story 1",
                    PageNumber = Math.Max(1, _project.PageNumber),
                    ElevationMm = 0.0
                });
            }

            UpdateStoriesUi();
        }

        private void SyncStoriesToProject()
        {
            if (_stories.Count == 0)
            {
                _stories.Add(new StoryDefinition
                {
                    Name = "Story 1",
                    PageNumber = PageSelector.SelectedIndex >= 0 ? PageSelector.SelectedIndex + 1 : Math.Max(1, _project.PageNumber),
                    ElevationMm = 0.0
                });
            }

            if (_stories.Count == 1 && PageSelector.SelectedIndex >= 0)
                _stories[0].PageNumber = PageSelector.SelectedIndex + 1;

            _project.Stories = _stories
                .Select(s => new StoryDefinition
                {
                    Name = string.IsNullOrWhiteSpace(s.Name) ? "Story" : s.Name,
                    PageNumber = Math.Max(1, s.PageNumber),
                    ElevationMm = s.ElevationMm
                })
                .ToList();
        }

        private async void LoadPdf_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select a PDF",
                Filter = "PDF files (*.pdf)|*.pdf",
                Multiselect = false
            };

            if (dialog.ShowDialog() != true)
                return;

            _loadedFilePath = dialog.FileName;
            _projectPath = null;
            FileNameText.Text = Path.GetFileName(_loadedFilePath);
            _excl.Clear();

            LoadPdfButton.IsEnabled = false;
            SetStatus("Analysing...", "#E8EAF6", "#3949AB");

            try
            {
                if (!int.TryParse(ScaleInput.Text.Trim(), out int previewScale) || previewScale <= 0)
                    previewScale = 100;

                var detectedScale = await Task.Run(() =>
                    PdfGeometryExtractor.DetectScale(_loadedFilePath));
                if (detectedScale.HasValue)
                {
                    previewScale = detectedScale.Value;
                    ScaleInput.Text = detectedScale.Value.ToString();
                }

                var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                _extractedGeometry = await Task.Run(() =>
                    PdfGeometryExtractor.Extract(_loadedFilePath, previewScale, 1,
                        slabMin, lineMin, excludeGrids));

                UpdateDetectionSummary(_extractedGeometry);
                BuildColorSwatches(_extractedGeometry);
                BuildSlabPropsRows(_extractedGeometry);
                await ApplyThicknessHintsAsync(_loadedFilePath, 1, previewScale);

                // Populate page selector
                _isPopulatingPageSelector = true;
                PageSelector.Items.Clear();
                for (int i = 1; i <= _extractedGeometry.PageCount; i++)
                    PageSelector.Items.Add($"Page {i}");
                PageSelector.SelectedIndex = 0;
                PageSelectorPanel.Visibility = _extractedGeometry.PageCount > 1
                    ? Visibility.Visible : Visibility.Collapsed;
                _isPopulatingPageSelector = false;
                _project = BuildCurrentProject();
                BuildStoriesFromProject();
                ReAnalyseButton.IsEnabled = true;

                UpdatePdfInfo(_extractedGeometry);
                PdfInfoPanel.Visibility = Visibility.Visible;
                ScalePanel.Visibility = Visibility.Visible;

                if (_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("Vector PDF detected — ready to export.", "#E8F5E9", "#2E7D32");
                    ExportDxfButton.IsEnabled = true; ExportF2kButton.IsEnabled = true;
                }
                else
                {
                    SetStatus("Raster or image-only PDF — not supported. Load a vector PDF exported from Revit or AutoCAD.", "#FFF3E0", "#E65100");
                    ExportDxfButton.IsEnabled = false; ExportF2kButton.IsEnabled = false;
                }
                UpdateWorkflowState();

                // Render page 1 for preview
#pragma warning disable CA1416
                await RenderPreviewAsync(_loadedFilePath);
#pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load PDF: {ex.Message}", "#FFEBEE", "#C62828");
                ExportDxfButton.IsEnabled = false; ExportF2kButton.IsEnabled = false;
            }
            finally
            {
                LoadPdfButton.IsEnabled = true;
            }
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

                double aspectRatio = (double)bitmap.PixelHeight / bitmap.PixelWidth;
                double canvasH = PdfToSafeConstants.PreviewBitmapWidth * aspectRatio;

                PreviewCanvas.Width  = PdfToSafeConstants.PreviewBitmapWidth;
                PreviewCanvas.Height = canvasH;
                CalibOverlay.Width   = PdfToSafeConstants.PreviewBitmapWidth;
                CalibOverlay.Height  = canvasH;
                PreviewImage.Width   = PdfToSafeConstants.PreviewBitmapWidth;
                PreviewImage.Height  = canvasH;
                PreviewImage.Source  = bitmap;

                DrawOverlay();

                // Enable AI button now that we have a rendered bitmap
                if (_aiService.IsConfigured && AiPanel.Visibility == Visibility.Visible)
                    AiAnalyseButton.IsEnabled = true;

                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                PreviewViewbox.Visibility     = Visibility.Visible;
                ZoomToolbar.Visibility        = Visibility.Visible;
                _ = Dispatcher.InvokeAsync(FitToView, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch
            {
                // Preview is non-critical — leave placeholder visible if rendering fails
            }
        }

        private void DrawOverlay()
        {
            if (_extractedGeometry is null) return;

            // Remove previous overlays (keep PreviewImage)
            var overlays = PreviewCanvas.Children
                .OfType<System.Windows.UIElement>()
                .Where(c => c != PreviewImage)
                .ToList();
            foreach (var el in overlays)
                PreviewCanvas.Children.Remove(el);

            double canvasW   = PreviewCanvas.Width;
            double canvasH   = PreviewCanvas.Height;
            if (double.IsNaN(canvasW) || canvasW == 0) return;
            double pageW     = _extractedGeometry.PageWidthPts;
            double pageH     = _extractedGeometry.PageHeightPts;
            int    scale     = _extractedGeometry.ScaleDenominator;
            var xform = new CoordinateTransformer(canvasW, pageW, pageH, scale);
            System.Windows.Point ToCanvas(double xMm, double yMm)
            {
                var (cx, cy) = xform.ToCanvas(xMm, yMm);
                return new System.Windows.Point(cx, cy);
            }

            // Slab outlines — green outline (excluded = white mask + faint outline)
            for (int i = 0; i < _extractedGeometry.Slabs.Count; i++)
            {
                var pts   = _extractedGeometry.Slabs[i];
                bool excl = _excl.IsSlabExcluded(i, _extractedGeometry.SlabColors);
                var canvasPts = new System.Windows.Media.PointCollection(pts.Select(p => ToCanvas(p.X, p.Y)));

                if (excl)
                {
                    var mask = new System.Windows.Shapes.Polygon
                    {
                        Fill            = new System.Windows.Media.SolidColorBrush(
                                              System.Windows.Media.Color.FromArgb(178, 255, 255, 255)),
                        Stroke          = System.Windows.Media.Brushes.LightGray,
                        StrokeThickness = 0.5,
                        Cursor          = System.Windows.Input.Cursors.Hand,
                        Tag             = Tuple.Create("slab", i),
                        Points          = canvasPts
                    };
                    mask.MouseDown += Shape_MouseDown;
                    System.Windows.Controls.Canvas.SetZIndex(mask, 1);
                    PreviewCanvas.Children.Add(mask);
                }
                else
                {
                    var shape = new System.Windows.Shapes.Polyline
                    {
                        Stroke          = System.Windows.Media.Brushes.LimeGreen,
                        StrokeThickness = 2,
                        Cursor          = System.Windows.Input.Cursors.Hand,
                        Tag             = Tuple.Create("slab", i),
                        Points          = canvasPts
                    };
                    if (pts.Count > 0)
                        shape.Points.Add(ToCanvas(pts[0].X, pts[0].Y));
                    shape.MouseDown += Shape_MouseDown;
                    System.Windows.Controls.Canvas.SetZIndex(shape, 1);
                    PreviewCanvas.Children.Add(shape);
                }
            }

            // Linear elements — cyan (red if excluded)
            for (int i = 0; i < _extractedGeometry.Lines.Count; i++)
            {
                var pts   = _extractedGeometry.Lines[i];
                bool excl = _excl.IsLineExcluded(i, _extractedGeometry.LineColors);
                var shape = new System.Windows.Shapes.Polyline
                {
                    Stroke          = excl ? System.Windows.Media.Brushes.White
                                           : System.Windows.Media.Brushes.Cyan,
                    StrokeThickness = excl ? 4 : 1.5,
                    Opacity         = excl ? 0.65 : 1.0,
                    Cursor          = System.Windows.Input.Cursors.Hand,
                    Tag             = Tuple.Create("line", i),
                    Points          = new System.Windows.Media.PointCollection(
                        pts.Select(p => ToCanvas(p.X, p.Y)))
                };
                shape.MouseDown += Shape_MouseDown;
                System.Windows.Controls.Canvas.SetZIndex(shape, 1);
                PreviewCanvas.Children.Add(shape);
            }

            // Columns — yellow dot (red if excluded)
            for (int i = 0; i < _extractedGeometry.Columns.Count; i++)
            {
                var (x, y) = _extractedGeometry.Columns[i];
                bool excl = _excl.IsColumnExcluded(i, _extractedGeometry.ColumnColors);
                var pt     = ToCanvas(x, y);
                var dot    = new System.Windows.Shapes.Ellipse
                {
                    Width           = 10,
                    Height          = 10,
                    Fill            = excl ? System.Windows.Media.Brushes.LightGray
                                           : System.Windows.Media.Brushes.Yellow,
                    Stroke          = excl ? System.Windows.Media.Brushes.Gray
                                           : System.Windows.Media.Brushes.DarkGoldenrod,
                    StrokeThickness = excl ? 0.5 : 1,
                    Opacity         = excl ? 0.18 : 1.0,
                    Cursor          = System.Windows.Input.Cursors.Hand,
                    Tag             = Tuple.Create("column", i)
                };
                dot.MouseDown += Shape_MouseDown;
                System.Windows.Controls.Canvas.SetLeft(dot, pt.X - 5);
                System.Windows.Controls.Canvas.SetTop(dot,  pt.Y - 5);
                System.Windows.Controls.Canvas.SetZIndex(dot, 2);
                PreviewCanvas.Children.Add(dot);
            }

            bool hasContent = _extractedGeometry.Slabs.Count > 0
                           || _extractedGeometry.Lines.Count > 0
                           || _extractedGeometry.Columns.Count > 0;
            PreviewLegend.Visibility = hasContent ? Visibility.Visible : Visibility.Collapsed;
            bool hasExclusions = _excl.HasIndexExclusions;
            ClearExclusionsButton.Visibility = hasExclusions
                ? Visibility.Visible : Visibility.Collapsed;
            if (_extractedGeometry != null)
            {
                LegendSlabRow.Opacity   = _extractedGeometry.Slabs.Count > 0   && Enumerable.Range(0, _extractedGeometry.Slabs.Count).All(i   => _excl.Slabs.Contains(i))   ? 0.35 : 1.0;
                LegendLineRow.Opacity   = _extractedGeometry.Lines.Count > 0   && Enumerable.Range(0, _extractedGeometry.Lines.Count).All(i   => _excl.Lines.Contains(i))   ? 0.35 : 1.0;
                LegendColumnRow.Opacity = _extractedGeometry.Columns.Count > 0 && Enumerable.Range(0, _extractedGeometry.Columns.Count).All(i => _excl.Columns.Contains(i)) ? 0.35 : 1.0;
            }
        }

        private async void ReAnalyse_Click(object sender, RoutedEventArgs e)
        {
            if (_loadedFilePath is null) return;

            if (!int.TryParse(ScaleInput.Text.Trim(), out int scale) || scale <= 0)
            {
                MessageBox.Show("Enter a valid scale denominator (e.g. 100 for 1:100).",
                    "Invalid Scale", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int pageNumber = PageSelector.SelectedIndex >= 0 ? PageSelector.SelectedIndex + 1 : 1;

            ReAnalyseButton.IsEnabled = false;
            _excl.Clear();
            SetStatus("Analysing...", "#E8EAF6", "#3949AB");

            try
            {
                var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                _extractedGeometry = await Task.Run(() =>
                    PdfGeometryExtractor.Extract(_loadedFilePath, scale, pageNumber,
                        slabMin, lineMin, excludeGrids));

                UpdateDetectionSummary(_extractedGeometry);
                BuildColorSwatches(_extractedGeometry);
                BuildSlabPropsRows(_extractedGeometry);
                await ApplyThicknessHintsAsync(_loadedFilePath, pageNumber, scale);
                UpdatePdfInfo(_extractedGeometry);

                if (_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("Vector PDF detected — ready to export.", "#E8F5E9", "#2E7D32");
                    ExportDxfButton.IsEnabled = true; ExportF2kButton.IsEnabled = true;
                }
                else
                {
                    SetStatus("Raster or image-only PDF — not supported. Load a vector PDF exported from Revit or AutoCAD.", "#FFF3E0", "#E65100");
                    ExportDxfButton.IsEnabled = false; ExportF2kButton.IsEnabled = false;
                }
                UpdateWorkflowState();

                DrawOverlay();
            }
            catch (Exception ex)
            {
                SetStatus($"Analysis failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                ReAnalyseButton.IsEnabled = true;
            }
        }

        private async void PageSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingPageSelector || _loadedFilePath is null || PageSelector.SelectedIndex < 0)
                return;

            if (!int.TryParse(ScaleInput.Text.Trim(), out int scale) || scale <= 0)
                scale = 100;

            int pageIndex  = PageSelector.SelectedIndex;
            int pageNumber = pageIndex + 1;

            PageSelector.IsEnabled    = false;
            ReAnalyseButton.IsEnabled = false;
            _excl.Clear();
            SetStatus("Analysing...", "#E8EAF6", "#3949AB");

            try
            {
                var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                _extractedGeometry = await Task.Run(() =>
                    PdfGeometryExtractor.Extract(_loadedFilePath, scale, pageNumber,
                        slabMin, lineMin, excludeGrids));

                UpdateDetectionSummary(_extractedGeometry);
                BuildColorSwatches(_extractedGeometry);
                BuildSlabPropsRows(_extractedGeometry);
                await ApplyThicknessHintsAsync(_loadedFilePath, pageNumber, scale);
                UpdatePdfInfo(_extractedGeometry);

                if (_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("Vector PDF detected — ready to export.", "#E8F5E9", "#2E7D32");
                    ExportDxfButton.IsEnabled = true; ExportF2kButton.IsEnabled = true;
                }
                else
                {
                    SetStatus("Raster or image-only PDF — not supported. Load a vector PDF exported from Revit or AutoCAD.", "#FFF3E0", "#E65100");
                    ExportDxfButton.IsEnabled = false; ExportF2kButton.IsEnabled = false;
                }
                UpdateWorkflowState();
                if (_stories.Count == 1)
                {
                    _stories[0].PageNumber = pageNumber;
                    SyncStoriesToProject();
                }

#pragma warning disable CA1416
                await RenderPreviewAsync(_loadedFilePath, pageIndex);
#pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                SetStatus($"Page load failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                PageSelector.IsEnabled    = true;
                ReAnalyseButton.IsEnabled = _loadedFilePath is not null;
            }
        }

        private async void ExportF2k_Click(object sender, RoutedEventArgs e)
        {
            SyncStoriesToProject();
            if (_loadedFilePath is null) return;

            if (!int.TryParse(ScaleInput.Text.Trim(), out int scale) || scale <= 0)
            {
                MessageBox.Show("Enter a valid scale denominator (e.g. 100 for 1:100).",
                    "Invalid Scale", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export for SAFE (F2K)",
                Filter = "SAFE ASCII files (*.f2k)|*.f2k",
                FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath) + "_SAFE"
            };
            if (saveDialog.ShowDialog() != true) return;

            ExportF2kButton.IsEnabled = false;
            SetStatus("Exporting F2K...", "#E8EAF6", "#3949AB");

            try
            {
                var slabColorSettings = BuildSlabColorSettings();
                string? loadCombCode = (LoadCombCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Tag as string;
                int exportedSlabs;
                int exportedLines;

                if (_stories.Count > 1)
                {
                    var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                    var storyGeometries = new List<(ExtractedGeometry Geom, string StoryName, double ElevationMm)>();
                    for (int i = 0; i < _stories.Count; i++)
                    {
                        var story = _stories[i];
                        int pageNumber = Math.Max(1, story.PageNumber);
                        SetStatus($"Extracting story {i + 1}/{_stories.Count}...", "#E8EAF6", "#3949AB");
                        var extracted = await Task.Run(() =>
                            PdfGeometryExtractor.Extract(_loadedFilePath, scale, pageNumber, slabMin, lineMin, excludeGrids));
                        storyGeometries.Add((FilterGeometryByColor(extracted), story.Name, story.ElevationMm));
                    }

                    await Task.Run(() =>
                        PdfGeometryExtractor.ExportF2k(saveDialog.FileName, storyGeometries, slabColorSettings, loadCombCode));

                    exportedSlabs = storyGeometries.Sum(s => s.Geom.Slabs.Count);
                    exportedLines = storyGeometries.Sum(s => s.Geom.Lines.Count);
                }
                else
                {
                    ExtractedGeometry geometry;
                    bool hasExclusions = _excl.HasIndexExclusions;
                    if (hasExclusions)
                    {
                        geometry = _extractedGeometry!;
                    }
                    else
                    {
                        int exportPage = PageSelector.SelectedIndex >= 0 ? PageSelector.SelectedIndex + 1 : 1;
                        var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                        geometry = await Task.Run(() =>
                            PdfGeometryExtractor.Extract(_loadedFilePath, scale, exportPage, slabMin, lineMin, excludeGrids));
                    }

                    await Task.Run(() =>
                        PdfGeometryExtractor.ExportF2k(geometry, saveDialog.FileName,
                            _excl.Slabs, _excl.Lines, _excl.Columns, _excl.Colors, slabColorSettings,
                            loadCombCode));

                    exportedSlabs = Enumerable.Range(0, geometry.Slabs.Count)
                        .Count(i => !_excl.Slabs.Contains(i) &&
                                    !(i < geometry.SlabColors.Count && _excl.Colors.Contains(geometry.SlabColors[i])));
                    exportedLines = Enumerable.Range(0, geometry.Lines.Count)
                        .Count(i => !_excl.Lines.Contains(i) &&
                                    !(i < geometry.LineColors.Count && _excl.Colors.Contains(geometry.LineColors[i])));
                }

                ExportResultsText.Text =
                    $"F2K exported: {exportedSlabs} slab(s), {exportedLines} beam segment(s). " +
                    "Per-color slab properties, loads, and pinned column supports were included where configured.";
                ExportResultsText.Visibility = Visibility.Visible;
                SetLastExportSummary($"{exportedSlabs} slabs, {_stories.Count} stor{(_stories.Count == 1 ? "y" : "ies")}, 0 errors");
                SetStatus("F2K exported. In SAFE: File → Import → SAFE v12.x", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                SetStatus($"F2K export failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                ExportF2kButton.IsEnabled = true;
            }
        }

        private ExtractedGeometry FilterGeometryByColor(ExtractedGeometry geometry)
        {
            if (_excl.Colors.Count == 0)
                return geometry;

            var filtered = new ExtractedGeometry
            {
                PageWidthPts = geometry.PageWidthPts,
                PageHeightPts = geometry.PageHeightPts,
                ScaleDenominator = geometry.ScaleDenominator,
                PageCount = geometry.PageCount,
                RawPathCount = geometry.RawPathCount,
                IsVectorPdf = geometry.IsVectorPdf
            };

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                var color = i < geometry.SlabColors.Count ? geometry.SlabColors[i] : ((byte)255, (byte)255, (byte)255);
                if (_excl.Colors.Contains(color)) continue;
                filtered.Slabs.Add(geometry.Slabs[i]);
                filtered.SlabColors.Add(color);
            }
            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                var color = i < geometry.LineColors.Count ? geometry.LineColors[i] : ((byte)0, (byte)0, (byte)0);
                if (_excl.Colors.Contains(color)) continue;
                filtered.Lines.Add(geometry.Lines[i]);
                filtered.LineColors.Add(color);
            }
            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                var color = i < geometry.ColumnColors.Count ? geometry.ColumnColors[i] : ((byte)0, (byte)0, (byte)0);
                if (_excl.Colors.Contains(color)) continue;
                filtered.Columns.Add(geometry.Columns[i]);
                filtered.ColumnColors.Add(color);
            }

            return filtered;
        }

        private async void ExportE2k_Click(object sender, RoutedEventArgs e)
        {
            SyncStoriesToProject();
            if (_extractedGeometry == null || _loadedFilePath is null)
                return;

            if (!int.TryParse(ScaleInput.Text.Trim(), out int scale) || scale <= 0)
            {
                MessageBox.Show("Enter a valid scale denominator (e.g. 100 for 1:100).",
                    "Invalid Scale", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Title = "Export for ETABS (E2K)",
                Filter = "ETABS text files (*.e2k)|*.e2k",
                FileName = Path.GetFileNameWithoutExtension(_loadedFilePath) + ".e2k"
            };
            if (saveDialog.ShowDialog() != true)
                return;

            var colorSettings = BuildSlabColorSettings();
            SetStatus("Exporting E2K...", "#E8EAF6", "#3949AB");

            try
            {
                if (_stories.Count > 1)
                {
                    var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                    var storyGeometries = new List<(ExtractedGeometry Geom, string StoryName, double ElevationMm)>();
                    for (int i = 0; i < _stories.Count; i++)
                    {
                        var story = _stories[i];
                        int pageNumber = Math.Max(1, story.PageNumber);
                        SetStatus($"Extracting story {i + 1}/{_stories.Count}...", "#E8EAF6", "#3949AB");
                        var extracted = await Task.Run(() =>
                            PdfGeometryExtractor.Extract(_loadedFilePath, scale, pageNumber, slabMin, lineMin, excludeGrids));
                        storyGeometries.Add((FilterGeometryByColor(extracted), story.Name, story.ElevationMm));
                    }

                    await Task.Run(() =>
                        PdfGeometryExtractor.ExportE2k(saveDialog.FileName, storyGeometries, colorSettings));
                }
                else
                {
                    await Task.Run(() =>
                        PdfGeometryExtractor.ExportE2k(saveDialog.FileName, FilterGeometryByColor(_extractedGeometry), colorSettings));
                }

                ExportResultsText.Text = "E2K exported - open in ETABS: File - Import - ETABS Text File (.e2k)";
                ExportResultsText.Visibility = Visibility.Visible;
                SetStatus("E2K exported successfully.", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                SetStatus($"E2K export failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_loadedFilePath))
            {
                MessageBox.Show("Load a PDF before saving a project.",
                    "Save Project", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (string.IsNullOrWhiteSpace(_projectPath))
            {
                var dialog = new SaveFileDialog
                {
                    Title = "Save KOR Project",
                    Filter = "KOR Project|*.kor",
                    DefaultExt = ".kor"
                };
                if (dialog.ShowDialog() != true)
                    return;
                _projectPath = dialog.FileName;
            }

            var project = BuildCurrentProject();
            try
            {
                project.Save(_projectPath!);
                SetStatus($"Project saved: {Path.GetFileName(_projectPath)}", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to save project: {ex.Message}", "#FFEBEE", "#C62828");
            }
        }

        private void AddStory_Click(object sender, RoutedEventArgs e)
        {
            _stories.Add(new StoryDefinition
            {
                Name = $"Story {_stories.Count + 1}",
                PageNumber = 1,
                ElevationMm = (_stories.LastOrDefault()?.ElevationMm ?? 0) + 3000
            });
            SyncStoriesToProject();
        }

        private void RemoveStory_Click(object sender, RoutedEventArgs e)
        {
            if (_stories.Count <= 1)
                return;

            if (StoriesGrid.SelectedItem is StoryDefinition story)
                _stories.Remove(story);

            SyncStoriesToProject();
        }

        private async void LoadProject_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Load KOR Project",
                Filter = "KOR Project|*.kor",
                Multiselect = false
            };
            if (dialog.ShowDialog() != true)
                return;

            PdfToSafeProject project;
            try
            {
                project = PdfToSafeProject.Load(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load project: {ex.Message}",
                    "Load Project", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(project.PdfPath) || !File.Exists(project.PdfPath))
            {
                MessageBox.Show("The saved PDF could not be found. Load the PDF manually and save the project again.",
                    "Load Project", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _projectPath = dialog.FileName;
            _loadedFilePath = project.PdfPath;
            _project = project;
            FileNameText.Text = Path.GetFileName(_loadedFilePath);
            ScaleInput.Text = project.ScaleDenominator.ToString();
            SlabMinInput.Text = project.SlabMinDiagonalMm.ToString("0.###");
            LineMinInput.Text = project.LineMinLengthMm.ToString("0.###");
            ExcludeGridLinesCheck.IsChecked = project.ExcludeGridLines;
            _excl.Clear();

            LoadProjectButton.IsEnabled = false;
            SetStatus("Loading project...", "#E8EAF6", "#3949AB");

            try
            {
                _extractedGeometry = await Task.Run(() =>
                    PdfGeometryExtractor.Extract(_loadedFilePath, project.ScaleDenominator, project.PageNumber,
                        project.SlabMinDiagonalMm, project.LineMinLengthMm, project.ExcludeGridLines));

                UpdateDetectionSummary(_extractedGeometry);
                BuildColorSwatches(_extractedGeometry);
                BuildSlabPropsRows(_extractedGeometry);
                await ApplyThicknessHintsAsync(_loadedFilePath, project.PageNumber, project.ScaleDenominator);
                ApplyProjectMappings(project);

                _isPopulatingPageSelector = true;
                PageSelector.Items.Clear();
                for (int i = 1; i <= _extractedGeometry.PageCount; i++)
                    PageSelector.Items.Add($"Page {i}");
                PageSelector.SelectedIndex = Math.Max(0, Math.Min(project.PageNumber - 1, _extractedGeometry.PageCount - 1));
                PageSelectorPanel.Visibility = _extractedGeometry.PageCount > 1
                    ? Visibility.Visible : Visibility.Collapsed;
                _isPopulatingPageSelector = false;
                ReAnalyseButton.IsEnabled = true;

                UpdatePdfInfo(_extractedGeometry);
                PdfInfoPanel.Visibility = Visibility.Visible;
                ScalePanel.Visibility = Visibility.Visible;

                if (_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("Project loaded — vector PDF ready to export.", "#E8F5E9", "#2E7D32");
                    ExportDxfButton.IsEnabled = true;
                    ExportF2kButton.IsEnabled = true;
                }
                else
                {
                    SetStatus("Project loaded, but the PDF page is not analyzable as vector geometry.", "#FFF3E0", "#E65100");
                    ExportDxfButton.IsEnabled = false;
                    ExportF2kButton.IsEnabled = false;
                }
                UpdateWorkflowState();

#pragma warning disable CA1416
                await RenderPreviewAsync(_loadedFilePath, Math.Max(0, project.PageNumber - 1));
#pragma warning restore CA1416
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load project: {ex.Message}", "#FFEBEE", "#C62828");
                ExportDxfButton.IsEnabled = false;
                ExportF2kButton.IsEnabled = false;
            }
            finally
            {
                LoadProjectButton.IsEnabled = true;
            }
        }

        private async void ExportDxf_Click(object sender, RoutedEventArgs e)
        {
            if (_loadedFilePath is null) return;

            if (!int.TryParse(ScaleInput.Text.Trim(), out int scale) || scale <= 0)
            {
                MessageBox.Show("Enter a valid scale denominator (e.g. 100 for 1:100).",
                    "Invalid Scale", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var saveDialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Save DXF for SAFE",
                Filter = "DXF files (*.dxf)|*.dxf",
                FileName = System.IO.Path.GetFileNameWithoutExtension(_loadedFilePath) + "_SAFE"
            };

            if (saveDialog.ShowDialog() != true) return;

            ExportDxfButton.IsEnabled = false; ExportF2kButton.IsEnabled = false;
            SetStatus("Extracting geometry...", "#E8EAF6", "#3949AB");

            try
            {
                bool hasExclusions = _excl.HasIndexExclusions;

                ExtractedGeometry geometry;
                if (hasExclusions)
                {
                    // Use the currently displayed geometry so exclusion indices remain valid
                    geometry = _extractedGeometry!;
                }
                else
                {
                    int exportPage = PageSelector.SelectedIndex >= 0 ? PageSelector.SelectedIndex + 1 : 1;
                    var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                    geometry = await Task.Run(() =>
                        PdfGeometryExtractor.Extract(_loadedFilePath, scale, exportPage,
                            slabMin, lineMin, excludeGrids));
                }

                await Task.Run(() =>
                    PdfGeometryExtractor.ExportDxf(geometry, saveDialog.FileName,
                        _excl.Slabs, _excl.Lines, _excl.Columns, _excl.Colors));

                _extractedGeometry = geometry;
                UpdateDetectionSummary(_extractedGeometry);
                DrawOverlay();

                int exportedSlabs = Enumerable.Range(0, geometry.Slabs.Count)
                    .Count(i => !_excl.Slabs.Contains(i) &&
                                !(i < geometry.SlabColors.Count && _excl.Colors.Contains(geometry.SlabColors[i])));
                int exportedCols = Enumerable.Range(0, geometry.Columns.Count)
                    .Count(i => !_excl.Columns.Contains(i) &&
                                !(i < geometry.ColumnColors.Count && _excl.Colors.Contains(geometry.ColumnColors[i])));
                int exportedLines = Enumerable.Range(0, geometry.Lines.Count)
                    .Count(i => !_excl.Lines.Contains(i) &&
                                !(i < geometry.LineColors.Count && _excl.Colors.Contains(geometry.LineColors[i])));
                ExportResultsText.Text =
                    $"Exported: {exportedSlabs} slab outline(s), " +
                    $"{exportedCols} column(s), " +
                    $"{exportedLines} line element(s).";
                ExportResultsText.Visibility = Visibility.Visible;
                SetLastExportSummary($"{exportedSlabs} slabs, {EstimateVisiblePointCount(geometry)} points, 0 errors");

                SetStatus("DXF exported successfully.", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                SetStatus($"Export failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                ExportDxfButton.IsEnabled = true; ExportF2kButton.IsEnabled = true;
            }
        }

        private void SetStatus(string message, string backgroundHex, string foregroundHex)
        {
            StatusText.Text = message;
            StatusBadge.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backgroundHex));
            StatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(foregroundHex));
            StatusBadge.Visibility = Visibility.Visible;
        }

        private (double slabMin, double lineMin, bool excludeGridLines) ReadThresholds()
        {
            double slabMin = double.TryParse(SlabMinInput.Text.Trim(), out double s) && s > 0
                ? s : 1000.0;
            double lineMin = double.TryParse(LineMinInput.Text.Trim(), out double l) && l > 0
                ? l : PdfToSafeConstants.DefaultLineMinLengthMm;
            return (slabMin, lineMin, ExcludeGridLinesCheck.IsChecked == true);
        }

        private void UpdateDetectionSummary(ExtractedGeometry geo)
        {
            if (!geo.IsVectorPdf)
            {
                DetectionSummaryPanel.Visibility = Visibility.Collapsed;
                return;
            }
            SlabCountText.Text   = geo.Slabs.Count.ToString();
            ColumnCountText.Text = geo.Columns.Count.ToString();
            LineCountText.Text   = geo.Lines.Count.ToString();
            DetectionSummaryPanel.Visibility = Visibility.Visible;
        }

        private void Shape_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_scaleCalibMode) return;
            if (sender is FrameworkElement fe && fe.Tag is Tuple<string, int> tag)
            {
                var set = tag.Item1 switch
                {
                    "slab"   => _excl.Slabs,
                    "line"   => _excl.Lines,
                    _        => _excl.Columns
                };
                if (!set.Remove(tag.Item2)) set.Add(tag.Item2);
                DrawOverlay();
                e.Handled = true;
            }
        }

        private void ClearExclusions_Click(object sender, RoutedEventArgs e)
        {
            _excl.Slabs.Clear();
            _excl.Lines.Clear();
            _excl.Columns.Clear();
            foreach (var row in _slabPropsRows)
            {
                if (string.Equals(row.TypeComboBox.SelectedItem as string, "Ignore", StringComparison.OrdinalIgnoreCase))
                    row.TypeComboBox.SelectedItem = row.DefaultElementType;
                UpdateElementRowUi(row, false);
            }
            RebuildExcludedColors();
            DrawOverlay();
        }

        private void BuildColorSwatches(ExtractedGeometry geo)
        {
            _excl.Colors.Clear();

            if (_aiService.IsConfigured)
            {
                AiPanel.Visibility = Visibility.Visible;
                AiAnalyseButton.IsEnabled = _renderedBitmap is not null;
                AiStatusBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void BuildSlabPropsRows(ExtractedGeometry geo)
        {
            var existing = _slabPropsRows.ToDictionary(
                r => r.Color,
                r => (Name: r.NameTextBox.Text, Type: r.TypeComboBox.SelectedItem as string,
                      Thickness: r.ThicknessTextBox.Text, Sdl: r.SdlTextBox.Text, Live: r.LiveTextBox.Text,
                      Grade: r.GradeComboBox.SelectedItem as string ?? PdfToSafeConstants.DefaultGradeCode));

            _slabPropsRows.Clear();
            _soloColor = null;
            ElementsConfigRowsPanel.Children.Clear();

            var colors = geo.SlabColors
                .Concat(geo.LineColors)
                .Concat(geo.ColumnColors)
                .Distinct()
                .ToList();

            if (colors.Count == 0)
            {
                ElementsConfigPanel.Visibility = Visibility.Collapsed;
                return;
            }

            for (int index = 0; index < colors.Count; index++)
            {
                var color = colors[index];
                existing.TryGetValue(color, out var values);
                string defaultType = GetElementType(color);

                var name = new TextBox
                {
                    Text = string.IsNullOrWhiteSpace(values.Name) ? AutoColorName(color, index) : values.Name,
                    FontSize = 12,
                    Padding = new Thickness(6, 4, 6, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                var type = new ComboBox
                {
                    Width = 82,
                    FontSize = 12,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                type.Items.Add("Slab");
                type.Items.Add("Beam");
                type.Items.Add("Column");
                type.Items.Add("Opening");
                type.Items.Add("Ignore");
                type.SelectedItem = string.IsNullOrWhiteSpace(values.Type) ? defaultType : values.Type;

                var thickness = new TextBox
                {
                    Width = 52,
                    Text = string.IsNullOrWhiteSpace(values.Thickness) ? PdfToSafeConstants.DefaultThicknessMm.ToString("0") : values.Thickness,
                    FontSize = 12,
                    Padding = new Thickness(6, 4, 6, 4),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                var autoIndicator = new TextBlock
                {
                    Text = "(auto)",
                    FontSize = 10,
                    Foreground = (System.Windows.Media.Brush)FindResource("Text.Secondary"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 0, 0, 0),
                    Visibility = Visibility.Collapsed
                };
                thickness.TextChanged += (_, _) =>
                {
                    autoIndicator.Visibility = thickness.Tag is string autoText &&
                                               string.Equals(thickness.Text.Trim(), autoText, StringComparison.Ordinal)
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                };
                var sdl = new TextBox
                {
                    Width = 52,
                    Text = string.IsNullOrWhiteSpace(values.Sdl) ? "0" : values.Sdl,
                    FontSize = 12,
                    Padding = new Thickness(6, 4, 6, 4),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                var live = new TextBox
                {
                    Width = 52,
                    Text = string.IsNullOrWhiteSpace(values.Live) ? "0" : values.Live,
                    FontSize = 12,
                    Padding = new Thickness(6, 4, 6, 4),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                var grade = new ComboBox
                {
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 4, 0),
                    ToolTip = "Concrete compressive strength grade (Eurocode fck)"
                };
                foreach (var g in StructuralMaterialDatabase.SupportedGrades)
                    grade.Items.Add(g);
                grade.SelectedItem = string.IsNullOrWhiteSpace(values.Grade) ? PdfToSafeConstants.DefaultGradeCode : values.Grade;

                var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(86) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(58) });

                var includeCheck = new CheckBox
                {
                    IsChecked = !string.Equals(string.IsNullOrWhiteSpace(values.Type) ? defaultType : values.Type,
                                               "Ignore", StringComparison.OrdinalIgnoreCase),
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    ToolTip = "Uncheck to exclude this color from export"
                };

                var thicknessHost = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 6, 0)
                };
                thicknessHost.Children.Add(thickness);
                thicknessHost.Children.Add(autoIndicator);

                var swatch = new System.Windows.Shapes.Rectangle
                {
                    Width = 16,
                    Height = 16,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(color.R, color.G, color.B)),
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray),
                    StrokeThickness = 0.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Cursor = System.Windows.Input.Cursors.Hand,
                    ToolTip = "Click to isolate this color. Click again to restore all."
                };
                swatch.MouseLeftButtonUp += (_, _) => SoloColor(color);

                Grid.SetColumn(includeCheck, 0);
                Grid.SetColumn(swatch, 1);
                Grid.SetColumn(name, 2);
                Grid.SetColumn(type, 3);
                Grid.SetColumn(grade, 4);
                Grid.SetColumn(thicknessHost, 5);
                Grid.SetColumn(sdl, 6);
                Grid.SetColumn(live, 7);

                row.Children.Add(includeCheck);
                row.Children.Add(swatch);
                row.Children.Add(name);
                row.Children.Add(type);
                row.Children.Add(grade);
                row.Children.Add(thicknessHost);
                row.Children.Add(sdl);
                row.Children.Add(live);
                ElementsConfigRowsPanel.Children.Add(row);

                var model = new SlabPropsRow
                {
                    Color = color,
                    NameTextBox = name,
                    TypeComboBox = type,
                    ThicknessTextBox = thickness,
                    SdlTextBox = sdl,
                    LiveTextBox = live,
                    IncludeCheckBox = includeCheck,
                    AutoIndicatorTextBlock = autoIndicator,
                    RowContainer = row,
                    GradeComboBox = grade,
                    GradeContainer = grade,
                    ThicknessContainer = thicknessHost,
                    SdlContainer = sdl,
                    LiveContainer = live,
                    DefaultElementType = defaultType
                };
                type.SelectionChanged += (_, _) =>
                {
                    bool isIgnore = string.Equals(type.SelectedItem as string, "Ignore", StringComparison.OrdinalIgnoreCase);
                    if (!isIgnore)
                        includeCheck.Tag = type.SelectedItem as string;
                    includeCheck.IsChecked = !isIgnore;
                    UpdateElementRowUi(model);
                };
                includeCheck.Unchecked += (_, _) =>
                {
                    var current = type.SelectedItem as string ?? model.DefaultElementType;
                    if (!string.Equals(current, "Ignore", StringComparison.OrdinalIgnoreCase))
                        includeCheck.Tag = current;
                    type.SelectedItem = "Ignore";
                };
                includeCheck.Checked += (_, _) =>
                {
                    var restored = includeCheck.Tag as string ?? model.DefaultElementType;
                    type.SelectedItem = restored;
                };
                _slabPropsRows.Add(model);
                UpdateElementRowUi(model, false);
            }

            ElementsConfigPanel.Visibility = Visibility.Visible;
            BuildQuantityTakeoff();
        }

        private Dictionary<(byte R, byte G, byte B), SlabColorSettings> BuildSlabColorSettings()
        {
            var result = new Dictionary<(byte R, byte G, byte B), SlabColorSettings>();

            foreach (var row in _slabPropsRows)
            {
                string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
                if (string.Equals(type, "Ignore", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(type, "Opening", StringComparison.OrdinalIgnoreCase)) continue;

                double thickness = double.TryParse(row.ThicknessTextBox.Text.Trim(), out var t) && t > 0
                    ? t : PdfToSafeConstants.DefaultThicknessMm;
                double sdl = double.TryParse(row.SdlTextBox.Text.Trim(), out var s) && s >= 0
                    ? s : 0.0;
                double live = double.TryParse(row.LiveTextBox.Text.Trim(), out var l) && l >= 0
                    ? l : 0.0;

                result[row.Color] = new SlabColorSettings
                {
                    ThicknessMm = thickness,
                    SdlKPa = sdl,
                    LiveKPa = live,
                    GradeCode = row.GradeComboBox.SelectedItem as string ?? PdfToSafeConstants.DefaultGradeCode
                };
            }

            return result;
        }

        private void BuildQuantityTakeoff()
        {
            if (_extractedGeometry is null)
            {
                QuantityPanel.Visibility = Visibility.Collapsed;
                return;
            }

            var slabAreaMm2 = new Dictionary<(byte R, byte G, byte B), double>();
            var lineLengthMm = new Dictionary<(byte R, byte G, byte B), double>();
            var columnCount = new Dictionary<(byte R, byte G, byte B), int>();

            for (int i = 0; i < _extractedGeometry.Slabs.Count; i++)
            {
                if (_excl.Slabs.Contains(i)) continue;
                var color = i < _extractedGeometry.SlabColors.Count
                    ? _extractedGeometry.SlabColors[i] : ((byte)255, (byte)255, (byte)255);
                if (_excl.Colors.Contains(color)) continue;
                slabAreaMm2.TryGetValue(color, out double existing);
                slabAreaMm2[color] = existing + PolygonProcessor.PolygonAreaMm2(_extractedGeometry.Slabs[i]);
            }
            for (int i = 0; i < _extractedGeometry.Lines.Count; i++)
            {
                if (_excl.Lines.Contains(i)) continue;
                var color = i < _extractedGeometry.LineColors.Count
                    ? _extractedGeometry.LineColors[i] : ((byte)0, (byte)0, (byte)0);
                if (_excl.Colors.Contains(color)) continue;
                lineLengthMm.TryGetValue(color, out double existing);
                lineLengthMm[color] = existing + PolygonProcessor.PolylineLengthMm(_extractedGeometry.Lines[i]);
            }
            for (int i = 0; i < _extractedGeometry.Columns.Count; i++)
            {
                if (_excl.Columns.Contains(i)) continue;
                var color = i < _extractedGeometry.ColumnColors.Count
                    ? _extractedGeometry.ColumnColors[i] : ((byte)0, (byte)0, (byte)0);
                if (_excl.Colors.Contains(color)) continue;
                columnCount.TryGetValue(color, out int cnt);
                columnCount[color] = cnt + 1;
            }

            QuantityRowsPanel.Children.Clear();
            QuantityTotalsPanel.Children.Clear();

            double totalSlabM2 = 0, totalLineM = 0, totalCols = 0;
            bool anyRows = false;

            foreach (var row in _slabPropsRows)
            {
                string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
                if (string.Equals(type, "Ignore", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(type, "Opening", StringComparison.OrdinalIgnoreCase)) continue;

                string qty;
                if (slabAreaMm2.TryGetValue(row.Color, out double areaMm2))
                {
                    double m2 = areaMm2 / 1_000_000.0;
                    qty = $"{m2:0.0} m2";
                    totalSlabM2 += m2;
                }
                else if (lineLengthMm.TryGetValue(row.Color, out double lenMm))
                {
                    double m = lenMm / 1000.0;
                    qty = $"{m:0.0} m";
                    totalLineM += m;
                }
                else if (columnCount.TryGetValue(row.Color, out int cnt))
                {
                    qty = $"{cnt}";
                    totalCols += cnt;
                }
                else continue;

                anyRows = true;

                var swatch = new System.Windows.Shapes.Rectangle
                {
                    Width = 14,
                    Height = 14,
                    RadiusX = 2,
                    RadiusY = 2,
                    Fill = new System.Windows.Media.SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(row.Color.R, row.Color.G, row.Color.B)),
                    Stroke = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray),
                    StrokeThickness = 0.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                var nameText = new TextBlock
                {
                    Text = row.NameTextBox.Text,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                var typeText = new TextBlock
                {
                    Text = type,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = (System.Windows.Media.Brush)FindResource("Text.Secondary")
                };
                var qtyText = new TextBlock
                {
                    Text = qty,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Right
                };

                var qRow = new Grid { Margin = new Thickness(0, 0, 0, 3) };
                qRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(22) });
                qRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                qRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(70) });
                qRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });

                Grid.SetColumn(swatch, 0);
                Grid.SetColumn(nameText, 1);
                Grid.SetColumn(typeText, 2);
                Grid.SetColumn(qtyText, 3);
                qRow.Children.Add(swatch);
                qRow.Children.Add(nameText);
                qRow.Children.Add(typeText);
                qRow.Children.Add(qtyText);
                QuantityRowsPanel.Children.Add(qRow);
            }

            void AddTotal(string label, string value)
            {
                var g = new Grid { Margin = new Thickness(0, 2, 0, 0) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(80) });
                var lbl = new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    Foreground = (System.Windows.Media.Brush)FindResource("Text.Secondary"),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var val = new TextBlock
                {
                    Text = value,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(lbl, 0);
                Grid.SetColumn(val, 1);
                g.Children.Add(lbl);
                g.Children.Add(val);
                QuantityTotalsPanel.Children.Add(g);
            }

            if (totalSlabM2 > 0) AddTotal("Total slab area", $"{totalSlabM2:0.0} m2");
            if (totalLineM > 0) AddTotal("Total beam length", $"{totalLineM:0.0} m");
            if (totalCols > 0) AddTotal("Total column points", $"{(int)totalCols}");

            QuantityPanel.Visibility = anyRows ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string AutoColorName((byte R, byte G, byte B) c, int index)
        {
            if (c == (0,   255, 255)) return "Cyan";
            if (c == (255, 255,   0)) return "Yellow";
            if (c == (255,   0,   0)) return "Red";
            if (c == (0,   255,   0)) return "Green";
            if (c == (0,     0, 255)) return "Blue";
            if (c == (255, 255, 255)) return "White";
            if (c == (0,     0,   0)) return "Black";
            if (c == (128, 128, 128)) return "Grey";
            if (c == (255, 165,   0)) return "Orange";
            if (c == (128,   0, 128)) return "Purple";
            return $"Color {index + 1}";
        }

        private void UpdateElementRowUi(SlabPropsRow row, bool redraw = true)
        {
            string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
            bool isSlab = string.Equals(type, "Slab", StringComparison.OrdinalIgnoreCase);
            bool isIgnored = string.Equals(type, "Ignore", StringComparison.OrdinalIgnoreCase);

            row.GradeContainer.Visibility = isSlab ? Visibility.Visible : Visibility.Collapsed;
            row.ThicknessContainer.Visibility = isSlab ? Visibility.Visible : Visibility.Collapsed;
            row.SdlContainer.Visibility = isSlab ? Visibility.Visible : Visibility.Collapsed;
            row.LiveContainer.Visibility = isSlab ? Visibility.Visible : Visibility.Collapsed;
            row.RowContainer.Opacity = isIgnored ? 0.45 : 1.0;

            if (isIgnored)
                _excl.Colors.Add(row.Color);
            else
                _excl.Colors.Remove(row.Color);

            if (redraw)
            {
                DrawOverlay();
                BuildQuantityTakeoff();
            }
        }

        private void UpdateWorkflowState()
        {
            bool hasLoadedPdf = !string.IsNullOrWhiteSpace(_loadedFilePath);
            bool canConfigure = _extractedGeometry is { IsVectorPdf: true };

            Step2Expander.IsExpanded = hasLoadedPdf && !canConfigure;
            Step3Expander.IsEnabled = canConfigure;
            Step3Expander.IsExpanded = canConfigure;
            Step4Expander.IsEnabled = canConfigure;
            Step4Expander.IsExpanded = canConfigure;

            bool hasPdf = _extractedGeometry != null;
            bool hasScale = hasPdf && int.TryParse(ScaleInput.Text.Trim(), out int sv) && sv > 0;
            bool hasElems = hasPdf && _slabPropsRows.Count > 0;

            SetDot(Step1Dot, hasPdf ? "#4CAF50" : "#BDBDBD");
            SetDot(Step2Dot, hasScale ? "#4CAF50" : hasPdf ? "#FF9800" : "#BDBDBD");
            SetDot(Step3Dot, hasElems ? "#4CAF50" : hasScale ? "#FF9800" : "#BDBDBD");
            SetDot(Step4Dot, hasElems ? "#FF9800" : "#BDBDBD");
            StoriesSection.Visibility = hasPdf ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetDot(System.Windows.Shapes.Ellipse dot, string hex)
        {
            if (dot == null) return;
            dot.Fill = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex));
        }

        private void UpdatePdfInfo(ExtractedGeometry geo)
        {
            const double ptsToMm = PdfToSafeConstants.PointsToMm;
            PageCountText.Text = $"Pages: {geo.PageCount}";
            PathCountText.Text = $"Page size: {(geo.PageWidthPts * ptsToMm):0} mm × {(geo.PageHeightPts * ptsToMm):0} mm";
        }

        private void SetLastExportSummary(string summary)
        {
            ExportResultsText.Text = $"Last export: {summary} - {DateTime.Now:h:mm tt}";
            ExportResultsText.Visibility = Visibility.Visible;
        }

        private int EstimateVisiblePointCount(ExtractedGeometry geometry)
        {
            var pts = new HashSet<(long X, long Y)>();

            for (int i = 0; i < geometry.Slabs.Count; i++)
            {
                if (_excl.Slabs.Contains(i)) continue;
                if (i < geometry.SlabColors.Count && _excl.Colors.Contains(geometry.SlabColors[i])) continue;
                foreach (var p in geometry.Slabs[i])
                    pts.Add(((long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10)));
            }
            for (int i = 0; i < geometry.Lines.Count; i++)
            {
                if (_excl.Lines.Contains(i)) continue;
                if (i < geometry.LineColors.Count && _excl.Colors.Contains(geometry.LineColors[i])) continue;
                foreach (var p in geometry.Lines[i])
                    pts.Add(((long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10)));
            }
            for (int i = 0; i < geometry.Columns.Count; i++)
            {
                if (_excl.Columns.Contains(i)) continue;
                if (i < geometry.ColumnColors.Count && _excl.Colors.Contains(geometry.ColumnColors[i])) continue;
                var p = geometry.Columns[i];
                pts.Add(((long)Math.Round(p.X * 10), (long)Math.Round(p.Y * 10)));
            }

            return pts.Count;
        }

        private async Task ApplyThicknessHintsAsync(string filePath, int pageNumber, int scaleDenominator)
        {
            if (_extractedGeometry is null || _slabPropsRows.Count == 0)
                return;

            var hints = await Task.Run(() =>
                PdfGeometryExtractor.ExtractThicknessHints(filePath, pageNumber, scaleDenominator, _extractedGeometry));

            int applied = 0;
            foreach (var row in _slabPropsRows)
            {
                row.AutoIndicatorTextBlock.Visibility = Visibility.Collapsed;
                if (!hints.TryGetValue(row.Color, out var hint))
                    continue;
                if (!string.Equals(row.TypeComboBox.SelectedItem as string, "Slab", StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = hint.ToString("0.###");

                bool neverAutoSet = row.ThicknessTextBox.Tag is null;
                bool stillShowsAuto = row.ThicknessTextBox.Tag is string prev &&
                                      string.Equals(row.ThicknessTextBox.Text.Trim(), prev, StringComparison.Ordinal);
                if (!neverAutoSet && !stillShowsAuto)
                    continue;

                row.ThicknessTextBox.Tag = value;
                row.ThicknessTextBox.Text = value;
                row.AutoIndicatorTextBlock.Visibility = Visibility.Visible;
                applied++;
            }

            UpdateThicknessHintStatus(applied, hints.Count);
        }

        private void UpdateThicknessHintStatus(int applied, int detected)
        {
            if (ThicknessHintStatus is null) return;
            if (detected == 0)
            {
                ThicknessHintStatus.Text = "No thickness callouts found in drawing.";
                ThicknessHintStatus.Visibility = Visibility.Visible;
            }
            else
            {
                ThicknessHintStatus.Text = applied > 0
                    ? $"Auto-detected thickness for {applied} slab color{(applied == 1 ? "" : "s")} from drawing callouts."
                    : $"Detected {detected} thickness callout{(detected == 1 ? "" : "s")} - not applied (manually overridden).";
                ThicknessHintStatus.Visibility = Visibility.Visible;
            }
        }

        private PdfToSafeProject BuildCurrentProject()
        {
            _project = new PdfToSafeProject
            {
                PdfPath = _loadedFilePath ?? "",
                PageNumber = PageSelector.SelectedIndex >= 0 ? PageSelector.SelectedIndex + 1 : 1,
                ScaleDenominator = int.TryParse(ScaleInput.Text.Trim(), out var scale) && scale > 0 ? scale : 100,
                SlabMinDiagonalMm = double.TryParse(SlabMinInput.Text.Trim(), out var slabMin) && slabMin > 0 ? slabMin : 1000.0,
                LineMinLengthMm = double.TryParse(LineMinInput.Text.Trim(), out var lineMin) && lineMin > 0 ? lineMin : PdfToSafeConstants.DefaultLineMinLengthMm,
                ExcludeGridLines = ExcludeGridLinesCheck.IsChecked == true
            };

            var slabSettings = BuildSlabColorSettings();
            foreach (var row in _slabPropsRows)
            {
                var color = row.Color;
                string selectedType = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
                var mapping = new ColorMapping
                {
                    ElementType = string.Equals(selectedType, "Ignore", StringComparison.OrdinalIgnoreCase)
                        ? row.DefaultElementType
                        : selectedType,
                    Excluded = string.Equals(selectedType, "Ignore", StringComparison.OrdinalIgnoreCase)
                };

                if (slabSettings.TryGetValue(color, out var slab))
                {
                    mapping.ThicknessMm = slab.ThicknessMm;
                    mapping.SdlKPa = slab.SdlKPa;
                    mapping.LiveKPa = slab.LiveKPa;
                    mapping.GradeCode = slab.GradeCode;
                }

                _project.ColorMappings[PdfToSafeProject.ColorKey(color)] = mapping;
            }

            SyncStoriesToProject();
            return _project;
        }

        private void ApplyProjectMappings(PdfToSafeProject project)
        {
            _project = project;
            var mappings = new Dictionary<string, ColorMapping>(project.ColorMappings, StringComparer.OrdinalIgnoreCase);

            foreach (var row in _slabPropsRows)
            {
                if (!mappings.TryGetValue(PdfToSafeProject.ColorKey(row.Color), out var mapping))
                    continue;

                row.ThicknessTextBox.Tag = null;
                row.ThicknessTextBox.Text = mapping.ThicknessMm.ToString("0.###");
                row.SdlTextBox.Text = mapping.SdlKPa.ToString("0.###");
                row.LiveTextBox.Text = mapping.LiveKPa.ToString("0.###");
                row.AutoIndicatorTextBlock.Visibility = Visibility.Collapsed;

                string restoredType = mapping.Excluded
                    ? "Ignore"
                    : (mapping.ElementType is "Slab" or "Beam" or "Column" or "Opening" ? mapping.ElementType : row.DefaultElementType);
                row.GradeComboBox.SelectedItem = string.IsNullOrWhiteSpace(mapping.GradeCode)
                    ? PdfToSafeConstants.DefaultGradeCode : mapping.GradeCode;
                row.IncludeCheckBox.Tag = mapping.Excluded ? mapping.ElementType : restoredType;
                row.IncludeCheckBox.IsChecked = !mapping.Excluded;
                row.TypeComboBox.SelectedItem = restoredType;
                UpdateElementRowUi(row, false);
            }

            RebuildExcludedColors();
            DrawOverlay();
            BuildQuantityTakeoff();
            BuildStoriesFromProject();
        }

        private string GetElementType((byte R, byte G, byte B) color)
        {
            if (_extractedGeometry is not null)
            {
                if (_extractedGeometry.SlabColors.Contains(color)) return "Slab";
                if (_extractedGeometry.LineColors.Contains(color)) return "Beam";
                if (_extractedGeometry.ColumnColors.Contains(color)) return "Column";
            }
            return "Slab";
        }

        private void RebuildExcludedColors()
        {
            _excl.Colors.Clear();
            foreach (var row in _slabPropsRows)
            {
                var type = row.TypeComboBox.SelectedItem as string ?? "";
                if (string.Equals(type, "Ignore", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(type, "Opening", StringComparison.OrdinalIgnoreCase))
                    _excl.Colors.Add(row.Color);
            }
        }

        private void SoloColor((byte R, byte G, byte B) color)
        {
            if (_soloColor == color)
            {
                _soloColor = null;
                foreach (var r in _slabPropsRows)
                    r.IncludeCheckBox.IsChecked = true;
            }
            else
            {
                _soloColor = color;
                foreach (var r in _slabPropsRows)
                    r.IncludeCheckBox.IsChecked = r.Color == color;
            }
        }

        private List<ExportSummaryRow> BuildExportSummaryRows()
        {
            var rows = new List<ExportSummaryRow>();
            if (_extractedGeometry is null)
                return rows;

            foreach (var row in _slabPropsRows)
            {
                if (row.IncludeCheckBox.IsChecked != true)
                    continue;

                string type = row.TypeComboBox.SelectedItem as string ?? row.DefaultElementType;
                if (type is "Ignore" or "Opening")
                    continue;
                double slabAreaMm2 = 0;
                double lineLengthMm = 0;
                int columnCount = 0;

                for (int i = 0; i < _extractedGeometry.Slabs.Count; i++)
                {
                    if (_excl.Slabs.Contains(i)) continue;
                    var c = i < _extractedGeometry.SlabColors.Count ? _extractedGeometry.SlabColors[i] : ((byte)255, (byte)255, (byte)255);
                    if (c == row.Color && !_excl.Colors.Contains(c))
                        slabAreaMm2 += PolygonProcessor.PolygonAreaMm2(_extractedGeometry.Slabs[i]);
                }
                for (int i = 0; i < _extractedGeometry.Lines.Count; i++)
                {
                    if (_excl.Lines.Contains(i)) continue;
                    var c = i < _extractedGeometry.LineColors.Count ? _extractedGeometry.LineColors[i] : ((byte)0, (byte)0, (byte)0);
                    if (c == row.Color && !_excl.Colors.Contains(c))
                        lineLengthMm += PolygonProcessor.PolylineLengthMm(_extractedGeometry.Lines[i]);
                }
                for (int i = 0; i < _extractedGeometry.Columns.Count; i++)
                {
                    if (_excl.Columns.Contains(i)) continue;
                    var c = i < _extractedGeometry.ColumnColors.Count ? _extractedGeometry.ColumnColors[i] : ((byte)0, (byte)0, (byte)0);
                    if (c == row.Color && !_excl.Colors.Contains(c))
                        columnCount++;
                }

                double slabAreaM2 = slabAreaMm2 / 1_000_000.0;
                double beamLengthM = lineLengthMm / 1000.0;
                string quantity = slabAreaM2 > 0 ? $"{slabAreaM2:0.00} m2"
                    : beamLengthM > 0 ? $"{beamLengthM:0.00} m"
                    : columnCount > 0 ? $"{columnCount}"
                    : "";

                if (string.IsNullOrEmpty(quantity))
                    continue;

                rows.Add(new ExportSummaryRow(
                    row.NameTextBox.Text,
                    type,
                    row.GradeComboBox.SelectedItem as string ?? PdfToSafeConstants.DefaultGradeCode,
                    row.ThicknessTextBox.Text.Trim(),
                    row.SdlTextBox.Text.Trim(),
                    row.LiveTextBox.Text.Trim(),
                    quantity,
                    slabAreaM2,
                    beamLengthM,
                    columnCount,
                    $"{row.Color.R:X2}{row.Color.G:X2}{row.Color.B:X2}"
                ));
            }

            return rows;
        }

        private string BuildExportSummaryPlainText()
            => HtmlReportBuilder.BuildExportSummaryPlainText(BuildExportSummaryRows());

        private string BuildExportSummaryHtml()
        {
            var rows = BuildExportSummaryRows();
            string pdfName = Path.GetFileName(_loadedFilePath ?? "Unknown.pdf");
            int pageNumber = PageSelector.SelectedIndex >= 0 ? PageSelector.SelectedIndex + 1 : 1;
            string scale = int.TryParse(ScaleInput.Text.Trim(), out int s) && s > 0 ? s.ToString() : "100";
            string? loadComb = (LoadCombCombo.SelectedItem as ComboBoxItem)?.Tag as string;
            return HtmlReportBuilder.BuildExportSummaryHtml(rows, pdfName, pageNumber, scale, loadComb);
        }

        private string BuildRevisionDiffHtml(PdfToSafeProject current, PdfToSafeProject previous)
            => HtmlReportBuilder.BuildRevisionDiffHtml(current, previous);

        private void ShowExportSummary_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry == null)
            {
                MessageBox.Show("No geometry is available to summarize yet.", "Export Summary",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string html = BuildExportSummaryHtml();
            var webBrowser = new WebBrowser();
            webBrowser.NavigateToString(html);

            var window = new Window
            {
                Title = "Export Summary",
                Width = 720,
                Height = 560,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Content = webBrowser,
                Owner = this
            };
            window.Show();
        }

        private void CompareRevision_Click(object sender, RoutedEventArgs e)
        {
            if (_slabPropsRows.Count == 0)
            {
                MessageBox.Show("Please open and analyse a PDF first.", "Revision Diff",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "KOR Project|*.kor",
                Title = "Select previous revision to compare against"
            };
            if (dlg.ShowDialog(this) != true)
                return;

            var previous = PdfToSafeProject.Load(dlg.FileName);
            var current = BuildCurrentProject();
            string html = BuildRevisionDiffHtml(current, previous);

            var webBrowser = new WebBrowser();
            webBrowser.NavigateToString(html);

            var window = new Window
            {
                Title = $"Revision Diff - {Path.GetFileName(dlg.FileName)} - current",
                Width = 720,
                Height = 600,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Content = webBrowser,
                Owner = this
            };
            window.Show();
        }

        private void CopyQuantities_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null) return;
            var data = new DataObject();
            data.SetData(DataFormats.Html, BuildExportSummaryHtml());
            data.SetText(BuildExportSummaryPlainText());
            System.Windows.Clipboard.SetDataObject(data, true);
        }

        private void LegendSlab_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_extractedGeometry is null) return;
            bool allExcluded = _extractedGeometry.Slabs.Count > 0 &&
                               Enumerable.Range(0, _extractedGeometry.Slabs.Count).All(i => _excl.Slabs.Contains(i));
            if (allExcluded)
                _excl.Slabs.ExceptWith(Enumerable.Range(0, _extractedGeometry.Slabs.Count));
            else
                for (int i = 0; i < _extractedGeometry.Slabs.Count; i++) _excl.Slabs.Add(i);
            DrawOverlay();
        }

        private void LegendLine_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_extractedGeometry is null) return;
            bool allExcluded = _extractedGeometry.Lines.Count > 0 &&
                               Enumerable.Range(0, _extractedGeometry.Lines.Count).All(i => _excl.Lines.Contains(i));
            if (allExcluded)
                _excl.Lines.ExceptWith(Enumerable.Range(0, _extractedGeometry.Lines.Count));
            else
                for (int i = 0; i < _extractedGeometry.Lines.Count; i++) _excl.Lines.Add(i);
            DrawOverlay();
        }

        private void LegendColumn_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_extractedGeometry is null) return;
            bool allExcluded = _extractedGeometry.Columns.Count > 0 &&
                               Enumerable.Range(0, _extractedGeometry.Columns.Count).All(i => _excl.Columns.Contains(i));
            if (allExcluded)
                _excl.Columns.ExceptWith(Enumerable.Range(0, _extractedGeometry.Columns.Count));
            else
                for (int i = 0; i < _extractedGeometry.Columns.Count; i++) _excl.Columns.Add(i);
            DrawOverlay();
        }

        private void EnterScaleCalibMode()
        {
            _scaleCalibMode = true;
            _calibPt1 = null;
            _calibPt2 = null;
            PreviewCanvas.Cursor = System.Windows.Input.Cursors.Cross;
            CalibStatusText.Text = "Click first point on drawing";
            CalibStatusText.Visibility = Visibility.Visible;
            CalibOverlay.Children.Clear();
            CalibOverlay.Visibility = Visibility.Visible;
            CalibrateScaleButton.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xFF, 0xF3, 0xCD));
            CalibrateScaleButton.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE0, 0xA8, 0x00));
        }

        private void ExitScaleCalibMode()
        {
            _scaleCalibMode = false;
            _calibPt1 = null;
            _calibPt2 = null;
            PreviewCanvas.Cursor = System.Windows.Input.Cursors.Arrow;
            CalibOverlay.Children.Clear();
            CalibOverlay.Visibility = Visibility.Collapsed;
            CalibrateScaleButton.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            CalibrateScaleButton.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
            CalibStatusText.Visibility = Visibility.Collapsed;
        }

        private void FinishScaleCalib()
        {
            if (_calibPt1 is null || _calibPt2 is null || _extractedGeometry is null)
            {
                ExitScaleCalibMode();
                return;
            }

            double pixelDist = Math.Sqrt(
                Math.Pow(_calibPt2.Value.X - _calibPt1.Value.X, 2) +
                Math.Pow(_calibPt2.Value.Y - _calibPt1.Value.Y, 2));

            string? input = ShowInputDialog("Known real-world length of this line (mm):", "Scale Calibration");
            if (string.IsNullOrWhiteSpace(input) || !double.TryParse(input.Trim(), out double knownMm) || knownMm <= 0)
            {
                ExitScaleCalibMode();
                return;
            }

            double pagePts = _extractedGeometry.PageWidthPts;
            double canvasW = PreviewCanvas.ActualWidth;
            if (pagePts <= 0 || canvasW <= 0 || double.IsNaN(canvasW))
            {
                ExitScaleCalibMode();
                return;
            }

            int suggested = CoordinateTransformer.SuggestScale(pixelDist, canvasW, pagePts, knownMm);

            var result = MessageBox.Show(
                $"Suggested scale: 1:{suggested}\n\nApply to Scale field?",
                "Scale Calibration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
                ScaleInput.Text = suggested.ToString();

            ExitScaleCalibMode();
        }

        private string? ShowInputDialog(string prompt, string title)
        {
            var textBox = new TextBox { MinWidth = 220, Margin = new Thickness(0, 8, 0, 12) };
            var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0) };
            var cancelButton = new Button { Content = "Cancel", Width = 80 };
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            buttons.Children.Add(okButton);
            buttons.Children.Add(cancelButton);

            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = prompt, TextWrapping = TextWrapping.Wrap });
            panel.Children.Add(textBox);
            panel.Children.Add(buttons);

            var dialog = new Window
            {
                Title = title,
                Width = 400,
                Height = 160,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = panel
            };

            string? value = null;
            okButton.Click += (_, _) =>
            {
                value = textBox.Text;
                dialog.DialogResult = true;
            };
            textBox.Loaded += (_, _) => { textBox.Focus(); textBox.SelectAll(); };
            okButton.IsDefault = true;
            cancelButton.IsCancel = true;

            return dialog.ShowDialog() == true ? value : null;
        }

        private void CalibrateScaleButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_scaleCalibMode)
                EnterScaleCalibMode();
            else
                ExitScaleCalibMode();
        }

        private void PreviewCanvas_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_scaleCalibMode) return;

            if (_calibPt1 == null)
            {
                _calibPt1 = e.GetPosition(PreviewCanvas);
                CalibStatusText.Text = "Click second point on drawing";
            }
            else if (_calibPt2 == null)
            {
                _calibPt2 = e.GetPosition(PreviewCanvas);
                FinishScaleCalib();
            }

            e.Handled = true;
        }

        private void PreviewCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_scaleCalibMode || _calibPt1 == null || _calibPt2 != null)
                return;

            var current = e.GetPosition(PreviewCanvas);
            CalibOverlay.Children.Clear();
            var line = new System.Windows.Shapes.Line
            {
                X1 = _calibPt1.Value.X,
                Y1 = _calibPt1.Value.Y,
                X2 = current.X,
                Y2 = current.Y,
                Stroke = System.Windows.Media.Brushes.Yellow,
                StrokeThickness = 2,
                StrokeDashArray = new System.Windows.Media.DoubleCollection { 4, 2 }
            };
            CalibOverlay.Children.Add(line);
        }

        private void ApplyTransform()
        {
            var t = _viewport.BuildTransform();
            PreviewCanvas.RenderTransform = t;
            CalibOverlay.RenderTransform = t;
        }

        private void FitToView()
        {
            _viewport.FitToView(
                PreviewViewbox.ActualWidth, PreviewViewbox.ActualHeight,
                PreviewCanvas.Width,        PreviewCanvas.Height);
            ApplyTransform();
        }

        private void PreviewContainer_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            if (PreviewViewbox.Visibility != Visibility.Visible) return;
            double factor = e.Delta > 0 ? 1.15 : 1.0 / 1.15;
            var cursor = e.GetPosition(PreviewViewbox);
            _viewport.ZoomAround(factor, cursor.X, cursor.Y);
            ApplyTransform();
            e.Handled = true;
        }

        private void PreviewContainer_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (PreviewViewbox.Visibility != Visibility.Visible) return;
            if (_scaleCalibMode) return;
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left && e.ClickCount == 2)
            {
                FitToView();
                return;
            }
            if (e.ChangedButton == System.Windows.Input.MouseButton.Right ||
                e.ChangedButton == System.Windows.Input.MouseButton.Middle)
            {
                _viewport.BeginPan(e.GetPosition(PreviewViewbox));
                PreviewViewbox.Cursor = System.Windows.Input.Cursors.SizeAll;
                ((System.Windows.IInputElement)sender).CaptureMouse();
                e.Handled = true;
            }
        }

        private void PreviewContainer_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (_scaleCalibMode) return;
            if (_viewport.UpdatePan(e.GetPosition(PreviewViewbox)))
                ApplyTransform();
        }

        private void PreviewContainer_MouseUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (_scaleCalibMode) return;
            if (!_viewport.IsPanning) return;
            _viewport.EndPan();
            PreviewViewbox.Cursor = System.Windows.Input.Cursors.Arrow;
            ((System.Windows.IInputElement)sender).ReleaseMouseCapture();
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            double cx = PreviewViewbox.ActualWidth  / 2.0;
            double cy = PreviewViewbox.ActualHeight / 2.0;
            _viewport.ZoomAround(1.3, cx, cy);
            ApplyTransform();
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            double cx = PreviewViewbox.ActualWidth  / 2.0;
            double cy = PreviewViewbox.ActualHeight / 2.0;
            _viewport.ZoomAround(1.0 / 1.3, cx, cy);
            ApplyTransform();
        }

        private void FitView_Click(object sender, RoutedEventArgs e) => FitToView();

        private void AiMode_Changed(object sender, RoutedEventArgs e)
        {
            if (AiPromptBox is null) return;
            AiPromptBox.Visibility = AiDescribeMode.IsChecked == true
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private async void AiAnalyse_Click(object sender, RoutedEventArgs e)
        {
            if (_extractedGeometry is null || _renderedBitmap is null) return;

            var allColors = _extractedGeometry.SlabColors
                .Concat(_extractedGeometry.LineColors)
                .Concat(_extractedGeometry.ColumnColors)
                .Distinct()
                .ToList();
            if (allColors.Count == 0) return;

            string? prompt = AiDescribeMode.IsChecked == true && !string.IsNullOrWhiteSpace(AiPromptBox.Text)
                ? AiPromptBox.Text.Trim()
                : null;

            AiAnalyseButton.IsEnabled = false;
            SetAiStatus("Sending image to Claude...", "#E8EAF6", "#3949AB");

            try
            {
                var result = await _aiService.AnalyseColorsAsync(_renderedBitmap, allColors, prompt);
                if (result is null)
                {
                    SetAiStatus("No response from Claude. Check API key or try again.", "#FFEBEE", "#C62828");
                    return;
                }

                // Update element type dropdowns based on AI classification
                foreach (var row in _slabPropsRows)
                {
                    string newType;
                    bool include;
                    if (result.SlabColors.Contains(row.Color))
                    { newType = "Slab"; include = true; }
                    else if (result.BeamColors.Contains(row.Color))
                    { newType = "Beam"; include = true; }
                    else if (result.ColumnColors.Contains(row.Color))
                    { newType = "Column"; include = true; }
                    else
                    { newType = row.DefaultElementType; include = false; }

                    row.IncludeCheckBox.Tag = newType;
                    row.IncludeCheckBox.IsChecked = include;
                    if (!include)
                        row.TypeComboBox.SelectedItem = "Ignore";
                }
                RebuildExcludedColors();
                DrawOverlay();

                SetAiStatus(result.Summary,
                    result.SlabColors.Count + result.BeamColors.Count + result.ColumnColors.Count > 0
                        ? "#E8F5E9" : "#FFF3E0",
                    result.SlabColors.Count + result.BeamColors.Count + result.ColumnColors.Count > 0
                        ? "#2E7D32" : "#E65100");
            }
            catch (Exception ex)
            {
                SetAiStatus($"Analysis failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                AiAnalyseButton.IsEnabled = _aiService.IsConfigured && _renderedBitmap is not null;
            }
        }

        private void SetAiStatus(string message, string backgroundHex, string foregroundHex)
        {
            AiStatusText.Text = message;
            AiStatusBadge.Background = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(backgroundHex));
            AiStatusText.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(foregroundHex));
            AiStatusBadge.Visibility = Visibility.Visible;
        }
    }
}
