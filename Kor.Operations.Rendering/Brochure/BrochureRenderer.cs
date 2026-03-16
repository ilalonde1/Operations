#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Brochure;
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kor.Operations.Rendering.Brochure
{
    public sealed class BrochureRenderer : IBrochureRenderer
    {
        private const string BrandNavy = "#435363";
        private const string BrandOrange = "#FF5C36";
        private const string PlaceholderGrey = "#E7E6E6";
        private const float HeaderHeightInches = 1.06f;
        private const float LogoLeftOffsetInches = 4.32f;
        private const float LogoWidthInches = 2.37f;
        private const float LogoHeightInches = 0.86f;
        private const float SinglePhotoMaxHeightInches = 5.5f;
        private const float TwoPhotoMaxHeightInches = 3.5f;
        private const float MultiPhotoMaxHeightInches = 2.5f;
        private const float PhotoGapInches = 0.1f;
        private const string OfficeAddress = "501 - 510 Burrard Street, Vancouver, BC V6C 3A8";

        private readonly ILogger<BrochureRenderer> _logger;

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

                var photoPages = content.Photos.Count > 0
                    ? content.Photos.ToList()
                    : new List<BrochurePhoto> { new() };

                var logoBytes = TryReadImageBytes(content.LogoPath);
                var fontFamilies = ResolveFontFamilies();

                try
                {
                    var outputDirectory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(outputDirectory))
                        Directory.CreateDirectory(outputDirectory);

                    _logger.LogInformation(
                        "Starting brochure render for template {TemplateName} to {OutputPath} with {PhotoPageCount} page(s).",
                        content.TemplateName,
                        outputPath,
                        photoPages.Count);

                    var document = Document.Create(container =>
                    {
                        foreach (var photo in photoPages)
                        {
                            ct.ThrowIfCancellationRequested();

                            container.Page(page =>
                            {
                                page.Size(PageSizes.Letter);
                                page.PageColor(Colors.White);
                                page.MarginLeft(1f, Unit.Inch);
                                page.MarginRight(1f, Unit.Inch);
                                page.MarginTop(1f, Unit.Inch);
                                page.MarginBottom(0.79f, Unit.Inch);
                                page.DefaultTextStyle(TextStyle.Default.FontFamily(fontFamilies));

                                page.Header().Height(HeaderHeightInches, Unit.Inch).Element(header =>
                                    ComposeHeader(header, content, logoBytes, fontFamilies));

                                page.Content().PaddingTop(18).Element(body =>
                                    ComposeProjectPage(body, content, photo, fontFamilies));

                                page.Footer().Element(footer =>
                                    ComposeFooter(footer, content, fontFamilies));
                            });
                        }
                    });

                    document.GeneratePdf(outputPath);

                    _logger.LogInformation(
                        "Completed brochure render for template {TemplateName} to {OutputPath}.",
                        content.TemplateName,
                        outputPath);

                    return outputPath;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Brochure render failed for template {TemplateName} to {OutputPath}.",
                        content.TemplateName,
                        outputPath);
                    throw;
                }
            }, ct);
        }

        private static void ComposeHeader(
            IContainer container,
            BrochureContent content,
            byte[]? logoBytes,
            string[] fontFamilies)
        {
            container.Layers(layers =>
            {
                layers.PrimaryLayer()
                    .Background(BrandNavy)
                    .Extend();

                layers.Layer()
                    .AlignLeft()
                    .AlignMiddle()
                    .PaddingLeft(0)
                    .Text(content.TemplateName ?? string.Empty)
                    .FontFamily(fontFamilies)
                    .FontSize(9)
                    .FontColor(Colors.White)
                    .Bold();

                if (logoBytes is null)
                    return;

                layers.Layer()
                    .PaddingLeft(LogoLeftOffsetInches, Unit.Inch)
                    .PaddingTop((HeaderHeightInches - LogoHeightInches) / 2f, Unit.Inch)
                    .Width(LogoWidthInches, Unit.Inch)
                    .Height(LogoHeightInches, Unit.Inch)
                    .Image(logoBytes)
                    .FitArea();
            });
        }

        private static void ComposeFooter(IContainer container, BrochureContent content, string[] fontFamilies)
        {
            container.Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    var companyName = string.IsNullOrWhiteSpace(content.CompanyName)
                        ? string.Empty
                        : content.CompanyName.Trim() + " ";

                    text.DefaultTextStyle(GetBodyTextStyle(fontFamilies, 8));
                    text.Span(companyName + OfficeAddress).FontColor(BrandNavy);
                });

                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(GetBodyTextStyle(fontFamilies, 8));
                    text.Span("Page ").FontColor(BrandNavy);
                    text.CurrentPageNumber().FontColor(BrandNavy);
                    text.Span(" of ").FontColor(BrandNavy);
                    text.TotalPages().FontColor(BrandNavy);
                });
            });
        }

        private static void ComposeProjectPage(
            IContainer container,
            BrochureContent content,
            BrochurePhoto photo,
            string[] fontFamilies)
        {
            container.Column(column =>
            {
                column.Spacing(0);

                column.Item().Text((content.TemplateName ?? string.Empty).ToUpperInvariant())
                    .FontFamily(fontFamilies)
                    .FontSize(11)
                    .FontColor(BrandOrange)
                    .Bold();

                column.Item().PaddingBottom(6).Text(string.Empty);

                column.Item().Text(photo.Caption ?? string.Empty)
                    .FontFamily(fontFamilies)
                    .FontSize(10)
                    .FontColor(BrandNavy);

                column.Item().PaddingBottom(8).Text(string.Empty);

                column.Item().Element(photoBlock =>
                    ComposePhotoBlock(photoBlock, new[] { photo }, fontFamilies));

                column.Item().PaddingTop(10).Text(content.ProjectDescription ?? string.Empty)
                    .FontFamily(fontFamilies)
                    .FontSize(10)
                    .FontColor(BrandNavy)
                    .Justify()
                    .LineHeight(1f);

                if (content.Stats.Count > 0)
                {
                    column.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(2f, Unit.Inch);
                            columns.RelativeColumn();
                        });

                        foreach (var stat in content.Stats)
                        {
                            table.Cell().PaddingBottom(4).Text(stat.Label ?? string.Empty)
                                .FontFamily(fontFamilies)
                                .FontSize(10)
                                .FontColor(BrandNavy)
                                .Bold();

                            table.Cell().PaddingBottom(4).Text(stat.Value ?? string.Empty)
                                .FontFamily(fontFamilies)
                                .FontSize(10)
                                .FontColor(BrandNavy);
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(content.Notes))
                {
                    column.Item().PaddingTop(8).Text(content.Notes)
                        .FontFamily(fontFamilies)
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Italic();
                }
            });
        }

        private static void ComposePhotoBlock(IContainer container, IReadOnlyList<BrochurePhoto> photos, string[] fontFamilies)
        {
            var visiblePhotos = photos.Count == 0
                ? new List<BrochurePhoto> { new() }
                : photos.ToList();

            if (visiblePhotos.Count == 1)
            {
                container.MaxHeight(SinglePhotoMaxHeightInches, Unit.Inch)
                    .Element(photoContainer => ComposePhotoCell(photoContainer, visiblePhotos[0], fontFamilies));
                return;
            }

            if (visiblePhotos.Count is 2 or 3)
            {
                container.Row(row =>
                {
                    row.Spacing(PhotoGapInches, Unit.Inch);

                    foreach (var photo in visiblePhotos)
                    {
                        row.RelativeItem()
                            .MaxHeight(visiblePhotos.Count == 2 ? TwoPhotoMaxHeightInches : MultiPhotoMaxHeightInches, Unit.Inch)
                            .Element(photoContainer => ComposePhotoCell(photoContainer, photo, fontFamilies));
                    }
                });
                return;
            }

            var firstRow = visiblePhotos.Take(2).ToList();
            var secondRow = visiblePhotos.Skip(2).Take(2).ToList();

            container.Column(column =>
            {
                column.Spacing(PhotoGapInches, Unit.Inch);

                column.Item().Row(row =>
                {
                    row.Spacing(PhotoGapInches, Unit.Inch);
                    foreach (var photo in firstRow)
                    {
                        row.RelativeItem()
                            .MaxHeight(MultiPhotoMaxHeightInches, Unit.Inch)
                            .Element(photoContainer => ComposePhotoCell(photoContainer, photo, fontFamilies));
                    }
                });

                column.Item().Row(row =>
                {
                    row.Spacing(PhotoGapInches, Unit.Inch);
                    foreach (var photo in secondRow)
                    {
                        row.RelativeItem()
                            .MaxHeight(MultiPhotoMaxHeightInches, Unit.Inch)
                            .Element(photoContainer => ComposePhotoCell(photoContainer, photo, fontFamilies));
                    }
                });
            });
        }

        private static void ComposePhotoCell(IContainer container, BrochurePhoto photo, string[] fontFamilies)
        {
            var imageBytes = TryReadImageBytes(photo.FilePath);

            if (imageBytes is null)
            {
                container.Background(PlaceholderGrey)
                    .AlignMiddle()
                    .AlignCenter()
                    .Text("Image not available")
                    .FontFamily(fontFamilies)
                    .FontSize(9)
                    .FontColor(BrandNavy);
                return;
            }

            container.Image(imageBytes).FitArea();
        }

        private static byte[]? TryReadImageBytes(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                return File.ReadAllBytes(path);
            }
            catch
            {
                return null;
            }
        }

        private static string[] ResolveFontFamilies()
        {
            // TODO: Bundle Mulish font files in Kor.Operations.Rendering/Fonts/
            return new[] { "Mulish", "Arial" };
        }

        private static TextStyle GetBodyTextStyle(string[] fontFamilies, float fontSize) =>
            TextStyle.Default
                .FontFamily(fontFamilies)
                .FontSize(fontSize)
                .FontColor(BrandNavy);
    }
}
