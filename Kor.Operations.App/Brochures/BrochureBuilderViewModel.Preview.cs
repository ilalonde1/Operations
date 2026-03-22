#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Brochures.SubVms;
using Kor.Operations.Rendering.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Serilog;

namespace Kor.Operations.Brochures
{
    public sealed partial class BrochureBuilderViewModel
    {
        // ── Preview state ─────────────────────────────────────────────────────
        private BitmapSource? _visualStylePreviewImage;
        private BitmapSource? _layoutPreviewImage;
        private CancellationTokenSource? _setupPreviewCts;
        private bool _suppressSetupPreviewRefresh;

        // ── Preview properties ────────────────────────────────────────────────
        public BitmapSource? VisualStylePreviewImage
        {
            get => _visualStylePreviewImage;
            private set => SetField(ref _visualStylePreviewImage, value);
        }

        public BitmapSource? LayoutPreviewImage
        {
            get => _layoutPreviewImage;
            private set => SetField(ref _layoutPreviewImage, value);
        }

        public string SelectedSkinDisplayName
        {
            get => BrochureSkinRegistry.All
                       .FirstOrDefault(s => s.Id == Cover.SkinId)?.DisplayName
                   ?? (SkinOptions.Count > 0 ? SkinOptions[0] : string.Empty);
            set
            {
                var skin = BrochureSkinRegistry.All.FirstOrDefault(s => s.DisplayName == value);
                Cover.SkinId = skin?.Id ?? "corporate-profile";
                Cover.TemplateName = value;
                OnPropertyChanged();
            }
        }

        public string SelectedLayoutDisplayName
        {
            get => BrochureLayoutTemplateCatalog.Default.All
                       .FirstOrDefault(t => t.Id == Cover.LayoutTemplateId)?.DisplayName
                   ?? (LayoutOptions.Count > 0 ? LayoutOptions[0] : string.Empty);
            set
            {
                Cover.LayoutTemplateId = ResolveLayoutTemplateId(value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(EstimatedPageCount));
            }
        }

        public int EstimatedPageCount
        {
            get
            {
                var layout = BrochureLayoutTemplateCatalog.Default.Resolve(Cover.LayoutTemplateId);
                return Math.Max(1, layout.EstimatePageCount(BuildBrochureContent()));
            }
        }

        // ── Commands ──────────────────────────────────────────────────────────
        public ICommand ProduceBrochureCommand { get; private set; } = null!;
        public ICommand ExportDocxCommand { get; private set; } = null!;
        public ICommand AnalyzeBrochureCommand { get; private set; } = null!;
        public ICommand PickPrimaryColorCommand { get; private set; } = null!;
        public ICommand PickAccentColorCommand { get; private set; } = null!;
        public ICommand PickLogoCommand { get; private set; } = null!;
        public ICommand PickCoverLogoCommand { get; private set; } = null!;
        public ICommand ClearLogoCommand { get; private set; } = null!;
        public ICommand PickCoverPhotoCommand { get; private set; } = null!;
        public ICommand ClearCoverPhotoCommand { get; private set; } = null!;

        private void InitPreviewCommands()
        {
            ProduceBrochureCommand = new AsyncRelayCommand(ExecProduceBrochureAsync);
            ExportDocxCommand = new AsyncRelayCommand(ExecExportDocxAsync);
            AnalyzeBrochureCommand = new AsyncRelayCommand(ExecAnalyzeBrochureAsync);
            PickPrimaryColorCommand = new RelayCommand(_ => ExecPickColor(
                Cover.PrimaryColorOverride,
                hex =>
                {
                    Cover.PrimaryColorOverride = hex;
                    QueueSetupPreviewRefresh();
                }));
            PickAccentColorCommand = new RelayCommand(_ => ExecPickColor(
                Cover.AccentColorOverride,
                hex =>
                {
                    Cover.AccentColorOverride = hex;
                    QueueSetupPreviewRefresh();
                }));
            PickLogoCommand = new RelayCommand(_ => ExecPickLogo());
            PickCoverLogoCommand = new RelayCommand(_ => ExecPickCoverLogo());
            ClearLogoCommand = new RelayCommand(_ =>
            {
                _contactConfig.LogoPath = string.Empty;
                _contactConfig.CoverLogoPath = string.Empty;
                _contactStore.Save(_contactConfig);
                OnPropertyChanged(nameof(ContactConfig));
                QueueSetupPreviewRefresh();
            });
            PickCoverPhotoCommand = new AsyncRelayCommand(ExecPickCoverPhotoAsync);
            ClearCoverPhotoCommand = new RelayCommand(_ =>
            {
                Cover.CoverPhotoPath = string.Empty;
                Cover.CoverPhotoBytes = Array.Empty<byte>();
            });
        }

        private async Task ExecAnalyzeBrochureAsync(object? _)
        {
            if (string.IsNullOrWhiteSpace(
                System.Configuration.ConfigurationManager.AppSettings["AnthropicApiKey"]))
            {
                MessageBox.Show(
                    "No Anthropic API key configured. Add AnthropicApiKey to App.config to use this feature.",
                    "API Key Required",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select a brochure PDF to analyze",
                Filter = "PDF Files (*.pdf)|*.pdf"
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                IsGenerating = true;
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var result = await _analysisService.AnalyzeAsync(
                    dialog.FileName, SkinOptions, LayoutOptions, cts.Token);
                if (result is null)
                {
                    MessageBox.Show(
                        "Brochure analysis could not be completed. Check the log for details.",
                        "Analysis Unavailable",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (!string.IsNullOrWhiteSpace(result.PrimaryColor))
                    Cover.PrimaryColorOverride = result.PrimaryColor;

                if (!string.IsNullOrWhiteSpace(result.AccentColor))
                    Cover.AccentColorOverride = result.AccentColor;

                if (!string.IsNullOrWhiteSpace(result.SkinDisplayName) &&
                    SkinOptions.Contains(result.SkinDisplayName))
                {
                    SelectedSkinDisplayName = result.SkinDisplayName;
                }

                if (!string.IsNullOrWhiteSpace(result.LayoutDisplayName) &&
                    LayoutOptions.Contains(result.LayoutDisplayName))
                {
                    SelectedLayoutDisplayName = result.LayoutDisplayName;
                }

                QueueSetupPreviewRefresh();

                MessageBox.Show(
                    "Analysis complete. Suggested style and colors have been applied  review and adjust as needed.",
                    "Brochure Analyzed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brochure analysis failed");
                MessageBox.Show(
                    "Failed to analyze brochure. Check that the API key is valid and the file is a readable PDF.",
                    "Analysis Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private async Task ExecProduceBrochureAsync(object? _)
        {
            var validationError = ValidateBrochureForExport();
            if (validationError != null)
            {
                MessageBox.Show(
                    validationError,
                    "Cannot Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var brochureTitle = !string.IsNullOrWhiteSpace(Cover.CoverTitle)
                ? Cover.CoverTitle
                : !string.IsNullOrWhiteSpace(ProposalName) ? ProposalName : "Brochure";

            var saveDialog = new SaveFileDialog
            {
                Title = "Save Brochure As",
                Filter = "PDF Files (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                FileName = SanitizeFileName(brochureTitle) + ".pdf"
            };
            if (saveDialog.ShowDialog() != true) return;

            var outputPath = saveDialog.FileName;

            try
            {
                IsGenerating = true;
                var content = BuildBrochureContent();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var (pdfPath, previewPages) = await _renderer.RenderWithPreviewAsync(
                    content, outputPath, 280, cts.Token);

                Process.Start(new ProcessStartInfo { FileName = pdfPath, UseShellExecute = true });

                try
                {
                    PreviewPages.Clear();
                    foreach (var pageBytes in previewPages)
                        PreviewPages.Add(CreateBitmap(pageBytes));
                    OnPropertyChanged(nameof(HasPreview));
                    OnPropertyChanged(nameof(IsPreviewEmpty));
                }
                catch (Exception previewEx)
                {
                    _logger.LogError(previewEx, "Brochure preview generation failed");
                }

                MessageBox.Show(
                    "Brochure generated successfully.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brochure generation failed");
                MessageBox.Show(
                    "Failed to generate brochure. Check the log for details.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private async Task ExecExportDocxAsync(object? _)
        {
            var validationError = ValidateBrochureForExport();
            if (validationError != null)
            {
                MessageBox.Show(
                    validationError,
                    "Cannot Export",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var brochureTitle = !string.IsNullOrWhiteSpace(Cover.CoverTitle)
                ? Cover.CoverTitle
                : !string.IsNullOrWhiteSpace(ProposalName) ? ProposalName : "Brochure";

            var saveDialog = new SaveFileDialog
            {
                Title = "Export Brochure to Word",
                Filter = "Word Documents (*.docx)|*.docx",
                DefaultExt = "docx",
                FileName = SanitizeFileName(brochureTitle) + ".docx"
            };
            if (saveDialog.ShowDialog() != true) return;

            var outputPath = saveDialog.FileName;

            try
            {
                IsGenerating = true;
                var content = BuildBrochureContent();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var docxPath = await _docxRenderer.RenderAsync(content, outputPath, cts.Token);

                Process.Start(new ProcessStartInfo { FileName = docxPath, UseShellExecute = true });

                MessageBox.Show(
                    "Word document exported successfully.",
                    "Success",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brochure Word export failed");
                MessageBox.Show(
                    "Failed to export Word document. Check the log for details.",
                    "Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                IsGenerating = false;
            }
        }

        private string? ValidateBrochureForExport()
        {
            if (Blocks.Count == 0)
                return "Add at least one section or personnel block";

            if (Blocks.Any(static b => b.BlockType == BrochureBlockType.Section && (b.Section?.Projects.Count ?? 0) == 0))
                return "All sections must have at least one project";

            return null;
        }

        private async Task ExecPickCoverPhotoAsync(object? _)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Multiselect = false
            };
            if (dialog.ShowDialog() == true)
            {
                Cover.CoverPhotoPath = dialog.FileName;
                Cover.CoverPhotoBytes = await File.ReadAllBytesAsync(dialog.FileName);
            }
        }

        private void ExecPickLogo()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                _contactConfig.LogoPath = dialog.FileName;
                _contactStore.Save(_contactConfig);
                OnPropertyChanged(nameof(ContactConfig));
                QueueSetupPreviewRefresh();
            }
        }

        private void ExecPickCoverLogo()
        {
            var dialog = new OpenFileDialog
            {
                Filter = "Image Files (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png",
                Multiselect = false
            };

            if (dialog.ShowDialog() == true)
            {
                _contactConfig.CoverLogoPath = dialog.FileName;
                _contactStore.Save(_contactConfig);
                OnPropertyChanged(nameof(ContactConfig));
                QueueSetupPreviewRefresh();
            }
        }

        private static void ExecPickColor(string currentHex, Action<string> onPicked)
        {
            var dialog = new System.Windows.Forms.ColorDialog
            {
                FullOpen = true,
                AllowFullOpen = true
            };

            if (!string.IsNullOrWhiteSpace(currentHex))
            {
                try
                {
                    var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(currentHex);
                    dialog.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
                }
                catch (Exception ex)
                {
                    Log.ForContext(typeof(BrochureBuilderViewModel))
                        .Warning(ex, "Failed to parse color hex '{Hex}' for color picker; using default color.", currentHex);
                }
            }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                var c = dialog.Color;
                onPicked($"#{c.R:X2}{c.G:X2}{c.B:X2}");
            }
        }

        // ── Cover change → preview refresh ───────────────────────────────────
        private void Cover_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_suppressSetupPreviewRefresh) return;

            if (e.PropertyName is nameof(BrochureCoverVm.SkinId) or
                nameof(BrochureCoverVm.LayoutTemplateId) or
                nameof(BrochureCoverVm.TemplateName) or
                nameof(BrochureCoverVm.CoverPhotoPath) or
                nameof(BrochureCoverVm.CoverPhotoBytes) or
                nameof(BrochureCoverVm.CoverPhotoOpacity) or
                nameof(BrochureCoverVm.PrimaryColorOverride) or
                nameof(BrochureCoverVm.AccentColorOverride))
            {
                SetDirty();
                QueueSetupPreviewRefresh();
            }
        }

        private void QueueSetupPreviewRefresh()
        {
            _setupPreviewCts?.Cancel();
            _setupPreviewCts?.Dispose();
            _setupPreviewCts = new CancellationTokenSource();
            _ = RefreshSetupPreviewsAsync(_setupPreviewCts.Token);
        }

        private async Task RefreshSetupPreviewsAsync(CancellationToken ct)
        {
            try
            {
                await Task.Delay(150, ct).ConfigureAwait(true);
                var content = BuildSetupPreviewContent();
                var previewPages = await _renderer.RenderPreviewAsync(content, 360, ct).ConfigureAwait(true);
                ct.ThrowIfCancellationRequested();

                VisualStylePreviewImage = previewPages.Count > 0 ? CreateBitmap(previewPages[0]) : null;
                LayoutPreviewImage = previewPages.Count > 1
                    ? CreateBitmap(previewPages[1])
                    : VisualStylePreviewImage;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to refresh brochure setup previews.");
                VisualStylePreviewImage = null;
                LayoutPreviewImage = null;
            }
        }

        private BrochureContent BuildSetupPreviewContent() => new()
        {
            TemplateName = string.IsNullOrWhiteSpace(Cover.TemplateName) ? SelectedSkinDisplayName : Cover.TemplateName,
            SkinId = string.IsNullOrWhiteSpace(Cover.SkinId) ? "corporate-profile" : Cover.SkinId,
            LayoutTemplateId = string.IsNullOrWhiteSpace(Cover.LayoutTemplateId) ? "standard-portfolio" : Cover.LayoutTemplateId,
            CoverTitle = string.IsNullOrWhiteSpace(Cover.CoverTitle) ? SelectedSkinDisplayName : Cover.CoverTitle,
            CoverPhotoPath = Cover.CoverPhotoPath,
            CoverPhotoBytes = Cover.CoverPhotoBytes,
            CoverPhotoOpacity = Cover.CoverPhotoOpacity,
            PrimaryColorOverride = Cover.PrimaryColorOverride,
            AccentColorOverride = Cover.AccentColorOverride,
            ContactConfig = _contactConfig,
            Blocks =
            {
                new BrochureBlock
                {
                    BlockType = BrochureBlockType.Section,
                    Section = new BrochureSection
                    {
                        Heading = "Featured Projects",
                        Blurb = "Representative brochure preview content for the selected style and layout.",
                        Projects =
                        {
                            new BrochureProject
                            {
                                ProjectName = "Harbour Centre Redevelopment",
                                ProjectDescription = "A mixed-use adaptive reuse project combining tower strengthening, podium renewal, and phased tenant fit-out delivery.",
                                Client = "Harbour Development Group",
                                Architect = "North Studio"
                            },
                            new BrochureProject
                            {
                                ProjectName = "Granite Point Residences",
                                ProjectDescription = "A residential concrete structure with stepped massing, transfer slabs, and an emphasis on efficient coordination with the design team.",
                                Client = "Granite Point Homes",
                                Architect = "Openform Architecture"
                            }
                        }
                    }
                }
            }
        };

        private static BitmapSource CreateBitmap(byte[] pageBytes)
        {
            using var stream = new MemoryStream(pageBytes);
            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.StreamSource = stream;
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.EndInit();
            bitmapImage.Freeze();
            return bitmapImage;
        }
    }
}
