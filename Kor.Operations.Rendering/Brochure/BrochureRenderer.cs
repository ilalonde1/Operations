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
        private const string OfficeAddress = "501 - 510 Burrard Street, Vancouver, BC V6C 3A8";
        private const string CoverBackground = "#44546A";
        private const string CoverOverlay = "#4D435363";
        private const float CoverLogoWidthInches = 3.5f;
        private const float CoverPhotoHeightInches = 3.5f;
        private const float CoverFooterStripHeightInches = 0.15f;
        private const float PairedProjectSlotHeightInches = 3.95f;
        private const float FullPageProjectSlotHeightInches = 7.9f;
        private const float ProjectPhotoWidthInches = 3f;
        private const float ProjectColumnGapInches = 0.2f;
        private const float ProjectPhotoVerticalPaddingInches = 0.2f;
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
                var logoBytes = TryReadImageBytes(resolvedLogoPath, "brochure logo");
                var resolvedCoverPhotoPath = ResolvePath(content.CoverPhotoPath);
                _logger.LogDebug("Resolved cover photo path to {CoverPhotoPath}", resolvedCoverPhotoPath);
                var coverPhotoBytes = TryReadImageBytes(resolvedCoverPhotoPath, "cover photo");
                var whiteLogoPath = ResolvePath(@"Resources\kor-logo-white.png");
                var coverLogoBytes = File.Exists(whiteLogoPath)
                    ? TryReadImageBytes(whiteLogoPath, "cover logo") ?? logoBytes
                    : logoBytes;
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
                        pageLayouts.Length);

                    if (coverPhotoBytes is { Length: > 0 })
                    {
                        _logger.LogInformation(
                            "Rendering cover photo strip using {CoverPhotoPath}.",
                            resolvedCoverPhotoPath);
                        _logger.LogInformation(
                            "Cover photo strip fix applied: using fixed-height container, primary image layer with FitArea, and secondary transparent overlay.");
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Rendering cover photo fallback strip. Resolved cover path was {CoverPhotoPath}.",
                            resolvedCoverPhotoPath);
                    }

                    var document = Document.Create(container =>
                    {
                        container.Page(page =>
                        {
                            page.Size(PageSizes.Letter);
                            page.PageColor(BrandNavy);
                            page.Margin(0);
                            page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish"));
                            page.Content().Element(body =>
                                ComposeCoverPage(body, content, coverLogoBytes, coverPhotoBytes));
                        });

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

        private static void ComposeCoverPage(
            IContainer container,
            BrochureContent content,
            byte[]? coverLogoBytes,
            byte[]? coverPhotoBytes)
        {
            var coverTitle = string.IsNullOrWhiteSpace(content.CoverTitle)
                ? string.IsNullOrWhiteSpace(content.TemplateName)
                    ? "KOR Structural"
                    : content.TemplateName
                : content.CoverTitle;

            container.Layers(layers =>
            {
                layers.PrimaryLayer().Background(BrandNavy);

                layers.Layer()
                    .AlignTop()
                    .AlignCenter()
                    .PaddingTop(2.2f, Unit.Inch)
                    .Width(CoverLogoWidthInches, Unit.Inch)
                    .Element(logoContainer =>
                    {
                        if (coverLogoBytes is null)
                            return;

                        logoContainer.Image(coverLogoBytes).FitWidth();
                    });

                layers.Layer()
                    .AlignCenter()
                    .AlignMiddle()
                    .PaddingBottom(0.6f, Unit.Inch)
                    .Column(column =>
                    {
                        column.Item().AlignCenter().Text(coverTitle.ToUpperInvariant())
                            .FontFamily("Mulish Black")
                            .FontSize(28)
                            .FontColor(Colors.White);

                        column.Item().PaddingTop(16).AlignCenter().Text(DateTime.Now.Year.ToString())
                            .FontFamily("Mulish")
                            .FontSize(14)
                            .FontColor(BrandOrange);
                    });

                layers.Layer()
                    .AlignBottom()
                    .PaddingBottom(CoverFooterStripHeightInches, Unit.Inch)
                    .Height(CoverPhotoHeightInches, Unit.Inch)
                    .Element(photoContainer => ComposeCoverPhotoStrip(photoContainer, coverPhotoBytes));

                layers.Layer()
                    .AlignBottom()
                    .Height(CoverFooterStripHeightInches, Unit.Inch)
                    .Background(BrandOrange);
            });
        }

        private static void ComposeCoverPhotoStrip(IContainer container, byte[]? coverPhotoBytes)
        {
            container.Layers(layers =>
            {
                if (coverPhotoBytes is { Length: > 0 })
                {
                    layers.PrimaryLayer()
                        .Image(coverPhotoBytes)
                        .FitArea();

                    layers.Layer()
                        .Background(CoverOverlay)
                        .Extend();

                    return;
                }

                layers.PrimaryLayer()
                    .Background(CoverBackground)
                    .Extend();
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

        private void ComposePageBody(IContainer container, PageLayout layout)
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
                    column.Item()
                        .Height(FullPageProjectSlotHeightInches, Unit.Inch)
                        .Element(projectContainer =>
                            ComposeProjectBlock(projectContainer, layout.PrimaryProject!, layout.PrimaryProjectNumber));
                    return;
                }

                column.Item()
                    .Height(PairedProjectSlotHeightInches, Unit.Inch)
                    .Element(projectContainer =>
                        ComposeProjectBlock(projectContainer, layout.PrimaryProject!, layout.PrimaryProjectNumber));

                column.Item()
                    .Height(1)
                    .Background(PlaceholderGrey);

                column.Item()
                    .Height(PairedProjectSlotHeightInches, Unit.Inch)
                    .Element(projectContainer =>
                        ComposeProjectBlock(projectContainer, layout.SecondaryProject, layout.SecondaryProjectNumber!.Value));
            });
        }

        private void ComposeProjectBlock(IContainer container, BrochureProject project, int projectNumber)
        {
            var photoOnLeft = projectNumber % 2 == 1;

            container.PaddingVertical(ProjectPhotoVerticalPaddingInches, Unit.Inch).Row(row =>
            {
                if (photoOnLeft)
                {
                    row.ConstantItem(ProjectPhotoWidthInches, Unit.Inch)
                        .Element(photoContainer => ComposeProjectPhoto(photoContainer, project));
                    row.Spacing(ProjectColumnGapInches, Unit.Inch);
                    row.RelativeItem().Element(textContainer => ComposeProjectText(textContainer, project));
                    return;
                }

                row.RelativeItem().Element(textContainer => ComposeProjectText(textContainer, project));
                row.Spacing(ProjectColumnGapInches, Unit.Inch);
                row.ConstantItem(ProjectPhotoWidthInches, Unit.Inch)
                    .Element(photoContainer => ComposeProjectPhoto(photoContainer, project));
            });
        }

        private static void ComposeProjectText(IContainer container, BrochureProject project)
        {
            container.Column(column =>
            {
                column.Item().Text((project.ProjectName ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish")
                    .FontSize(11)
                    .FontColor(BrandNavy)
                    .Bold();

                column.Item().PaddingTop(4).Height(1.5f).Background(BrandOrange);
                column.Item().PaddingBottom(8).Text(string.Empty);

                if (!string.IsNullOrWhiteSpace(project.SectionLabel))
                {
                    column.Item().Text(project.SectionLabel.ToUpperInvariant())
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandOrange)
                        .Bold();

                    column.Item().PaddingBottom(6).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(project.ProjectDescription))
                {
                    column.Item().Text(project.ProjectDescription)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Justify()
                        .LineHeight(1f);

                    column.Item().PaddingBottom(10).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(project.Client))
                {
                    column.Item().Text("Client")
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Bold();

                    column.Item().PaddingTop(2).Height(1).Background(BrandOrange);
                    column.Item().PaddingBottom(4).Text(string.Empty);
                    column.Item().Text(project.Client)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy);
                    column.Item().PaddingBottom(8).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(project.Architect))
                {
                    column.Item().Text("Architect")
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Bold();

                    column.Item().PaddingTop(2).Height(1).Background(BrandOrange);
                    column.Item().PaddingBottom(4).Text(string.Empty);
                    column.Item().Text(project.Architect)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy);
                }
            });
        }

        private void ComposeProjectPhoto(IContainer container, BrochureProject project)
        {
            var photo = project.Photos.FirstOrDefault();
            var imageBytes = photo is null
                ? null
                : TryReadImageBytes(ResolvePath(photo.FilePath), "project photo");

            if (imageBytes is { Length: > 0 })
            {
                container.Image(imageBytes).FitArea();
                return;
            }

            container.Background(PlaceholderGrey)
                .AlignMiddle()
                .AlignCenter()
                .Text("Image not available")
                .FontFamily("Mulish")
                .FontSize(9)
                .FontColor(BrandNavy);
        }

        private static PageLayout[] BuildPageLayouts(IReadOnlyList<BrochureProject> projects)
        {
            if (projects.Count == 0)
                return new[] { PageLayout.Empty };

            var layouts = new PageLayout[(projects.Count + 1) / 2];
            var layoutIndex = 0;

            for (var index = 0; index < projects.Count; index += 2)
            {
                var primaryNumber = index + 1;
                var secondaryProject = index + 1 < projects.Count ? projects[index + 1] : null;

                layouts[layoutIndex++] = new PageLayout(
                    projects[index],
                    primaryNumber,
                    secondaryProject,
                    secondaryProject is null ? null : index + 2);
            }

            return layouts;
        }

        private string ResolveLogoPath(string? logoPath)
        {
            return ResolvePath(logoPath);
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

            public PageLayout(
                BrochureProject primaryProject,
                int primaryProjectNumber,
                BrochureProject? secondaryProject = null,
                int? secondaryProjectNumber = null)
            {
                PrimaryProject = primaryProject;
                PrimaryProjectNumber = primaryProjectNumber;
                SecondaryProject = secondaryProject;
                SecondaryProjectNumber = secondaryProjectNumber;
            }

            public bool IsEmpty { get; }

            public BrochureProject? PrimaryProject { get; }

            public int PrimaryProjectNumber { get; }

            public BrochureProject? SecondaryProject { get; }

            public int? SecondaryProjectNumber { get; }
        }
    }
}
