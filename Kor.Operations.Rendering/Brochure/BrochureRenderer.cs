#nullable enable
using System;
using System.Collections.Generic;
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
        private const float CoverTopZoneHeightInches = 9.35f;
        private const float CoverBottomBannerHeightInches = 1.65f;
        private const float CoverBottomBannerContentHeightInches = 1.5944f;
        private const float CoverBottomStripHeightPoints = 4f;
        private const float ContactColumnGapInches = 0.3f;
        private const float CoverBannerLogoWidthInches = 1.5f;
        private const float ProjectPhotoWidthInches = 3f;
        private const float ProjectColumnGapInches = 0.2f;
        private const float ProjectPhotoVerticalPaddingInches = 0.05f;
        private static readonly (string Region, string Contact, string Phone, string Email, string Hours)[] Offices =
        {
            ("Vancouver", "John Markulin, M.Eng., P.Eng., Struct.Eng., PE, SE", "(604) 685-9533", "contact@korstructural.com", "9AM to 5PM (Monday to Friday)"),
            ("Vancouver Island", "Rory Beirne, M.Eng., P.Eng., Struct.Eng.", "(778) 652-1895", "rbeire@korstructural.com", "9AM to 5PM (Monday to Friday)"),
            ("Okanagan", "Conor Murtagh, B.A.Sc., P.Eng.", "(778) 652-1887", "cmurtagh@korstructural.com", "9AM to 5PM (Monday to Friday)"),
            ("United States", "Jim DesRoches, BASc., P.Eng., PE", "(604) 999-7758", "jdesroches@korstructural.com", "9AM to 5PM (Monday to Friday)")
        };
        private static readonly string[] CoverContactLines =
        {
            "Suite 501 - 510 Burrard Street",
            "Vancouver, BC, V6C3A8",
            "Office: +1 604 685 9533",
            "contact@korstructural.com",
            "www.korstructural.com"
        };
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
                        .MinHeight(0.35f, Unit.Inch)
                        .AlignBottom()
                        .Element(footer => ComposeFooter(footer, content));
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
                            ComposeSection(container, block.Section, content, logoBytes);
                        break;
                    case BrochureBlockType.Personnel:
                        ComposePersonnel(container, block.People, content, logoBytes);
                        break;
                    case BrochureBlockType.CompanyOverview:
                        ComposeOverview(container, block.OverviewSections, content, logoBytes);
                        break;
                    case BrochureBlockType.Contact:
                        ComposeContact(container, content, logoBytes);
                        break;
                    case BrochureBlockType.PageBreak:
                        break;
                }
            }
        }

        private void ComposeSection(
            IDocumentContainer container,
            BrochureSection section,
            BrochureContent content,
            byte[]? logoBytes)
        {
            if (section.Projects.Count == 0)
                return;

            container.Page(page =>
            {
                ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => ComposeHeader(header, content, logoBytes));

                page.Content().PaddingTop(18).Element(body =>
                {
                    body.Column(column =>
                    {
                        column.Item().Element(c => ComposeSectionHeading(c, section));

                        for (var i = 0; i < section.Projects.Count; i++)
                        {
                            if (i > 0)
                                column.Item().PaddingVertical(4).Height(1).Background(PlaceholderGrey);

                            var project = section.Projects[i];
                            var photoOnLeft = i % 2 == 0;
                            column.Item().MinHeight(3.5f, Unit.Inch).Element(c => ComposeProjectBlock(c, project, photoOnLeft));
                        }
                    });
                });

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .MinHeight(0.35f, Unit.Inch)
                    .AlignBottom()
                    .Element(footer => ComposeFooter(footer, content));
            });
        }

        private void ComposePersonnel(
            IDocumentContainer container,
            IReadOnlyList<BrochurePerson> people,
            BrochureContent content,
            byte[]? logoBytes)
        {
            if (people.Count == 0)
                return;

            container.Page(page =>
            {
                ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => ComposeHeader(header, content, logoBytes));

                page.Content().PaddingTop(18).Element(body =>
                {
                    body.Column(column =>
                    {
                        for (var i = 0; i < people.Count; i++)
                        {
                            if (i > 0)
                                column.Item().PaddingVertical(4).Height(1).Background(PlaceholderGrey);

                            var person = people[i];
                            column.Item().MinHeight(3.5f, Unit.Inch).Element(c => ComposePersonBlock(c, person));
                        }
                    });
                });

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .MinHeight(0.35f, Unit.Inch)
                    .AlignBottom()
                    .Element(footer => ComposeFooter(footer, content));
            });
        }

        private void ComposeOverview(
            IDocumentContainer container,
            IReadOnlyList<BrochureOverviewSection> sections,
            BrochureContent content,
            byte[]? logoBytes)
        {
            if (sections.Count == 0)
                return;

            container.Page(page =>
            {
                ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => ComposeHeader(header, content, logoBytes));

                page.Content().PaddingTop(18).Element(body =>
                {
                    body.Column(column =>
                    {
                        for (var i = 0; i < sections.Count; i++)
                        {
                            if (i > 0)
                                column.Item().Height(14);

                            var section = sections[i];
                            column.Item().Element(slot => ComposeOverviewSection(slot, section));
                        }
                    });
                });

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .MinHeight(0.35f, Unit.Inch)
                    .AlignBottom()
                    .Element(footer => ComposeFooter(footer, content));
            });
        }

        private void ComposeContact(
            IDocumentContainer container,
            BrochureContent content,
            byte[]? logoBytes)
        {
            container.Page(page =>
            {
                ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => ComposeHeader(header, content, logoBytes));

                page.Content().PaddingTop(18).Element(body =>
                    ComposeContactPage(body));

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .Element(footer => ComposeFooter(footer, content));
            });
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
                    .FitArea();
            });
        }

        private static void ComposeCoverPage(
            IContainer container,
            BrochureContent content,
            byte[]? coverLogoBytes,
            byte[]? coverPhotoBytes)
        {
            var clampedOpacity = Math.Clamp(content.CoverPhotoOpacity, 0f, 1f);
            var alpha = (int)((1.0f - clampedOpacity) * 255);
            var overlayColor = $"#{alpha:X2}435363";
            var coverTitle = string.IsNullOrWhiteSpace(content.CoverTitle)
                ? string.IsNullOrWhiteSpace(content.TemplateName)
                    ? "KOR Structural"
                    : content.TemplateName
                : content.CoverTitle;

            container.Column(column =>
            {
                column.Item()
                    .Height(CoverTopZoneHeightInches, Unit.Inch)
                    .Element(topZone => ComposeCoverTopZone(
                        topZone,
                        coverTitle,
                        coverPhotoBytes,
                        overlayColor,
                        content.CoverYear ?? DateTime.Now.Year));

                column.Item()
                    .Height(CoverBottomBannerHeightInches, Unit.Inch)
                    .Element(bottomBanner => ComposeCoverBottomBanner(bottomBanner, coverLogoBytes));
            });
        }

        private static void ComposeCoverTopZone(
            IContainer container,
            string coverTitle,
            byte[]? coverPhotoBytes,
            string overlayColor,
            int coverYear)
        {
            container.Layers(layers =>
            {
                if (coverPhotoBytes is { Length: > 0 })
                {
                    layers.PrimaryLayer()
                        .Image(coverPhotoBytes)
                        .FitArea();
                }
                else
                {
                    layers.PrimaryLayer()
                        .Background(BrandNavy)
                        .Extend();
                }

                layers.Layer()
                    .Background(overlayColor)
                    .Extend();

                layers.Layer()
                    .PaddingLeft(0.5f, Unit.Inch)
                    .PaddingTop(3.25f, Unit.Inch)
                    .Column(column =>
                    {
                        column.Item().Text(coverTitle.ToUpperInvariant())
                            .FontFamily("Mulish Black")
                            .FontSize(32)
                            .FontColor(Colors.White);

                        column.Item().PaddingTop(12).Text(coverYear.ToString())
                            .FontFamily("Mulish")
                            .FontSize(16)
                            .FontColor(BrandOrange)
                            .Bold();
                    });
            });
        }

        private static void ComposeCoverBottomBanner(IContainer container, byte[]? coverLogoBytes)
        {
            container.Column(column =>
            {
                column.Item()
                    .Height(CoverBottomBannerContentHeightInches, Unit.Inch)
                    .Background(BrandNavy)
                    .Row(row =>
                    {
                        row.RelativeItem(4)
                            .PaddingLeft(0.5f, Unit.Inch)
                            .AlignMiddle()
                            .Element(left =>
                            {
                                if (coverLogoBytes is null)
                                    return;

                                left.Width(CoverBannerLogoWidthInches, Unit.Inch)
                                    .Image(coverLogoBytes)
                                    .FitWidth();
                            });

                        row.RelativeItem(6)
                            .PaddingRight(0.3f, Unit.Inch)
                            .AlignMiddle()
                            .AlignRight()
                            .Column(textColumn =>
                            {
                                foreach (var line in CoverContactLines)
                                {
                                    textColumn.Item().AlignRight().Text(line)
                                        .FontFamily("Mulish")
                                        .FontSize(8)
                                        .FontColor(Colors.White);
                                }
                            });
                    });

                column.Item()
                    .Height(CoverBottomStripHeightPoints, Unit.Point)
                    .Background(BrandOrange);
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
            page.MarginTop(0);
            page.MarginBottom(0.2f, Unit.Inch);
            page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish"));
        }

        private static void ComposeSectionHeading(IContainer container, BrochureSection section)
        {
            container.Column(column =>
            {
                column.Item().Text((section.Heading ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish Black")
                    .FontSize(11)
                    .FontColor(BrandNavy);

                column.Item().PaddingTop(3).Height(1.5f).Background(BrandOrange);
                column.Item().PaddingBottom(6).Text(string.Empty);

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
                    .FontSize(9)
                    .FontColor(BrandNavy)
                    .Bold();

                column.Item().PaddingTop(3).Height(1.5f).Background(BrandOrange);
                column.Item().PaddingBottom(4).Text(string.Empty);

                if (!string.IsNullOrWhiteSpace(project.ProjectDescription))
                {
                    column.Item().Text(project.ProjectDescription)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Justify()
                        .LineHeight(1.2f);

                    column.Item().PaddingBottom(6).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(project.Client))
                {
                    column.Item().Text("Client")
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Bold();

                    column.Item().PaddingTop(2).Height(1).Background(BrandOrange);
                    column.Item().Text(project.Client)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy);
                    column.Item().PaddingBottom(4).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(project.Architect))
                {
                    column.Item().Text("Architect")
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(BrandNavy)
                        .Bold();

                    column.Item().PaddingTop(2).Height(1).Background(BrandOrange);
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
                container.AlignTop().Image(imageBytes).FitWidth();
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
                container.AlignTop().Image(imageBytes).FitWidth();
                return;
            }

            container.Background(PlaceholderGrey);
        }

        private static void ComposeOverviewSection(IContainer container, BrochureOverviewSection section)
        {
            container.Column(column =>
            {
                column.Item().PaddingBottom(4).Text((section.Heading ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish Black")
                    .FontSize(11)
                    .FontColor(BrandOrange);

                column.Item().Text(section.Body ?? string.Empty)
                    .FontFamily("Mulish")
                    .FontSize(9)
                    .FontColor(BrandNavy)
                    .Justify()
                    .LineHeight(1.2f);
            });
        }

        private static void ComposeContactPage(IContainer container)
        {
            container.Column(column =>
            {
                column.Item().Text("CONTACT")
                    .FontFamily("Mulish Black")
                    .FontSize(14)
                    .FontColor(BrandOrange);

                column.Item().PaddingTop(4).Height(2).Background(BrandOrange);
                column.Item().PaddingBottom(16).Text(string.Empty);

                for (var i = 0; i < Offices.Length; i += 2)
                {
                    var leftOffice = Offices[i];
                    var hasRightOffice = i + 1 < Offices.Length;

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Element(cell => ComposeOfficeCell(cell, leftOffice));
                        row.Spacing(ContactColumnGapInches, Unit.Inch);

                        if (hasRightOffice)
                        {
                            var rightOffice = Offices[i + 1];
                            row.RelativeItem().Element(cell => ComposeOfficeCell(cell, rightOffice));
                        }
                        else
                        {
                            row.RelativeItem();
                        }
                    });

                    if (i + 2 < Offices.Length)
                        column.Item().PaddingBottom(16).Text(string.Empty);
                }
            });
        }

        private static void ComposeOfficeCell(
            IContainer container,
            (string Region, string Contact, string Phone, string Email, string Hours) office)
        {
            container.Column(column =>
            {
                column.Item().Text(office.Region)
                    .FontFamily("Mulish")
                    .FontSize(11)
                    .FontColor(BrandNavy)
                    .Bold();

                column.Item().PaddingTop(4).Height(1).Background(PlaceholderGrey);
                column.Item().PaddingBottom(4).Text(string.Empty);

                column.Item().Text(office.Contact)
                    .FontFamily("Mulish")
                    .FontSize(9)
                    .FontColor(BrandNavy);

                column.Item().PaddingTop(4).Text(text =>
                {
                    text.DefaultTextStyle(GetBodyTextStyle(9));
                    text.Span("T: ").Bold();
                    text.Span(office.Phone);
                });

                column.Item().PaddingTop(2).Text(text =>
                {
                    text.DefaultTextStyle(GetBodyTextStyle(9));
                    text.Span("E: ").Bold();
                    text.Span(office.Email).FontColor("#0563C1");
                });

                column.Item().PaddingTop(2).Text(text =>
                {
                    text.DefaultTextStyle(GetBodyTextStyle(9));
                    text.Span("H: ").Bold();
                    text.Span(office.Hours);
                });
            });
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
