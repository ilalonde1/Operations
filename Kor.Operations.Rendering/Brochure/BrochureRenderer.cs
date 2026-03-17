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
                var contentPageCount = content.Blocks.Count == 0
                    ? 1
                    : content.Blocks.Sum(static block =>
                    {
                        if (block.BlockType == BrochureBlockType.Section)
                        {
                            if (block.Section is null || block.Section.Projects.Count == 0)
                                return 0;

                            return (block.Section.Projects.Count + 1) / 2;
                        }

                        if (block.People.Count == 0)
                            return 0;

                        return (block.People.Count + 1) / 2;
                    });

                try
                {
                    var outputDirectory = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrWhiteSpace(outputDirectory))
                        Directory.CreateDirectory(outputDirectory);

                    _logger.LogInformation(
                        "Starting brochure render to {OutputPath} with {SectionCount} section(s) across {PageCount} page(s).",
                        outputPath,
                        content.Blocks.Count,
                        contentPageCount);

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

                        if (content.Blocks.Count == 0)
                        {
                            container.Page(page =>
                            {
                                ConfigureStandardPage(page);

                                page.Header().PaddingHorizontal(-1, Unit.Inch)
                                    .Element(header => ComposeHeader(header, content, logoBytes));

                                page.Content().PaddingTop(18).Element(body =>
                                    body.AlignMiddle().AlignCenter().Text("No content added")
                                        .FontFamily("Mulish")
                                        .FontSize(12)
                                        .FontColor(BrandNavy));

                                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                                    .Element(footer => ComposeFooter(footer, content));
                            });

                            return;
                        }

                        foreach (var block in content.Blocks)
                        {
                            ct.ThrowIfCancellationRequested();

                            if (block.BlockType == BrochureBlockType.Section)
                            {
                                var section = block.Section;
                                if (section is null || section.Projects.Count == 0)
                                    continue;

                                var isFirstPageOfSection = true;

                                for (var i = 0; i < section.Projects.Count; i += 2)
                                {
                                    var primary = section.Projects[i];
                                    var secondary = i + 1 < section.Projects.Count
                                        ? section.Projects[i + 1]
                                        : null;
                                    var primaryPhotoLeft = (i / 2) % 2 == 0;

                                    container.Page(page =>
                                    {
                                        ConfigureStandardPage(page);

                                        page.Header().PaddingHorizontal(-1, Unit.Inch)
                                            .Element(header => ComposeHeader(header, content, logoBytes));

                                        page.Content().PaddingTop(18).Element(body =>
                                        {
                                            body.Column(column =>
                                            {
                                                if (isFirstPageOfSection)
                                                {
                                                    column.Item().Element(sectionContainer =>
                                                        ComposeSectionHeading(sectionContainer, section));
                                                    isFirstPageOfSection = false;
                                                }

                                                column.Item().Element(projectContainer =>
                                                    ComposeProjectPair(projectContainer, primary, secondary, primaryPhotoLeft));
                                            });
                                        });

                                        page.Footer().PaddingHorizontal(-1, Unit.Inch)
                                            .Element(footer => ComposeFooter(footer, content));
                                    });
                                }
                            }
                            else if (block.BlockType == BrochureBlockType.Personnel)
                            {
                                if (block.People.Count == 0)
                                    continue;

                                for (var i = 0; i < block.People.Count; i += 2)
                                {
                                    var primary = block.People[i];
                                    var secondary = i + 1 < block.People.Count
                                        ? block.People[i + 1]
                                        : null;

                                    container.Page(page =>
                                    {
                                        ConfigureStandardPage(page);

                                        page.Header().PaddingHorizontal(-1, Unit.Inch)
                                            .Element(header => ComposeHeader(header, content, logoBytes));

                                        page.Content().PaddingTop(18).Element(body =>
                                        {
                                            body.Column(column =>
                                            {
                                                column.Item().Element(personContainer =>
                                                    ComposePersonPair(personContainer, primary, secondary));
                                            });
                                        });

                                        page.Footer().PaddingHorizontal(-1, Unit.Inch)
                                            .Element(footer => ComposeFooter(footer, content));
                                    });
                                }
                            }
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
            container
                .Height(HeaderHeightInches, Unit.Inch)
                .Background(BrandNavy)
                .Row(row =>
            {
                row.ConstantItem(3f, Unit.Inch)
                    .PaddingLeft(0.25f, Unit.Inch)
                    .AlignMiddle()
                    .Text(content.TemplateName ?? string.Empty)
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
                    .FitHeight();
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
            container.PaddingHorizontal(0.25f, Unit.Inch).Row(row =>
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

        private static void ConfigureStandardPage(PageDescriptor page)
        {
            page.Size(PageSizes.Letter);
            page.PageColor(Colors.White);
            page.MarginLeft(1f, Unit.Inch);
            page.MarginRight(1f, Unit.Inch);
            page.MarginTop(1f, Unit.Inch);
            page.MarginBottom(0.79f, Unit.Inch);
            page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish"));
        }

        private static void ComposeSectionHeading(IContainer container, BrochureSection section)
        {
            container.Column(column =>
            {
                column.Item().Text((section.Heading ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish Black")
                    .FontSize(16)
                    .FontColor(BrandNavy);

                column.Item().PaddingTop(4).Height(2).Background(BrandOrange);
                column.Item().PaddingBottom(8).Text(string.Empty);

                if (!string.IsNullOrWhiteSpace(section.Blurb))
                {
                    column.Item().Text(section.Blurb)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Justify();

                    column.Item().PaddingBottom(12).Text(string.Empty);
                }

                column.Item().Height(1).Background(PlaceholderGrey);
                column.Item().PaddingBottom(12).Text(string.Empty);
            });
        }

        private void ComposeProjectPair(
            IContainer container,
            BrochureProject primary,
            BrochureProject? secondary,
            bool primaryPhotoLeft)
        {
            if (secondary is null)
            {
                container.Height(FullPageProjectSlotHeightInches, Unit.Inch)
                    .Element(projectContainer =>
                        ComposeProjectBlock(projectContainer, primary, primaryPhotoLeft));
                return;
            }

            container.Column(column =>
            {
                column.Item()
                    .Height(PairedProjectSlotHeightInches, Unit.Inch)
                    .Element(projectContainer =>
                        ComposeProjectBlock(projectContainer, primary, primaryPhotoLeft));

                column.Item()
                    .Height(1)
                    .Background(PlaceholderGrey);

                column.Item()
                    .Height(PairedProjectSlotHeightInches, Unit.Inch)
                    .Element(projectContainer =>
                        ComposeProjectBlock(projectContainer, secondary, !primaryPhotoLeft));
            });
        }

        private void ComposeProjectBlock(IContainer container, BrochureProject project, bool photoOnLeft)
        {
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

        private void ComposePersonPair(
            IContainer container,
            BrochurePerson primary,
            BrochurePerson? secondary)
        {
            if (secondary is null)
            {
                container.Height(FullPageProjectSlotHeightInches, Unit.Inch)
                    .Element(personContainer => ComposePersonBlock(personContainer, primary));
                return;
            }

            container.Column(column =>
            {
                column.Item()
                    .Height(PairedProjectSlotHeightInches, Unit.Inch)
                    .Element(personContainer => ComposePersonBlock(personContainer, primary));

                column.Item()
                    .Height(1)
                    .Background(PlaceholderGrey);

                column.Item()
                    .Height(PairedProjectSlotHeightInches, Unit.Inch)
                    .Element(personContainer => ComposePersonBlock(personContainer, secondary));
            });
        }

        private void ComposePersonBlock(IContainer container, BrochurePerson person)
        {
            container.PaddingVertical(ProjectPhotoVerticalPaddingInches, Unit.Inch).Row(row =>
            {
                row.ConstantItem(2f, Unit.Inch)
                    .Element(photoContainer => ComposePersonPhoto(photoContainer, person));
                row.Spacing(ProjectColumnGapInches, Unit.Inch);
                row.RelativeItem().Element(textContainer => ComposePersonText(textContainer, person));
            });
        }

        private static void ComposePersonText(IContainer container, BrochurePerson person)
        {
            container.Column(column =>
            {
                column.Item().Text(person.Name ?? string.Empty)
                    .FontFamily("Mulish")
                    .FontSize(11)
                    .FontColor(BrandNavy)
                    .Bold();

                column.Item().PaddingTop(2).Height(1.5f).Background(BrandOrange);
                column.Item().PaddingBottom(4).Text(string.Empty);

                if (!string.IsNullOrWhiteSpace(person.Credentials))
                {
                    column.Item().Text(person.Credentials)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Italic();

                    column.Item().PaddingBottom(8).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(person.Bio))
                {
                    column.Item().Text(person.Bio)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Justify()
                        .LineHeight(1f);
                }
            });
        }

        private void ComposePersonPhoto(IContainer container, BrochurePerson person)
        {
            var imageBytes = TryReadImageBytes(ResolvePath(person.PhotoPath), "person photo");

            if (imageBytes is { Length: > 0 })
            {
                container.Image(imageBytes).FitArea();
                return;
            }

            container.Background(PlaceholderGrey);
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
    }
}
