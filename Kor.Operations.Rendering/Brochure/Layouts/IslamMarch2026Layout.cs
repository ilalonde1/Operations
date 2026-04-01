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
    internal sealed class IslamMarch2026Layout : IBrochureLayoutTemplate
    {
        private const float CoverTopZoneHeightInches = 9.35f;
        private const string IslamHeaderGray = "#435363";
        private const float IslamCoverSidebarWidthInches = 0.85f;
        private static readonly StandardPortfolioLayout _standard = new();

        public string Id => "islam-march-2026";
        public string DisplayName => "Islam March 2026";

        // ── Cover: standard layered structure with right-aligned KOR brand taglines ─
        public void ComposeCoverPage(IContainer container, BrochureRenderContext ctx)
        {
            var clampedOpacity = Math.Clamp(ctx.Content.CoverPhotoOpacity, 0f, 1f);
            var overlayColor = BrochureRenderHelpers.BuildOverlayColor(IslamHeaderGray, clampedOpacity);
            var coverTitle = string.IsNullOrWhiteSpace(ctx.Content.CoverTitle)
                ? string.IsNullOrWhiteSpace(ctx.Content.TemplateName) ? "KOR Structural" : ctx.Content.TemplateName
                : ctx.Content.CoverTitle;

            // Use proposal cover photo if set; fall back to the bundled default for this template
            var coverPhotoBytes = ctx.CoverPhotoBytes is { Length: > 0 }
                ? ctx.CoverPhotoBytes
                : ctx.ReadImage(ctx.ResolvePath(@"Brochures\islam-cover.jpg"), "Islam March 2026 default cover");

            container.Column(column =>
            {
                column.Item()
                    .Height(CoverTopZoneHeightInches, Unit.Inch)
                    .Layers(layers =>
                    {
                        if (coverPhotoBytes is { Length: > 0 })
                            layers.PrimaryLayer().Image(coverPhotoBytes).FitArea();
                        else
                            layers.PrimaryLayer().Background(IslamHeaderGray).Extend();

                        layers.Layer().Background(overlayColor).Extend();

                        // Right gray sidebar strip — matches target document
                        layers.Layer().Row(row =>
                        {
                            row.RelativeItem();
                            row.ConstantItem(IslamCoverSidebarWidthInches, Unit.Inch)
                               .Background(IslamHeaderGray)
                               .Extend();
                        });

                        layers.Layer()
                            .PaddingLeft(0.5f, Unit.Inch)
                            .PaddingRight(0.5f + IslamCoverSidebarWidthInches, Unit.Inch)
                            .PaddingTop(5.2f, Unit.Inch)
                            .Column(col =>
                            {
                                col.Item()
                                    .Text(coverTitle.ToUpperInvariant())
                                    .FontFamily("Mulish Black").FontSize(42).FontColor(Colors.White);

                                col.Item().PaddingTop(2)
                                    .Text(DateTime.Now.Year.ToString())
                                    .FontFamily("Mulish Black").FontSize(42).FontColor(Colors.White);

                                col.Item().PaddingTop(16)
                                    .Text("2000+ PROJECTS IN 4 COUNTRIES \u00b7 5 STATES \u00b7 7 PROVINCES \u00b7 1 COMPANY")
                                    .FontFamily("Mulish").FontSize(10).FontColor(Colors.White);

                                col.Item().PaddingTop(22).AlignRight().Text(text =>
                                {
                                    text.Span("we are ").FontFamily("Mulish").FontSize(14).FontColor(Colors.White);
                                    text.Span("KOR").FontFamily("Mulish Black").FontSize(26).FontColor(Colors.White);
                                });

                                col.Item().PaddingTop(4).AlignRight()
                                    .Text("we engineer the vertical future")
                                    .FontFamily("Mulish").FontSize(13).FontColor(ctx.Skin.AccentColor).Italic();
                            });
                    });

                column.Item()
                    .Height(BrochureRenderHelpers.CoverBottomBannerHeightInches, Unit.Inch)
                    .Element(b => BrochureRenderHelpers.ComposeCoverBottomBanner(
                        b, IslamHeaderSkin(ctx.Skin), ctx.LogoBytes, BrochureRenderHelpers.GetContact(ctx.Content)));
            });
        }

        // ── Disclaimer: standard header/footer, disclaimer text bottom-right ──────
        public void ComposeAfterCover(IDocumentContainer container, BrochureRenderContext ctx)
        {
            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => BrochureRenderHelpers.ComposeHeader(
                        header, ctx.Content, IslamHeaderSkin(ctx.Skin), ctx.LogoBytes));

                page.Content().PaddingTop(18).AlignBottom().AlignRight().Column(col =>
                {
                    col.Item()
                        .Text("Disclaimer: All images in this portfolio are used courtesy of their respective " +
                              "owners. KOR Structural acknowledges the original copyright holders. Images are " +
                              "included for illustrative and informational purposes only.")
                        .FontFamily("Mulish").FontSize(8).FontColor(ctx.Skin.PrimaryColor)
                        .LineHeight(1.35f);

                    col.Item().PaddingTop(8)
                        .Text($"\u00a9 {DateTime.Now.Year} Kor Structural. All rights reserved.")
                        .FontFamily("Mulish").FontSize(8).FontColor(ctx.Skin.PrimaryColor);
                });

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .MinHeight(0.35f, Unit.Inch)
                    .AlignBottom()
                    .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
            });
        }

        // ── Section: heading page with first project, then exactly 2 per page ──────
        public void ComposeSection(IDocumentContainer container, BrochureSection section, BrochureRenderContext ctx)
        {
            if (section.Projects.Count == 0) return;
            var islamCtx = WithIslamStyle(ctx);
            var projects = section.Projects.ToList();

            // Available content height after header/footer/paddingTop ≈ 658pt; 2 blocks + gap = 316+26+316
            const float ProjectBlockHeightPt = 316f;
            const float PairGapPt = 26f;

            // First page: heading/blurb + project[0]
            ctx.CancellationToken.ThrowIfCancellationRequested();
            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);
                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(h => BrochureRenderHelpers.ComposeHeader(
                        h, islamCtx.Content, IslamHeaderSkin(ctx.Skin), ctx.LogoBytes));
                page.Content().PaddingTop(18).Column(col =>
                {
                    col.Item().Column(hcol =>
                    {
                        hcol.Item().Text((section.Heading ?? string.Empty).ToUpperInvariant())
                            .FontFamily("Mulish Black").FontSize(11).FontColor(islamCtx.Skin.AccentColor);
                        hcol.Item().PaddingTop(3).Height(1.5f).Background(islamCtx.Skin.AccentColor);
                        hcol.Item().PaddingBottom(6).Text(string.Empty);
                        if (!string.IsNullOrWhiteSpace(section.Blurb))
                        {
                            hcol.Item().Text(section.Blurb)
                                .FontFamily("Mulish").FontSize(9)
                                .FontColor(islamCtx.Skin.PrimaryColor).Justify();
                            hcol.Item().PaddingBottom(12).Text(string.Empty);
                        }
                        hcol.Item().Height(1).Background(BrochureRenderHelpers.PlaceholderGrey);
                        hcol.Item().PaddingBottom(12).Text(string.Empty);
                    });
                    col.Item().MinHeight(ProjectBlockHeightPt)
                        .Element(c => ComposeIslamProjectBlock(c, projects[0], islamCtx, true));
                });
                page.Footer().PaddingHorizontal(-1, Unit.Inch).MinHeight(0.35f, Unit.Inch).AlignBottom()
                    .Element(f => BrochureRenderHelpers.ComposeFooter(f, islamCtx.Content, islamCtx.Skin));
            });

            // Subsequent pages: exactly 2 projects per page at equal fixed heights
            for (var p = 1; p < projects.Count; p += 2)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                var proj1 = projects[p];
                var proj2 = p + 1 < projects.Count ? projects[p + 1] : null;
                var p1Left = p % 2 == 0;
                var p2Left = (p + 1) % 2 == 0;

                container.Page(page =>
                {
                    BrochureRenderHelpers.ConfigureStandardPage(page);
                    page.Header().PaddingHorizontal(-1, Unit.Inch)
                        .Element(h => BrochureRenderHelpers.ComposeHeader(
                            h, islamCtx.Content, IslamHeaderSkin(ctx.Skin), ctx.LogoBytes));
                    page.Content().PaddingTop(18).Column(col =>
                    {
                        col.Item().Height(ProjectBlockHeightPt)
                            .Element(c => ComposeIslamProjectBlock(c, proj1, islamCtx, p1Left));
                        if (proj2 is not null)
                        {
                            col.Item().Height(PairGapPt);
                            col.Item().Height(ProjectBlockHeightPt)
                                .Element(c => ComposeIslamProjectBlock(c, proj2, islamCtx, p2Left));
                        }
                    });
                    page.Footer().PaddingHorizontal(-1, Unit.Inch).MinHeight(0.35f, Unit.Inch).AlignBottom()
                        .Element(f => BrochureRenderHelpers.ComposeFooter(f, islamCtx.Content, islamCtx.Skin));
                });
            }
        }

        // ── Personnel: heading, blurb, people, closing tagline ───────────────────────
        public void ComposePersonnel(IDocumentContainer container, BrochureBlock block, BrochureRenderContext ctx)
        {
            var people = block.People;
            if (people.Count == 0) return;
            var islamCtx = WithIslamStyle(ctx);

            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);
                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(h => BrochureRenderHelpers.ComposeHeader(h, islamCtx.Content, IslamHeaderSkin(ctx.Skin), ctx.LogoBytes));
                page.Content().PaddingTop(18).Element(body =>
                {
                    body.Column(col =>
                    {
                        col.Item().Text((block.PersonnelHeading ?? string.Empty).ToUpperInvariant())
                            .FontFamily("Mulish Black").FontSize(14).FontColor(islamCtx.Skin.AccentColor);
                        col.Item().PaddingTop(4).Height(2).Background(islamCtx.Skin.AccentColor);
                        col.Item().PaddingBottom(12).Text(string.Empty);

                        if (!string.IsNullOrWhiteSpace(block.PersonnelBlurb))
                        {
                            col.Item().Text(block.PersonnelBlurb)
                                .FontFamily("Mulish").FontSize(10).FontColor(islamCtx.Skin.PrimaryColor).Italic();
                            col.Item().PaddingBottom(12).Text(string.Empty);
                        }

                        foreach (var person in people)
                            col.Item().MinHeight(3.5f, Unit.Inch)
                                .Element(slot => ComposeIslamPersonBlock(slot, person, islamCtx));

                        col.Item().PaddingTop(16).AlignCenter()
                            .Text("This is our team \u2014 let\u2019s build the future together, come partner with us!")
                            .FontFamily("Mulish").FontSize(11).FontColor(islamCtx.Skin.AccentColor).Italic();
                    });
                });
                page.Footer().PaddingHorizontal(-1, Unit.Inch).MinHeight(0.35f, Unit.Inch).AlignBottom()
                    .Element(f => BrochureRenderHelpers.ComposeFooter(f, islamCtx.Content, islamCtx.Skin));
            });
        }

        public void ComposeClientList(IDocumentContainer container, BrochureBlock block, BrochureRenderContext ctx)
            => _standard.ComposeClientList(container, block, WithIslamStyle(ctx));

        // ── Overview: standard pages, honeybee inline in right column on last page ─
        public void ComposeOverview(
            IDocumentContainer container,
            IReadOnlyList<BrochureOverviewSection> sections,
            IReadOnlyList<int>? pageBreaks,
            BrochureRenderContext ctx)
        {
            if (sections.Count == 0)
                return;

            // Build page groups (same logic as StandardPortfolioLayout)
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

            var honeybeeBytes = ctx.ReadImage(ctx.ResolvePath(@"Brochures\honeybee.png"), "honeybee");

            for (var g = 0; g < overviewGroups.Count; g++)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();

                var group = overviewGroups[g];
                var isLastGroup = g == overviewGroups.Count - 1;

                container.Page(page =>
                {
                    BrochureRenderHelpers.ConfigureStandardPage(page);

                    page.Header().PaddingHorizontal(-1, Unit.Inch)
                        .Element(header => BrochureRenderHelpers.ComposeHeader(
                            header, ctx.Content, ctx.Skin, ctx.CoverLogoBytes));

                    page.Content().PaddingTop(18).Element(body =>
                    {
                        if (isLastGroup && honeybeeBytes is { Length: > 0 })
                        {
                            // Last page: sections left (60%), honeybee + caption right (40%)
                            body.Row(row =>
                            {
                                row.RelativeItem(3).Column(col =>
                                {
                                    for (var i = 0; i < group.Count; i++)
                                    {
                                        if (i > 0) col.Item().Height(14);
                                        col.Item().Element(slot =>
                                            ComposeIslamOverviewSection(slot, group[i], ctx.Skin));
                                    }
                                });

                                row.Spacing(0.25f, Unit.Inch);

                                row.RelativeItem(2).Column(col =>
                                {
                                    col.Item().AlignTop().Image(honeybeeBytes).FitWidth();

                                    col.Item().PaddingTop(4).AlignRight()
                                        .Text("\u201cLike a hive, we build strength through collaboration.\u201d")
                                        .FontFamily("Mulish").FontSize(8).FontColor(ctx.Skin.PrimaryColor)
                                        .Italic().LineHeight(1.2f);

                                    col.Item().PaddingTop(2).AlignRight()
                                        .Text("\u2014 John Markulin, Managing Principal")
                                        .FontFamily("Mulish").FontSize(8).FontColor(ctx.Skin.PrimaryColor);
                                });
                            });
                        }
                        else
                        {
                            // Non-last pages: standard full-width layout
                            body.Column(col =>
                            {
                                for (var i = 0; i < group.Count; i++)
                                {
                                    if (i > 0) col.Item().Height(14);
                                    col.Item().Element(slot =>
                                        ComposeIslamOverviewSection(slot, group[i], ctx.Skin));
                                }
                            });
                        }
                    });

                    page.Footer().PaddingHorizontal(-1, Unit.Inch)
                        .MinHeight(0.35f, Unit.Inch)
                        .AlignBottom()
                        .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
                });
            }
        }

        // ── Contact: offices grid with map+quote fitting odd or even office counts ─
        public void ComposeContact(IDocumentContainer container, BrochureRenderContext ctx)
        {
            var contact = BrochureRenderHelpers.GetContact(ctx.Content);
            var mapBytes = ctx.ReadImage(ctx.ResolvePath(@"Brochures\contactmap.png"), "contact map");
            var offices = contact.Offices;
            var hasOddOffice = offices.Count % 2 == 1;
            // Offices rendered in standard left|right pairs (excludes last office when count is odd)
            var pairedCount = hasOddOffice ? offices.Count - 1 : offices.Count;

            container.Page(page =>
            {
                BrochureRenderHelpers.ConfigureStandardPage(page);

                page.Header().PaddingHorizontal(-1, Unit.Inch)
                    .Element(header => BrochureRenderHelpers.ComposeHeader(
                        header, ctx.Content, IslamHeaderSkin(ctx.Skin), ctx.LogoBytes));

                page.Content().PaddingTop(18).Element(body =>
                {
                    body.Column(col =>
                    {
                        col.Item().Text("CONTACT")
                            .FontFamily("Mulish Black").FontSize(14).FontColor(ctx.Skin.PrimaryColor);

                        col.Item().PaddingTop(4).Height(2).Background(ctx.Skin.AccentColor);
                        col.Item().PaddingBottom(16).Text(string.Empty);

                        // Paired offices
                        for (var i = 0; i < pairedCount; i += 2)
                        {
                            col.Item().Row(row =>
                            {
                                row.RelativeItem().Element(cell =>
                                    BrochureRenderHelpers.ComposeOfficeCell(cell, offices[i], ctx.Skin));
                                row.Spacing(BrochureRenderHelpers.ContactColumnGapInches, Unit.Inch);
                                row.RelativeItem().Element(cell =>
                                    BrochureRenderHelpers.ComposeOfficeCell(cell, offices[i + 1], ctx.Skin));
                            });

                            if (i + 2 < pairedCount)
                                col.Item().PaddingBottom(16).Text(string.Empty);
                        }

                        col.Item().PaddingTop(16).Height(1.5f).Background(ctx.Skin.AccentColor);

                        if (hasOddOffice)
                        {
                            // Odd count (e.g. 5): last office left, map right, quote below last office
                            col.Item().PaddingTop(12).Row(lastRow =>
                            {
                                lastRow.RelativeItem(2).Column(leftCol =>
                                {
                                    leftCol.Item().Element(cell =>
                                        BrochureRenderHelpers.ComposeOfficeCell(cell, offices[offices.Count - 1], ctx.Skin));

                                    leftCol.Item().PaddingTop(20)
                                        .Text("\u201cOur work spans North America, and we continue to seek new " +
                                              "partnerships across the continent and internationally as we expand " +
                                              "into what\u2019s next.\u201d")
                                        .FontFamily("Mulish").FontSize(9).FontColor(ctx.Skin.PrimaryColor)
                                        .Italic().Justify().LineHeight(1.35f);

                                    leftCol.Item().PaddingTop(6)
                                        .Text("\u2014 Jim DesRoches, Managing Principal")
                                        .FontFamily("Mulish").FontSize(8).FontColor(ctx.Skin.PrimaryColor).Bold();
                                });

                                lastRow.Spacing(BrochureRenderHelpers.ContactColumnGapInches, Unit.Inch);

                                if (mapBytes is { Length: > 0 })
                                    lastRow.RelativeItem(3).AlignTop().Image(mapBytes).FitWidth();
                                else
                                    lastRow.RelativeItem(3);
                            });
                        }
                        else
                        {
                            // Even count (e.g. 4): quote left, map right as separate bottom row
                            col.Item().PaddingTop(12).Row(mapRow =>
                            {
                                mapRow.RelativeItem(2).AlignMiddle().Column(quoteCol =>
                                {
                                    quoteCol.Item()
                                        .Text("\u201cOur work spans North America, and we continue to seek new " +
                                              "partnerships across the continent and internationally as we expand " +
                                              "into what\u2019s next.\u201d")
                                        .FontFamily("Mulish").FontSize(9).FontColor(ctx.Skin.PrimaryColor)
                                        .Italic().Justify().LineHeight(1.35f);

                                    quoteCol.Item().PaddingTop(6).AlignCenter()
                                        .Text("\u2014 Jim DesRoches, Managing Principal")
                                        .FontFamily("Mulish").FontSize(8).FontColor(ctx.Skin.PrimaryColor).Bold();
                                });

                                mapRow.Spacing(BrochureRenderHelpers.ContactColumnGapInches, Unit.Inch);

                                if (mapBytes is { Length: > 0 })
                                    mapRow.RelativeItem(3).AlignTop().Image(mapBytes).FitWidth();
                                else
                                    mapRow.RelativeItem(3);
                            });
                        }
                    });
                });

                page.Footer().PaddingHorizontal(-1, Unit.Inch)
                    .Element(footer => BrochureRenderHelpers.ComposeFooter(footer, ctx.Content, ctx.Skin));
            });
        }

        // ── Page count: standard + 1 for disclaimer (honeybee is on existing page) ─
        public int EstimatePageCount(BrochureContent content) =>
            _standard.EstimatePageCount(content) + 1;

        // ── Interior pages: gray header background, full-color logo ─────────────────
        private static BrochureSkinDefinition IslamHeaderSkin(BrochureSkinDefinition skin) => new()
        {
            Id          = skin.Id,
            DisplayName = skin.DisplayName,
            PrimaryColor = IslamHeaderGray,
            AccentColor  = skin.AccentColor,
            HeaderText   = skin.HeaderText
        };

        private static BrochureRenderContext WithIslamStyle(BrochureRenderContext ctx) => new()
        {
            Content           = ctx.Content,
            Skin              = IslamHeaderSkin(ctx.Skin),
            LogoBytes         = ctx.LogoBytes,
            CoverLogoBytes    = ctx.CoverLogoBytes,
            CoverPhotoBytes   = ctx.CoverPhotoBytes,
            ReadImage         = ctx.ReadImage,
            ResolvePath       = ctx.ResolvePath,
            CancellationToken = ctx.CancellationToken
        };

        // ── Project block: photo + text, alternating sides ───────────────────────────
        private static void ComposeIslamProjectBlock(IContainer container, BrochureProject project, BrochureRenderContext ctx, bool photoOnLeft)
        {
            container.PaddingVertical(0.05f, Unit.Inch).Row(row =>
            {
                if (photoOnLeft)
                {
                    row.ConstantItem(3f, Unit.Inch).Element(p => ComposeIslamProjectPhoto(p, project, ctx));
                    row.Spacing(0.2f, Unit.Inch);
                    row.RelativeItem().Element(t => BrochureRenderHelpers.ComposeProjectText(t, project, ctx.Skin));
                }
                else
                {
                    row.RelativeItem().Element(t => BrochureRenderHelpers.ComposeProjectText(t, project, ctx.Skin));
                    row.Spacing(0.2f, Unit.Inch);
                    row.ConstantItem(3f, Unit.Inch).Element(p => ComposeIslamProjectPhoto(p, project, ctx));
                }
            });
        }

        private static void ComposeIslamProjectPhoto(IContainer container, BrochureProject project, BrochureRenderContext ctx)
        {
            var photo = project.Photos.FirstOrDefault();
            var bytes = photo is null ? null
                : photo.ImageBytes is { Length: > 0 } ? photo.ImageBytes
                : ctx.ReadImage(ctx.ResolvePath(photo.FilePath), "project photo");
            if (bytes is { Length: > 0 })
                container.AlignTop().Image(bytes).FitArea();
            else
                container.Background(BrochureRenderHelpers.PlaceholderGrey);
        }

        // ── Person block: photo left, name/credentials/bio right ─────────────────────
        private static void ComposeIslamPersonBlock(IContainer container, BrochurePerson person, BrochureRenderContext ctx)
        {
            container.PaddingVertical(0.035f, Unit.Inch).Row(row =>
            {
                row.ConstantItem(2f, Unit.Inch).Element(p =>
                {
                    var bytes = person.PhotoBytes is { Length: > 0 } ? person.PhotoBytes
                        : ctx.ReadImage(ctx.ResolvePath(person.PhotoPath), "person photo");
                    if (bytes is { Length: > 0 })
                        p.AlignTop().Image(bytes).FitWidth();
                    else
                        p.Background(BrochureRenderHelpers.PlaceholderGrey);
                });
                row.Spacing(0.2f, Unit.Inch);
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(person.Name ?? string.Empty)
                        .FontFamily("Mulish").FontSize(11).FontColor(ctx.Skin.PrimaryColor).Bold();
                    col.Item().PaddingTop(1).Height(1.5f).Background(ctx.Skin.AccentColor);
                    col.Item().PaddingBottom(2).Text(string.Empty);
                    if (!string.IsNullOrWhiteSpace(person.Credentials))
                    {
                        col.Item().Text(person.Credentials)
                            .FontFamily("Mulish").FontSize(9).FontColor(ctx.Skin.PrimaryColor).Italic();
                        col.Item().PaddingBottom(4).Text(string.Empty);
                    }
                    if (!string.IsNullOrWhiteSpace(person.Bio))
                        col.Item().Text(person.Bio)
                            .FontFamily("Mulish").FontSize(8).FontColor(ctx.Skin.PrimaryColor)
                            .Justify().LineHeight(1f);
                });
            });
        }

        // ── Overview section: AccentColor headings to match target document ─────────
        private static void ComposeIslamOverviewSection(
            IContainer container,
            BrochureOverviewSection section,
            BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                column.Item().PaddingBottom(4)
                    .Text((section.Heading ?? string.Empty).ToUpperInvariant())
                    .FontFamily("Mulish Black").FontSize(11).FontColor(skin.AccentColor);

                column.Item()
                    .Text(section.Body ?? string.Empty)
                    .FontFamily("Mulish").FontSize(9).FontColor(skin.PrimaryColor)
                    .Justify().LineHeight(1.2f);
            });
        }
    }
}
