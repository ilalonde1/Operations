#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kor.Operations.Rendering.Brochure.Layouts
{
    internal sealed class StandardPortfolioLayout : IBrochureLayoutTemplate
    {
        private const float HeaderHeightInches = 1.06f;
        private const float CoverTopZoneHeightInches = 9.35f;
        private const float CoverBottomBannerHeightInches = 1.65f;
        private const float CoverBottomBannerContentHeightInches = 1.5944f;
        private const float CoverBottomStripHeightPoints = 4f;
        private const float CoverBannerLogoWidthInches = 1.5f;
        private const float ProjectPhotoWidthInches = 3f;
        private const float ProjectColumnGapInches = 0.2f;
        private const float ProjectPhotoVerticalPaddingInches = 0.05f;
        private const float PersonPhotoVerticalPaddingInches = 0.035f;
        private const float ContactColumnGapInches = 0.3f;
        private const string PlaceholderGrey = "#E7E6E6";
        private const string OfficeAddress = "501 - 510 Burrard Street, Vancouver, BC V6C 3A8";

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

        public string Id => "standard-portfolio";

        public string DisplayName => "Standard Portfolio";

        public void ComposeCoverPage(IContainer container, BrochureRenderContext ctx)
        {
            var clampedOpacity = Math.Clamp(ctx.Content.CoverPhotoOpacity, 0f, 1f);
            var overlayColor = BrochureRenderHelpers.BuildOverlayColor(ctx.Skin.PrimaryColor, clampedOpacity);
            var coverTitle = string.IsNullOrWhiteSpace(ctx.Content.CoverTitle)
                ? string.IsNullOrWhiteSpace(ctx.Content.TemplateName)
                    ? "KOR Structural"
                    : ctx.Content.TemplateName
                : ctx.Content.CoverTitle;

            container.Column(column =>
            {
                column.Item()
                    .Height(CoverTopZoneHeightInches, Unit.Inch)
                    .Element(topZone => ComposeCoverTopZone(
                        topZone,
                        coverTitle,
                        ctx.Skin,
                        ctx.CoverPhotoBytes,
                        overlayColor,
                        DateTime.Now.Year));

                column.Item()
                    .Height(CoverBottomBannerHeightInches, Unit.Inch)
                    .Element(bottomBanner => ComposeCoverBottomBanner(bottomBanner, ctx.Skin, ctx.CoverLogoBytes));
            });
        }

        public void ComposeSection(IDocumentContainer container, BrochureSection section, BrochureRenderContext ctx)
        {
            if (section.Projects.Count == 0)
                return;

            var splitIndexes = (section.PageBreakAfterProjectIndex ?? new List<int>())
                .Where(index => index >= 0 && index < section.Projects.Count - 1)
                .Distinct()
                .OrderBy(index => index)
                .ToList();

            var projectGroups = new List<List<BrochureProject>>();
            var startIndex = 0;

            foreach (var splitIndex in splitIndexes)
            {
                var count = splitIndex - startIndex + 1;
                if (count > 0)
                    projectGroups.Add(section.Projects.Skip(startIndex).Take(count).ToList());

                startIndex = splitIndex + 1;
            }

            if (startIndex < section.Projects.Count)
                projectGroups.Add(section.Projects.Skip(startIndex).ToList());

            foreach (var projectGroup in projectGroups)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                container.Page(page =>
                {
                    BrochureRenderHelpers.ConfigureStandardPage(page);

                    page.Header().PaddingHorizontal(-1, Unit.Inch)
                        .Element(header => ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                    page.Content().PaddingTop(18).Element(body =>
                    {
                        body.Column(column =>
                        {
                            column.Item().Element(c => ComposeSectionHeading(c, section, ctx.Skin));

                            for (var i = 0; i < projectGroup.Count; i++)
                            {
                                var project = projectGroup[i];
                                var photoOnLeft = i % 2 == 0;
                                column.Item().MinHeight(3.5f, Unit.Inch)
                                    .Element(c => ComposeProjectBlock(c, project, ctx.Skin, photoOnLeft, ctx.ReadImage, ctx.ResolvePath));
                            }
                        });
                    });

                    page.Footer().PaddingHorizontal(-1, Unit.Inch)
                        .MinHeight(0.35f, Unit.Inch)
                        .AlignBottom()
                        .Element(footer => ComposeFooter(footer, ctx.Content, ctx.Skin));
                });
            }
        }

        public void ComposePersonnel(IDocumentContainer container, BrochureBlock block, BrochureRenderContext ctx)
        {
            var people = block.People;
            if (people.Count == 0)
                return;

            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                page.Content().PaddingTop(18).Element(body =>
                {
                    body.Column(column =>
                    {
                        column.Item().Text((block.PersonnelHeading ?? string.Empty).ToUpperInvariant())
                            .FontFamily("Mulish Black")
                            .FontSize(14)
                            .FontColor(ctx.Skin.AccentColor);

                        column.Item().PaddingTop(4).Height(2).Background(ctx.Skin.AccentColor);
                        column.Item().PaddingBottom(12).Text(string.Empty);

                        if (!string.IsNullOrWhiteSpace(block.PersonnelBlurb))
                        {
                            column.Item().Text(block.PersonnelBlurb)
                                .FontFamily("Mulish")
                                .FontSize(10)
                                .FontColor(ctx.Skin.PrimaryColor)
                                .Italic();

                            column.Item().PaddingBottom(12).Text(string.Empty);
                        }

                        for (var i = 0; i < people.Count; i++)
                        {
                            var person = people[i];
                            column.Item().MinHeight(3.5f, Unit.Inch)
                                .Element(c => ComposePersonBlock(c, person, ctx.Skin, ctx.ReadImage, ctx.ResolvePath));
                        }
                    });
                });

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .MinHeight(0.35f, Unit.Inch)
                    .AlignBottom()
                    .Element(footer => ComposeFooter(footer, ctx.Content, ctx.Skin));
            });
        }

        public void ComposeOverview(
            IDocumentContainer container,
            IReadOnlyList<BrochureOverviewSection> sections,
            IReadOnlyList<int>? pageBreaks,
            BrochureRenderContext ctx)
        {
            if (sections.Count == 0)
                return;

            var splitIndexes = (pageBreaks ?? new List<int>())
                .Where(index => index >= 0 && index < sections.Count - 1)
                .Distinct()
                .OrderBy(index => index)
                .ToList();

            var overviewGroups = new List<List<BrochureOverviewSection>>();
            var startIndex = 0;

            foreach (var splitIndex in splitIndexes)
            {
                var count = splitIndex - startIndex + 1;
                if (count > 0)
                    overviewGroups.Add(sections.Skip(startIndex).Take(count).ToList());

                startIndex = splitIndex + 1;
            }

            if (startIndex < sections.Count)
                overviewGroups.Add(sections.Skip(startIndex).ToList());

            foreach (var overviewGroup in overviewGroups)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                container.Page(page =>
                {
                    BrochureRenderHelpers.ConfigureStandardPage(page);

                    page.Header().PaddingHorizontal(-1, Unit.Inch)
                        .Element(header => ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                    page.Content().PaddingTop(18).Element(body =>
                    {
                        body.Column(column =>
                        {
                            for (var i = 0; i < overviewGroup.Count; i++)
                            {
                                if (i > 0)
                                    column.Item().Height(14);

                                var section = overviewGroup[i];
                                column.Item().Element(slot => ComposeOverviewSection(slot, section, ctx.Skin));
                            }
                        });
                    });

                    page.Footer().PaddingHorizontal(-1, Unit.Inch)
                        .MinHeight(0.35f, Unit.Inch)
                        .AlignBottom()
                        .Element(footer => ComposeFooter(footer, ctx.Content, ctx.Skin));
                });
            }
        }

        public void ComposeContact(IDocumentContainer container, BrochureRenderContext ctx)
        {
            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                page.Content().PaddingTop(18).Element(body =>
                    ComposeContactPage(body, ctx.Skin));

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .Element(footer => ComposeFooter(footer, ctx.Content, ctx.Skin));
            });
        }

        public int EstimatePageCount(BrochureContent content) =>
            content.Blocks.Count == 0
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

        private static void ComposeSectionHeading(IContainer container, BrochureSection section, BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().Text((section.Heading ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish Black")
                    .FontSize(11)
                    .FontColor(skin.PrimaryColor);

                column.Item().PaddingTop(3).Height(1.5f).Background(skin.AccentColor);
                column.Item().PaddingBottom(6).Text(string.Empty);

                if (!string.IsNullOrWhiteSpace(section.Blurb))
                {
                    column.Item().Text(section.Blurb)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Justify();

                    column.Item().PaddingBottom(12).Text(string.Empty);
                }

                column.Item().Height(1).Background(PlaceholderGrey);
                column.Item().PaddingBottom(12).Text(string.Empty);
            });
        }

        private static void ComposeProjectBlock(
            IContainer container,
            BrochureProject project,
            BrochureSkinDefinition skin,
            bool photoOnLeft,
            Func<string?, string?, byte[]?> tryReadImage,
            Func<string?, string> resolvePath)
        {
            container.PaddingVertical(ProjectPhotoVerticalPaddingInches, Unit.Inch).Row(row =>
            {
                if (photoOnLeft)
                {
                    row.ConstantItem(ProjectPhotoWidthInches, Unit.Inch)
                        .Element(photoContainer => ComposeProjectPhoto(photoContainer, project, skin, tryReadImage, resolvePath));
                    row.Spacing(ProjectColumnGapInches, Unit.Inch);
                    row.RelativeItem().Element(textContainer => ComposeProjectText(textContainer, project, skin));
                    return;
                }

                row.RelativeItem().Element(textContainer => ComposeProjectText(textContainer, project, skin));
                row.Spacing(ProjectColumnGapInches, Unit.Inch);
                row.ConstantItem(ProjectPhotoWidthInches, Unit.Inch)
                    .Element(photoContainer => ComposeProjectPhoto(photoContainer, project, skin, tryReadImage, resolvePath));
            });
        }

        private static void ComposeProjectText(IContainer container, BrochureProject project, BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().Text((project.ProjectName ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish")
                    .FontSize(9)
                    .FontColor(skin.PrimaryColor)
                    .Bold();

                column.Item().PaddingTop(3).Height(1.5f).Background(skin.AccentColor);
                column.Item().PaddingBottom(4).Text(string.Empty);

                if (!string.IsNullOrWhiteSpace(project.ProjectDescription))
                {
                    column.Item().Text(project.ProjectDescription)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Justify()
                        .LineHeight(1.2f);

                    column.Item().PaddingBottom(6).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(project.Client))
                {
                    column.Item().Text("Client")
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Bold();

                    column.Item().PaddingTop(2).Height(1).Background(skin.AccentColor);
                    column.Item().Text(project.Client)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor);
                    column.Item().PaddingBottom(4).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(project.Architect))
                {
                    column.Item().Text("Architect")
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Bold();

                    column.Item().PaddingTop(2).Height(1).Background(skin.AccentColor);
                    column.Item().Text(project.Architect)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor);
                }
            });
        }

        private static void ComposeProjectPhoto(
            IContainer container,
            BrochureProject project,
            BrochureSkinDefinition skin,
            Func<string?, string?, byte[]?> tryReadImage,
            Func<string?, string> resolvePath)
        {
            var photo = project.Photos.FirstOrDefault();
            var imageBytes = photo is null
                ? null
                : tryReadImage(resolvePath(photo.FilePath), "project photo");

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
                .FontColor(skin.PrimaryColor);
        }

        private static void ComposePersonBlock(
            IContainer container,
            BrochurePerson person,
            BrochureSkinDefinition skin,
            Func<string?, string?, byte[]?> tryReadImage,
            Func<string?, string> resolvePath)
        {
            container.PaddingVertical(PersonPhotoVerticalPaddingInches, Unit.Inch).Row(row =>
            {
                row.ConstantItem(2f, Unit.Inch)
                    .Element(photoContainer => ComposePersonPhoto(photoContainer, person, tryReadImage, resolvePath));
                row.Spacing(ProjectColumnGapInches, Unit.Inch);
                row.RelativeItem().Element(textContainer => ComposePersonText(textContainer, person, skin));
            });
        }

        private static void ComposePersonText(IContainer container, BrochurePerson person, BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().Text(person.Name ?? string.Empty)
                    .FontFamily("Mulish")
                    .FontSize(11)
                    .FontColor(skin.PrimaryColor)
                    .Bold();

                column.Item().PaddingTop(1).Height(1.5f).Background(skin.AccentColor);
                column.Item().PaddingBottom(2).Text(string.Empty);

                if (!string.IsNullOrWhiteSpace(person.Credentials))
                {
                    column.Item().Text(person.Credentials)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Italic();

                    column.Item().PaddingBottom(4).Text(string.Empty);
                }

                if (!string.IsNullOrWhiteSpace(person.Bio))
                {
                    column.Item().Text(person.Bio)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Justify()
                        .LineHeight(1f);
                }
            });
        }

        private static void ComposePersonPhoto(
            IContainer container,
            BrochurePerson person,
            Func<string?, string?, byte[]?> tryReadImage,
            Func<string?, string> resolvePath)
        {
            var imageBytes = tryReadImage(resolvePath(person.PhotoPath), "person photo");

            if (imageBytes is { Length: > 0 })
            {
                container.AlignTop().Image(imageBytes).FitWidth();
                return;
            }

            container.Background(PlaceholderGrey);
        }

        private static void ComposeOverviewSection(IContainer container, BrochureOverviewSection section, BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().PaddingBottom(4).Text((section.Heading ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish Black")
                    .FontSize(11)
                    .FontColor(skin.AccentColor);

                column.Item().Text(section.Body ?? string.Empty)
                    .FontFamily("Mulish")
                    .FontSize(9)
                    .FontColor(skin.PrimaryColor)
                    .Justify()
                    .LineHeight(1.2f);
            });
        }

        private static void ComposeContactPage(IContainer container, BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().Text("CONTACT")
                    .FontFamily("Mulish Black")
                    .FontSize(14)
                    .FontColor(skin.AccentColor);

                column.Item().PaddingTop(4).Height(2).Background(skin.AccentColor);
                column.Item().PaddingBottom(16).Text(string.Empty);

                for (var i = 0; i < Offices.Length; i += 2)
                {
                    var leftOffice = Offices[i];
                    var hasRightOffice = i + 1 < Offices.Length;

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Element(cell => ComposeOfficeCell(cell, leftOffice, skin));
                        row.Spacing(ContactColumnGapInches, Unit.Inch);

                        if (hasRightOffice)
                        {
                            var rightOffice = Offices[i + 1];
                            row.RelativeItem().Element(cell => ComposeOfficeCell(cell, rightOffice, skin));
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
            (string Region, string Contact, string Phone, string Email, string Hours) office,
            BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().Text(office.Region)
                    .FontFamily("Mulish")
                    .FontSize(11)
                    .FontColor(skin.PrimaryColor)
                    .Bold();

                column.Item().PaddingTop(4).Height(1).Background(PlaceholderGrey);
                column.Item().PaddingBottom(4).Text(string.Empty);

                column.Item().Text(office.Contact)
                    .FontFamily("Mulish")
                    .FontSize(9)
                    .FontColor(skin.PrimaryColor);

                column.Item().PaddingTop(4).Text(text =>
                {
                    text.DefaultTextStyle(BrochureRenderHelpers.GetBodyTextStyle(9, skin.PrimaryColor));
                    text.Span("T: ").Bold();
                    text.Span(office.Phone);
                });

                column.Item().PaddingTop(2).Text(text =>
                {
                    text.DefaultTextStyle(BrochureRenderHelpers.GetBodyTextStyle(9, skin.PrimaryColor));
                    text.Span("E: ").Bold();
                    text.Span(office.Email).FontColor("#0563C1");
                });

                column.Item().PaddingTop(2).Text(text =>
                {
                    text.DefaultTextStyle(BrochureRenderHelpers.GetBodyTextStyle(9, skin.PrimaryColor));
                    text.Span("H: ").Bold();
                    text.Span(office.Hours);
                });
            });
        }

        private static void ComposeHeader(
            IContainer container,
            BrochureContent content,
            BrochureSkinDefinition skin,
            byte[]? logoBytes)
        {
            container
                .Height(HeaderHeightInches, Unit.Inch)
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
                });
        }

        private static void ComposeCoverTopZone(
            IContainer container,
            string coverTitle,
            BrochureSkinDefinition skin,
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
                        .Background(skin.PrimaryColor)
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
                            .FontColor(skin.AccentColor)
                            .Bold();
                    });
            });
        }

        private static void ComposeCoverBottomBanner(IContainer container, BrochureSkinDefinition skin, byte[]? coverLogoBytes)
        {
            container.Column(column =>
            {
                column.Item()
                    .Height(CoverBottomBannerContentHeightInches, Unit.Inch)
                    .Background(skin.PrimaryColor)
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
                    .Background(skin.AccentColor);
            });
        }

        private static void ComposeFooter(IContainer container, BrochureContent content, BrochureSkinDefinition skin)
        {
            container.PaddingHorizontal(0.25f, Unit.Inch).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    var companyName = string.IsNullOrWhiteSpace(content.CompanyName)
                        ? string.Empty
                        : content.CompanyName.Trim() + " ";

                    text.DefaultTextStyle(BrochureRenderHelpers.GetBodyTextStyle(8, skin.PrimaryColor));
                    text.Span(companyName + OfficeAddress).FontColor(skin.PrimaryColor);
                });

                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(BrochureRenderHelpers.GetBodyTextStyle(8, skin.PrimaryColor));
                    text.Span("Page ").FontColor(skin.PrimaryColor);
                    text.CurrentPageNumber().FontColor(skin.PrimaryColor);
                    text.Span(" of ").FontColor(skin.PrimaryColor);
                    text.TotalPages().FontColor(skin.PrimaryColor);
                });
            });
        }
    }
}
