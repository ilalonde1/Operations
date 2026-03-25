#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Kor.Operations.Rendering.Brochure
{
    public sealed class BrochureDocxRenderer : IBrochureDocxRenderer
    {
        // Page (Letter) in twips (1 inch = 1440 twips)
        private const int PageWidthTwips  = 12240; // 8.5"
        private const int PageHeightTwips = 15840; // 11"
        private const int MarginHorzTwips = 1440;  // 1"
        private const int MarginVertTwips = 720;   // 0.5"
        private const int ContentWidthTwips = PageWidthTwips - MarginHorzTwips * 2; // 9360

        // EMU (1 inch = 914400 EMU)
        private const long EmuPerInch      = 914400L;
        private const long ContentWidthEmu = (long)(6.5 * EmuPerInch);  // 5943600
        private const long PhotoWidthEmu   = (long)(3.0 * EmuPerInch);  // project photo max
        private const long HeadshotWidthEmu = (long)(1.75 * EmuPerInch);
        private const long LogoMaxWidthEmu  = (long)(1.4 * EmuPerInch);
        private const long LogoMaxHeightEmu = (long)(0.4 * EmuPerInch);
        private const long CoverPhotoHeightEmu = (long)(3.5 * EmuPerInch);

        private uint _imgId = 1;

        // ── Public entry point ──────────────────────────────────────────────

        public Task<string> RenderAsync(BrochureContent content, string outputPath, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ct.ThrowIfCancellationRequested();

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            PrepareContent(content);
            var skin = ResolveSkin(content);
            var primary = HexColor(skin.PrimaryColor);
            var accent  = HexColor(skin.AccentColor);

            var logoBytes       = TryLoadImage(content.LogoPath);
            var coverPhotoBytes = content.CoverPhotoBytes is { Length: > 0 }
                ? content.CoverPhotoBytes
                : TryLoadImage(content.CoverPhotoPath);

            _imgId = 1;

            using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();
            mainPart.Document = new Document(body);

            var headerPart = mainPart.AddNewPart<HeaderPart>();
            var footerPart = mainPart.AddNewPart<FooterPart>();
            var headerId   = mainPart.GetIdOfPart(headerPart);
            var footerId   = mainPart.GetIdOfPart(footerPart);

            BuildHeader(headerPart, skin, primary, logoBytes);
            BuildFooter(footerPart, content, primary);

            var contact = content.ContactConfig ?? new BrochureContactConfig();

            AppendCover(body, mainPart, content, skin, primary, accent, coverPhotoBytes, ct);

            foreach (var block in content.Blocks)
            {
                ct.ThrowIfCancellationRequested();
                switch (block.BlockType)
                {
                    case BrochureBlockType.PageBreak:
                        body.Append(MakePageBreak());
                        break;
                    case BrochureBlockType.Section when block.Section is not null:
                        AppendSection(body, mainPart, block.Section, skin, primary, accent, ct);
                        break;
                    case BrochureBlockType.Personnel:
                        AppendPersonnel(body, mainPart, block, skin, primary, accent, ct);
                        break;
                    case BrochureBlockType.CompanyOverview:
                        AppendOverview(body, block, skin, primary, accent, ct);
                        break;
                    case BrochureBlockType.Contact:
                        AppendContact(body, contact, skin, primary, accent, ct);
                        break;
                    case BrochureBlockType.ClientList:
                        AppendClientList(body, block, skin, primary, accent, ct);
                        break;
                }
            }

            body.Append(new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = headerId },
                new FooterReference { Type = HeaderFooterValues.Default, Id = footerId },
                new PageSize { Width = PageWidthTwips, Height = PageHeightTwips },
                new PageMargin
                {
                    Top    = MarginVertTwips,
                    Bottom = MarginVertTwips,
                    Left   = (uint)MarginHorzTwips,
                    Right  = (uint)MarginHorzTwips,
                    Header = 360U,
                    Footer = 360U
                }));

            mainPart.Document.Save();
            return Task.FromResult(outputPath);
        }

        // ── Header ──────────────────────────────────────────────────────────

        private void BuildHeader(HeaderPart part, BrochureSkinDefinition skin, string primaryHex, byte[]? logoBytes)
        {
            var header = new Header();

            int leftWidth  = (int)(ContentWidthTwips * 0.62);
            int rightWidth = ContentWidthTwips - leftWidth;

            // Left cell: header text
            var leftCell  = MakeShadedCell(leftWidth, primaryHex, alignment: JustificationValues.Left,
                para: MakeRun(skin.HeaderText, skin.FontFamily, 18, bold: true, colorHex: "FFFFFF"));

            // Right cell: logo or empty
            var rightCell = new TableCell();
            rightCell.Append(new TableCellProperties(
                new TableCellWidth { Width = rightWidth.ToString(), Type = TableWidthUnitValues.Dxa },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = primaryHex },
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }));

            if (logoBytes is { Length: > 0 })
            {
                var imagePart = part.AddImagePart(DetectImageType(logoBytes) == "png"
                    ? ImagePartType.Png : ImagePartType.Jpeg);
                imagePart.FeedData(new MemoryStream(logoBytes));
                var relId = part.GetIdOfPart(imagePart);
                var (w, h) = ScaleToFit(GetImageDimensions(logoBytes), LogoMaxWidthEmu, LogoMaxHeightEmu);
                var logoPara = new Paragraph(
                    new ParagraphProperties(
                        new Justification { Val = JustificationValues.Right },
                        new SpacingBetweenLines { Before = "0", After = "0" }),
                    new Run(CreateDrawing(relId, w, h, _imgId++)));
                rightCell.Append(logoPara);
            }
            else
            {
                rightCell.Append(EmptyPara());
            }

            var tbl = new Table(
                new TableProperties(
                    new TableWidth { Width = ContentWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                    NoBorders(),
                    new TableCellMarginDefault(
                        new TopMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin { Width = "80", Type = TableWidthUnitValues.Dxa },
                        new LeftMargin { Width = "120", Type = TableWidthUnitValues.Dxa },
                        new RightMargin { Width = "120", Type = TableWidthUnitValues.Dxa })),
                new TableRow(leftCell, rightCell));

            header.Append(tbl);
            part.Header = header;
        }

        // ── Footer ──────────────────────────────────────────────────────────

        private static void BuildFooter(FooterPart part, BrochureContent content, string primaryHex)
        {
            var footer = new Footer();
            var contact = content.ContactConfig ?? new BrochureContactConfig();
            var addressText = contact.CoverContactLines.Count > 0
                ? string.Join("  •  ", contact.CoverContactLines.Take(2))
                : contact.OfficeAddress;

            var para = new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = "0" }),
                // Address left
                new Run(
                    new RunProperties(new Color { Val = "888888" }, new FontSize { Val = "16" }),
                    new Text(addressText) { Space = SpaceProcessingModeValues.Preserve }),
                // Tab to right
                new Run(new TabChar()),
                // PAGE field
                new Run(new RunProperties(new Color { Val = "888888" }, new FontSize { Val = "16" }),
                    new FieldChar { FieldCharType = FieldCharValues.Begin }),
                new Run(new RunProperties(new Color { Val = "888888" }, new FontSize { Val = "16" }),
                    new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new RunProperties(new Color { Val = "888888" }, new FontSize { Val = "16" }),
                    new FieldChar { FieldCharType = FieldCharValues.Separate }),
                new Run(new RunProperties(new Color { Val = "888888" }, new FontSize { Val = "16" }),
                    new Text("1")),
                new Run(new RunProperties(new Color { Val = "888888" }, new FontSize { Val = "16" }),
                    new FieldChar { FieldCharType = FieldCharValues.End }));

            // Right-align page number using tab stop
            var tabStops = new Tabs(new TabStop { Val = TabStopValues.Right, Position = ContentWidthTwips });
            ((ParagraphProperties)para.ParagraphProperties!).Append(tabStops);

            footer.Append(para);
            part.Footer = footer;
        }

        // ── Cover page ──────────────────────────────────────────────────────

        private void AppendCover(
            Body body,
            MainDocumentPart mainPart,
            BrochureContent content,
            BrochureSkinDefinition skin,
            string primaryHex,
            string accentHex,
            byte[]? coverPhotoBytes,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var coverTitle = string.IsNullOrWhiteSpace(content.CoverTitle)
                ? (string.IsNullOrWhiteSpace(content.TemplateName) ? "KOR Structural" : content.TemplateName)
                : content.CoverTitle;

            // Full-width colored title banner
            var titleCell = MakeShadedCell(
                ContentWidthTwips, primaryHex,
                alignment: JustificationValues.Left,
                para: MakeRun(coverTitle.ToUpperInvariant(), skin.TitleFontFamily, 48, bold: true, colorHex: "FFFFFF"),
                vertPad: 280);

            var titleTable = new Table(
                new TableProperties(
                    new TableWidth { Width = ContentWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                    NoBorders()),
                new TableRow(titleCell));
            body.Append(titleTable);

            // Subtitle: year + company name
            body.Append(MakeStyledPara(
                DateTime.Now.Year + "  —  " + (content.CompanyName ?? "KOR Structural"),
                skin.FontFamily, 20, colorHex: accentHex, spaceBefore: 80, spaceAfter: 120));

            // Accent rule
            body.Append(MakeAccentLine(accentHex));

            // Cover photo
            if (coverPhotoBytes is { Length: > 0 })
            {
                var imagePart = mainPart.AddImagePart(DetectImageType(coverPhotoBytes) == "png"
                    ? ImagePartType.Png : ImagePartType.Jpeg);
                imagePart.FeedData(new MemoryStream(coverPhotoBytes));
                var relId = mainPart.GetIdOfPart(imagePart);
                var (srcW, srcH) = GetImageDimensions(coverPhotoBytes);
                long imgW = ContentWidthEmu;
                long imgH = srcW > 0 && srcH > 0
                    ? (long)((double)srcH / srcW * ContentWidthEmu)
                    : CoverPhotoHeightEmu;
                imgH = Math.Min(imgH, CoverPhotoHeightEmu);

                var photoPara = new Paragraph(
                    new ParagraphProperties(
                        new Justification { Val = JustificationValues.Center },
                        new SpacingBetweenLines { Before = "0", After = "0" }),
                    new Run(CreateDrawing(relId, imgW, imgH, _imgId++)));
                body.Append(photoPara);
            }
            else
            {
                // Colored placeholder strip
                var placeholderCell = MakeShadedCell(ContentWidthTwips, primaryHex,
                    alignment: JustificationValues.Center,
                    para: MakeRun(string.Empty, skin.FontFamily, 24, colorHex: "FFFFFF"),
                    vertPad: 960);
                var placeholderTable = new Table(
                    new TableProperties(
                        new TableWidth { Width = ContentWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                        NoBorders()),
                    new TableRow(placeholderCell));
                body.Append(placeholderTable);
            }

            body.Append(MakePageBreak());
        }

        // ── Section (projects) ───────────────────────────────────────────────

        private void AppendSection(
            Body body,
            MainDocumentPart mainPart,
            BrochureSection section,
            BrochureSkinDefinition skin,
            string primaryHex,
            string accentHex,
            CancellationToken ct)
        {
            if (section.Projects.Count == 0) return;

            body.Append(MakeSectionHeading(section.Heading, skin, primaryHex));

            if (!string.IsNullOrWhiteSpace(section.Blurb))
                body.Append(MakeStyledPara(section.Blurb, skin.FontFamily, 18, spaceAfter: 80));

            for (int i = 0; i < section.Projects.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var project = section.Projects[i];

                // Photo (left) + text (right) table
                var photoBytes = project.Photos.Count > 0
                    ? (project.Photos[0].ImageBytes is { Length: > 0 } ? project.Photos[0].ImageBytes : TryLoadImage(project.Photos[0].FilePath))
                    : null;

                int photoColTwips = (int)(ContentWidthTwips * 0.44); // ~3"
                int textColTwips  = ContentWidthTwips - photoColTwips;

                var photoCell = new TableCell();
                photoCell.Append(new TableCellProperties(
                    new TableCellWidth { Width = photoColTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));

                if (photoBytes is { Length: > 0 })
                {
                    var imgPart = mainPart.AddImagePart(DetectImageType(photoBytes) == "png"
                        ? ImagePartType.Png : ImagePartType.Jpeg);
                    imgPart.FeedData(new MemoryStream(photoBytes));
                    var relId = mainPart.GetIdOfPart(imgPart);
                    long maxW = (long)(photoColTwips / 1440.0 * EmuPerInch);
                    long maxH = (long)(maxW * 2.0 / 3.0); // 3:2 fallback
                    var (w, h) = ScaleToFit(GetImageDimensions(photoBytes), maxW, maxH);
                    photoCell.Append(new Paragraph(
                        new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
                        new Run(CreateDrawing(relId, w, h, _imgId++))));
                }
                else
                {
                    photoCell.Append(EmptyPara());
                }

                // Additional project photos (up to 2 more) stacked
                for (int pi = 1; pi < Math.Min(project.Photos.Count, 3); pi++)
                {
                    ct.ThrowIfCancellationRequested();
                    var extra = project.Photos[pi];
                    var extraBytes = extra.ImageBytes is { Length: > 0 } ? extra.ImageBytes : TryLoadImage(extra.FilePath);
                    if (extraBytes is { Length: > 0 })
                    {
                        var imgPart2 = mainPart.AddImagePart(DetectImageType(extraBytes) == "png"
                            ? ImagePartType.Png : ImagePartType.Jpeg);
                        imgPart2.FeedData(new MemoryStream(extraBytes));
                        var relId2 = mainPart.GetIdOfPart(imgPart2);
                        long maxW2 = (long)(photoColTwips / 1440.0 * EmuPerInch);
                        long maxH2 = (long)(maxW2 * 2.0 / 3.0);
                        var (w2, h2) = ScaleToFit(GetImageDimensions(extraBytes), maxW2, maxH2);
                        photoCell.Append(new Paragraph(
                            new ParagraphProperties(new SpacingBetweenLines { Before = "40", After = "0" }),
                            new Run(CreateDrawing(relId2, w2, h2, _imgId++))));
                    }
                }

                // Text cell
                var textCell = new TableCell();
                textCell.Append(new TableCellProperties(
                    new TableCellWidth { Width = textColTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableCellMargin(
                        new LeftMargin { Width = "180", Type = TableWidthUnitValues.Dxa }),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));

                textCell.Append(MakeStyledPara(project.ProjectName, skin.TitleFontFamily, 22, bold: true,
                    colorHex: primaryHex, spaceAfter: 40));

                if (!string.IsNullOrWhiteSpace(project.SectionLabel))
                    textCell.Append(MakeStyledPara(project.SectionLabel.ToUpperInvariant(), skin.FontFamily, 16,
                        colorHex: accentHex, spaceAfter: 40));

                if (!string.IsNullOrWhiteSpace(project.ProjectDescription))
                    textCell.Append(MakeStyledPara(project.ProjectDescription, skin.FontFamily, 18, spaceAfter: 60));

                if (!string.IsNullOrWhiteSpace(project.Client))
                    textCell.Append(MakeStyledPara("Client: " + project.Client, skin.FontFamily, 16,
                        italic: true, colorHex: "555555", spaceAfter: 20));

                if (!string.IsNullOrWhiteSpace(project.Architect))
                    textCell.Append(MakeStyledPara("Architect: " + project.Architect, skin.FontFamily, 16,
                        italic: true, colorHex: "555555", spaceAfter: 20));

                var row = new TableRow(photoCell, textCell);
                var projectTable = new Table(
                    new TableProperties(
                        new TableWidth { Width = ContentWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                        NoBorders()),
                    row);
                body.Append(projectTable);
                body.Append(MakeAccentLine(accentHex, spaceAfter: 120));

                if (section.PageBreakAfterProjectIndex.Contains(i))
                    body.Append(MakePageBreak());
            }
        }

        // ── Personnel ────────────────────────────────────────────────────────

        private void AppendPersonnel(
            Body body,
            MainDocumentPart mainPart,
            BrochureBlock block,
            BrochureSkinDefinition skin,
            string primaryHex,
            string accentHex,
            CancellationToken ct)
        {
            if (block.People.Count == 0) return;

            body.Append(MakeSectionHeading(block.PersonnelHeading, skin, primaryHex));

            if (!string.IsNullOrWhiteSpace(block.PersonnelBlurb))
                body.Append(MakeStyledPara(block.PersonnelBlurb, skin.FontFamily, 18, spaceAfter: 80));

            foreach (var person in block.People)
            {
                ct.ThrowIfCancellationRequested();

                int photoColTwips = (int)(ContentWidthTwips * 0.26); // ~1.75"
                int textColTwips  = ContentWidthTwips - photoColTwips;

                var photoBytes = person.PhotoBytes is { Length: > 0 }
                    ? person.PhotoBytes
                    : TryLoadImage(person.PhotoPath);

                var photoCell = new TableCell();
                photoCell.Append(new TableCellProperties(
                    new TableCellWidth { Width = photoColTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));

                if (photoBytes is { Length: > 0 })
                {
                    var imgPart = mainPart.AddImagePart(DetectImageType(photoBytes) == "png"
                        ? ImagePartType.Png : ImagePartType.Jpeg);
                    imgPart.FeedData(new MemoryStream(photoBytes));
                    var relId = mainPart.GetIdOfPart(imgPart);
                    long maxW = (long)(photoColTwips / 1440.0 * EmuPerInch);
                    long maxH = (long)(maxW * 1.25); // portrait-ish
                    var (w, h) = ScaleToFit(GetImageDimensions(photoBytes), maxW, maxH);
                    photoCell.Append(new Paragraph(
                        new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
                        new Run(CreateDrawing(relId, w, h, _imgId++))));
                }
                else
                {
                    photoCell.Append(EmptyPara());
                }

                var textCell = new TableCell();
                textCell.Append(new TableCellProperties(
                    new TableCellWidth { Width = textColTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableCellMargin(
                        new LeftMargin { Width = "180", Type = TableWidthUnitValues.Dxa }),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));

                textCell.Append(MakeStyledPara(person.Name, skin.TitleFontFamily, 22, bold: true,
                    colorHex: primaryHex, spaceAfter: 40));

                if (!string.IsNullOrWhiteSpace(person.Credentials))
                    textCell.Append(MakeStyledPara(person.Credentials, skin.FontFamily, 18,
                        italic: true, colorHex: accentHex, spaceAfter: 60));

                if (!string.IsNullOrWhiteSpace(person.Bio))
                    textCell.Append(MakeStyledPara(person.Bio, skin.FontFamily, 18,
                        colorHex: "333333", spaceAfter: 40));

                body.Append(new Table(
                    new TableProperties(
                        new TableWidth { Width = ContentWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                        NoBorders()),
                    new TableRow(photoCell, textCell)));

                body.Append(MakeAccentLine(accentHex, spaceAfter: 120));
            }
        }

        // ── Overview ────────────────────────────────────────────────────────

        private static void AppendOverview(
            Body body,
            BrochureBlock block,
            BrochureSkinDefinition skin,
            string primaryHex,
            string accentHex,
            CancellationToken ct)
        {
            for (int i = 0; i < block.OverviewSections.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var sec = block.OverviewSections[i];

                if (!string.IsNullOrWhiteSpace(sec.Heading))
                    body.Append(MakeSectionHeading(sec.Heading, skin, primaryHex));

                if (!string.IsNullOrWhiteSpace(sec.Body))
                    body.Append(MakeStyledPara(sec.Body, skin.FontFamily, 18, spaceAfter: 80));

                body.Append(MakeAccentLine(accentHex, spaceAfter: 80));

                if (block.PageBreakAfterOverviewIndex.Contains(i))
                    body.Append(MakePageBreak());
            }
        }

        // ── Contact ─────────────────────────────────────────────────────────

        private static void AppendContact(
            Body body,
            BrochureContactConfig contact,
            BrochureSkinDefinition skin,
            string primaryHex,
            string accentHex,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            body.Append(MakeSectionHeading("Contact", skin, primaryHex));

            if (contact.Offices.Count == 0) return;

            // 2-column table of offices
            int colWidth = ContentWidthTwips / 2;
            var rows = new List<TableRow>();
            for (int i = 0; i < contact.Offices.Count; i += 2)
            {
                ct.ThrowIfCancellationRequested();
                var left  = contact.Offices[i];
                var right = i + 1 < contact.Offices.Count ? contact.Offices[i + 1] : null;
                rows.Add(MakeOfficeRow(left, right, skin, primaryHex, accentHex, colWidth));
            }

            var contactTable = new Table(
                new TableProperties(
                    new TableWidth { Width = ContentWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                    NoBorders()));
            foreach (var r in rows)
                contactTable.Append(r);

            body.Append(contactTable);
        }

        private static TableRow MakeOfficeRow(
            BrochureOfficeContact left,
            BrochureOfficeContact? right,
            BrochureSkinDefinition skin,
            string primaryHex,
            string accentHex,
            int colWidth)
        {
            return new TableRow(
                MakeOfficeCell(left, skin, primaryHex, accentHex, colWidth),
                right is not null
                    ? MakeOfficeCell(right, skin, primaryHex, accentHex, colWidth)
                    : MakeEmptyCell(colWidth));
        }

        private static TableCell MakeOfficeCell(
            BrochureOfficeContact office,
            BrochureSkinDefinition skin,
            string primaryHex,
            string accentHex,
            int widthTwips)
        {
            var cell = new TableCell();
            cell.Append(new TableCellProperties(
                new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableCellMargin(
                    new RightMargin { Width = "180", Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));

            cell.Append(MakeStyledPara(office.Region, skin.TitleFontFamily, 20, bold: true,
                colorHex: primaryHex, spaceAfter: 40));
            if (!string.IsNullOrWhiteSpace(office.Contact))
                cell.Append(MakeStyledPara(office.Contact, skin.FontFamily, 17, bold: true, spaceAfter: 20));
            if (!string.IsNullOrWhiteSpace(office.Phone))
                cell.Append(MakeStyledPara(office.Phone, skin.FontFamily, 17, spaceAfter: 10));
            if (!string.IsNullOrWhiteSpace(office.Email))
                cell.Append(MakeStyledPara(office.Email, skin.FontFamily, 17, colorHex: accentHex, spaceAfter: 10));
            if (!string.IsNullOrWhiteSpace(office.Hours))
                cell.Append(MakeStyledPara(office.Hours, skin.FontFamily, 16, italic: true,
                    colorHex: "888888", spaceAfter: 80));

            return cell;
        }

        // ── Client list ─────────────────────────────────────────────────────

        private static void AppendClientList(
            Body body,
            BrochureBlock block,
            BrochureSkinDefinition skin,
            string primaryHex,
            string accentHex,
            CancellationToken ct)
        {
            if (block.ClientNames.Count == 0) return;

            var heading = string.IsNullOrWhiteSpace(block.ClientListHeading)
                ? "Our Clients"
                : block.ClientListHeading;
            body.Append(MakeSectionHeading(heading, skin, primaryHex));

            if (!string.IsNullOrWhiteSpace(block.ClientListPreamble))
                body.Append(MakeStyledPara(block.ClientListPreamble, skin.FontFamily, 18, spaceAfter: 80));

            var sorted = block.ClientNames
                .Where(static n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(static n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 3-column table
            int colWidth = ContentWidthTwips / 3;
            var tbl = new Table(
                new TableProperties(
                    new TableWidth { Width = ContentWidthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                    NoBorders()));

            for (int i = 0; i < sorted.Count; i += 3)
            {
                ct.ThrowIfCancellationRequested();
                var row = new TableRow();
                for (int col = 0; col < 3; col++)
                {
                    var cell = new TableCell();
                    cell.Append(new TableCellProperties(
                        new TableCellWidth { Width = colWidth.ToString(), Type = TableWidthUnitValues.Dxa }));

                    var name = i + col < sorted.Count ? sorted[i + col] : string.Empty;
                    cell.Append(MakeStyledPara(name, skin.FontFamily, 17, spaceAfter: 20));
                    row.Append(cell);
                }
                tbl.Append(row);
            }

            body.Append(tbl);

            if (!string.IsNullOrWhiteSpace(block.ClientListNote))
            {
                body.Append(EmptyPara());
                body.Append(MakeStyledPara(block.ClientListNote, skin.FontFamily, 16,
                    italic: true, colorHex: "888888", spaceAfter: 40));
            }
        }

        // ── Style helpers ────────────────────────────────────────────────────

        private static Paragraph MakeSectionHeading(string text, BrochureSkinDefinition skin, string primaryHex)
        {
            var pPr = new ParagraphProperties(
                new SpacingBetweenLines { Before = "160", After = "80" },
                new ParagraphBorders(new BottomBorder
                {
                    Val   = BorderValues.Single,
                    Color = primaryHex,
                    Size  = 8U,
                    Space = 4U
                }));

            var rPr = MakeRunProps(skin.TitleFontFamily, 24, bold: true, colorHex: primaryHex);
            return new Paragraph(pPr,
                new Run(rPr, new Text(text.ToUpperInvariant()) { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static Paragraph MakeStyledPara(
            string text,
            string font,
            int halfPts,
            bool bold = false,
            bool italic = false,
            string? colorHex = null,
            int spaceBefore = 0,
            int spaceAfter = 60)
        {
            var pPr = new ParagraphProperties(
                new SpacingBetweenLines { Before = spaceBefore.ToString(), After = spaceAfter.ToString() });
            var rPr = MakeRunProps(font, halfPts, bold, italic, colorHex);
            return new Paragraph(pPr,
                new Run(rPr, new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        }

        private static RunProperties MakeRunProps(
            string font, int halfPts, bool bold = false, bool italic = false, string? colorHex = null)
        {
            var rPr = new RunProperties(
                new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font },
                new FontSize { Val = halfPts.ToString() });

            if (bold)   rPr.Append(new Bold());
            if (italic) rPr.Append(new Italic());
            if (!string.IsNullOrWhiteSpace(colorHex))
                rPr.Append(new Color { Val = colorHex });

            return rPr;
        }

        private static Run MakeRun(string text, string font, int halfPts,
            bool bold = false, bool italic = false, string? colorHex = null)
        {
            var rPr = MakeRunProps(font, halfPts, bold, italic, colorHex);
            return new Run(rPr, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }

        private static Paragraph MakeAccentLine(string accentHex, int spaceAfter = 80)
        {
            return new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = spaceAfter.ToString() },
                    new ParagraphBorders(new BottomBorder
                    {
                        Val   = BorderValues.Single,
                        Color = accentHex,
                        Size  = 6U,
                        Space = 1U
                    })));
        }

        private static Paragraph MakePageBreak()
        {
            return new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "0", After = "0" }),
                new Run(new Break { Type = BreakValues.Page }));
        }

        private static Paragraph EmptyPara() =>
            new(new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = "0" }));

        // ── Table cell helpers ───────────────────────────────────────────────

        private static TableCell MakeShadedCell(
            int widthTwips,
            string fillHex,
            JustificationValues alignment,
            Run para,
            int vertPad = 120)
        {
            var cell = new TableCell();
            cell.Append(new TableCellProperties(
                new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fillHex },
                new TableCellMargin(
                    new TopMargin    { Width = vertPad.ToString(), Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = vertPad.ToString(), Type = TableWidthUnitValues.Dxa },
                    new LeftMargin   { Width = "180",              Type = TableWidthUnitValues.Dxa },
                    new RightMargin  { Width = "180",              Type = TableWidthUnitValues.Dxa })));

            var p = new Paragraph(
                new ParagraphProperties(
                    new Justification { Val = alignment },
                    new SpacingBetweenLines { Before = "0", After = "0" }),
                para);
            cell.Append(p);
            return cell;
        }

        private static TableCell MakeEmptyCell(int widthTwips)
        {
            var cell = new TableCell();
            cell.Append(new TableCellProperties(
                new TableCellWidth { Width = widthTwips.ToString(), Type = TableWidthUnitValues.Dxa }));
            cell.Append(EmptyPara());
            return cell;
        }

        private static TableBorders NoBorders() =>
            new TableBorders(
                new TopBorder    { Val = BorderValues.None },
                new BottomBorder { Val = BorderValues.None },
                new LeftBorder   { Val = BorderValues.None },
                new RightBorder  { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder   { Val = BorderValues.None });

        // ── Image helpers ────────────────────────────────────────────────────

        private static Drawing CreateDrawing(string relId, long widthEmu, long heightEmu, uint imgId)
        {
            return new Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = widthEmu, Cy = heightEmu },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties { Id = imgId, Name = $"Image{imgId}" },
                    new DW.NonVisualGraphicFrameDrawingProperties(
                        new A.GraphicFrameLocks { NoChangeAspect = true }),
                    new A.Graphic(
                        new A.GraphicData(
                            new PIC.Picture(
                                new PIC.NonVisualPictureProperties(
                                    new PIC.NonVisualDrawingProperties { Id = 0U, Name = string.Empty },
                                    new PIC.NonVisualPictureDrawingProperties()),
                                new PIC.BlipFill(
                                    new A.Blip { Embed = relId },
                                    new A.Stretch(new A.FillRectangle())),
                                new PIC.ShapeProperties(
                                    new A.Transform2D(
                                        new A.Offset { X = 0L, Y = 0L },
                                        new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                                    new A.PresetGeometry(new A.AdjustValueList())
                                    { Preset = A.ShapeTypeValues.Rectangle })))
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))
                {
                    DistanceFromTop    = 0U,
                    DistanceFromBottom = 0U,
                    DistanceFromLeft   = 0U,
                    DistanceFromRight  = 0U
                });
        }

        private static (long W, long H) ScaleToFit((int W, int H) dims, long maxW, long maxH)
        {
            if (dims.W <= 0 || dims.H <= 0)
                return (maxW, maxH);

            double scale = Math.Min((double)maxW / dims.W, (double)maxH / dims.H);
            return ((long)(dims.W * scale), (long)(dims.H * scale));
        }

        private static (int W, int H) GetImageDimensions(byte[] bytes)
        {
            try
            {
                if (bytes.Length >= 24 &&
                    bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                {
                    // PNG: IHDR width at offset 16, height at 20 (big-endian)
                    int w = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
                    int h = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
                    return (w, h);
                }

                if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8)
                {
                    // JPEG: scan for SOF0/SOF1/SOF2 marker
                    int i = 2;
                    while (i + 8 < bytes.Length)
                    {
                        if (bytes[i] != 0xFF) break;
                        byte marker = bytes[i + 1];
                        int segLen = (bytes[i + 2] << 8) | bytes[i + 3];
                        if (marker == 0xC0 || marker == 0xC1 || marker == 0xC2)
                        {
                            int h = (bytes[i + 5] << 8) | bytes[i + 6];
                            int w = (bytes[i + 7] << 8) | bytes[i + 8];
                            return (w, h);
                        }
                        i += 2 + segLen;
                    }
                }
            }
            catch { /* swallow; caller uses fallback */ }

            return (0, 0);
        }

        private static string DetectImageType(byte[] bytes)
        {
            if (bytes.Length >= 4 &&
                bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
                return "png";
            return "jpeg";
        }

        // ── Asset resolution (mirrors BrochureRenderer) ──────────────────────

        private static void PrepareContent(BrochureContent content)
        {
            content.CompanyName = "KOR Structural";
            var contactConfig = content.ContactConfig;
            content.LogoPath = !string.IsNullOrWhiteSpace(contactConfig?.LogoPath)
                ? contactConfig.LogoPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Resources\kor-logo.png");
        }

        private static byte[]? TryLoadImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            var resolved = Path.IsPathRooted(path)
                ? path
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

            if (!File.Exists(resolved)) return null;

            try { return File.ReadAllBytes(resolved); }
            catch { return null; }
        }

        // ── Skin helpers ─────────────────────────────────────────────────────

        private static BrochureSkinDefinition ResolveSkin(BrochureContent content)
        {
            var base_ = BrochureSkinRegistry.Resolve(content.SkinId, content.TemplateName);

            if (string.IsNullOrWhiteSpace(content.PrimaryColorOverride) &&
                string.IsNullOrWhiteSpace(content.AccentColorOverride))
                return base_;

            return new BrochureSkinDefinition
            {
                Id                  = base_.Id,
                DisplayName         = base_.DisplayName,
                PrimaryColor        = !string.IsNullOrWhiteSpace(content.PrimaryColorOverride) ? content.PrimaryColorOverride : base_.PrimaryColor,
                AccentColor         = !string.IsNullOrWhiteSpace(content.AccentColorOverride)  ? content.AccentColorOverride  : base_.AccentColor,
                HeaderText          = base_.HeaderText,
                FontFamily          = base_.FontFamily,
                TitleFontFamily     = base_.TitleFontFamily,
                CoverTitleFontSize  = base_.CoverTitleFontSize,
                CoverTreatment      = base_.CoverTreatment,
                DividerStyle        = base_.DividerStyle,
                DividerThickness    = base_.DividerThickness,
                SectionSpacingPoints = base_.SectionSpacingPoints
            };
        }

        private static string HexColor(string hex) =>
            hex.StartsWith('#') ? hex[1..] : hex;

    }
}
