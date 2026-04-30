#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Kor.Operations.Core.Models.Brochure;
using Kor.Operations.Rendering.Brochure.Skins;
using A   = DocumentFormat.OpenXml.Drawing;
using DW  = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Kor.Operations.Rendering.Brochure
{
    public sealed class BrochureDocxRenderer : IBrochureDocxRenderer
    {
        // ── Page geometry (twips; 1 inch = 1440) ────────────────────────────
        // Page left/right margins are set to ZERO in the section properties so
        // that full-page-width header/footer tables work without negative hacks.
        // All body content is indented explicitly via BodyInd.
        private const int PageW    = 12240; // 8.5"
        private const int PageH    = 15840; // 11"
        private const int MarginT  = 1440;  // 1" top (body starts here; clears the header band)
        private const int MarginB  = 1080;  // 0.75" bottom
        private const int HdrDist  = 0;     // header band flush with top page edge
        private const int FtrDist  = 360;   // footer 0.25" from bottom edge
        private const int ContentW = 9360;  // effective content width 6.5" (used for column sizing)
        private const int BodyInd  = 1440;  // explicit left/right indent on all body elements

        // ── EMU (1 inch = 914400) ────────────────────────────────────────────
        private const long Epu     = 914400L;
        private const long PgWEmu  = (long)(8.5 * Epu);
        private const long LogoW   = (long)(1.6 * Epu);
        private const long LogoH   = (long)(0.55 * Epu);
        private const long CvLogoW = (long)(1.6 * Epu);
        private const long CvLogoH = (long)(0.6 * Epu);

        private const string BF = "Calibri";
        private const string HF = "Calibri";

        private uint _id = 1;

        // ── Entry point ──────────────────────────────────────────────────────

        public Task<string> RenderAsync(BrochureContent content, string outputPath, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ct.ThrowIfCancellationRequested();

            var dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

            PrepareContent(content);
            var skin    = ResolveSkin(content);
            var primary = Hex(skin.PrimaryColor);
            var accent  = Hex(skin.AccentColor);
            var logo    = TryLoad(content.LogoPath);
            var cover   = content.CoverPhotoBytes is { Length: > 0 }
                ? content.CoverPhotoBytes : TryLoad(content.CoverPhotoPath);

            _id = 1;
            using var doc = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var main      = doc.AddMainDocumentPart();
            var body      = new Body();
            main.Document = new Document(body);

            var p1h = main.AddNewPart<HeaderPart>(); // first-page header (empty)
            var p1f = main.AddNewPart<FooterPart>();  // first-page footer (cover band)
            var dh  = main.AddNewPart<HeaderPart>();  // default header
            var df  = main.AddNewPart<FooterPart>();  // default footer

            p1h.Header = new Header(new Paragraph(new ParagraphProperties(Sp(0, 0))));
            BuildCoverFooter(p1f, content, primary, logo);
            BuildDefaultHeader(dh, skin, primary, logo);
            BuildDefaultFooter(df, content);

            var contact = content.ContactConfig ?? new BrochureContactConfig();

            AppendCover(body, main, content, skin, primary, accent, cover, ct);

            foreach (var block in content.Blocks)
            {
                ct.ThrowIfCancellationRequested();
                switch (block.BlockType)
                {
                    case BrochureBlockType.PageBreak:
                        body.Append(PageBreak()); break;
                    case BrochureBlockType.Section when block.Section is not null:
                        AppendSection(body, main, block.Section, primary, accent, ct); break;
                    case BrochureBlockType.Personnel:
                        AppendPersonnel(body, main, block, primary, accent, ct); break;
                    case BrochureBlockType.CompanyOverview:
                        AppendOverview(body, block, accent, ct); break;
                    case BrochureBlockType.Contact:
                        AppendContact(body, contact, primary, accent, ct); break;
                    case BrochureBlockType.ClientList:
                        AppendClientList(body, block, accent, ct); break;
                }
            }

            // Page left/right = 0; body indentation simulates the 1" margin.
            body.Append(new SectionProperties(
                new TitlePage(),
                new HeaderReference { Type = HeaderFooterValues.First,   Id = main.GetIdOfPart(p1h) },
                new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(dh)  },
                new FooterReference { Type = HeaderFooterValues.First,   Id = main.GetIdOfPart(p1f) },
                new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(df)  },
                new PageSize   { Width = PageW, Height = PageH },
                new PageMargin { Top = MarginT, Bottom = MarginB, Left = 0, Right = 0,
                                 Header = (uint)HdrDist, Footer = (uint)FtrDist }));

            main.Document.Save();
            return Task.FromResult(outputPath);
        }

        // ── Default header: full-page-width primary band ─────────────────────

        private void BuildDefaultHeader(HeaderPart part, BrochureSkinDefinition skin, string px, byte[]? logo)
        {
            // With page margin = 0 the table naturally spans the full page width.
            int lw = (int)(PageW * 0.50), rw = PageW - lw;

            // Left cell: primary-color background, text indented 1.25" from page edge
            var lc = ShadedCell(lw, px,
                new TableCellMargin(
                    new LeftMargin   { Width = "1800", Type = TableWidthUnitValues.Dxa },
                    new TopMargin    { Width = "220",  Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "220",  Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
            lc.Append(new Paragraph(new ParagraphProperties(Sp(0, 0)),
                       Run(skin.HeaderText, HF, 18, color: "FFFFFF")));

            // Right cell: logo right-aligned (tagline already embedded in logo image)
            var rc = ShadedCell(rw, px,
                new TableCellMargin(
                    new RightMargin  { Width = "360", Type = TableWidthUnitValues.Dxa },
                    new TopMargin    { Width = "220", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "220", Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

            if (logo is { Length: > 0 })
            {
                var ip = part.AddImagePart(ImgType(logo));
                ip.FeedData(new MemoryStream(logo));
                var rid = part.GetIdOfPart(ip);
                var (w, h) = Fit(Dims(logo), LogoW, LogoH);
                rc.Append(new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Right }, Sp(0, 0)),
                    Run(Inline(rid, w, h, _id++))));
            }
            else
            {
                rc.Append(new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Right }, Sp(0, 0)),
                    Run("Structured Engineering", HF, 14, italic: true, color: "AAAAAA")));
            }

            part.Header = new Header(FwTable(lc, rc));
        }

        // ── Default footer: thin rule, address left, page number right ────────

        private static void BuildDefaultFooter(FooterPart part, BrochureContent content)
        {
            var cfg  = content.ContactConfig ?? new BrochureContactConfig();
            var addr = "KOR Structural  " + (string.IsNullOrWhiteSpace(cfg.OfficeAddress)
                ? "501-510 Burrard Street, Vancouver, BC, V6C 3A8" : cfg.OfficeAddress);

            // Tab stop at right content edge (BodyInd + ContentW from page left = 10800)
            var para = new Paragraph(
                new ParagraphProperties(
                    new Indentation { Left = BodyInd.ToString() },
                    new Tabs(new TabStop { Val = TabStopValues.Right, Position = BodyInd + ContentW }),
                    Sp(60, 0),
                    new ParagraphBorders(new TopBorder
                        { Val = BorderValues.Single, Color = "CCCCCC", Size = 4U, Space = 4U })));

            para.Append(Run(addr, BF, 14, color: "888888"));
            para.Append(new Run(new TabChar()));
            AddPageField(para, "888888", 14);

            part.Footer = new Footer(para);
        }

        // ── Cover footer: dark band spanning full page width ──────────────────

        private void BuildCoverFooter(FooterPart part, BrochureContent content, string px, byte[]? logo)
        {
            var cfg = content.ContactConfig ?? new BrochureContactConfig();
            int lw  = (int)(PageW * 0.45), rw = PageW - lw;

            var lc = ShadedCell(lw, px,
                new TableCellMargin(
                    new LeftMargin   { Width = "1440", Type = TableWidthUnitValues.Dxa },
                    new TopMargin    { Width = "200",  Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "200",  Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

            if (logo is { Length: > 0 })
            {
                var ip = part.AddImagePart(ImgType(logo));
                ip.FeedData(new MemoryStream(logo));
                var rid = part.GetIdOfPart(ip);
                var (w, h) = Fit(Dims(logo), CvLogoW, CvLogoH);
                lc.Append(new Paragraph(new ParagraphProperties(Sp(0, 40)),
                           Run(Inline(rid, w, h, _id++))));
            }
            lc.Append(new Paragraph(new ParagraphProperties(Sp(0, 0)),
                       Run("Structured Engineering", BF, 15, italic: true, color: "AAAAAA")));

            var rc = ShadedCell(rw, px,
                new TableCellMargin(
                    new RightMargin  { Width = "1440", Type = TableWidthUnitValues.Dxa },
                    new TopMargin    { Width = "200",  Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "200",  Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

            foreach (var line in cfg.CoverContactLines.Take(5))
                rc.Append(new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Right }, Sp(0, 20)),
                    Run(line, BF, 15, color: "BBBBBB")));

            part.Footer = new Footer(FwTable(lc, rc));
        }

        // ── Cover page ───────────────────────────────────────────────────────

        private void AppendCover(Body body, MainDocumentPart main, BrochureContent content,
            BrochureSkinDefinition skin, string px, string ax, byte[]? coverBytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var title = string.IsNullOrWhiteSpace(content.CoverTitle)
                ? (string.IsNullOrWhiteSpace(content.TemplateName) ? "KOR Structural" : content.TemplateName)
                : content.CoverTitle;

            // Full-page background photo anchored at page (0,0), behind text
            if (coverBytes is { Length: > 0 })
            {
                var ip = main.AddImagePart(ImgType(coverBytes));
                ip.FeedData(new MemoryStream(coverBytes));
                var rid = main.GetIdOfPart(ip);
                var (sw, sh) = Dims(coverBytes);
                long imgW = PgWEmu;
                long imgH = sw > 0 && sh > 0
                    ? Math.Min((long)((double)sh / sw * PgWEmu), (long)(9.2 * Epu))
                    : (long)(9.0 * Epu);
                body.Append(new Paragraph(new ParagraphProperties(Sp(0, 0)),
                             Run(Anchor(rid, imgW, imgH, _id++))));
            }

            // Exact-height spacer pushes the title ~55% down the page.
            // Word ignores SpaceBefore on the first paragraph, so we use LineRule=Exact.
            // 5" ≈ 7200 twentieths-of-a-point.
            body.Append(new Paragraph(new ParagraphProperties(
                new SpacingBetweenLines { Line = "7200", LineRule = LineSpacingRuleValues.Exact,
                                         Before = "0", After = "0" })));

            body.Append(new Paragraph(new ParagraphProperties(Sp(0, 60)),
                         Run(title.ToUpperInvariant(), HF, 64, bold: true, color: "FFFFFF")));
            body.Append(new Paragraph(new ParagraphProperties(Sp(0, 0)),
                         Run(DateTime.Now.Year.ToString(), HF, 28, bold: true, color: ax)));

            body.Append(PageBreak());
        }

        // ── Section (projects) ───────────────────────────────────────────────

        private void AppendSection(Body body, MainDocumentPart main, BrochureSection section,
            string px, string ax, CancellationToken ct)
        {
            if (section.Projects.Count == 0) return;

            body.Append(PageBreak());
            body.Append(SectionHeading(section.Heading, ax));
            if (!string.IsNullOrWhiteSpace(section.Blurb))
                body.Append(Body9(section.Blurb, 120, topLevel: true));

            for (int i = 0; i < section.Projects.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var proj = section.Projects[i];
                int pcw  = (int)(ContentW * 0.44);
                int tcw  = ContentW - pcw;

                var pc = PhotoCell(main, FirstPhoto(proj), pcw, px);

                var tc = new TableCell();
                tc.Append(CellP(tcw,
                    new TableCellMargin(new LeftMargin { Width = "200", Type = TableWidthUnitValues.Dxa }),
                    new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));
                tc.Append(NamePara(proj.ProjectName, px));
                tc.Append(AccentRule(ax));
                if (!string.IsNullOrWhiteSpace(proj.SectionLabel))
                    tc.Append(Styled(proj.SectionLabel.ToUpperInvariant(), HF, 16, color: ax, after: 60));
                if (!string.IsNullOrWhiteSpace(proj.ProjectDescription))
                    tc.Append(Body9(proj.ProjectDescription, 80));
                if (!string.IsNullOrWhiteSpace(proj.Client))
                    tc.Append(Styled("Client: " + proj.Client, BF, 17, italic: true, color: "555555", after: 30));
                if (!string.IsNullOrWhiteSpace(proj.Architect))
                    tc.Append(Styled("Architect: " + proj.Architect, BF, 17, italic: true, color: "555555", after: 30));

                body.Append(TwoColTable(pc, tc));
                body.Append(new Paragraph(new ParagraphProperties(Sp(0, 160))));

                if (section.PageBreakAfterProjectIndex.Contains(i)) body.Append(PageBreak());
            }
        }

        // ── Personnel ────────────────────────────────────────────────────────

        private void AppendPersonnel(Body body, MainDocumentPart main, BrochureBlock block,
            string px, string ax, CancellationToken ct)
        {
            if (block.People.Count == 0) return;
            body.Append(PageBreak());

            var pages = GroupPeople(block.People);
            for (int pi = 0; pi < pages.Count; pi++)
            {
                ct.ThrowIfCancellationRequested();

                // Word ignores SpaceBefore on the first paragraph of a page.
                // Emit a small exact-height spacer so the heading's SpaceBefore is respected.
                body.Append(new Paragraph(new ParagraphProperties(
                    new SpacingBetweenLines { Line = "240", LineRule = LineSpacingRuleValues.Exact,
                                             Before = "0", After = "0" })));

                if (pi == 0)
                {
                    body.Append(SectionHeading(block.PersonnelHeading, ax));
                    body.Append(new Paragraph(new ParagraphProperties(Sp(0, 60))));
                    if (!string.IsNullOrWhiteSpace(block.PersonnelBlurb))
                        body.Append(Styled(block.PersonnelBlurb, BF, 18, italic: true, after: 120, topLevel: true));
                }

                foreach (var person in pages[pi])
                {
                    ct.ThrowIfCancellationRequested();
                    PersonEntry(body, main, person, px, ax);
                    body.Append(new Paragraph(new ParagraphProperties(Sp(0, 0))));
                }

                if (pi < pages.Count - 1) body.Append(PageBreak());
            }
        }

        // Two people per page. The list order in the brochure content determines page assignment.
        private static List<List<BrochurePerson>> GroupPeople(IEnumerable<BrochurePerson> people)
        {
            var pages = new List<List<BrochurePerson>>();
            List<BrochurePerson>? pair = null;
            foreach (var p in people)
            {
                pair ??= new List<BrochurePerson>();
                pair.Add(p);
                if (pair.Count == 2) { pages.Add(pair); pair = null; }
            }
            if (pair is { Count: > 0 }) pages.Add(pair);
            return pages;
        }

        private void PersonEntry(Body body, MainDocumentPart main, BrochurePerson person,
            string px, string ax)
        {
            int pcw = (int)(ContentW * 0.30);
            int tcw = ContentW - pcw;
            var pb  = person.PhotoBytes is { Length: > 0 } ? person.PhotoBytes : TryLoad(person.PhotoPath);

            var pc = PhotoCell(main, pb, pcw, px, maxW: 2.0, ratio: 1.3);

            var tc = new TableCell();
            tc.Append(CellP(tcw,
                new TableCellMargin(new LeftMargin { Width = "240", Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));
            tc.Append(NamePara(person.Name, px));
            tc.Append(AccentRule(ax));
            if (!string.IsNullOrWhiteSpace(person.Credentials))
                tc.Append(Styled(person.Credentials, BF, 18, italic: true, after: 40));
            if (!string.IsNullOrWhiteSpace(person.Bio))
                tc.Append(Body9(person.Bio, after: 40));

            body.Append(TwoColTable(pc, tc));
        }

        // ── Overview ─────────────────────────────────────────────────────────

        private static void AppendOverview(Body body, BrochureBlock block, string ax, CancellationToken ct)
        {
            for (int i = 0; i < block.OverviewSections.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var s = block.OverviewSections[i];
                if (!string.IsNullOrWhiteSpace(s.Heading)) body.Append(SectionHeading(s.Heading, ax));
                if (!string.IsNullOrWhiteSpace(s.Body))    body.Append(Body9(s.Body, 120, topLevel: true));
                if (block.PageBreakAfterOverviewIndex.Contains(i)) body.Append(PageBreak());
            }
        }

        // ── Contact ──────────────────────────────────────────────────────────

        private static void AppendContact(Body body, BrochureContactConfig cfg,
            string px, string ax, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            body.Append(PageBreak());
            body.Append(SectionHeading("Contact", ax));
            if (cfg.Offices.Count == 0) return;

            int cw = ContentW / 2;
            var tbl = new Table(new TableProperties(
                new TableWidth { Width = ContentW.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableIndentation { Width = BodyInd, Type = TableWidthUnitValues.Dxa },
                NoBorders()));

            for (int i = 0; i < cfg.Offices.Count; i += 2)
            {
                ct.ThrowIfCancellationRequested();
                var r = new TableRow();
                r.Append(OfficeCell(cfg.Offices[i], px, ax, cw));
                r.Append(i + 1 < cfg.Offices.Count ? OfficeCell(cfg.Offices[i + 1], px, ax, cw) : EmptyCell(cw));
                tbl.Append(r);
            }
            body.Append(tbl);
        }

        private static TableCell OfficeCell(BrochureOfficeContact o, string px, string ax, int w)
        {
            var c = new TableCell();
            c.Append(CellP(w,
                new TableCellMargin(new RightMargin { Width = "200", Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));
            c.Append(Styled(o.Region, HF, 20, bold: true, color: px, after: 40));
            if (!string.IsNullOrWhiteSpace(o.Contact)) c.Append(Styled(o.Contact, BF, 17, bold: true, after: 20));
            if (!string.IsNullOrWhiteSpace(o.Phone))   c.Append(Body9(o.Phone, 10));
            if (!string.IsNullOrWhiteSpace(o.Email))   c.Append(Styled(o.Email, BF, 17, color: ax, after: 10));
            if (!string.IsNullOrWhiteSpace(o.Hours))   c.Append(Styled(o.Hours, BF, 16, italic: true, color: "888888", after: 80));
            return c;
        }

        // ── Client list ───────────────────────────────────────────────────────

        private static void AppendClientList(Body body, BrochureBlock block, string ax, CancellationToken ct)
        {
            if (block.ClientNames.Count == 0) return;
            var h = string.IsNullOrWhiteSpace(block.ClientListHeading) ? "Our Clients" : block.ClientListHeading;
            body.Append(PageBreak());
            body.Append(SectionHeading(h, ax));
            if (!string.IsNullOrWhiteSpace(block.ClientListPreamble))
                body.Append(Body9(block.ClientListPreamble, 120, topLevel: true));

            var sorted = block.ClientNames.Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            int cw = ContentW / 3;
            var tbl = new Table(new TableProperties(
                new TableWidth { Width = ContentW.ToString(), Type = TableWidthUnitValues.Dxa },
                new TableIndentation { Width = BodyInd, Type = TableWidthUnitValues.Dxa },
                NoBorders()));

            for (int i = 0; i < sorted.Count; i += 3)
            {
                ct.ThrowIfCancellationRequested();
                var row = new TableRow();
                for (int col = 0; col < 3; col++)
                {
                    var cell = new TableCell();
                    cell.Append(CellP(cw));
                    cell.Append(Body9(i + col < sorted.Count ? sorted[i + col] : string.Empty, 20));
                    row.Append(cell);
                }
                tbl.Append(row);
            }
            body.Append(tbl);
            if (!string.IsNullOrWhiteSpace(block.ClientListNote))
            {
                body.Append(new Paragraph(new ParagraphProperties(Sp(0, 0))));
                body.Append(Styled(block.ClientListNote, BF, 16, italic: true, color: "888888", after: 40, topLevel: true));
            }
        }

        // ── Table builders ────────────────────────────────────────────────────

        // Full-page-width table. With page margin = 0 this naturally spans edge to edge.
        private static Table FwTable(TableCell left, TableCell right) =>
            new Table(
                new TableProperties(
                    new TableWidth { Width = PageW.ToString(), Type = TableWidthUnitValues.Dxa },
                    NoBorders()),
                new TableRow(left, right));

        // Content-width table offset by BodyInd to align with body text.
        private static Table TwoColTable(TableCell left, TableCell right) =>
            new Table(
                new TableProperties(
                    new TableWidth { Width = ContentW.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableIndentation { Width = BodyInd, Type = TableWidthUnitValues.Dxa },
                    NoBorders()),
                new TableRow(left, right));

        private TableCell PhotoCell(MainDocumentPart main, byte[]? bytes, int colW, string px,
            double maxW = 3.0, double ratio = 0.667)
        {
            var cell = new TableCell();
            cell.Append(CellP(colW, new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Top }));

            if (bytes is { Length: > 0 })
            {
                var ip  = main.AddImagePart(ImgType(bytes));
                ip.FeedData(new MemoryStream(bytes));
                var rid = main.GetIdOfPart(ip);
                long mw = (long)(Math.Min(maxW, colW / 1440.0) * Epu);
                var d   = Dims(bytes);
                long mh = d.W > 0 && d.H > 0 ? (long)((double)d.H / d.W * mw) : (long)(mw * ratio);
                var (w, h) = Fit(d, mw, mh);
                cell.Append(new Paragraph(new ParagraphProperties(Sp(0, 0)), Run(Inline(rid, w, h, _id++))));
            }
            else
            {
                cell.Append(new Paragraph(new ParagraphProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = "EEEEEE" },
                    Sp(0, 0))));
            }
            return cell;
        }

        private static TableCell ShadedCell(int w, string fill, params OpenXmlElement[] extras)
        {
            var cell  = new TableCell();
            var props = new TableCellProperties(
                new TableCellWidth { Width = w.ToString(), Type = TableWidthUnitValues.Dxa },
                new Shading { Val = ShadingPatternValues.Clear, Color = "auto", Fill = fill });
            foreach (var e in extras) props.Append(e);
            cell.Append(props);
            return cell;
        }

        private static TableCellProperties CellP(int w, params OpenXmlElement[] extras)
        {
            var p = new TableCellProperties(
                new TableCellWidth { Width = w.ToString(), Type = TableWidthUnitValues.Dxa });
            foreach (var e in extras) p.Append(e);
            return p;
        }

        private static TableCell EmptyCell(int w)
        {
            var c = new TableCell();
            c.Append(CellP(w));
            c.Append(new Paragraph(new ParagraphProperties(Sp(0, 0))));
            return c;
        }

        private static TableBorders NoBorders() =>
            new TableBorders(
                new TopBorder              { Val = BorderValues.None },
                new BottomBorder           { Val = BorderValues.None },
                new LeftBorder             { Val = BorderValues.None },
                new RightBorder            { Val = BorderValues.None },
                new InsideHorizontalBorder { Val = BorderValues.None },
                new InsideVerticalBorder   { Val = BorderValues.None });

        // ── Paragraph builders ────────────────────────────────────────────────

        // Section heading: accent-colored ALL-CAPS text with bottom rule.
        // topLevel=true indents it to align with body content.
        private static Paragraph SectionHeading(string text, string accentHex) =>
            new Paragraph(
                new ParagraphProperties(
                    new Indentation { Left = BodyInd.ToString(), Right = BodyInd.ToString() },
                    Sp(200, 80),
                    new ParagraphBorders(new BottomBorder
                        { Val = BorderValues.Single, Color = accentHex, Size = 6U, Space = 4U })),
                Run(text.ToUpperInvariant(), HF, 22, bold: true, color: accentHex));

        private static Paragraph NamePara(string name, string px) =>
            new Paragraph(new ParagraphProperties(Sp(0, 20)),
                          Run(name, HF, 26, bold: true, color: px));

        private static Paragraph AccentRule(string ax) =>
            new Paragraph(
                new ParagraphProperties(
                    Sp(0, 40),
                    new ParagraphBorders(new BottomBorder
                        { Val = BorderValues.Single, Color = ax, Size = 6U, Space = 2U })));

        // Body paragraph. topLevel=true adds BodyInd left/right so it aligns
        // with section headings when used directly on the document body.
        private static Paragraph Body9(string text, int after = 60, bool topLevel = false)
        {
            var pp = new ParagraphProperties(
                new Justification { Val = JustificationValues.Both },
                Sp(0, after));
            if (topLevel) pp.Append(new Indentation { Left = BodyInd.ToString(), Right = BodyInd.ToString() });
            return new Paragraph(pp, Run(text, BF, 18, color: "222222"));
        }

        // Styled paragraph. topLevel=true adds BodyInd left indentation.
        private static Paragraph Styled(string text, string font, int hp,
            bool bold = false, bool italic = false, string? color = null, int after = 60, bool topLevel = false)
        {
            var pp = new ParagraphProperties(Sp(0, after));
            if (topLevel) pp.Append(new Indentation { Left = BodyInd.ToString() });
            return new Paragraph(pp, Run(text, font, hp, bold, italic, color));
        }

        private static Paragraph PageBreak() =>
            new Paragraph(new ParagraphProperties(Sp(0, 0)),
                          new Run(new Break { Type = BreakValues.Page }));

        // ── Run / property helpers ────────────────────────────────────────────

        private static Run Run(string text, string font, int hp,
            bool bold = false, bool italic = false, string? color = null)
        {
            var rp = new RunProperties(
                new RunFonts { Ascii = font, HighAnsi = font, ComplexScript = font },
                new FontSize { Val = hp.ToString() });
            if (bold)   rp.Append(new Bold());
            if (italic) rp.Append(new Italic());
            if (color is not null) rp.Append(new Color { Val = color });
            return new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }

        private static Run Run(Drawing d) => new Run(d);

        // SpacingBetweenLines — always used inside ParagraphProperties, never directly in Paragraph.
        private static SpacingBetweenLines Sp(int before, int after) =>
            new SpacingBetweenLines { Before = before.ToString(), After = after.ToString() };

        private static void AddPageField(Paragraph para, string color, int hp)
        {
            RunProperties Rp() => new RunProperties(
                new RunFonts { Ascii = BF, HighAnsi = BF },
                new FontSize { Val = hp.ToString() },
                new Color { Val = color });

            para.Append(new Run(Rp(), new Text("Page ") { Space = SpaceProcessingModeValues.Preserve }));
            para.Append(new Run(Rp(), new FieldChar { FieldCharType = FieldCharValues.Begin }));
            para.Append(new Run(Rp(), new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }));
            para.Append(new Run(Rp(), new FieldChar { FieldCharType = FieldCharValues.Separate }));
            para.Append(new Run(Rp(), new Text("1")));
            para.Append(new Run(Rp(), new FieldChar { FieldCharType = FieldCharValues.End }));
            para.Append(new Run(Rp(), new Text(" of ") { Space = SpaceProcessingModeValues.Preserve }));
            para.Append(new Run(Rp(), new FieldChar { FieldCharType = FieldCharValues.Begin }));
            para.Append(new Run(Rp(), new FieldCode(" NUMPAGES ") { Space = SpaceProcessingModeValues.Preserve }));
            para.Append(new Run(Rp(), new FieldChar { FieldCharType = FieldCharValues.Separate }));
            para.Append(new Run(Rp(), new Text("1")));
            para.Append(new Run(Rp(), new FieldChar { FieldCharType = FieldCharValues.End }));
        }

        // ── Image / drawing helpers ───────────────────────────────────────────

        private static Drawing Inline(string rid, long w, long h, uint id) =>
            new Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = w, Cy = h },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties { Id = id, Name = $"i{id}" },
                    new DW.NonVisualGraphicFrameDrawingProperties(
                        new A.GraphicFrameLocks { NoChangeAspect = true }),
                    Graphic(rid, w, h))
                { DistanceFromTop = 0U, DistanceFromBottom = 0U,
                  DistanceFromLeft = 0U, DistanceFromRight = 0U });

        private static Drawing Anchor(string rid, long w, long h, uint id) =>
            new Drawing(
                new DW.Anchor(
                    new DW.SimplePosition { X = 0L, Y = 0L },
                    new DW.HorizontalPosition(new DW.PositionOffset("0"))
                        { RelativeFrom = DW.HorizontalRelativePositionValues.Page },
                    new DW.VerticalPosition(new DW.PositionOffset("0"))
                        { RelativeFrom = DW.VerticalRelativePositionValues.Page },
                    new DW.Extent { Cx = w, Cy = h },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.WrapNone(),
                    new DW.DocProperties { Id = id, Name = $"a{id}" },
                    new DW.NonVisualGraphicFrameDrawingProperties(
                        new A.GraphicFrameLocks { NoChangeAspect = true }),
                    Graphic(rid, w, h))
                { SimplePos = false, RelativeHeight = 1U, BehindDoc = true,
                  Locked = false, LayoutInCell = true, AllowOverlap = true,
                  DistanceFromTop = 0U, DistanceFromBottom = 0U,
                  DistanceFromLeft = 0U, DistanceFromRight = 0U });

        private static A.Graphic Graphic(string rid, long w, long h) =>
            new A.Graphic(
                new A.GraphicData(
                    new PIC.Picture(
                        new PIC.NonVisualPictureProperties(
                            new PIC.NonVisualDrawingProperties { Id = 0U, Name = string.Empty },
                            new PIC.NonVisualPictureDrawingProperties()),
                        new PIC.BlipFill(
                            new A.Blip { Embed = rid },
                            new A.Stretch(new A.FillRectangle())),
                        new PIC.ShapeProperties(
                            new A.Transform2D(
                                new A.Offset { X = 0L, Y = 0L },
                                new A.Extents { Cx = w, Cy = h }),
                            new A.PresetGeometry(new A.AdjustValueList())
                            { Preset = A.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" });

        private static (long W, long H) Fit((int W, int H) d, long mw, long mh)
        {
            if (d.W <= 0 || d.H <= 0) return (mw, mh);
            double s = Math.Min((double)mw / d.W, (double)mh / d.H);
            return ((long)(d.W * s), (long)(d.H * s));
        }

        private static (int W, int H) Dims(byte[] b)
        {
            try
            {
                if (b.Length >= 24 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47)
                    return (
                        (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19],
                        (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23]);

                if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8)
                {
                    int i = 2;
                    while (i + 8 < b.Length)
                    {
                        if (b[i] != 0xFF) break;
                        byte m = b[i + 1];
                        int  l = (b[i + 2] << 8) | b[i + 3];
                        if (m == 0xC0 || m == 0xC1 || m == 0xC2)
                            return ((b[i + 7] << 8) | b[i + 8], (b[i + 5] << 8) | b[i + 6]);
                        i += 2 + l;
                    }
                }
            }
            catch (Exception) { /* malformed image header — return (0,0) to skip sizing */ }
            return (0, 0);
        }

        private static PartTypeInfo ImgType(byte[] b) =>
            b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
                ? ImagePartType.Png : ImagePartType.Jpeg;

        private static byte[]? FirstPhoto(BrochureProject p)
        {
            foreach (var ph in p.Photos)
            {
                if (ph.ImageBytes is { Length: > 0 }) return ph.ImageBytes;
                var b = TryLoad(ph.FilePath);
                if (b is { Length: > 0 }) return b;
            }
            return null;
        }

        // ── Asset / skin ──────────────────────────────────────────────────────

        private static void PrepareContent(BrochureContent c)
        {
            c.CompanyName = "KOR Structural";
            c.LogoPath = !string.IsNullOrWhiteSpace(c.ContactConfig?.LogoPath)
                ? c.ContactConfig.LogoPath
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Resources\kor-logo.png");
        }

        private static byte[]? TryLoad(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var r = Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
            if (!File.Exists(r)) return null;
            try { return File.ReadAllBytes(r); } catch (Exception) { return null; }
        }

        private static BrochureSkinDefinition ResolveSkin(BrochureContent c)
        {
            var b = BrochureSkinRegistry.Resolve(c.SkinId, c.TemplateName);
            if (string.IsNullOrWhiteSpace(c.PrimaryColorOverride) && string.IsNullOrWhiteSpace(c.AccentColorOverride))
                return b;
            return new BrochureSkinDefinition
            {
                Id = b.Id, DisplayName = b.DisplayName,
                PrimaryColor = string.IsNullOrWhiteSpace(c.PrimaryColorOverride) ? b.PrimaryColor : c.PrimaryColorOverride,
                AccentColor  = string.IsNullOrWhiteSpace(c.AccentColorOverride)  ? b.AccentColor  : c.AccentColorOverride,
                HeaderText = b.HeaderText, FontFamily = b.FontFamily, TitleFontFamily = b.TitleFontFamily,
                CoverTitleFontSize = b.CoverTitleFontSize, CoverTreatment = b.CoverTreatment,
                DividerStyle = b.DividerStyle, DividerThickness = b.DividerThickness,
                SectionSpacingPoints = b.SectionSpacingPoints
            };
        }

        private static string Hex(string s) => s.StartsWith('#') ? s[1..] : s;
    }
}
