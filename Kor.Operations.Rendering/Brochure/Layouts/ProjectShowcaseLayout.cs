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
    internal sealed class ProjectShowcaseLayout : IBrochureLayoutTemplate
    {
        private const float CoverTopZoneHeightInches = 9.35f;
        private const float ShowcasePhotoHeightInches = 4.5f;
        private const float PersonPhotoHeightInches = 2.0f;
        private const float PersonGridGapInches = 0.3f;

        public string Id => "project-showcase";

        public string DisplayName => "Project Showcase";

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
                    .Element(topZone => BrochureRenderHelpers.ComposeCoverTopZone(
                        topZone,
                        coverTitle,
                        ctx.Skin,
                        ctx.CoverPhotoBytes,
                        overlayColor,
                        DateTime.Now.Year));

                column.Item()
                    .Height(BrochureRenderHelpers.CoverBottomBannerHeightInches, Unit.Inch)
                    .Element(bottomBanner => BrochureRenderHelpers.ComposeCoverBottomBanner(
                        bottomBanner,
                        ctx.Skin,
                        ctx.CoverLogoBytes,
                        BrochureRenderHelpers.GetContact(ctx.Content)));
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
                for (var i = 0; i < projectGroup.Count; i++)
                {
                    ctx.CancellationToken.ThrowIfCancellationRequested();
                    var project = projectGroup[i];
                    var showHeading = i == 0;

                    container.Page(page =>
                    {
                        BrochureRenderHelpers.ConfigureStandardPage(page);

                        page.Header().PaddingHorizontal(-1, Unit.Inch)
                            .Element(header => BrochureRenderHelpers.ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                        page.Content().PaddingTop(18).Element(body =>
                        {
                            body.Column(column =>
                            {
                                if (showHeading)
                                    column.Item().Element(c => ComposeSectionHeading(c, section, ctx.Skin));

                                column.Item().Element(slot => ComposeShowcaseProject(slot, project, ctx.Skin, ctx));
                            });
                        });

                        page.Footer().PaddingHorizontal(-1, Unit.Inch)
                            .MinHeight(0.35f, Unit.Inch)
                            .AlignBottom()
                            .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
                    });
                }
            }
        }

        public void ComposePersonnel(IDocumentContainer container, BrochureBlock block, BrochureRenderContext ctx)
        {
            var people = block.People;
            if (people.Count == 0)
                return;

            var peopleGroups = new List<List<BrochurePerson>>();
            for (var i = 0; i < people.Count; i += 2)
                peopleGroups.Add(people.Skip(i).Take(2).ToList());

            foreach (var peopleGroup in peopleGroups)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                container.Page(page =>
                {
                    BrochureRenderHelpers.ConfigureStandardPage(page);

                    page.Header().PaddingHorizontal(-1, Unit.Inch)
                        .Element(header => BrochureRenderHelpers.ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

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

                            column.Item().Row(row =>
                            {
                                row.RelativeItem().Element(cell => ComposePersonCell(cell, peopleGroup[0], ctx.Skin, ctx));
                                row.Spacing(PersonGridGapInches, Unit.Inch);

                                if (peopleGroup.Count > 1)
                                    row.RelativeItem().Element(cell => ComposePersonCell(cell, peopleGroup[1], ctx.Skin, ctx));
                                else
                                    row.RelativeItem();
                            });
                        });
                    });

                    page.Footer().PaddingHorizontal(-1, Unit.Inch)
                        .MinHeight(0.35f, Unit.Inch)
                        .AlignBottom()
                        .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
                });
            }
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
                        .Element(header => BrochureRenderHelpers.ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                    page.Content().PaddingTop(18).Element(body =>
                    {
                        body.Column(column =>
                        {
                            for (var i = 0; i < overviewGroup.Count; i++)
                            {
                                if (i > 0)
                                    column.Item().Height(14);

                                var section = overviewGroup[i];
                                column.Item().Element(slot => BrochureRenderHelpers.ComposeOverviewSection(slot, section, ctx.Skin));
                            }
                        });
                    });

                    page.Footer().PaddingHorizontal(-1, Unit.Inch)
                        .MinHeight(0.35f, Unit.Inch)
                        .AlignBottom()
                        .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
                });
            }
        }

        public void ComposeContact(IDocumentContainer container, BrochureRenderContext ctx)
        {
            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => BrochureRenderHelpers.ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                page.Content().PaddingTop(18).Element(body =>
                    BrochureRenderHelpers.ComposeContactPage(body, ctx.Skin, BrochureRenderHelpers.GetContact(ctx.Content)));

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
            });
        }

        public void ComposeClientList(IDocumentContainer container, BrochureBlock block, BrochureRenderContext ctx)
        {
            if (block.ClientNames.Count == 0)
                return;

            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => BrochureRenderHelpers.ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                page.Content().PaddingTop(18).Element(body =>
                    BrochureRenderHelpers.ComposeClientListPage(body, block, ctx.Skin));

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .MinHeight(0.35f, Unit.Inch)
                    .AlignBottom()
                    .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
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

                        return block.Section.Projects.Count;
                    }

                    if (block.BlockType == BrochureBlockType.Personnel)
                    {
                        if (block.People.Count == 0)
                            return 0;

                        return (int)Math.Ceiling(block.People.Count / 2d);
                    }

                    if (block.BlockType == BrochureBlockType.ClientList)
                        return block.ClientNames.Count == 0 ? 0 : 1;

                    if (block.People.Count == 0)
                        return 0;

                    return (block.People.Count + 1) / 2;
                });

        private static void ComposeShowcaseProject(
            IContainer container,
            BrochureProject project,
            BrochureSkinDefinition skin,
            BrochureRenderContext ctx)
        {
            container.Column(column =>
            {
                column.Item()
                    .Height(ShowcasePhotoHeightInches, Unit.Inch)
                    .Element(photoContainer => ComposeShowcaseProjectPhoto(photoContainer, project, ctx));

                column.Item()
                    .PaddingTop(16)
                    .Element(textContainer => BrochureRenderHelpers.ComposeProjectText(textContainer, project, skin));
            });
        }

        private static void ComposeShowcaseProjectPhoto(IContainer container, BrochureProject project, BrochureRenderContext ctx)
        {
            var photo = project.Photos.FirstOrDefault();
            var imageBytes = photo is null
                ? null
                : photo.ImageBytes is { Length: > 0 }
                    ? photo.ImageBytes
                    : ctx.ReadImage(ctx.ResolvePath(photo.FilePath), "project photo");

            if (imageBytes is { Length: > 0 })
            {
                container.Image(imageBytes).FitWidth();
                return;
            }

            container.Background(BrochureRenderHelpers.PlaceholderGrey);
        }

        private static void ComposePersonCell(
            IContainer container,
            BrochurePerson person,
            BrochureSkinDefinition skin,
            BrochureRenderContext ctx)
        {
            container.Column(column =>
            {
                column.Item()
                    .Height(PersonPhotoHeightInches, Unit.Inch)
                    .Element(photoContainer => ComposePersonPhoto(photoContainer, person, ctx));

                column.Item()
                    .PaddingTop(12)
                    .Element(textContainer => ComposePersonText(textContainer, person, skin));
            });
        }

        private static void ComposePersonPhoto(IContainer container, BrochurePerson person, BrochureRenderContext ctx)
        {
            var imageBytes = person.PhotoBytes is { Length: > 0 }
                ? person.PhotoBytes
                : ctx.ReadImage(ctx.ResolvePath(person.PhotoPath), "person photo");

            if (imageBytes is { Length: > 0 })
            {
                container.Image(imageBytes).FitWidth();
                return;
            }

            container.Background(BrochureRenderHelpers.PlaceholderGrey);
        }

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

                column.Item().Height(1).Background(BrochureRenderHelpers.PlaceholderGrey);
                column.Item().PaddingBottom(12).Text(string.Empty);
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

    }
}
