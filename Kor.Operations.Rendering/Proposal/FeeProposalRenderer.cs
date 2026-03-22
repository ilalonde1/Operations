#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Kor.Operations.Core.Models.Proposal;
using Microsoft.Extensions.Logging;
using QuestPDF.Drawing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static Kor.Operations.Rendering.Proposal.ProposalRenderHelpers;

namespace Kor.Operations.Rendering.Proposal
{
    public sealed class FeeProposalRenderer : IFeeProposalRenderer
    {
        private const string BrandSlate = "#3D5166";
        private const string BrandOrange = "#E8722A";
        private const string LightRow = "#F8FAFC";
        private const string DarkText = "#1F2937";
        private const string MutedText = "#475569";
        private const string BorderColor = "#D9E2EC";
        private const string KorAddress = "# 501 - 510 Burrard Street, Vancouver, B.C. V6C 3A8";
        private const string MissingStaffPlaceholder = "[Staff not found]";

        private static readonly object _fontLock = new();
        private static bool _fontsRegistered;

        private readonly ILogger<FeeProposalRenderer> _logger;
        private IReadOnlyList<ProposalStaffMember> _staff = Array.Empty<ProposalStaffMember>();
        private Dictionary<string, ProposalStaffMember> _staffIndex = new();

        static FeeProposalRenderer()
        {
            QuestPDF.Settings.License = LicenseType.Community;
            lock (_fontLock)
            {
                if (_fontsRegistered)
                    return;

                FontManager.RegisterFontFromEmbeddedResource("Kor.Operations.Rendering.Fonts.Mulish.Mulish-Regular.ttf");
                FontManager.RegisterFontFromEmbeddedResource("Kor.Operations.Rendering.Fonts.Mulish.Mulish-Bold.ttf");
                FontManager.RegisterFontFromEmbeddedResource("Kor.Operations.Rendering.Fonts.Mulish.Mulish-Italic.ttf");
                FontManager.RegisterFontFromEmbeddedResource("Kor.Operations.Rendering.Fonts.Mulish.Mulish-BoldItalic.ttf");
                FontManager.RegisterFontFromEmbeddedResource("Kor.Operations.Rendering.Fonts.Mulish.Mulish-Black.ttf");
                _fontsRegistered = true;
            }
        }

        public FeeProposalRenderer(ILogger<FeeProposalRenderer> logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<string> RenderAsync(
            FeeProposal proposal,
            IReadOnlyList<ProposalStaffMember> staff,
            string outputPath,
            CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(proposal);
            ArgumentNullException.ThrowIfNull(staff);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();

                var outputDirectory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrWhiteSpace(outputDirectory))
                    Directory.CreateDirectory(outputDirectory);

                _staff = staff;
                _staffIndex = BuildStaffIndex(staff);

                try
                {
                    _logger.LogInformation(
                        "Starting fee proposal render to {OutputPath} with {BlockCount} block(s).",
                        outputPath,
                        proposal.Blocks.Count);

                    var document = Document.Create(container => ComposeDocument(container, proposal, ct));
                    document.GeneratePdf(outputPath);

                    _logger.LogInformation(
                        "Completed fee proposal render to {OutputPath}.",
                        outputPath);

                    return outputPath;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "Fee proposal render failed to {OutputPath}.",
                        outputPath);
                    throw;
                }
                finally
                {
                    _staff = Array.Empty<ProposalStaffMember>();
                    _staffIndex = new Dictionary<string, ProposalStaffMember>();
                }
            }, ct);
        }

        private void ComposeDocument(IDocumentContainer container, FeeProposal proposal, CancellationToken ct)
        {
            var blocks = proposal.Blocks.Count > 0
                ? proposal.Blocks
                : new List<FeeProposalBlock>
                {
                    new()
                    {
                        BlockType = ProposalBlockType.FreeText,
                        TemplateName = "Empty Proposal",
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

                if (block.BlockType == ProposalBlockType.PageBreak)
                    continue;

                if (block.BlockType == ProposalBlockType.Cover)
                {
                    container.Page(page =>
                    {
                        ConfigureCoverPage(page);
                        page.Content().Element(content => RenderCover(content, block.Cover ?? new CoverBlockContent()));
                    });

                    continue;
                }

                var sectionName = GetSectionName(block);

                container.Page(page =>
                {
                    ConfigureStandardPage(page);
                    page.Header().Element(header => RenderPageHeader(header, sectionName));
                    page.Content().PaddingHorizontal(54).PaddingTop(24).Element(content => RenderBlock(content, block));
                });
            }
        }

        private static void ConfigureCoverPage(PageDescriptor page)
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish").FontSize(10.5f).FontColor(DarkText));
        }

        private static void ConfigureStandardPage(PageDescriptor page)
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(0);
            page.MarginTop(0);
            page.MarginBottom(54);
            page.PageColor(Colors.White);
            page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish").FontSize(10.5f).FontColor(DarkText));
        }

        private void RenderPageHeader(IContainer container, string sectionName)
        {
            container
                .Background(BrandSlate)
                .PaddingHorizontal(54)
                .PaddingVertical(10)
                .Row(row =>
                {
                    row.RelativeItem()
                        .Text(sectionName.ToUpperInvariant())
                        .FontSize(9)
                        .Bold()
                        .FontColor(Colors.White);

                    row.ConstantItem(70)
                        .AlignRight()
                        .Text(text =>
                        {
                            text.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish").FontSize(9).FontColor(Colors.White));
                            text.CurrentPageNumber();
                        });
                });
        }

        private void RenderBlock(IContainer container, FeeProposalBlock block)
        {
            container.Column(column =>
            {
                switch (block.BlockType)
                {
                    case ProposalBlockType.Introduction:
                        RenderIntroduction(column, block.Introduction ?? new IntroductionBlockContent());
                        break;
                    case ProposalBlockType.Company:
                        RenderCompany(column, block.Company ?? new CompanyBlockContent());
                        break;
                    case ProposalBlockType.Personnel:
                        RenderPersonnel(column, block.Personnel ?? new PersonnelBlockContent());
                        break;
                    case ProposalBlockType.References:
                        RenderReferences(column, block.References ?? new ReferencesBlockContent());
                        break;
                    case ProposalBlockType.ProjectDescription:
                        RenderProjectDescription(column, block.ProjectDescription ?? new ProjectDescriptionBlockContent());
                        break;
                    case ProposalBlockType.FeeTable:
                        RenderFeeTable(column, block.FeeTable ?? new FeeTableBlockContent());
                        break;
                    case ProposalBlockType.Scope:
                        RenderScope(column, block.Scope ?? new ScopeBlockContent());
                        break;
                    case ProposalBlockType.ExcludedServices:
                        RenderExcludedServices(column, block.ExcludedServices ?? new ExcludedServicesBlockContent());
                        break;
                    case ProposalBlockType.ApprovalToProceed:
                        RenderApprovalToProceed(column, block.ApprovalToProceed ?? new ApprovalToProceedBlockContent());
                        break;
                    case ProposalBlockType.SignaturePage:
                        RenderSignaturePage(column, block.SignaturePage ?? new SignaturePageBlockContent());
                        break;
                    case ProposalBlockType.RatesTable:
                        RenderRatesTable(column, block.RatesTable ?? new RatesTableBlockContent());
                        break;
                    case ProposalBlockType.FreeText:
                        RenderFreeText(column, block.FreeText ?? new FreeTextBlockContent());
                        break;
                }
            });
        }

        private void RenderCover(IContainer container, CoverBlockContent content)
        {
            var preparer = GetStaffOrPlaceholder(content.PreparerStaffId);

            container.Column(column =>
            {
                column.Item()
                    .Background(BrandSlate)
                    .PaddingHorizontal(40)
                    .PaddingVertical(16)
                    .MaxHeight(56)
                    .Image(LoadEmbeddedImage("Kor.Operations.Rendering.Images.prop_logo.png"))
                    .FitHeight();

                column.Item()
                    .Padding(54)
                    .Column(body =>
                    {
                        body.Spacing(24);

                        body.Item().Text("FEE PROPOSAL FOR STRUCTURAL ENGINEERING SERVICES")
                            .FontSize(11)
                            .Bold()
                            .FontColor(DarkText);

                        body.Item().Element(section =>
                            ComposeLabeledSection(section, "PROJECT", lines =>
                            {
                                AddLine(lines, content.ProjectName, bold: true);
                                AddLine(lines, content.ProjectAddress);
                            }));

                        body.Item().Element(section =>
                            ComposeLabeledSection(section, "SUBMITTED TO", lines =>
                            {
                                AddLine(lines, content.ClientCompany, bold: true);
                                AddLine(lines, content.ClientAddress);
                                AddLine(lines, BuildAttentionLine(content.AttentionName, content.AttentionTitle, content.AttentionEmail));
                                AddLine(lines, BuildCcLine(content.CcName, content.CcTitle, content.CcEmail));
                            }));

                        body.Item().Element(section =>
                            ComposeLabeledSection(section, "SUBMITTED BY", lines =>
                            {
                                AddLine(lines, "Kor Structural", bold: true);
                                AddLine(lines, KorAddress);
                                AddLine(lines, BuildPreparedByLine(preparer));
                            }));

                        body.Item().Element(section =>
                            ComposeLabeledSection(section, "PROPOSAL DATE", lines =>
                            {
                                AddLine(lines, content.ProposalDate, bold: true);
                            }));

                        if (!string.IsNullOrWhiteSpace(content.Jurisdiction))
                        {
                            body.Item().Element(section =>
                                ComposeLabeledSection(section, "JURISDICTION", lines =>
                                {
                                    AddLine(lines, content.Jurisdiction);
                                }));
                        }
                    });
            });
        }

        private void RenderIntroduction(ColumnDescriptor column, IntroductionBlockContent content)
        {
            var signatory = GetStaffOrPlaceholder(content.SignatoryStaffId);
            ComposeParagraph(column, $"Dear {DefaultIfEmpty(content.SalutationName, "Client")},");
            ComposeParagraph(
                column,
                $"Please find our fee proposal for structural engineering services for {DefaultIfEmpty(content.ProjectAddress, "the project")} based on {DefaultIfEmpty(content.DrawingReference, "the referenced drawing package")}.");
            ComposeParagraph(column, content.CloserText);

            column.Item().PaddingTop(16).Column(signature =>
            {
                signature.Spacing(3);
                signature.Item().Text("Best Regards,").FontColor(MutedText);
                signature.Item().Text("Kor Structural").Bold();

                signature.Item().Text(BuildStaffDisplay(signatory)).Bold();
                signature.Item().Text(signatory.Title);

                var contact = string.Join(
                    " | ",
                    new[] { signatory.Email, signatory.Phone }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()));
                if (!string.IsNullOrWhiteSpace(contact))
                    signature.Item().Text(contact);
            });
        }

        private void RenderCompany(ColumnDescriptor column, CompanyBlockContent content)
        {
            foreach (var section in content.Sections.Where(s => !string.IsNullOrWhiteSpace(s.Title) || !string.IsNullOrWhiteSpace(s.Body)))
            {
                column.Item().PaddingTop(12).Element(item => ComposeOrangeHeading(item, section.Title));
                ComposeParagraph(column, section.Body);
            }
        }

        private void RenderPersonnel(ColumnDescriptor column, PersonnelBlockContent content)
        {
            column.Item().Element(item => ComposeOrangeHeading(item, "Project Personnel and Experience"));

            ComposeStaffBio(column, content.LeadStaffId, content.LeadBioOverride);
            ComposeStaffBio(column, content.SupportingStaffId, content.SupportingBioOverride);

            if (!string.IsNullOrWhiteSpace(content.CollaborationNote))
                ComposeParagraph(column, content.CollaborationNote);

            var additionalStaff = content.AdditionalStaffIds
                .Select(GetStaffOrPlaceholder)
                .ToList();

            if (additionalStaff.Count == 0)
                return;

            ComposeParagraph(column, "Additional staff dedicated to this project will include:");

            foreach (var member in additionalStaff)
            {
                ComposeBullet(
                    column,
                    JoinText(BuildStaffDisplay(member), member.Title));
            }
        }

        private void RenderReferences(ColumnDescriptor column, ReferencesBlockContent content)
        {
            column.Item().Element(item => ComposeOrangeHeading(item, content.Heading));
            ComposeParagraph(column, content.Preamble);

            var clientNames = content.ClientNames.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (clientNames.Count > 0)
            {
                column.Item().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(definition =>
                    {
                        definition.RelativeColumn();
                        definition.RelativeColumn();
                        definition.RelativeColumn();
                    });

                    for (var i = 0; i < clientNames.Count; i++)
                    {
                        table.Cell()
                            .PaddingVertical(6)
                            .PaddingRight(12)
                            .Text(clientNames[i])
                            .FontColor(DarkText);
                    }
                });
            }

            if (!string.IsNullOrWhiteSpace(content.ProjectSpecificNote))
            {
                column.Item()
                    .PaddingTop(10)
                    .Text(content.ProjectSpecificNote)
                    .Italic()
                    .FontColor(MutedText);
            }
        }

        private void RenderProjectDescription(ColumnDescriptor column, ProjectDescriptionBlockContent content)
        {
            column.Item().Element(item => ComposeOrangeHeading(item, content.Heading));
            ComposeParagraph(column, content.Preamble);

            foreach (var bullet in content.AssumptionBullets.Where(x => !string.IsNullOrWhiteSpace(x)))
                ComposeBullet(column, bullet);
        }

        private void RenderFeeTable(ColumnDescriptor column, FeeTableBlockContent content)
        {
            column.Item().Element(item => ComposeOrangeHeading(item, content.Heading));
            ComposeParagraph(column, content.Preamble);

            var phases = content.Phases.Where(p => !string.IsNullOrWhiteSpace(p.PhaseName)).ToList();
            var total = phases.Sum(p => p.Fee);

            column.Item().PaddingTop(14).Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    definition.RelativeColumn(3);
                    definition.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    header.Cell().Element(ComposeTableHeaderCell).Text("Phase");
                    header.Cell().Element(ComposeTableHeaderCell).AlignRight().Text("Fee");
                });

                for (var i = 0; i < phases.Count; i++)
                {
                    var background = i % 2 == 0 ? "#FFFFFF" : LightRow;
                    table.Cell().Element(c => ComposeTableBodyCell(c, background)).Text(phases[i].PhaseName);
                    table.Cell().Element(c => ComposeTableBodyCell(c, background)).AlignRight().Text(FormatCurrency(phases[i].Fee));
                }

                table.Cell().Element(c => ComposeTableSummaryCell(c)).Text("Total Base Fees:");
                table.Cell().Element(c => ComposeTableSummaryCell(c)).AlignRight().Text(FormatCurrency(total));
            });

            if (content.FieldReviewVisits > 0)
            {
                ComposeParagraph(
                    column,
                    $"Including up to {content.FieldReviewVisits.ToString(CultureInfo.InvariantCulture)} field review visits");
            }

            ComposeParagraph(
                column,
                $"A disbursements allowance of {FormatCurrency(content.DisbursementsAllowance)} has been included.");

            foreach (var note in content.AdditionalNotes.Where(x => !string.IsNullOrWhiteSpace(x)))
                ComposeParagraph(column, note);
        }

        private void RenderScope(ColumnDescriptor column, ScopeBlockContent content)
        {
            column.Item().Element(item => ComposeOrangeHeading(item, content.Heading));

            if (!string.IsNullOrWhiteSpace(content.Narrative))
                ComposeParagraph(column, content.Narrative);

            var activeItems = content.IncludedServices.Where(item => item.IsActive && !string.IsNullOrWhiteSpace(item.Text)).ToList();
            for (var i = 0; i < activeItems.Count; i++)
                ComposeNumberedItem(column, i + 1, activeItems[i].Text);
        }

        private void RenderExcludedServices(ColumnDescriptor column, ExcludedServicesBlockContent content)
        {
            column.Item().Element(item => ComposeOrangeHeading(item, content.Heading));
            ComposeParagraph(column, content.Preamble);

            var items = content.ExcludedItems.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            for (var i = 0; i < items.Count; i++)
                ComposeNumberedItem(column, i + 1, items[i]);
        }

        private void RenderApprovalToProceed(ColumnDescriptor column, ApprovalToProceedBlockContent content)
        {
            column.Item().Element(item => ComposeOrangeHeading(item, content.Heading));
            ComposeParagraph(column, content.IntroParagraph);
            ComposeParagraph(column, content.InvoicingParagraph);
        }

        private void RenderSignaturePage(ColumnDescriptor column, SignaturePageBlockContent content)
        {
            ComposeParagraph(column, content.ClosingParagraph);

            column.Item().PaddingTop(16).Column(signature =>
            {
                signature.Spacing(3);
                signature.Item().Text("Yours truly,");
                signature.Item().Text("Kor Structural").Bold();
                signature.Item().Text("EGBC Permit #1000378");
            });

            var signatories = content.SignatoryStaffIds
                .Select(GetStaffOrPlaceholder)
                .Take(2)
                .ToList();

            if (signatories.Count > 0)
            {
                column.Item().PaddingTop(28).Table(table =>
                {
                    table.ColumnsDefinition(definition =>
                    {
                        definition.RelativeColumn();
                        definition.RelativeColumn();
                    });

                    foreach (var signatory in signatories)
                    {
                        table.Cell().PaddingRight(16).Column(cell =>
                        {
                            cell.Item().PaddingBottom(8).LineHorizontal(1).LineColor(BorderColor);
                            cell.Item().Text(BuildStaffDisplay(signatory)).Bold();
                            cell.Item().Text(signatory.Title).FontColor(MutedText);
                        });
                    }
                });
            }

            ComposeParagraph(
                column,
                "Fee Proposal accepted, and Professional Liability Insurance Advice acknowledged.");

            column.Item().PaddingTop(22).Column(client =>
            {
                client.Spacing(10);
                client.Item().Text(DefaultIfEmpty(content.ClientCompanyName, "Client Company")).Bold();
                client.Item().Text("Per:");
                client.Item().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().PaddingBottom(8).LineHorizontal(1).LineColor(BorderColor);
                        col.Item().Text("Authorized Signatory").FontSize(9).FontColor(MutedText);
                    });
                    row.ConstantItem(24);
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().PaddingBottom(8).LineHorizontal(1).LineColor(BorderColor);
                        col.Item().Text("Date").FontSize(9).FontColor(MutedText);
                    });
                });
            });

            var attachments = content.IncludeRatesAppendix
                ? "Attachments: Appendix A - Schedule of Standard Hourly Billing Rates."
                : "Attachments: None.";
            ComposeParagraph(column, attachments);
        }

        private void RenderRatesTable(ColumnDescriptor column, RatesTableBlockContent content)
        {
            column.Item().Element(item => ComposeOrangeHeading(item, "Schedule of Standard Hourly Billing Rates"));

            if (!string.IsNullOrWhiteSpace(content.EffectiveDate))
                ComposeParagraph(column, $"Effective date: {content.EffectiveDate}");

            var rates = content.Rates.Where(r => !string.IsNullOrWhiteSpace(r.Role)).ToList();

            column.Item().PaddingTop(14).Table(table =>
            {
                table.ColumnsDefinition(definition =>
                {
                    definition.RelativeColumn(3);
                    definition.RelativeColumn(1);
                });

                table.Header(header =>
                {
                    header.Cell().Element(ComposeTableHeaderCell).Text("Role");
                    header.Cell().Element(ComposeTableHeaderCell).AlignRight().Text("Rate / Hour");
                });

                for (var i = 0; i < rates.Count; i++)
                {
                    var background = i % 2 == 0 ? "#FFFFFF" : LightRow;
                    table.Cell().Element(c => ComposeTableBodyCell(c, background)).Text(rates[i].Role);
                    table.Cell().Element(c => ComposeTableBodyCell(c, background)).AlignRight().Text(FormatRate(rates[i].RatePerHour));
                }
            });
        }

        private void RenderFreeText(ColumnDescriptor column, FreeTextBlockContent content)
        {
            if (!string.IsNullOrWhiteSpace(content.Heading))
                column.Item().Element(item => ComposeOrangeHeading(item, content.Heading));

            ComposeParagraph(column, content.Body);
        }

        private void ComposeStaffBio(ColumnDescriptor column, string staffId, string bioOverride)
        {
            var hasStaffId = !string.IsNullOrWhiteSpace(staffId);
            var staff = hasStaffId ? GetStaffOrPlaceholder(staffId) : null;
            if (staff is null && string.IsNullOrWhiteSpace(bioOverride))
                return;

            var bio = !string.IsNullOrWhiteSpace(bioOverride) ? bioOverride : staff?.Bio;
            var heading = staff is not null ? JoinText(BuildStaffDisplay(staff), staff.Title) : string.Empty;

            if (!string.IsNullOrWhiteSpace(heading))
            {
                column.Item().PaddingTop(16).Text(heading)
                    .Bold()
                    .FontColor(DarkText);
            }

            if (!string.IsNullOrWhiteSpace(bio))
                ComposeParagraph(column, bio);
        }

        private void ComposeSectionTitle(ColumnDescriptor column, string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return;

            column.Item()
                .PaddingBottom(10)
                .Text(title.ToUpperInvariant())
                .FontSize(9)
                .Bold()
                .LetterSpacing(0.06f)
                .FontColor(MutedText);
        }

        private void ComposeOrangeHeading(IContainer container, string heading)
        {
            if (string.IsNullOrWhiteSpace(heading))
                return;

            container.Column(column =>
            {
                column.Item().Text(heading)
                    .FontSize(14)
                    .Bold()
                    .FontColor(BrandOrange);
                column.Item().PaddingTop(3).Width(120).LineHorizontal(2).LineColor(BrandOrange);
            });
        }

        private void ComposeParagraph(ColumnDescriptor column, string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            column.Item()
                .PaddingTop(10)
                .Text(text.Trim())
                .LineHeight(1.5f)
                .FontColor(DarkText);
        }

        private void ComposeBullet(ColumnDescriptor column, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            column.Item().PaddingTop(8).PaddingLeft(8).Row(row =>
            {
                row.ConstantItem(18).Text("•").FontColor(BrandOrange).Bold();
                row.RelativeItem().Text(text).LineHeight(1.45f);
            });
        }

        private void ComposeNumberedItem(ColumnDescriptor column, int number, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            column.Item().PaddingTop(8).PaddingLeft(8).Row(row =>
            {
                row.ConstantItem(26).Text($"{number.ToString(CultureInfo.InvariantCulture)}.").Bold().FontColor(BrandOrange);
                row.RelativeItem().Text(text).LineHeight(1.45f);
            });
        }

        private void ComposeLabeledSection(IContainer container, string label, Action<List<(string Text, bool Bold)>> buildLines)
        {
            var lines = new List<(string Text, bool Bold)>();
            buildLines(lines);

            container.Column(column =>
            {
                column.Spacing(5);
                column.Item().Text(label).Bold().FontSize(10).FontColor(DarkText);

                foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l.Text)))
                {
                    var item = column.Item().Text(line.Text);
                    if (line.Bold)
                        item.Bold();
                }
            });
        }

        private static IContainer ComposeTableHeaderCell(IContainer container) =>
            container
                .Background(BrandSlate)
                .PaddingHorizontal(14)
                .PaddingVertical(10)
                .Border(0.5f)
                .BorderColor(BrandSlate)
                .DefaultTextStyle(TextStyle.Default.FontFamily("Mulish").FontColor(Colors.White).Bold());

        private static IContainer ComposeTableBodyCell(IContainer container, string background) =>
            container
                .Background(background)
                .PaddingHorizontal(14)
                .PaddingVertical(10)
                .Border(0.5f)
                .BorderColor(BorderColor);

        private static IContainer ComposeTableSummaryCell(IContainer container) =>
            container
                .Background("#EEF2F6")
                .PaddingHorizontal(14)
                .PaddingVertical(10)
                .Border(0.5f)
                .BorderColor(BorderColor)
                .DefaultTextStyle(TextStyle.Default.FontFamily("Mulish").Bold().FontColor(DarkText));

        private string GetSectionName(FeeProposalBlock block) => block.BlockType switch
        {
            ProposalBlockType.Introduction => "Introduction",
            ProposalBlockType.Company => "Our Company",
            ProposalBlockType.Personnel => "Our Company",
            ProposalBlockType.References => "Our Company",
            ProposalBlockType.ProjectDescription => "Our Proposal",
            ProposalBlockType.FeeTable => "Our Proposal",
            ProposalBlockType.Scope => "Our Proposal",
            ProposalBlockType.ExcludedServices => "Our Proposal",
            ProposalBlockType.ApprovalToProceed => "Our Proposal",
            ProposalBlockType.SignaturePage => "Our Proposal",
            ProposalBlockType.RatesTable => "Appendix A",
            ProposalBlockType.FreeText => "Our Proposal",
            _ => block.TemplateName
        };

        private ProposalStaffMember? FindStaff(string id) =>
            _staffIndex.TryGetValue(id, out var s) ? s : null;

        private ProposalStaffMember GetStaffOrPlaceholder(string id) =>
            FindStaff(id) ?? CreateMissingStaffPlaceholder();

        private static ProposalStaffMember CreateMissingStaffPlaceholder() =>
            new()
            {
                FullName = MissingStaffPlaceholder,
                Credentials = MissingStaffPlaceholder,
                Title = MissingStaffPlaceholder,
                Email = MissingStaffPlaceholder,
                Phone = MissingStaffPlaceholder,
                Bio = MissingStaffPlaceholder,
            };

        private static void AddLine(List<(string Text, bool Bold)> lines, string? text, bool bold = false)
        {
            if (!string.IsNullOrWhiteSpace(text))
                lines.Add((text.Trim(), bold));
        }

        private static byte[] LoadEmbeddedImage(string resourceName)
        {
            var asm = typeof(FeeProposalRenderer).Assembly;
            using var stream = asm.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            return ms.ToArray();
        }
    }
}
