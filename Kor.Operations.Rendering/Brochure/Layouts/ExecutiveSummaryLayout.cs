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
    internal sealed class ExecutiveSummaryLayout : IBrochureLayoutTemplate
    {

        public string Id => "executive-summary";

        public string DisplayName => "Executive Summary";

        public void ComposeCoverPage(IContainer container, BrochureRenderContext ctx)
        {
            var coverTitle = string.IsNullOrWhiteSpace(ctx.Content.CoverTitle)
                ? string.IsNullOrWhiteSpace(ctx.Content.TemplateName)
                    ? "KOR Structural"
                    : ctx.Content.TemplateName
                : ctx.Content.CoverTitle;

            container.Column(column =>
            {
                column.Item()
                    .ExtendVertical()
                    .Background(ctx.Skin.PrimaryColor)
                    .AlignMiddle()
                    .AlignCenter()
                    .Column(contentColumn =>
                    {
                        contentColumn.Item().AlignCenter().Text(coverTitle.ToUpperInvariant())
                            .FontFamily("Mulish Black")
                            .FontSize(36)
                            .FontColor(Colors.White);

                        contentColumn.Item().Height(16);

                        contentColumn.Item().AlignCenter().Text(DateTime.Now.Year.ToString())
                            .FontFamily("Mulish")
                            .FontSize(18)
                            .FontColor(ctx.Skin.AccentColor)
                            .Bold();

                        contentColumn.Item().Height(24);

                        contentColumn.Item()
                            .AlignCenter()
                            .Width(2f, Unit.Inch)
                            .Height(2)
                            .Background(ctx.Skin.AccentColor);
                    });

                column.Item()
                    .Height(BrochureRenderHelpers.CoverBottomBannerHeightInches, Unit.Inch)
                    .Element(bottomBanner => BrochureRenderHelpers.ComposeCoverBottomBanner(bottomBanner, ctx.Skin, ctx.CoverLogoBytes));
            });
        }

        public void ComposeSection(IDocumentContainer container, BrochureSection section, BrochureRenderContext ctx)
        {
            if (section.Projects.Count == 0)
                return;

            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => BrochureRenderHelpers.ComposeHeader(header, ctx.Content, ctx.Skin, ctx.LogoBytes));

                page.Content().PaddingTop(18).Element(body =>
                {
                    body.Column(column =>
                    {
                        column.Item().Element(c => ComposeSectionHeading(c, section, ctx.Skin));

                        foreach (var project in section.Projects)
                        {
                            column.Item().Element(c => ComposeCompactProject(c, project, ctx.Skin));
                            column.Item().Height(10);
                        }
                    });
                });

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .MinHeight(0.35f, Unit.Inch)
                    .AlignBottom()
                    .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
            });
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

                        foreach (var person in people)
                        {
                            column.Item().Element(c => ComposeCompactPerson(c, person, ctx.Skin));
                            column.Item().Height(8);
                        }
                    });
                });

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .MinHeight(0.35f, Unit.Inch)
                    .AlignBottom()
                    .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
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
                .ToList();

            var orderedSplitIndexes = new HashSet<int>(splitIndexes);
            var overviewGroups = new List<List<BrochureOverviewSection>>();
            var currentGroup = new List<BrochureOverviewSection>();

            for (var i = 0; i < sections.Count; i++)
            {
                currentGroup.Add(sections[i]);

                if (orderedSplitIndexes.Contains(i))
                {
                    overviewGroups.Add(currentGroup);
                    currentGroup = new List<BrochureOverviewSection>();
                }
            }

            if (currentGroup.Count > 0)
                overviewGroups.Add(currentGroup);

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
                    BrochureRenderHelpers.ComposeContactPage(body, ctx.Skin));

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
            });
        }

        public int EstimatePageCount(BrochureContent content)
        {
            if (content.Blocks.Count == 0)
                return 1;

            return content.Blocks.Sum(static block =>
            {
                if (block.BlockType == BrochureBlockType.Section)
                {
                    if (block.Section is null || block.Section.Projects.Count == 0)
                        return 0;

                    return (int)Math.Ceiling(block.Section.Projects.Count / 4.0);
                }

                if (block.BlockType == BrochureBlockType.Personnel)
                {
                    if (block.People.Count == 0)
                        return 0;

                    return (int)Math.Ceiling(block.People.Count / 6.0);
                }

                if (block.BlockType == BrochureBlockType.Contact)
                    return 1;

                return 0;
            });
        }

        private static void ComposeCompactProject(IContainer container, BrochureProject project, BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().Text((project.ProjectName ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish")
                    .FontSize(9)
                    .FontColor(skin.PrimaryColor)
                    .Bold();

                column.Item().PaddingTop(2).Height(1).Background(skin.AccentColor);

                if (!string.IsNullOrWhiteSpace(project.ProjectDescription))
                {
                    column.Item().PaddingTop(2).Text(project.ProjectDescription)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Justify()
                        .LineHeight(1.15f);
                }

                var clientText = string.IsNullOrWhiteSpace(project.Client) ? null : $"Client: {project.Client}";
                var architectText = string.IsNullOrWhiteSpace(project.Architect) ? null : $"Architect: {project.Architect}";
                var summaryLine = clientText is not null && architectText is not null
                    ? clientText + "  |  " + architectText
                    : clientText ?? architectText;

                if (!string.IsNullOrWhiteSpace(summaryLine))
                {
                    column.Item().PaddingTop(2).Text(summaryLine)
                        .FontFamily("Mulish")
                        .FontSize(8)
                        .FontColor(skin.PrimaryColor)
                        .Italic();
                }
            });
        }

        private static void ComposeCompactPerson(IContainer container, BrochurePerson person, BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().Text(person.Name ?? string.Empty)
                    .FontFamily("Mulish")
                    .FontSize(10)
                    .FontColor(skin.PrimaryColor)
                    .Bold();

                if (!string.IsNullOrWhiteSpace(person.Credentials))
                {
                    column.Item().Text(person.Credentials)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Italic();
                }

                column.Item().PaddingTop(1).Height(1).Background(skin.AccentColor);

                if (!string.IsNullOrWhiteSpace(person.Bio))
                {
                    column.Item().PaddingTop(2).Text(person.Bio)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Justify()
                        .LineHeight(1.1f);
                }
            });
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
            });
        }

    }
}
