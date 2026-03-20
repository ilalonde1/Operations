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

namespace Kor.Operations.Rendering.Proposal
{
    public sealed class FeeProposalDocxRenderer : IFeeProposalDocxRenderer
    {
        private const string BrandSlate = "3D5166";
        private const string BrandOrange = "E8722A";
        private const string White = "FFFFFF";
        private const string DarkText = "1F2937";
        private const string MutedText = "475569";
        private const string PageWidth = "11906";
        private const string PageHeight = "16838";
        private const string MarginSize = "720";
        private const string KorAddress = "# 501 - 510 Burrard Street, Vancouver, B.C. V6C 3A8";

        private IReadOnlyList<ProposalStaffMember> _staff = Array.Empty<ProposalStaffMember>();

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

            _staff = staff;

            using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();
            mainPart.Document = new Document(body);

            AppendPageSetup(body);

            var blocks = proposal.Blocks.Count > 0
                ? proposal.Blocks
                : new List<FeeProposalBlock>
                {
                    new()
                    {
                        BlockType = ProposalBlockType.FreeText,
                        FreeText = new FreeTextBlockContent
                        {
                            Heading = "Proposal",
                            Body = "No proposal blocks have been added."
                        }
                    }
                };

            foreach (var block in blocks)
            {
                ct.ThrowIfCancellationRequested();

                switch (block.BlockType)
                {
                    case ProposalBlockType.Cover:
                        AppendCover(body, block.Cover ?? new CoverBlockContent());
                        break;
                    case ProposalBlockType.Introduction:
                        AppendIntroduction(body, block.Introduction ?? new IntroductionBlockContent());
                        break;
                    case ProposalBlockType.Company:
                        AppendCompany(body, block.Company ?? new CompanyBlockContent());
                        break;
                    case ProposalBlockType.Personnel:
                        AppendPersonnel(body, block.Personnel ?? new PersonnelBlockContent());
                        break;
                    case ProposalBlockType.References:
                        AppendReferences(body, block.References ?? new ReferencesBlockContent());
                        break;
                    case ProposalBlockType.ProjectDescription:
                        AppendProjectDescription(body, block.ProjectDescription ?? new ProjectDescriptionBlockContent());
                        break;
                    case ProposalBlockType.FeeTable:
                        AppendFeeTable(body, block.FeeTable ?? new FeeTableBlockContent());
                        break;
                    case ProposalBlockType.Scope:
                        AppendScope(body, block.Scope ?? new ScopeBlockContent());
                        break;
                    case ProposalBlockType.ExcludedServices:
                        AppendExcludedServices(body, block.ExcludedServices ?? new ExcludedServicesBlockContent());
                        break;
                    case ProposalBlockType.ApprovalToProceed:
                        AppendApprovalToProceed(body, block.ApprovalToProceed ?? new ApprovalToProceedBlockContent());
                        break;
                    case ProposalBlockType.SignaturePage:
                        AppendSignaturePage(body, block.SignaturePage ?? new SignaturePageBlockContent());
                        break;
                    case ProposalBlockType.RatesTable:
                        AppendRatesTable(body, block.RatesTable ?? new RatesTableBlockContent());
                        break;
                    case ProposalBlockType.FreeText:
                        AppendFreeText(body, block.FreeText ?? new FreeTextBlockContent());
                        break;
                    case ProposalBlockType.PageBreak:
                        body.Append(CreatePageBreakParagraph());
                        break;
                }
            }

            mainPart.Document.Save();
            return Task.FromResult(outputPath);
        }

        private static void AppendPageSetup(Body body)
        {
            body.Append(new SectionProperties(
                new PageSize { Width = UInt32Value.FromUInt32(uint.Parse(PageWidth, CultureInfo.InvariantCulture)), Height = UInt32Value.FromUInt32(uint.Parse(PageHeight, CultureInfo.InvariantCulture)) },
                new PageMargin
                {
                    Top = Int32Value.FromInt32(int.Parse(MarginSize, CultureInfo.InvariantCulture)),
                    Right = UInt32Value.FromUInt32(uint.Parse(MarginSize, CultureInfo.InvariantCulture)),
                    Bottom = Int32Value.FromInt32(int.Parse(MarginSize, CultureInfo.InvariantCulture)),
                    Left = UInt32Value.FromUInt32(uint.Parse(MarginSize, CultureInfo.InvariantCulture)),
                    Header = UInt32Value.FromUInt32(360),
                    Footer = UInt32Value.FromUInt32(360),
                    Gutter = UInt32Value.FromUInt32(0),
                }));
        }

        private void AppendCover(Body body, CoverBlockContent content)
        {
            var preparer = FindStaff(content.PreparerStaffId);

            body.Append(CreateBandParagraph("Kor Structural", 22, BrandSlate, White, center: false));
            AppendParagraph(body, "FEE PROPOSAL FOR STRUCTURAL ENGINEERING SERVICES", bold: true, sizePt: 11);
            AppendLabeledValue(body, "PROJECT", BuildLineValue(content.ProjectName, content.ProjectAddress), valueBold: true);
            AppendLabeledValue(
                body,
                "SUBMITTED TO",
                BuildMultilineValue(
                    content.ClientCompany,
                    content.ClientAddress,
                    BuildAttentionLine(content.AttentionName, content.AttentionTitle),
                    content.AttentionEmail,
                    BuildCcLine(content.CcName, content.CcTitle),
                    content.CcEmail),
                valueBold: true);
            AppendLabeledValue(
                body,
                "SUBMITTED BY",
                BuildMultilineValue(
                    "Kor Structural",
                    KorAddress,
                    BuildPreparedByLine(preparer),
                    preparer?.Title,
                    BuildContactLine(preparer)),
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
            AppendParagraph(
                body,
                $"Please find our fee proposal for structural engineering services for {DefaultIfEmpty(content.ProjectAddress, "the project")} based on {DefaultIfEmpty(content.DrawingReference, "the referenced drawing package")}.");
            AppendParagraph(body, content.CloserText);
            AppendParagraph(body, "Best Regards,");
            AppendParagraph(body, "Kor Structural", bold: true);

            if (signatory is not null)
            {
                AppendParagraph(body, BuildStaffDisplay(signatory), bold: true);
                AppendParagraph(body, signatory.Title);

                var contact = BuildContactLine(signatory);
                if (!string.IsNullOrWhiteSpace(contact))
                    AppendParagraph(body, contact);
            }

            body.Append(CreateSpacerParagraph());
        }

        private void AppendCompany(Body body, CompanyBlockContent content)
        {
            AppendSectionHeader(body, DefaultIfEmpty(content.Heading, "OUR COMPANY").ToUpperInvariant());

            foreach (var section in content.Sections.Where(s => !string.IsNullOrWhiteSpace(s.Title) || !string.IsNullOrWhiteSpace(s.Body)))
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
                .Where(s => s is not null)
                .Cast<ProposalStaffMember>()
                .Select(member => $"{BuildStaffDisplay(member)}{JoinText(", ", member.Title)}")
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
                {
                    rows.Add(new[]
                    {
                        clients[i],
                        i + 1 < clients.Count ? clients[i + 1] : string.Empty,
                        i + 2 < clients.Count ? clients[i + 2] : string.Empty,
                    });
                }

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
            var rows = phases.Select(p => new[] { p.PhaseName, FormatCurrency(p.Fee) }).ToList();
            rows.Add(new[] { "Total Base Fees:", FormatCurrency(phases.Sum(x => x.Fee)) });
            AppendTable(body, new[] { "Phase", "Fee" }, rows, lastRowBold: true);

            if (content.FieldReviewVisits > 0)
            {
                AppendParagraph(
                    body,
                    $"Including up to {content.FieldReviewVisits.ToString(CultureInfo.InvariantCulture)} field review visits");
            }

            AppendParagraph(
                body,
                $"A disbursements allowance of {FormatCurrency(content.DisbursementsAllowance)} has been included.");

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

            AppendBulletList(
                body,
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

            var signatories = content.SignatoryStaffIds
                .Select(FindStaff)
                .Where(s => s is not null)
                .Cast<ProposalStaffMember>()
                .Take(2)
                .ToList();

            foreach (var signatory in signatories)
            {
                AppendParagraph(body, "______________________________", sizePt: 10);
                AppendParagraph(body, BuildStaffDisplay(signatory), bold: true);
                AppendParagraph(body, signatory.Title);
            }

            AppendParagraph(body, "Fee Proposal accepted, and Professional Liability Insurance Advice acknowledged.");
            AppendParagraph(body, DefaultIfEmpty(content.ClientCompanyName, "Client Company"), bold: true);
            AppendParagraph(body, "Per: ______________________________");
            AppendParagraph(body, "Authorized Signatory: ______________________________");
            AppendParagraph(body, "Date: ______________________________");
            AppendParagraph(
                body,
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

            AppendTable(
                body,
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

        private static void AppendSectionHeader(Body body, string text)
        {
            body.Append(CreateBandParagraph(text, 9, BrandSlate, White, allCaps: true));
        }

        private static void AppendOrangeHeading(Body body, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var paragraph = new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "160", After = "100" },
                    new ParagraphBorders(
                        new BottomBorder
                        {
                            Val = BorderValues.Single,
                            Color = BrandOrange,
                            Size = 8U,
                        })),
                CreateRun(text, 14, bold: true, colorHex: BrandOrange));

            body.Append(paragraph);
        }

        private static void AppendParagraph(Body body, string text, bool bold = false, int sizePt = 11)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            body.Append(CreateParagraph(text, sizePt, bold: bold));
        }

        private static void AppendBulletList(Body body, IEnumerable<string> items, bool numbered = false)
        {
            var index = 1;
            foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                var prefix = numbered
                    ? $"{index.ToString(CultureInfo.InvariantCulture)}."
                    : "•";

                body.Append(
                    new Paragraph(
                        new ParagraphProperties(
                            new Indentation { Left = "720", Hanging = "360" },
                            new SpacingBetweenLines { After = "60" }),
                        CreateRun(prefix + " ", 11, bold: true, colorHex: BrandOrange),
                        CreateRun(item, 11)));
                index++;
            }
        }

        private static void AppendTable(Body body, string[] headers, IEnumerable<string[]> rows, bool lastRowBold = false)
        {
            var rowList = rows.ToList();
            if (headers.Length == 0 && rowList.Count == 0)
                return;

            var table = new Table(
                new TableProperties(
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 8U, Color = "D9E2EC" },
                        new BottomBorder { Val = BorderValues.Single, Size = 8U, Color = "D9E2EC" },
                        new LeftBorder { Val = BorderValues.Single, Size = 8U, Color = "D9E2EC" },
                        new RightBorder { Val = BorderValues.Single, Size = 8U, Color = "D9E2EC" },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4U, Color = "D9E2EC" },
                        new InsideVerticalBorder { Val = BorderValues.Single, Size = 4U, Color = "D9E2EC" })));

            if (headers.Any(h => !string.IsNullOrWhiteSpace(h)))
            {
                table.Append(new TableRow(headers.Select(header => CreateTableCell(header, true, false, BrandSlate, White))));
            }

            for (var i = 0; i < rowList.Count; i++)
            {
                var isLast = i == rowList.Count - 1;
                table.Append(new TableRow(rowList[i].Select((value, index) =>
                    CreateTableCell(
                        value,
                        bold: lastRowBold && isLast,
                        rightAlign: index == rowList[i].Length - 1,
                        shadingFill: isLast && lastRowBold ? "EEF2F6" : (i % 2 == 0 ? White : "F8FAFC"),
                        textColor: DarkText))));
            }

            body.Append(table);
        }

        private void AppendStaffBio(Body body, string staffId, string bioOverride)
        {
            var staff = FindStaff(staffId);
            if (staff is null && string.IsNullOrWhiteSpace(bioOverride))
                return;

            var heading = staff is not null ? $"{BuildStaffDisplay(staff)}{JoinText(", ", staff.Title)}" : string.Empty;
            if (!string.IsNullOrWhiteSpace(heading))
                AppendParagraph(body, heading, bold: true);

            var bio = !string.IsNullOrWhiteSpace(bioOverride) ? bioOverride : staff?.Bio;
            if (!string.IsNullOrWhiteSpace(bio))
                AppendParagraph(body, bio);
        }

        private static void AppendLabeledValue(Body body, string label, string? value, bool valueBold = false)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            var runs = new List<Run>
            {
                CreateRun(label + ": ", 11, bold: true),
            };

            var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                runs.Add(CreateRun(lines[i], 11, bold: valueBold));
                if (i < lines.Length - 1)
                    runs.Add(new Run(new Break()));
            }

            var paragraph = new Paragraph(CreateStandardParagraphProperties());
            paragraph.Append(runs);
            body.Append(paragraph);
        }

        private static Paragraph CreateBandParagraph(string text, int sizePt, string fillHex, string textHex, bool center = false, bool allCaps = false)
        {
            return new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines { Before = "120", After = "120" },
                    new Justification { Val = center ? JustificationValues.Center : JustificationValues.Left },
                    new Shading { Val = ShadingPatternValues.Clear, Fill = fillHex }),
                CreateRun(allCaps ? text.ToUpperInvariant() : text, sizePt, bold: true, colorHex: textHex));
        }

        private static Paragraph CreateParagraph(string? text, int fontSize, bool bold = false)
        {
            return new Paragraph(
                CreateStandardParagraphProperties(),
                CreateRun(text ?? string.Empty, fontSize, bold: bold));
        }

        private static Paragraph CreatePageBreakParagraph()
        {
            return new Paragraph(new Run(new Break { Type = BreakValues.Page }));
        }

        private static Paragraph CreateSpacerParagraph()
        {
            return new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "120" }));
        }

        private static ParagraphProperties CreateStandardParagraphProperties()
        {
            return new ParagraphProperties(
                new SpacingBetweenLines
                {
                    After = "100",
                    Line = "276",
                    LineRule = LineSpacingRuleValues.Auto,
                });
        }

        private static Run CreateRun(string text, int sizePt, bool bold = false, string? colorHex = null)
        {
            var runProperties = new RunProperties(
                new RunFonts { Ascii = "Mulish", HighAnsi = "Mulish" },
                new FontSize { Val = (sizePt * 2).ToString(CultureInfo.InvariantCulture) },
                new Color { Val = colorHex ?? DarkText });

            if (bold)
                runProperties.Append(new Bold());

            return new Run(runProperties, new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        }

        private static TableCell CreateTableCell(string? text, bool bold, bool rightAlign, string shadingFill, string textColor)
        {
            return new TableCell(
                new TableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Fill = shadingFill },
                    new TableCellMargin(
                        new TopMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                        new BottomMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                        new LeftMargin { Width = "90", Type = TableWidthUnitValues.Dxa },
                        new RightMargin { Width = "90", Type = TableWidthUnitValues.Dxa })),
                new Paragraph(
                    new ParagraphProperties(
                        new Justification { Val = rightAlign ? JustificationValues.Right : JustificationValues.Left }),
                    CreateRun(text ?? string.Empty, 11, bold: bold, colorHex: textColor)));
        }

        private ProposalStaffMember? FindStaff(string id) =>
            _staff.FirstOrDefault(s => s.Id == id);

        private static string BuildStaffDisplay(ProposalStaffMember staff)
        {
            var name = staff.FullName?.Trim() ?? string.Empty;
            var credentials = staff.Credentials?.Trim() ?? string.Empty;
            return string.IsNullOrWhiteSpace(credentials) ? name : $"{name}, {credentials}";
        }

        private static string BuildPreparedByLine(ProposalStaffMember? staff)
        {
            if (staff is null)
                return "Prepared by:";

            return $"Prepared by: {BuildStaffDisplay(staff)}";
        }

        private static string BuildContactLine(ProposalStaffMember? staff)
        {
            if (staff is null)
                return string.Empty;

            if (string.IsNullOrWhiteSpace(staff.Email) && string.IsNullOrWhiteSpace(staff.Phone))
                return string.Empty;

            return $"{staff.Email}{JoinText(" | ", staff.Phone)}";
        }

        private static string BuildAttentionLine(string name, string title)
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(title))
                return string.Empty;

            return string.IsNullOrWhiteSpace(title) ? name : $"{name} ({title})";
        }

        private static string BuildCcLine(string name, string title)
        {
            if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(title))
                return string.Empty;

            return string.IsNullOrWhiteSpace(title) ? $"CC: {name}" : $"CC: {name} ({title})";
        }

        private static string BuildLineValue(params string?[] values)
        {
            return BuildMultilineValue(values);
        }

        private static string BuildMultilineValue(params string?[] values)
        {
            return string.Join(
                "\n",
                values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!.Trim()));
        }

        private static string DefaultIfEmpty(string? text, string fallback) =>
            string.IsNullOrWhiteSpace(text) ? fallback : text.Trim();

        private static string JoinText(string separator, string? value) =>
            string.IsNullOrWhiteSpace(value) ? string.Empty : $"{separator}{value.Trim()}";

        private static string FormatCurrency(decimal amount) =>
            string.Format(CultureInfo.InvariantCulture, "${0:N0}", amount);

        private static string FormatRate(decimal amount) =>
            string.Format(CultureInfo.InvariantCulture, "${0:N0}", amount);
    }
}
