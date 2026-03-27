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
    public partial class PdfToSafeWindow : Window
    {
        private string? _loadedFilePath;
        private ExtractedGeometry? _extractedGeometry;
        private bool _isPopulatingPageSelector;
        private readonly HashSet<int> _excludedSlabs   = new();
        private readonly HashSet<int> _excludedLines   = new();
        private readonly HashSet<int> _excludedColumns = new();

        public PdfToSafeWindow()
        {
            InitializeComponent();
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
            FileNameText.Text = Path.GetFileName(_loadedFilePath);
            _excludedSlabs.Clear();
            _excludedLines.Clear();
            _excludedColumns.Clear();

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

                // Populate page selector
                _isPopulatingPageSelector = true;
                PageSelector.Items.Clear();
                for (int i = 1; i <= _extractedGeometry.PageCount; i++)
                    PageSelector.Items.Add($"Page {i}");
                PageSelector.SelectedIndex = 0;
                PageSelectorPanel.Visibility = _extractedGeometry.PageCount > 1
                    ? Visibility.Visible : Visibility.Collapsed;
                _isPopulatingPageSelector = false;
                ReAnalyseButton.IsEnabled = true;

                PageCountText.Text = $"Pages: {_extractedGeometry.PageCount}";
                PathCountText.Text = $"Paths detected: {_extractedGeometry.RawPathCount}";
                PdfInfoPanel.Visibility = Visibility.Visible;
                ScalePanel.Visibility = Visibility.Visible;

                if (_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("Vector PDF detected — ready to export.", "#E8F5E9", "#2E7D32");
                    ExportDxfButton.IsEnabled = true;
                }
                else
                {
                    SetStatus("Raster or image-only PDF — not supported. Load a vector PDF exported from Revit or AutoCAD.", "#FFF3E0", "#E65100");
                    ExportDxfButton.IsEnabled = false;
                }

                // Render page 1 for preview
                await RenderPreviewAsync(_loadedFilePath);
            }
            catch (Exception ex)
            {
                SetStatus($"Failed to load PDF: {ex.Message}", "#FFEBEE", "#C62828");
                ExportDxfButton.IsEnabled = false;
            }
            finally
            {
                LoadPdfButton.IsEnabled = true;
            }
        }

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
                    DestinationWidth = 1800
                });

                stream.Seek(0);

                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream.AsStream();
                bitmap.EndInit();
                bitmap.Freeze();

                double aspectRatio = (double)bitmap.PixelHeight / bitmap.PixelWidth;
                double canvasH = 1800.0 * aspectRatio;

                PreviewCanvas.Width  = 1800;
                PreviewCanvas.Height = canvasH;
                PreviewImage.Width   = 1800;
                PreviewImage.Height  = canvasH;
                PreviewImage.Source  = bitmap;

                DrawOverlay();

                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                PreviewViewbox.Visibility     = Visibility.Visible;
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
            const double PtsToMm = 25.4 / 72.0;
            double mmToCanvas = (1.0 / (scale * PtsToMm)) * (canvasW / pageW);

            System.Windows.Point ToCanvas(double xMm, double yMm) => new(
                xMm * mmToCanvas,
                (pageH - yMm / (scale * PtsToMm)) * (canvasW / pageW));

            // Slab outlines — green (red if excluded)
            for (int i = 0; i < _extractedGeometry.Slabs.Count; i++)
            {
                var pts     = _extractedGeometry.Slabs[i];
                bool excl   = _excludedSlabs.Contains(i);
                var shape   = new System.Windows.Shapes.Polyline
                {
                    Stroke          = excl ? System.Windows.Media.Brushes.Red
                                           : System.Windows.Media.Brushes.LimeGreen,
                    StrokeThickness = 2,
                    Opacity         = excl ? 0.3 : 1.0,
                    Cursor          = System.Windows.Input.Cursors.Hand,
                    Tag             = Tuple.Create("slab", i),
                    Points          = new System.Windows.Media.PointCollection(
                        pts.Select(p => ToCanvas(p.X, p.Y)))
                };
                if (pts.Count > 0)
                    shape.Points.Add(ToCanvas(pts[0].X, pts[0].Y));
                shape.MouseDown += Shape_MouseDown;
                System.Windows.Controls.Canvas.SetZIndex(shape, 1);
                PreviewCanvas.Children.Add(shape);
            }

            // Linear elements — cyan (red if excluded)
            for (int i = 0; i < _extractedGeometry.Lines.Count; i++)
            {
                var pts   = _extractedGeometry.Lines[i];
                bool excl = _excludedLines.Contains(i);
                var shape = new System.Windows.Shapes.Polyline
                {
                    Stroke          = excl ? System.Windows.Media.Brushes.Red
                                           : System.Windows.Media.Brushes.Cyan,
                    StrokeThickness = 1.5,
                    Opacity         = excl ? 0.3 : 1.0,
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
                bool excl  = _excludedColumns.Contains(i);
                var pt     = ToCanvas(x, y);
                var dot    = new System.Windows.Shapes.Ellipse
                {
                    Width           = 10,
                    Height          = 10,
                    Fill            = excl ? System.Windows.Media.Brushes.Red
                                           : System.Windows.Media.Brushes.Yellow,
                    Stroke          = excl ? System.Windows.Media.Brushes.DarkRed
                                           : System.Windows.Media.Brushes.DarkGoldenrod,
                    StrokeThickness = 1,
                    Opacity         = excl ? 0.3 : 1.0,
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
            bool hasExclusions = _excludedSlabs.Count > 0
                              || _excludedLines.Count > 0
                              || _excludedColumns.Count > 0;
            ClearExclusionsButton.Visibility = hasExclusions
                ? Visibility.Visible : Visibility.Collapsed;
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
            _excludedSlabs.Clear();
            _excludedLines.Clear();
            _excludedColumns.Clear();
            SetStatus("Analysing...", "#E8EAF6", "#3949AB");

            try
            {
                var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                _extractedGeometry = await Task.Run(() =>
                    PdfGeometryExtractor.Extract(_loadedFilePath, scale, pageNumber,
                        slabMin, lineMin, excludeGrids));

                UpdateDetectionSummary(_extractedGeometry);
                PageCountText.Text = $"Pages: {_extractedGeometry.PageCount}";
                PathCountText.Text = $"Paths detected: {_extractedGeometry.RawPathCount}";

                if (_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("Vector PDF detected — ready to export.", "#E8F5E9", "#2E7D32");
                    ExportDxfButton.IsEnabled = true;
                }
                else
                {
                    SetStatus("Raster or image-only PDF — not supported. Load a vector PDF exported from Revit or AutoCAD.", "#FFF3E0", "#E65100");
                    ExportDxfButton.IsEnabled = false;
                }

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
            _excludedSlabs.Clear();
            _excludedLines.Clear();
            _excludedColumns.Clear();
            SetStatus("Analysing...", "#E8EAF6", "#3949AB");

            try
            {
                var (slabMin, lineMin, excludeGrids) = ReadThresholds();
                _extractedGeometry = await Task.Run(() =>
                    PdfGeometryExtractor.Extract(_loadedFilePath, scale, pageNumber,
                        slabMin, lineMin, excludeGrids));

                UpdateDetectionSummary(_extractedGeometry);
                PageCountText.Text = $"Pages: {_extractedGeometry.PageCount}";
                PathCountText.Text = $"Paths detected: {_extractedGeometry.RawPathCount}";

                if (_extractedGeometry.IsVectorPdf)
                {
                    SetStatus("Vector PDF detected — ready to export.", "#E8F5E9", "#2E7D32");
                    ExportDxfButton.IsEnabled = true;
                }
                else
                {
                    SetStatus("Raster or image-only PDF — not supported. Load a vector PDF exported from Revit or AutoCAD.", "#FFF3E0", "#E65100");
                    ExportDxfButton.IsEnabled = false;
                }

                await RenderPreviewAsync(_loadedFilePath, pageIndex);
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

            ExportDxfButton.IsEnabled = false;
            SetStatus("Extracting geometry...", "#E8EAF6", "#3949AB");

            try
            {
                bool hasExclusions = _excludedSlabs.Count > 0
                                  || _excludedLines.Count > 0
                                  || _excludedColumns.Count > 0;

                ExtractedGeometry geometry;
                if (hasExclusions)
                {
                    // Use the currently displayed geometry so exclusion indices remain valid
                    geometry = _extractedGeometry;
                    if (geometry is null) return;
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
                        _excludedSlabs, _excludedLines, _excludedColumns));

                _extractedGeometry = geometry;
                UpdateDetectionSummary(_extractedGeometry);
                DrawOverlay();

                int exportedSlabs   = geometry.Slabs.Count   - _excludedSlabs.Count;
                int exportedCols    = geometry.Columns.Count - _excludedColumns.Count;
                int exportedLines   = geometry.Lines.Count   - _excludedLines.Count;
                ExportResultsText.Text =
                    $"Exported: {exportedSlabs} slab outline(s), " +
                    $"{exportedCols} column(s), " +
                    $"{exportedLines} line element(s).";
                ExportResultsText.Visibility = Visibility.Visible;

                SetStatus("DXF exported successfully.", "#E8F5E9", "#2E7D32");
            }
            catch (Exception ex)
            {
                SetStatus($"Export failed: {ex.Message}", "#FFEBEE", "#C62828");
            }
            finally
            {
                ExportDxfButton.IsEnabled = true;
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
                ? l : 200.0;
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
            if (sender is FrameworkElement fe && fe.Tag is Tuple<string, int> tag)
            {
                var set = tag.Item1 switch
                {
                    "slab"   => _excludedSlabs,
                    "line"   => _excludedLines,
                    _        => _excludedColumns
                };
                if (!set.Remove(tag.Item2)) set.Add(tag.Item2);
                DrawOverlay();
                e.Handled = true;
            }
        }

        private void ClearExclusions_Click(object sender, RoutedEventArgs e)
        {
            _excludedSlabs.Clear();
            _excludedLines.Clear();
            _excludedColumns.Clear();
            DrawOverlay();
        }
    }
}
