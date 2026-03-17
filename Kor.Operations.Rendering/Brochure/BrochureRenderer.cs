#nullable enable
using System;
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
        private const float SmallSinglePhotoMaxHeightInches = 2.5f;
        private const float SmallTwoPhotoMaxHeightInches = 2.0f;
        private const float SmallThreePhotoMaxHeightInches = 1.8f;
        private const float SmallFourPhotoMaxHeightInches = 1.5f;
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

                content.CompanyName = "KOR Structural";
                content.LogoPath = @"Resources\kor-logo.png";
                var resolvedLogoPath = ResolveLogoPath(content.LogoPath);
                var logoBytes = TryReadImageBytes(resolvedLogoPath);
                var pageLayouts = BuildPageLayouts(content.Projects);

                try
                {
                    var outputDirectory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(outputDirectory))
                        Directory.CreateDirectory(outputDirectory);

                    _logger.LogInformation(
                        "Starting brochure render to {OutputPath} with {ProjectCount} project(s) across {PageCount} page(s).",
                        outputPath,
                        content.Projects.Count,
                        pageLayouts.Count);

                    var document = Document.Create(container =>
                    {
                        foreach (var layout in pageLayouts)
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
                                page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish"));

                                page.Header().Height(HeaderHeightInches, Unit.Inch).Element(header =>
                                    ComposeHeader(header, content, logoBytes));

                                page.Content().PaddingTop(18).Element(body =>
                                    ComposePageBody(body, layout));

                                page.Footer().Element(footer =>
                                    ComposeFooter(footer, content));
                            });
                        }
                    });

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

        private static void ComposeHeader(
            IContainer container,
            BrochureContent content,
            byte[]? logoBytes)
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
                    .Text("KOR Structural")
                    .FontFamily("Mulish")
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

        private static void ComposeFooter(IContainer container, BrochureContent content)
        {
            container.Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    var companyName = string.IsNullOrWhiteSpace(content.CompanyName)
                        ? string.Empty
                        : content.CompanyName.Trim() + " ";

                    text.DefaultTextStyle(GetBodyTextStyle(8));
                    text.Span(companyName + OfficeAddress).FontColor(BrandNavy);
                });

                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(GetBodyTextStyle(8));
                    text.Span("Page ").FontColor(BrandNavy);
                    text.CurrentPageNumber().FontColor(BrandNavy);
                    text.Span(" of ").FontColor(BrandNavy);
                    text.TotalPages().FontColor(BrandNavy);
                });
            });
        }

        private static void ComposePageBody(IContainer container, PageLayout layout)
        {
            if (layout.IsEmpty)
            {
                container.AlignMiddle().AlignCenter()
                    .Text("No projects added")
                    .FontFamily("Mulish")
                    .FontSize(12)
                    .FontColor(BrandNavy);
                return;
            }

            container.Column(column =>
            {
                column.Spacing(0);

                if (layout.SecondaryProject is null)
                {
                    column.Item().Element(projectContainer =>
                        ComposeProjectBlock(projectContainer, layout.PrimaryProject!, isPairedSmallProject: false));
                    return;
                }

                column.Item().Element(projectContainer =>
                    ComposeProjectBlock(projectContainer, layout.PrimaryProject!, isPairedSmallProject: true));

                column.Item()
                    .PaddingTop(12)
                    .PaddingBottom(12)
                    .Height(1)
                    .Background(PlaceholderGrey);

                column.Item().Element(projectContainer =>
                    ComposeProjectBlock(projectContainer, layout.SecondaryProject, isPairedSmallProject: true));
            });
        }

        private static void ComposeProjectBlock(IContainer container, BrochureProject project, bool isPairedSmallProject)
        {
            container.Column(column =>
            {
                column.Spacing(0);

                if (!string.IsNullOrWhiteSpace(project.SectionLabel))
                {
                    column.Item().Text(project.SectionLabel.ToUpperInvariant())
                        .FontFamily("Mulish")
                        .FontSize(11)
                        .FontColor(BrandOrange)
                        .Bold();

                    column.Item().PaddingBottom(6).Text(string.Empty);
                }

                column.Item().Text(project.ProjectName ?? string.Empty)
                    .FontFamily("Mulish")
                    .FontSize(10)
                    .FontColor(BrandNavy)
                    .Bold();

                column.Item().PaddingBottom(4).Text(string.Empty);

                if (!string.IsNullOrWhiteSpace(project.ProjectLocation))
                {
                    column.Item().Text(project.ProjectLocation)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Italic();

                    column.Item().PaddingBottom(8).Text(string.Empty);
                }

                if (project.Photos.Count > 0)
                {
                    column.Item().Element(photoBlock =>
                        ComposePhotoBlock(photoBlock, project.Photos, isPairedSmallProject));
                }

                if (!string.IsNullOrWhiteSpace(project.ProjectDescription))
                {
                    column.Item().PaddingTop(10).Text(project.ProjectDescription)
                        .FontFamily("Mulish")
                        .FontSize(10)
                        .FontColor(BrandNavy)
                        .Justify()
                        .LineHeight(1f);
                }

                if (project.Stats.Count > 0)
                {
                    column.Item().PaddingTop(8).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(2f, Unit.Inch);
                            columns.RelativeColumn();
                        });

                        foreach (var stat in project.Stats)
                        {
                            table.Cell().PaddingBottom(4).Text(stat.Label ?? string.Empty)
                                .FontFamily("Mulish")
                                .FontSize(10)
                                .FontColor(BrandNavy)
                                .Bold();

                            table.Cell().PaddingBottom(4).Text(stat.Value ?? string.Empty)
                                .FontFamily("Mulish")
                                .FontSize(10)
                                .FontColor(BrandNavy);
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(project.Notes))
                {
                    column.Item().PaddingTop(8).Text(project.Notes)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Italic();
                }
            });
        }

        private static void ComposePhotoBlock(IContainer container, IReadOnlyList<BrochurePhoto> photos, bool isPairedSmallProject)
        {
            var visiblePhotos = photos.Where(static x => x is not null).ToList();
            if (visiblePhotos.Count == 0)
                return;

            if (visiblePhotos.Count == 1)
            {
                container.MaxHeight(
                        isPairedSmallProject ? SmallSinglePhotoMaxHeightInches : 4.5f,
                        Unit.Inch)
                    .Element(photoContainer => ComposePhotoCell(photoContainer, visiblePhotos[0]));
                return;
            }

            if (visiblePhotos.Count == 2)
            {
                container.Row(row =>
                {
                    row.Spacing(PhotoGapInches, Unit.Inch);

                    foreach (var photo in visiblePhotos)
                    {
                        row.RelativeItem()
                            .MaxHeight(isPairedSmallProject ? SmallTwoPhotoMaxHeightInches : TwoPhotoMaxHeightInches, Unit.Inch)
                            .Element(photoContainer => ComposePhotoCell(photoContainer, photo));
                    }
                });
                return;
            }

            if (visiblePhotos.Count == 3)
            {
                container.Row(row =>
                {
                    row.Spacing(PhotoGapInches, Unit.Inch);

                    foreach (var photo in visiblePhotos)
                    {
                        row.RelativeItem()
                            .MaxHeight(isPairedSmallProject ? SmallThreePhotoMaxHeightInches : MultiPhotoMaxHeightInches, Unit.Inch)
                            .Element(photoContainer => ComposePhotoCell(photoContainer, photo));
                    }
                });
                return;
            }

            var rows = visiblePhotos
                .Take(4)
                .Chunk(2)
                .Select(static x => x.ToList())
                .ToList();

            container.Column(column =>
            {
                column.Spacing(PhotoGapInches, Unit.Inch);

                foreach (var rowPhotos in rows)
                {
                    column.Item().Row(row =>
                    {
                        row.Spacing(PhotoGapInches, Unit.Inch);

                        foreach (var photo in rowPhotos)
                        {
                            row.RelativeItem()
                                .MaxHeight(isPairedSmallProject ? SmallFourPhotoMaxHeightInches : MultiPhotoMaxHeightInches, Unit.Inch)
                                .Element(photoContainer => ComposePhotoCell(photoContainer, photo));
                        }
                    });
                }
            });
        }

        private static void ComposePhotoCell(IContainer container, BrochurePhoto photo)
        {
            var imageBytes = TryReadImageBytes(photo.FilePath);

            container.Column(column =>
            {
                if (imageBytes is null)
                {
                    column.Item()
                        .Background(PlaceholderGrey)
                        .AlignMiddle()
                        .AlignCenter()
                        .MinHeight(72)
                        .Text("Image not available")
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy);
                }
                else
                {
                    column.Item().Image(imageBytes).FitArea();
                }

                if (!string.IsNullOrWhiteSpace(photo.Caption))
                {
                    column.Item().PaddingTop(4).AlignCenter().Text(photo.Caption)
                        .FontFamily("Mulish")
                        .FontSize(8)
                        .FontColor(BrandNavy)
                        .Italic();
                }
            });
        }

        private static List<PageLayout> BuildPageLayouts(IReadOnlyList<BrochureProject> projects)
        {
            if (projects.Count == 0)
                return new List<PageLayout> { PageLayout.Empty };

            var layouts = new List<PageLayout>();
            var index = 0;

            while (index < projects.Count)
            {
                var current = projects[index];

                if (IsSmallProject(current) &&
                    index + 1 < projects.Count &&
                    IsSmallProject(projects[index + 1]))
                {
                    layouts.Add(new PageLayout(current, projects[index + 1]));
                    index += 2;
                    continue;
                }

                layouts.Add(new PageLayout(current));
                index++;
            }

            return layouts;
        }

        private static bool IsSmallProject(BrochureProject project)
        {
            return project.Photos.Count <= 2
                && project.Stats.Count <= 3
                && (project.ProjectDescription?.Length ?? 0) < 300;
        }

        private string ResolveLogoPath(string? logoPath)
        {
            if (string.IsNullOrWhiteSpace(logoPath))
                return string.Empty;

            var resolvedPath = Path.IsPathRooted(logoPath)
                ? logoPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, logoPath);

            if (!File.Exists(resolvedPath))
            {
                _logger.LogWarning("Brochure logo file not found at {LogoPath}", resolvedPath);
                return string.Empty;
            }

            return resolvedPath;
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

        private static TextStyle GetBodyTextStyle(float fontSize) =>
            TextStyle.Default
                .FontFamily("Mulish")
                .FontSize(fontSize)
                .FontColor(BrandNavy);

        private sealed class PageLayout
        {
            public static PageLayout Empty { get; } = new();

            public PageLayout()
            {
                IsEmpty = true;
            }

            public PageLayout(BrochureProject primaryProject, BrochureProject? secondaryProject = null)
            {
                PrimaryProject = primaryProject;
                SecondaryProject = secondaryProject;
            }

            public bool IsEmpty { get; }

            public BrochureProject? PrimaryProject { get; }

            public BrochureProject? SecondaryProject { get; }
        }
    }
}
