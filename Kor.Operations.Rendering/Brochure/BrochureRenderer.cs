#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure.Layouts;
using Kor.Operations.Rendering.Brochure.Skins;
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kor.Operations.Rendering.Brochure
{
    public sealed class BrochureRenderer : IBrochureRenderer
    {
        private readonly ILogger<BrochureRenderer> _logger;

        static BrochureRenderer()
        {
            FontManager.RegisterFontFromEmbeddedResource(
                "Kor.Operations.Rendering.Fonts.Mulish.Mulish-Regular.ttf");
            FontManager.RegisterFontFromEmbeddedResource(
                "Kor.Operations.Rendering.Fonts.Mulish.Mulish-Bold.ttf");
            FontManager.RegisterFontFromEmbeddedResource(
                "Kor.Operations.Rendering.Fonts.Mulish.Mulish-Italic.ttf");
            FontManager.RegisterFontFromEmbeddedResource(
                "Kor.Operations.Rendering.Fonts.Mulish.Mulish-BoldItalic.ttf");
            FontManager.RegisterFontFromEmbeddedResource(
                "Kor.Operations.Rendering.Fonts.Mulish.Mulish-Black.ttf");
        }

        public BrochureRenderer(ILogger<BrochureRenderer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<string> RenderAsync(BrochureContent content, string outputPath, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                PrepareContent(content);
                var (logoBytes, coverLogoBytes, coverPhotoBytes) = ResolveDocumentAssets(content);

                try
                {
                    var outputDirectory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(outputDirectory))
                        Directory.CreateDirectory(outputDirectory);

                    _logger.LogInformation(
                        "Starting brochure render to {OutputPath} with {BlockCount} block(s).",
                        outputPath,
                        content.Blocks.Count);

                    if (coverPhotoBytes is { Length: > 0 })
                    {
                        _logger.LogInformation(
                            "Rendering cover photo strip using {CoverPhotoPath}.",
                            ResolvePath(content.CoverPhotoPath));
                        _logger.LogInformation(
                            "Cover photo strip fix applied: using fixed-height container, primary image layer with FitArea, and secondary transparent overlay.");
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Rendering cover photo fallback strip. Resolved cover path was {CoverPhotoPath}.",
                            ResolvePath(content.CoverPhotoPath));
                    }

                    var document = CreateDocument(content, logoBytes, coverLogoBytes, coverPhotoBytes, ct);

                    document.GeneratePdf(outputPath);

                    _logger.LogInformation(
                        "Completed brochure render to {OutputPath}.",
                        outputPath);

                    return outputPath;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Brochure render failed to {OutputPath}.",
                        outputPath);
                    throw;
                }
            }, ct);
        }

        public async Task<(string PdfPath, IReadOnlyList<byte[]> PreviewPages)> RenderWithPreviewAsync(
            BrochureContent content,
            string outputPath,
            int previewWidthPixels,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            try
            {
                PrepareContent(content);
                var assets = ResolveDocumentAssets(content);
                var document = CreateDocument(content, assets.LogoBytes, assets.CoverLogoBytes, assets.CoverPhotoBytes, ct);
                var rasterDpi = Math.Max(36, (int)Math.Ceiling(previewWidthPixels / 8.5d));

                var outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                _logger.LogInformation(
                    "Starting brochure render with preview to {OutputPath}.",
                    outputPath);

                await Task.Run(() => document.GeneratePdf(outputPath), ct)
                    .ConfigureAwait(false);

                var pages = await Task.Run(
                        () => document.GenerateImages(new ImageGenerationSettings
                        {
                            RasterDpi = rasterDpi
                        }).ToList(),
                        ct)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Completed brochure render with preview to {OutputPath}.",
                    outputPath);

                return (outputPath, pages);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Brochure render with preview failed for {OutputPath}.",
                    outputPath);
                throw;
            }
        }

        public async Task<IReadOnlyList<byte[]>> RenderPreviewAsync(
            BrochureContent content,
            int maxWidthPixels,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(content);

            if (maxWidthPixels <= 0)
                return Array.Empty<byte[]>();

            try
            {
                PrepareContent(content);
                var (logoBytes, coverLogoBytes, coverPhotoBytes) = ResolveDocumentAssets(content);
                var document = CreateDocument(content, logoBytes, coverLogoBytes, coverPhotoBytes, ct);
                var rasterDpi = Math.Max(36, (int)Math.Ceiling(maxWidthPixels / 8.5d));

                var imageBytes = await Task.Run(
                    () => document.GenerateImages(new ImageGenerationSettings
                    {
                        RasterDpi = rasterDpi
                    }).ToList(),
                    ct).ConfigureAwait(false);

                return imageBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "BrochureRenderer preview generation failed");
                throw;
            }
        }

        private void PrepareContent(BrochureContent content)
        {
            content.CompanyName = "KOR Structural";
            content.LogoPath = @"Resources\kor-logo.png";
        }

        private (byte[]? LogoBytes, byte[]? CoverLogoBytes, byte[]? CoverPhotoBytes) ResolveDocumentAssets(BrochureContent content)
        {
            var resolvedLogoPath = ResolvePath(content.LogoPath);
            var logoBytes = TryReadImageBytes(resolvedLogoPath, "brochure logo");
            var resolvedCoverPhotoPath = ResolvePath(content.CoverPhotoPath);
            _logger.LogDebug("Resolved cover photo path to {CoverPhotoPath}", resolvedCoverPhotoPath);
            var coverPhotoBytes = TryReadImageBytes(resolvedCoverPhotoPath, "cover photo");
            var whiteLogoPath = ResolvePath(@"Resources\kor-logo-white.png");
            var coverLogoBytes = File.Exists(whiteLogoPath)
                ? TryReadImageBytes(whiteLogoPath, "cover logo") ?? logoBytes
                : logoBytes;

            return (logoBytes, coverLogoBytes, coverPhotoBytes);
        }

        private IDocument CreateDocument(
            BrochureContent content,
            byte[]? logoBytes,
            byte[]? coverLogoBytes,
            byte[]? coverPhotoBytes,
            CancellationToken ct)
        {
            return Document.Create(container => ComposeDocument(container, content, logoBytes, coverLogoBytes, coverPhotoBytes, ct));
        }

        private void ComposeDocument(
            IDocumentContainer container,
            BrochureContent content,
            byte[]? logoBytes,
            byte[]? coverLogoBytes,
            byte[]? coverPhotoBytes,
            CancellationToken ct)
        {
            var skin = BrochureSkinRegistry.Resolve(content.SkinId, content.TemplateName);
            var layout = BrochureLayoutTemplateCatalog.Default.Resolve(content.LayoutTemplateId);
            var ctx = new BrochureRenderContext
            {
                Content = content,
                Skin = skin,
                LogoBytes = logoBytes,
                CoverLogoBytes = coverLogoBytes,
                CoverPhotoBytes = coverPhotoBytes,
                ReadImage = (path, label) => TryReadImageBytes(path, label ?? string.Empty),
                ResolvePath = ResolvePath,
                CancellationToken = ct
            };

            container.Page(page =>
            {
                page.Size(PageSizes.Letter);
                page.PageColor(skin.PrimaryColor);
                page.Margin(0);
                page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish"));
                page.Content().Element(body => layout.ComposeCoverPage(body, ctx));
            });

            if (content.Blocks.Count == 0)
            {
                container.Page(page =>
                {
                    BrochureRenderHelpers.ConfigureStandardPage(page);

                    page.Header().PaddingHorizontal(-1, Unit.Inch)
                        .Element(header => header
                            .Height(1.06f, Unit.Inch)
                            .Background(skin.PrimaryColor)
                            .Row(row =>
                            {
                                row.ConstantItem(3f, Unit.Inch)
                                    .PaddingLeft(0.25f, Unit.Inch)
                                    .AlignMiddle()
                                    .Text(skin.HeaderText)
                                    .FontFamily("Mulish")
                                    .FontSize(9)
                                    .FontColor(Colors.White);

                                row.RelativeItem();

                                if (logoBytes is null)
                                    return;

                                row.ConstantItem(2.62f, Unit.Inch)
                                    .PaddingRight(0.25f, Unit.Inch)
                                    .AlignMiddle()
                                    .AlignRight()
                                    .Image(logoBytes)
                                    .FitArea();
                            }));

                    page.Content().PaddingTop(18).Element(body =>
                        body.AlignMiddle().AlignCenter().Text("No content added")
                            .FontFamily("Mulish")
                            .FontSize(12)
                            .FontColor(skin.PrimaryColor));

                    page.Footer().PaddingHorizontal(-1, Unit.Inch)
                        .MinHeight(0.35f, Unit.Inch)
                        .AlignBottom()
                        .Element(footer =>
                            footer.PaddingHorizontal(0.25f, Unit.Inch).Row(row =>
                            {
                                row.RelativeItem().Text(text =>
                                {
                                    var companyName = string.IsNullOrWhiteSpace(content.CompanyName)
                                        ? string.Empty
                                        : content.CompanyName.Trim() + " ";

                                    text.DefaultTextStyle(BrochureRenderHelpers.GetBodyTextStyle(8, skin.PrimaryColor));
                                    text.Span(companyName + "501 - 510 Burrard Street, Vancouver, BC V6C 3A8").FontColor(skin.PrimaryColor);
                                });

                                row.ConstantItem(100).AlignRight().Text(text =>
                                {
                                    text.DefaultTextStyle(BrochureRenderHelpers.GetBodyTextStyle(8, skin.PrimaryColor));
                                    text.Span("Page ").FontColor(skin.PrimaryColor);
                                    text.CurrentPageNumber().FontColor(skin.PrimaryColor);
                                    text.Span(" of ").FontColor(skin.PrimaryColor);
                                    text.TotalPages().FontColor(skin.PrimaryColor);
                                });
                            }));
                });

                return;
            }

            foreach (var block in content.Blocks)
            {
                ct.ThrowIfCancellationRequested();

                switch (block.BlockType)
                {
                    case BrochureBlockType.Section:
                        if (block.Section is not null)
                            layout.ComposeSection(container, block.Section, ctx);
                        break;
                    case BrochureBlockType.Personnel:
                        layout.ComposePersonnel(container, block, ctx);
                        break;
                    case BrochureBlockType.CompanyOverview:
                        layout.ComposeOverview(container, block.OverviewSections, block.PageBreakAfterOverviewIndex, ctx);
                        break;
                    case BrochureBlockType.Contact:
                        layout.ComposeContact(container, ctx);
                        break;
                    case BrochureBlockType.PageBreak:
                        break;
                }
            }
        }

        private string ResolvePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            return Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
        }

        private byte[]? TryReadImageBytes(string? path, string imageLabel)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            _logger.LogDebug("Attempting to load {ImageLabel} from {ImagePath}", imageLabel, path);

            if (!File.Exists(path))
            {
                _logger.LogWarning("{ImageLabel} file not found at {ImagePath}", imageLabel, path);
                return null;
            }

            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to load {ImageLabel} at {ImagePath}: {ErrorMessage}",
                    imageLabel,
                    path,
                    ex.Message);
                return null;
            }
        }

    }
}
