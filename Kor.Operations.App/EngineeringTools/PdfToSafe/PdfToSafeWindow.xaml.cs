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

                if (_isVectorPdf)
                {
                    SetStatus("Vector PDF — geometry extraction supported.", "#E8F5E9", "#2E7D32");
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

                PreviewImage.Source = bitmap;
                PreviewPlaceholder.Visibility = Visibility.Collapsed;
                PreviewScroller.Visibility = Visibility.Visible;
            }
            catch
            {
                // Preview is non-critical — leave placeholder visible if rendering fails
            }
        }

        private void ExportDxf_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "DXF export coming soon.",
                "PDF → SAFE",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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
