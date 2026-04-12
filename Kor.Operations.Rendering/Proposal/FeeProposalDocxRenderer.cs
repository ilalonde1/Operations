#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Kor.Operations.Core.Models.Proposal;
using static Kor.Operations.Rendering.Proposal.ProposalRenderHelpers;
using A   = DocumentFormat.OpenXml.Drawing;
using DW  = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Kor.Operations.Rendering.Proposal
{
    public sealed class FeeProposalDocxRenderer : IFeeProposalDocxRenderer
    {
        // ── Page geometry (twips; 1 inch = 1440) ─────────────────────────────
        // Page L/R margins = 0 so header/footer tables span edge-to-edge.
        // All body content indented explicitly via BodyInd.
        private const int PageW   = 11906; // A4 width
        private const int PageH   = 16838; // A4 height
        private const int MarginT = 1008;  // 0.7" top
        private const int MarginB = 1008;  // 0.7" bottom
        private const int HdrDist = 0;     // header flush with top edge
        private const int FtrDist = 360;   // footer 0.25" from bottom
        private const int BodyInd = 720;   // explicit left/right indent for all body content
        private const int ContentW = PageW - BodyInd * 2; // effective content width

        // ── EMU (1 inch = 914400) ────────────────────────────────────────────
        private const long Epu   = 914400L;
        private const long LogoW = (long)(1.6 * Epu);
        private const long LogoH = (long)(0.55 * Epu);

        // ── Brand colours ─────────────────────────────────────────────────────
        private const string BrandSlate  = "3D5166";
        private const string BrandOrange = "E8722A";
        private const string White       = "FFFFFF";
        private const string DarkText    = "1F2937";

        private const string Font        = "Calibri";
        private const string KorAddress  = "# 501 - 510 Burrard Street, Vancouver, B.C. V6C 3A8";

        private IReadOnlyList<ProposalStaffMember> _staff = Array.Empty<ProposalStaffMember>();
        private Dictionary<string, ProposalStaffMember> _staffIndex = new();
        private uint _id = 1;

        // ── Entry point ──────────────────────────────────────────────────────

        public Task<string> RenderAsync(
            FeeProposal proposal,
            IReadOnlyList<ProposalStaffMember> staff,
            string outputPath,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(proposal);
            ArgumentNullException.ThrowIfNull(staff);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ct.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            _staff      = staff;
            _staffIndex = BuildStaffIndex(staff);
            _id         = 1;

            var logo = TryLoadLogo();

            using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var main   = document.AddMainDocumentPart();
            var body   = new Body();
            main.Document = new Document(body);

            // Headers / footers
            var p1h = main.AddNewPart<HeaderPart>(); // first-page header (empty)
            var dh  = main.AddNewPart<HeaderPart>(); // default running header
            var df  = main.AddNewPart<FooterPart>();  // default footer

            p1h.Header = new Header(new Paragraph(new ParagraphProperties(
                new SpacingBetweenLines { Before = "0", After = "0" })));
            BuildDefaultHeader(dh, main, logo);
            BuildDefaultFooter(df);

            var blocks = proposal.Blocks.Count > 0
                ? proposal.Blocks
                : new List<FeeProposalBlock>
                {
                    new()
                    {
                        BlockType = ProposalBlockType.FreeText,
                        FreeText  = new FreeTextBlockContent
                        {
                            Heading = "Proposal",
                            Body    = "No proposal blocks have been added."
                        }
                    }
                };

            foreach (var block in blocks)
            {
                ct.ThrowIfCancellationRequested();

                switch (block.BlockType)
                {
                    case ProposalBlockType.Cover:
                        AppendCover(body, block.Cover ?? new CoverBlockContent()); break;
                    case ProposalBlockType.Introduction:
                        AppendIntroduction(body, block.Introduction ?? new IntroductionBlockContent()); break;
                    case ProposalBlockType.Company:
                        AppendCompany(body, block.Company ?? new CompanyBlockContent()); break;
                    case ProposalBlockType.Personnel:
                        AppendPersonnel(body, block.Personnel ?? new PersonnelBlockContent()); break;
                    case ProposalBlockType.References:
                        AppendReferences(body, block.References ?? new ReferencesBlockContent()); break;
                    case ProposalBlockType.ProjectDescription:
                        AppendProjectDescription(body, block.ProjectDescription ?? new ProjectDescriptionBlockContent()); break;
                    case ProposalBlockType.FeeTable:
                        AppendFeeTable(body, block.FeeTable ?? new FeeTableBlockContent()); break;
                    case ProposalBlockType.Scope:
                        AppendScope(body, block.Scope ?? new ScopeBlockContent()); break;
                    case ProposalBlockType.ExcludedServices:
                        AppendExcludedServices(body, block.ExcludedServices ?? new ExcludedServicesBlockContent()); break;
                    case ProposalBlockType.ApprovalToProceed:
                        AppendApprovalToProceed(body, block.ApprovalToProceed ?? new ApprovalToProceedBlockContent()); break;
                    case ProposalBlockType.SignaturePage:
                        AppendSignaturePage(body, block.SignaturePage ?? new SignaturePageBlockContent()); break;
                    case ProposalBlockType.RatesTable:
                        AppendRatesTable(body, block.RatesTable ?? new RatesTableBlockContent()); break;
                    case ProposalBlockType.FreeText:
                        AppendFreeText(body, block.FreeText ?? new FreeTextBlockContent()); break;
                    case ProposalBlockType.PageBreak:
                        body.Append(CreatePageBreakParagraph()); break;
                }
            }

            // SectionProperties must be the LAST child of body
            body.Append(new SectionProperties(
                new TitlePage(),
                new HeaderReference { Type = HeaderFooterValues.First,   Id = main.GetIdOfPart(p1h) },
                new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(dh)  },
                new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(df)  },
                new PageSize   { Width = PageW, Height = PageH },
                new PageMargin { Top = MarginT, Bottom = MarginB, Left = 0, Right = 0,
                                 Header = (uint)HdrDist, Footer = (uint)FtrDist }));

            main.Document.Save();
            return Task.FromResult(outputPath);
        }

        // ── Default running header ────────────────────────────────────────────

        private void BuildDefaultHeader(HeaderPart part, MainDocumentPart main, byte[]? logo)
        {
            int lw = (int)(PageW * 0.50), rw = PageW - lw;

            var lc = ShadedCell(lw, BrandSlate,
                new TableCellMargin(
                    new LeftMargin   { Width = (BodyInd + 360).ToString(), Type = TableWidthUnitValues.Dxa },
                    new TopMargin    { Width = "200", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "200", Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });
            lc.Append(new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
                CreateRun("KOR Structural", 18, bold: true, colorHex: White)));

            var rc = ShadedCell(rw, BrandSlate,
                new TableCellMargin(
                    new RightMargin  { Width = "360", Type = TableWidthUnitValues.Dxa },
                    new TopMargin    { Width = "200", Type = TableWidthUnitValues.Dxa },
                    new BottomMargin { Width = "200", Type = TableWidthUnitValues.Dxa }),
                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center });

            if (logo is { Length: > 0 })
            {
                var ip = part.AddImagePart(ImgType(logo));
                ip.FeedData(new MemoryStream(logo));
                var rid = part.GetIdOfPart(ip);
                var (w, h) = Fit(Dims(logo), LogoW, LogoH);
                rc.Append(new Paragraph(
                    new ParagraphProperties(
                        new Justification { Val = JustificationValues.Right },
                        new SpacingBetweenLines { Before = "0", After = "0" }),
                    new Run(Inline(rid, w, h, _id++))));
            }
            else
            {
                rc.Append(new Paragraph(
                    new ParagraphProperties(
                        new Justification { Val = JustificationValues.Right },
                        new SpacingBetweenLines { Before = "0", After = "0" }),
                    CreateRun("Structured Engineering", 14, colorHex: "AAAAAA")));
            }

            part.Header = new Header(FwTable(lc, rc));
        }

        // ── Default footer ────────────────────────────────────────────────────

        private static void BuildDefaultFooter(FooterPart part)
        {
            var para = new Paragraph(
                new ParagraphProperties(
                    new Indentation { Left = BodyInd.ToString() },
                    new Tabs(new TabStop { Val = TabStopValues.Right, Position = BodyInd + ContentW }),
                    new SpacingBetweenLines { Before = "60", After = "0" },
                    new ParagraphBorders(new TopBorder
                        { Val = BorderValues.Single, Color = "CCCCCC", Size = 4U, Space = 4U })));

            para.Append(CreateRun(KorAddress, 9, colorHex: "888888"));
            para.Append(new Run(new TabChar()));
            AddPageField(para);

            part.Footer = new Footer(para);
        }

        // ── Block renderers ───────────────────────────────────────────────────

        private void AppendCover(Body body, CoverBlockContent content)
        {
            var preparer = FindStaff(content.PreparerStaffId);

            // Cover uses a full-width band paragraph (no indentation needed)
            body.Append(CreateBandParagraph("KOR Structural", 22, BrandSlate, White));
            AppendParagraph(body, "FEE PROPOSAL FOR STRUCTURAL ENGINEERING SERVICES", bold: true);
            AppendLabeledValue(body, "PROJECT", BuildMultilineValue(content.ProjectName, content.ProjectAddress), valueBold: true);
            AppendLabeledValue(
                body, "SUBMITTED TO",
                BuildMultilineValue(
                    content.ClientCompany,
                    content.ClientAddress,
                    BuildAttentionLine(content.AttentionName, content.AttentionTitle, content.AttentionEmail),
                    BuildCcLine(content.CcName, content.CcTitle, content.CcEmail)),
                valueBold: true);
            AppendLabeledValue(
                body, "SUBMITTED BY",
                BuildMultilineValue("Kor Structural", KorAddress, BuildPreparedByLine(preparer)),
                valueBold: true);
            AppendLabeledValue(body, "PROPOSAL DATE", content.ProposalDate, valueBold: true);
            if (!string.IsNullOrWhiteSpace(content.Jurisdiction))
                AppendLabeledValue(body, "JURISDICTION", content.Jurisdiction);

            body.Append(CreateSpacerParagraph());
        }

        private void AppendIntroduction(Body body, IntroductionBlockContent content)
        {
            var signatory = FindStaff(content.SignatoryStaffId);

            AppendSectionHeader(body, "INTRODUCTION");
            AppendParagraph(body, $"Dear {DefaultIfEmpty(content.SalutationName, "Client")},");
            AppendParagraph(body,
                $"Please find our fee proposal for structural engineering services for " +
                $"{DefaultIfEmpty(content.ProjectAddress, "the project")} based on " +
                $"{DefaultIfEmpty(content.DrawingReference, "the referenced drawing package")}.");
            AppendParagraph(body, content.CloserText);
            AppendParagraph(body, "Best Regards,");
            AppendParagraph(body, "Kor Structural", bold: true);
            AppendParagraph(body, BuildStaffDisplay(signatory), bold: true);
            AppendParagraph(body, signatory?.Title ?? "[Staff not found]");
            AppendParagraph(body,
                signatory is null
                    ? "[Staff not found]"
                    : string.Join(" | ",
                        new[] { signatory.Email, signatory.Phone }
                            .Where(x => !string.IsNullOrWhiteSpace(x))
                            .Select(x => x!.Trim())));

            body.Append(CreateSpacerParagraph());
        }

        private void AppendCompany(Body body, CompanyBlockContent content)
        {
            AppendSectionHeader(body, DefaultIfEmpty(content.Heading, "OUR COMPANY").ToUpperInvariant());

            foreach (var section in content.Sections.Where(s =>
                !string.IsNullOrWhiteSpace(s.Title) || !string.IsNullOrWhiteSpace(s.Body)))
            {
                AppendOrangeHeading(body, section.Title);
                AppendParagraph(body, section.Body);
            }

            body.Append(CreateSpacerParagraph());
        }

        private void AppendPersonnel(Body body, PersonnelBlockContent content)
        {
            AppendSectionHeader(body, "OUR COMPANY");
            AppendOrangeHeading(body, "Project Personnel and Experience");

            AppendStaffBio(body, content.LeadStaffId, content.LeadBioOverride);
            AppendStaffBio(body, content.SupportingStaffId, content.SupportingBioOverride);

            if (!string.IsNullOrWhiteSpace(content.CollaborationNote))
                AppendParagraph(body, content.CollaborationNote);

            var additionalStaff = content.AdditionalStaffIds
                .Select(FindStaff)
                .Select(m => JoinText(BuildStaffDisplay(m), m?.Title))
                .ToList();

            if (additionalStaff.Count > 0)
            {
                AppendParagraph(body, "Additional staff dedicated to this project will include:");
                AppendBulletList(body, additionalStaff);
            }

            body.Append(CreateSpacerParagraph());
        }

        private void AppendReferences(Body body, ReferencesBlockContent content)
        {
            AppendSectionHeader(body, "OUR COMPANY");
            AppendOrangeHeading(body, content.Heading);
            AppendParagraph(body, content.Preamble);

            var clients = content.ClientNames.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (clients.Count > 0)
            {
                var rows = new List<string[]>();
                for (var i = 0; i < clients.Count; i += 3)
                    rows.Add(new[]
                    {
                        clients[i],
                        i + 1 < clients.Count ? clients[i + 1] : string.Empty,
                        i + 2 < clients.Count ? clients[i + 2] : string.Empty,
                    });
                AppendTable(body, new[] { string.Empty, string.Empty, string.Empty }, rows);
            }

            if (!string.IsNullOrWhiteSpace(content.ProjectSpecificNote))
                AppendParagraph(body, content.ProjectSpecificNote);

            body.Append(CreateSpacerParagraph());
        }

        private void AppendProjectDescription(Body body, ProjectDescriptionBlockContent content)
        {
            AppendSectionHeader(body, "OUR PROPOSAL");
            AppendOrangeHeading(body, content.Heading);
            AppendParagraph(body, content.Preamble);
            AppendBulletList(body, content.AssumptionBullets.Where(x => !string.IsNullOrWhiteSpace(x)));
            body.Append(CreateSpacerParagraph());
        }

        private void AppendFeeTable(Body body, FeeTableBlockContent content)
        {
            AppendOrangeHeading(body, content.Heading);
            AppendParagraph(body, content.Preamble);

            var phases = content.Phases.Where(p => !string.IsNullOrWhiteSpace(p.PhaseName)).ToList();
            var rows   = phases.Select(p => new[] { p.PhaseName, FormatCurrency(p.Fee) }).ToList();
            rows.Add(new[] { "Total Base Fees:", FormatCurrency(phases.Sum(x => x.Fee)) });
            AppendTable(body, new[] { "Phase", "Fee" }, rows, lastRowBold: true);

            if (content.FieldReviewVisits > 0)
                AppendParagraph(body, $"Including up to {content.FieldReviewVisits.ToString(CultureInfo.InvariantCulture)} field review visits");

            AppendParagraph(body, $"A disbursements allowance of {FormatCurrency(content.DisbursementsAllowance)} has been included.");

            foreach (var note in content.AdditionalNotes.Where(x => !string.IsNullOrWhiteSpace(x)))
                AppendParagraph(body, note);

            body.Append(CreateSpacerParagraph());
        }

        private void AppendScope(Body body, ScopeBlockContent content)
        {
            AppendSectionHeader(body, "OUR PROPOSAL");
            AppendOrangeHeading(body, content.Heading);

            if (!string.IsNullOrWhiteSpace(content.Narrative))
                AppendParagraph(body, content.Narrative);

            AppendBulletList(body,
                content.IncludedServices
                    .Where(item => item.IsActive && !string.IsNullOrWhiteSpace(item.Text))
                    .Select(item => item.Text),
                numbered: true);

            body.Append(CreateSpacerParagraph());
        }

        private void AppendExcludedServices(Body body, ExcludedServicesBlockContent content)
        {
            AppendSectionHeader(body, "OUR PROPOSAL");
            AppendOrangeHeading(body, content.Heading);
            AppendParagraph(body, content.Preamble);
            AppendBulletList(body, content.ExcludedItems.Where(x => !string.IsNullOrWhiteSpace(x)), numbered: true);
            body.Append(CreateSpacerParagraph());
        }

        private void AppendApprovalToProceed(Body body, ApprovalToProceedBlockContent content)
        {
            AppendOrangeHeading(body, content.Heading);
            AppendParagraph(body, content.IntroParagraph);
            AppendParagraph(body, content.InvoicingParagraph);
            body.Append(CreateSpacerParagraph());
        }

        private void AppendSignaturePage(Body body, SignaturePageBlockContent content)
        {
            AppendSectionHeader(body, "OUR PROPOSAL");
            AppendParagraph(body, content.ClosingParagraph);
            AppendParagraph(body, "Yours truly,");
            AppendParagraph(body, "Kor Structural", bold: true);
            AppendParagraph(body, "EGBC Permit #1000378");

            foreach (var signatory in content.SignatoryStaffIds.Select(FindStaff).Take(2))
            {
                AppendParagraph(body, "______________________________", sizePt: 10);
                AppendParagraph(body, BuildStaffDisplay(signatory), bold: true);
                AppendParagraph(body, signatory?.Title ?? "[Staff not found]");
            }

            AppendParagraph(body, "Fee Proposal accepted, and Professional Liability Insurance Advice acknowledged.");
            AppendParagraph(body, DefaultIfEmpty(content.ClientCompanyName, "Client Company"), bold: true);
            AppendParagraph(body, "Per: ______________________________");
            AppendParagraph(body, "Authorized Signatory: ______________________________");
            AppendParagraph(body, "Date: ______________________________");
            AppendParagraph(body,
                content.IncludeRatesAppendix
                    ? "Attachments: Appendix A - Schedule of Standard Hourly Billing Rates."
                    : "Attachments: None.");
            body.Append(CreateSpacerParagraph());
        }

        private void AppendRatesTable(Body body, RatesTableBlockContent content)
        {
            AppendSectionHeader(body, "APPENDIX A");
            AppendOrangeHeading(body, "Schedule of Standard Hourly Billing Rates");

            if (!string.IsNullOrWhiteSpace(content.EffectiveDate))
                AppendParagraph(body, $"Effective date: {content.EffectiveDate}");

            AppendTable(body,
                new[] { "Role", "Rate / Hour" },
                content.Rates
                    .Where(r => !string.IsNullOrWhiteSpace(r.Role))
                    .Select(r => new[] { r.Role, FormatRate(r.RatePerHour) }));

            body.Append(CreateSpacerParagraph());
        }

        private void AppendFreeText(Body body, FreeTextBlockContent content)
        {
            if (!string.IsNullOrWhiteSpace(content.Heading))
                AppendOrangeHeading(body, content.Heading);

            AppendParagraph(body, content.Body);
            body.Append(CreateSpacerParagraph());
        }

        // ── Shared paragraph helpers ──────────────────────────────────────────

        private static void AppendSectionHeader(Body body, string text)
        {
            // Full-width band (no indentation) — page margin is 0 so shading spans edge-to-edge
            body.Append(CreateBandParagraph(text, 9, BrandSlate, White, allCaps: true));
        }

        private static void AppendOrangeHeading(Body body, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            body.Append(new Paragraph(
                new ParagraphProperties(
                    new Indentation { Left = BodyInd.ToString() },
                    new SpacingBetweenLines { Before = "160", After = "100" },
                    new ParagraphBorders(new BottomBorder
                        { Val = BorderValues.Single, Color = BrandOrange, Size = 8U })),
                CreateRun(text, 14, bold: true, colorHex: BrandOrange)));
        }

        private static void AppendParagraph(Body body, string? text, bool bold = false, int sizePt = 11)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            body.Append(CreateParagraph(text, sizePt, bold: bold));
        }

        private static void AppendBulletList(Body body, IEnumerable<string?> items, bool numbered = false)
        {
            var index = 1;
            foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var prefix = numbered
                    ? $"{index.ToString(CultureInfo.InvariantCulture)}."
                    : "•";
                body.Append(new Paragraph(
                    new ParagraphProperties(
                        new Indentation { Left = (BodyInd + 720).ToString(), Hanging = "360" },
                        new SpacingBetweenLines { After = "60" }),
                    CreateRun(prefix + " ", 11, bold: true, colorHex: BrandOrange),
                    CreateRun(item!, 11)));
                index++;
            }
        }

        private static void AppendTable(Body body, string[] headers, IEnumerable<string[]> rows, bool lastRowBold = false)
        {
            var rowList = rows.ToList();
            if (headers.Length == 0 && rowList.Count == 0) return;

            var table = new Table(
                new TableProperties(
                    new TableWidth { Width = ContentW.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableIndentation { Width = BodyInd, Type = TableWidthUnitValues.Dxa },
                    new TableBorders(
                        new TopBorder               { Val = BorderValues.Single, Size = 8U, Color = "D9E2EC" },
                        new BottomBorder            { Val = BorderValues.Single, Size = 8U, Color = "D9E2EC" },
                        new LeftBorder              { Val = BorderValues.Single, Size = 8U, Color = "D9E2EC" },
                        new RightBorder             { Val = BorderValues.Single, Size = 8U, Color = "D9E2EC" },
                        new InsideHorizontalBorder  { Val = BorderValues.Single, Size = 4U, Color = "D9E2EC" },
                        new InsideVerticalBorder    { Val = BorderValues.Single, Size = 4U, Color = "D9E2EC" })));

            if (headers.Any(h => !string.IsNullOrWhiteSpace(h)))
                table.Append(new TableRow(headers.Select(h => CreateTableCell(h, bold: true, rightAlign: false, shadingFill: BrandSlate, textColor: White))));

            for (var i = 0; i < rowList.Count; i++)
            {
                var isLast = i == rowList.Count - 1;
                table.Append(new TableRow(rowList[i].Select((value, col) =>
                    CreateTableCell(
                        value,
                        bold: lastRowBold && isLast,
                        rightAlign: col == rowList[i].Length - 1,
                        shadingFill: isLast && lastRowBold ? "EEF2F6" : (i % 2 == 0 ? White : "F8FAFC"),
                        textColor: DarkText))));
            }

            body.Append(table);
        }

        private void AppendStaffBio(Body body, string staffId, string bioOverride)
        {
            var member = FindStaff(staffId);
            if (member is null && string.IsNullOrWhiteSpace(bioOverride)) return;

            var heading = member is not null ? JoinText(BuildStaffDisplay(member), member.Title) : string.Empty;
            if (!string.IsNullOrWhiteSpace(heading))
                AppendParagraph(body, heading, bold: true);

            var bio = !string.IsNullOrWhiteSpace(bioOverride) ? bioOverride : member?.Bio;
            if (!string.IsNullOrWhiteSpace(bio))
                AppendParagraph(body, bio);
        }

        private static void AppendLabeledValue(Body body, string label, string? value, bool valueBold = false)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            var runs = new List<Run> { CreateRun(label + ": ", 11, bold: true) };
            var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                runs.Add(CreateRun(lines[i], 11, bold: valueBold));
                if (i < lines.Length - 1) runs.Add(new Run(new Break()));
            }

            var para = new Paragraph(CreateStandardParagraphProperties());
            para.Append(runs);
            body.Append(para);
        }

        // ── Paragraph / run factories ─────────────────────────────────────────

        // Band paragraph: full-width shaded background (no indentation — page margin is 0)
        private static Paragraph CreateBandParagraph(string text, int sizePt, string fillHex, string textHex,
            bool center = false, bool allCaps = false) =>
            new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "120", After = "120" },
                    new Justification { Val = center ? JustificationValues.Center : JustificationValues.Left },
                    new Shading { Val = ShadingPatternValues.Clear, Fill = fillHex }),
                CreateRun(allCaps ? text.ToUpperInvariant() : text, sizePt, bold: true, colorHex: textHex));

        private static Paragraph CreateParagraph(string? text, int sizePt, bool bold = false) =>
            new Paragraph(CreateStandardParagraphProperties(), CreateRun(text ?? string.Empty, sizePt, bold: bold));

        private static Paragraph CreatePageBreakParagraph() =>
            new Paragraph(new ParagraphProperties(new SpacingBetweenLines { Before = "0", After = "0" }),
                          new Run(new Break { Type = BreakValues.Page }));

        private static Paragraph CreateSpacerParagraph() =>
            new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "120" }));

        private static ParagraphProperties CreateStandardParagraphProperties() =>
            new ParagraphProperties(
                new Indentation { Left = BodyInd.ToString() },
                new SpacingBetweenLines { After = "100", Line = "276", LineRule = LineSpacingRuleValues.Auto });

        private static Run CreateRun(string text, int sizePt, bool bold = false, string? colorHex = null)
        {
            var rp = new RunProperties(
                new RunFonts { Ascii = Font, HighAnsi = Font, ComplexScript = Font },
                new FontSize { Val = (sizePt * 2).ToString(CultureInfo.InvariantCulture) },
                new Color { Val = colorHex ?? DarkText });
            if (bold) rp.Append(new Bold());
            return new Run(rp, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }

        private static TableCell CreateTableCell(string? text, bool bold, bool rightAlign, string shadingFill, string textColor) =>
            new TableCell(
                new TableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Fill = shadingFill },
                    new TableCellMargin(
                        new TopMargin    { Width = "90", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                        new LeftMargin   { Width = "90", Type = TableWidthUnitValues.Dxa },
                        new RightMargin  { Width = "90", Type = TableWidthUnitValues.Dxa })),
                new Paragraph(
                    new ParagraphProperties(
                        new Justification { Val = rightAlign ? JustificationValues.Right : JustificationValues.Left }),
                    CreateRun(text ?? string.Empty, 11, bold: bold, colorHex: textColor)));

        // ── Full-width header table (page margin = 0, no negative indent needed) ──

        private static Table FwTable(TableCell left, TableCell right) =>
            new Table(
                new TableProperties(
                    new TableWidth { Width = PageW.ToString(), Type = TableWidthUnitValues.Dxa },
                    new TableBorders(
                        new TopBorder              { Val = BorderValues.None },
                        new BottomBorder           { Val = BorderValues.None },
                        new LeftBorder             { Val = BorderValues.None },
                        new RightBorder            { Val = BorderValues.None },
                        new InsideHorizontalBorder { Val = BorderValues.None },
                        new InsideVerticalBorder   { Val = BorderValues.None })),
                new TableRow(left, right));

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

        // ── Page-number field ─────────────────────────────────────────────────

        private static void AddPageField(Paragraph para)
        {
            var rp = new RunProperties(
                new RunFonts { Ascii = Font, HighAnsi = Font },
                new FontSize { Val = "18" },
                new Color { Val = "888888" });

            RunProperties Rp() => (RunProperties)rp.CloneNode(true);

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

        // ── Image helpers (logo in header) ────────────────────────────────────

        private static Drawing Inline(string rid, long w, long h, uint id) =>
            new Drawing(
                new DW.Inline(
                    new DW.Extent { Cx = w, Cy = h },
                    new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
                    new DW.DocProperties { Id = id, Name = $"i{id}" },
                    new DW.NonVisualGraphicFrameDrawingProperties(
                        new A.GraphicFrameLocks { NoChangeAspect = true }),
                    MakeGraphic(rid, w, h))
                { DistanceFromTop = 0U, DistanceFromBottom = 0U,
                  DistanceFromLeft = 0U, DistanceFromRight = 0U });

        private static A.Graphic MakeGraphic(string rid, long w, long h) =>
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

        private static byte[]? TryLoadLogo()
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"Resources\kor-logo.png");
            if (!File.Exists(path)) return null;
            try { return File.ReadAllBytes(path); } catch (Exception) { return null; }
        }

        private ProposalStaffMember? FindStaff(string id) =>
            _staffIndex.TryGetValue(id, out var s) ? s : null;

        private static string BuildMultilineValue(params string?[] values) =>
            string.Join("\n", values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
    }
}
