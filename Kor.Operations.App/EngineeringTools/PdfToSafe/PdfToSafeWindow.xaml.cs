#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using UglyToad.PdfPig;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Kor.Operations.EngineeringTools.PdfToSafe
{
    public partial class PdfToSafeWindow : Window
    {
        private string? _loadedFilePath;
        private bool _isVectorPdf;
        private ExtractedGeometry? _extractedGeometry;

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

            LoadPdfButton.IsEnabled = false;
            SetStatus("Analysing...", "#E8EAF6", "#3949AB");

            try
            {
                // Detect vector vs raster using PdfPig
                int pageCount;
                int pathCount;

                using (var doc = PdfDocument.Open(_loadedFilePath))
                {
                    pageCount = doc.NumberOfPages;
                    var page = doc.GetPage(1);
                    pathCount = page.ExperimentalAccess.Paths.Count;
                }

                _isVectorPdf = pathCount > 20;

                PageCountText.Text = $"Pages: {pageCount}";
                PathCountText.Text = $"Paths on page 1: {pathCount}";
                PdfInfoPanel.Visibility = Visibility.Visible;
                ScalePanel.Visibility = Visibility.Visible;

                if (_isVectorPdf)
                {
                    if (!int.TryParse(ScaleInput.Text.Trim(), out int previewScale) || previewScale <= 0)
                        previewScale = 100;

                    _extractedGeometry = PdfGeometryExtractor.Extract(_loadedFilePath, previewScale);

                    SetStatus(
                        $"Vector PDF — {_extractedGeometry.Slabs.Count} slab(s), " +
                        $"{_extractedGeometry.Columns.Count} column(s), " +
                        $"{_extractedGeometry.Lines.Count} line(s) detected.",
                        "#E8F5E9", "#2E7D32");
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

        private async Task RenderPreviewAsync(string filePath)
        {
            try
            {
                var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
                var pdfDoc = await PdfDocument.LoadFromFileAsync(storageFile);

                using var page = pdfDoc.GetPage(0);
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
            double pageW     = _extractedGeometry.PageWidthPts;
            double pageH     = _extractedGeometry.PageHeightPts;
            int    scale     = _extractedGeometry.ScaleDenominator;
            const double PtsToMm = 25.4 / 72.0;
            double mmToCanvas = (1.0 / (scale * PtsToMm)) * (canvasW / pageW);

            System.Windows.Point ToCanvas(double xMm, double yMm) => new(
                xMm * mmToCanvas,
                (pageH - yMm / (scale * PtsToMm)) * (canvasW / pageW));

            // Slab outlines — green
            foreach (var pts in _extractedGeometry.Slabs)
            {
                var shape = new System.Windows.Shapes.Polyline
                {
                    Stroke          = System.Windows.Media.Brushes.LimeGreen,
                    StrokeThickness = 2,
                    Points          = new System.Windows.Media.PointCollection(
                        pts.Select(p => ToCanvas(p.X, p.Y)))
                };
                // Close visually
                if (pts.Count > 0)
                    shape.Points.Add(ToCanvas(pts[0].X, pts[0].Y));
                System.Windows.Controls.Canvas.SetZIndex(shape, 1);
                PreviewCanvas.Children.Add(shape);
            }

            // Linear elements — cyan
            foreach (var pts in _extractedGeometry.Lines)
            {
                var shape = new System.Windows.Shapes.Polyline
                {
                    Stroke          = System.Windows.Media.Brushes.Cyan,
                    StrokeThickness = 1.5,
                    Points          = new System.Windows.Media.PointCollection(
                        pts.Select(p => ToCanvas(p.X, p.Y)))
                };
                System.Windows.Controls.Canvas.SetZIndex(shape, 1);
                PreviewCanvas.Children.Add(shape);
            }

            // Columns — yellow dot
            foreach (var (x, y) in _extractedGeometry.Columns)
            {
                var pt = ToCanvas(x, y);
                var dot = new System.Windows.Shapes.Ellipse
                {
                    Width           = 10,
                    Height          = 10,
                    Fill            = System.Windows.Media.Brushes.Yellow,
                    Stroke          = System.Windows.Media.Brushes.DarkGoldenrod,
                    StrokeThickness = 1
                };
                System.Windows.Controls.Canvas.SetLeft(dot, pt.X - 5);
                System.Windows.Controls.Canvas.SetTop(dot,  pt.Y - 5);
                System.Windows.Controls.Canvas.SetZIndex(dot, 2);
                PreviewCanvas.Children.Add(dot);
            }
        }

        private void ExportDxf_Click(object sender, RoutedEventArgs e)
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
                var geometry = PdfGeometryExtractor.Extract(_loadedFilePath, scale);
                PdfGeometryExtractor.ExportDxf(geometry, saveDialog.FileName);

                ExportResultsText.Text =
                    $"Exported: {geometry.Slabs.Count} slab outline(s), " +
                    $"{geometry.Columns.Count} column(s), " +
                    $"{geometry.Lines.Count} line element(s).";
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
    }
}
