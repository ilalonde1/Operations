#nullable enable
using System;
using System.Linq;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kor.Operations.Rendering.Brochure
{
    internal static class BrochureRenderHelpers
    {
        internal const float HeaderHeightInches = 1.06f;
        internal const float CoverBottomBannerHeightInches = 1.65f;
        internal const float CoverBottomBannerContentHeightInches = 1.5944f;
        internal const float CoverBottomStripHeightPoints = 4f;
        internal const float CoverBannerLogoWidthInches = 1.5f;
        internal const float ContactColumnGapInches = 0.3f;
        internal const string PlaceholderGrey = "#E7E6E6";

        internal static void ConfigureStandardPage(PageDescriptor page)
        {
            page.Size(PageSizes.Letter);
            page.PageColor(Colors.White);
            page.MarginLeft(1f, Unit.Inch);
            page.MarginRight(1f, Unit.Inch);
            page.MarginTop(0);
            page.MarginBottom(0.2f, Unit.Inch);
            page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish"));
        }

        internal static TextStyle GetBodyTextStyle(float fontSize, string fontColor) =>
            TextStyle.Default
                .FontFamily("Mulish")
                .FontSize(fontSize)
                .FontColor(fontColor);

        internal static string BuildOverlayColor(string baseColor, float clampedOpacity)
        {
            var alpha = (int)((1.0f - clampedOpacity) * 255);
            var rgb = baseColor.StartsWith("#", StringComparison.Ordinal)
                ? baseColor[1..]
                : baseColor;

            return $"#{alpha:X2}{rgb}";
        }

        internal static BrochureContactConfig GetContact(BrochureContent content) =>
            content.ContactConfig ?? new BrochureContactConfig();

        internal static void ComposeHeader(
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

        internal static void ComposeCoverTopZone(
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

        internal static void ComposeCoverBottomBanner(
            IContainer container,
            BrochureSkinDefinition skin,
            byte[]? coverLogoBytes,
            BrochureContactConfig contact)
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
                                foreach (var line in contact.CoverContactLines)
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

        internal static void ComposeProjectText(IContainer container, BrochureProject project, BrochureSkinDefinition skin)
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

        internal static void ComposeOverviewSection(IContainer container, BrochureOverviewSection section, BrochureSkinDefinition skin)
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

        internal static void ComposeClientListPage(IContainer container, BrochureBlock block, BrochureSkinDefinition skin)
        {
            container.Column(column =>
            {
                var heading = string.IsNullOrWhiteSpace(block.ClientListHeading)
                    ? "OUR CLIENTS"
                    : block.ClientListHeading.ToUpperInvariant();

                column.Item().PaddingBottom(4).Text(heading)
                    .FontFamily("Mulish Black")
                    .FontSize(14)
                    .FontColor(skin.AccentColor);

                column.Item().PaddingBottom(16).Height(2).Background(skin.AccentColor);

                if (!string.IsNullOrWhiteSpace(block.ClientListPreamble))
                {
                    column.Item().PaddingBottom(16).Text(block.ClientListPreamble)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Justify()
                        .LineHeight(1.2f);
                }

                var names = block.ClientNames
                    .Where(static n => !string.IsNullOrWhiteSpace(n))
                    .OrderBy(static n => n, System.StringComparer.OrdinalIgnoreCase)
                    .ToList();

                if (names.Count > 0)
                {
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.RelativeColumn();
                            cols.ConstantColumn(0.2f, Unit.Inch);
                            cols.RelativeColumn();
                            cols.ConstantColumn(0.2f, Unit.Inch);
                            cols.RelativeColumn();
                        });

                        for (var i = 0; i < names.Count; i += 3)
                        {
                            var b = i + 1 < names.Count ? names[i + 1] : string.Empty;
                            var c = i + 2 < names.Count ? names[i + 2] : string.Empty;

                            table.Cell().PaddingBottom(2).Text(names[i])
                                .FontFamily("Mulish").FontSize(8)
                                .FontColor(skin.PrimaryColor).LineHeight(1.15f);
                            table.Cell();
                            table.Cell().PaddingBottom(2).Text(b)
                                .FontFamily("Mulish").FontSize(8)
                                .FontColor(skin.PrimaryColor).LineHeight(1.15f);
                            table.Cell();
                            table.Cell().PaddingBottom(2).Text(c)
                                .FontFamily("Mulish").FontSize(8)
                                .FontColor(skin.PrimaryColor).LineHeight(1.15f);
                        }
                    });
                }

                if (!string.IsNullOrWhiteSpace(block.ClientListNote))
                {
                    column.Item().PaddingTop(16).Text(block.ClientListNote)
                        .FontFamily("Mulish")
                        .FontSize(9)
                        .FontColor(skin.PrimaryColor)
                        .Italic()
                        .Justify()
                        .LineHeight(1.2f);
                }
            });
        }

        internal static void ComposeContactPage(IContainer container, BrochureSkinDefinition skin, BrochureContactConfig contact)
        {
            container.Column(column =>
            {
                column.Item().Text("CONTACT")
                    .FontFamily("Mulish Black")
                    .FontSize(14)
                    .FontColor(skin.AccentColor);

                column.Item().PaddingTop(4).Height(2).Background(skin.AccentColor);
                column.Item().PaddingBottom(16).Text(string.Empty);

                for (var i = 0; i < contact.Offices.Count; i += 2)
                {
                    var leftOffice = contact.Offices[i];
                    var hasRightOffice = i + 1 < contact.Offices.Count;

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Element(cell => ComposeOfficeCell(cell, leftOffice, skin));
                        row.Spacing(ContactColumnGapInches, Unit.Inch);

                        if (hasRightOffice)
                        {
                            var rightOffice = contact.Offices[i + 1];
                            row.RelativeItem().Element(cell => ComposeOfficeCell(cell, rightOffice, skin));
                        }
                        else
                        {
                            row.RelativeItem();
                        }
                    });

                    if (i + 2 < contact.Offices.Count)
                        column.Item().PaddingBottom(16).Text(string.Empty);
                }
            });
        }

        internal static void ComposeOfficeCell(
            IContainer container,
            BrochureOfficeContact office,
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
                    text.DefaultTextStyle(GetBodyTextStyle(9, skin.PrimaryColor));
                    text.Span("T: ").Bold();
                    text.Span(office.Phone);
                });

                column.Item().PaddingTop(2).Text(text =>
                {
                    text.DefaultTextStyle(GetBodyTextStyle(9, skin.PrimaryColor));
                    text.Span("E: ").Bold();
                    text.Span(office.Email).FontColor("#0563C1");
                });

                column.Item().PaddingTop(2).Text(text =>
                {
                    text.DefaultTextStyle(GetBodyTextStyle(9, skin.PrimaryColor));
                    text.Span("H: ").Bold();
                    text.Span(office.Hours);
                });
            });
        }

        internal static void ComposeFooter(IContainer container, BrochureContent content, BrochureSkinDefinition skin)
        {
            var contact = GetContact(content);

            container.PaddingHorizontal(0.25f, Unit.Inch).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    var companyName = string.IsNullOrWhiteSpace(content.CompanyName)
                        ? string.Empty
                        : content.CompanyName.Trim() + " ";

                    text.DefaultTextStyle(GetBodyTextStyle(8, skin.PrimaryColor));
                    text.Span(companyName + contact.OfficeAddress).FontColor(skin.PrimaryColor);
                });

                row.ConstantItem(100).AlignRight().Text(text =>
                {
                    text.DefaultTextStyle(GetBodyTextStyle(8, skin.PrimaryColor));
                    text.Span("Page ").FontColor(skin.PrimaryColor);
                    text.CurrentPageNumber().FontColor(skin.PrimaryColor);
                    text.Span(" of ").FontColor(skin.PrimaryColor);
                    text.TotalPages().FontColor(skin.PrimaryColor);
                });
            });
        }
    }
}
