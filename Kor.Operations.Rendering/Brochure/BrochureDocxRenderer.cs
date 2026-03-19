#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Kor.Operations.Core.Models.Brochure;

namespace Kor.Operations.Rendering.Brochure
{
    public sealed class BrochureDocxRenderer : IBrochureDocxRenderer
    {
        public Task<string> RenderAsync(BrochureContent content, string outputPath, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
            ct.ThrowIfCancellationRequested();

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using var document = WordprocessingDocument.Create(outputPath, WordprocessingDocumentType.Document);
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();
            mainPart.Document = new Document(body);
            var contact = content.ContactConfig ?? new BrochureContactConfig();

            AppendCover(body, content, ct);

            foreach (var block in content.Blocks)
            {
                ct.ThrowIfCancellationRequested();

                switch (block.BlockType)
                {
                    case BrochureBlockType.Section when block.Section is not null:
                        AppendSection(body, block.Section, ct);
                        break;

                    case BrochureBlockType.Personnel:
                        AppendPersonnel(body, block, ct);
                        break;

                    case BrochureBlockType.CompanyOverview:
                        AppendOverview(body, block, ct);
                        break;

                    case BrochureBlockType.Contact:
                        AppendContact(body, contact, ct);
                        break;
                }
            }

            mainPart.Document.Save();
            return Task.FromResult(outputPath);
        }

        private static void AppendCover(Body body, BrochureContent content, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            var coverTitle = string.IsNullOrWhiteSpace(content.CoverTitle)
                ? string.IsNullOrWhiteSpace(content.TemplateName) ? "KOR Structural" : content.TemplateName
                : content.CoverTitle;

            body.Append(
                CreateParagraph(coverTitle, 28, bold: true),
                CreateParagraph(DateTime.Now.Year.ToString(), 14),
                CreateSpacerParagraph());
        }

        private static void AppendSection(Body body, BrochureSection section, CancellationToken ct)
        {
            if (section.Projects.Count == 0)
                return;

            body.Append(CreateParagraph((section.Heading ?? string.Empty).ToUpperInvariant(), 14, bold: true));

            foreach (var project in section.Projects)
            {
                ct.ThrowIfCancellationRequested();

                body.Append(CreateParagraph(project.ProjectName, 11, bold: true));

                if (!string.IsNullOrWhiteSpace(project.ProjectDescription))
                    body.Append(CreateParagraph(project.ProjectDescription, 10));

                if (!string.IsNullOrWhiteSpace(project.Client))
                    body.Append(CreateParagraph($"Client: {project.Client}", 9, italic: true));

                if (!string.IsNullOrWhiteSpace(project.Architect))
                    body.Append(CreateParagraph($"Architect: {project.Architect}", 9, italic: true));

                body.Append(CreateSpacerParagraph());
            }
        }

        private static void AppendPersonnel(Body body, BrochureBlock block, CancellationToken ct)
        {
            if (block.People.Count == 0)
                return;

            body.Append(CreateParagraph((block.PersonnelHeading ?? string.Empty).ToUpperInvariant(), 14, bold: true));

            foreach (var person in block.People)
            {
                ct.ThrowIfCancellationRequested();

                body.Append(CreateParagraph(person.Name, 11, bold: true));

                if (!string.IsNullOrWhiteSpace(person.Credentials))
                    body.Append(CreateParagraph(person.Credentials, 10, italic: true));

                if (!string.IsNullOrWhiteSpace(person.Bio))
                    body.Append(CreateParagraph(person.Bio, 9));

                body.Append(CreateSpacerParagraph());
            }
        }

        private static void AppendOverview(Body body, BrochureBlock block, CancellationToken ct)
        {
            foreach (var section in block.OverviewSections)
            {
                ct.ThrowIfCancellationRequested();

                body.Append(CreateParagraph((section.Heading ?? string.Empty).ToUpperInvariant(), 12, bold: true));

                if (!string.IsNullOrWhiteSpace(section.Body))
                    body.Append(CreateParagraph(section.Body, 10));

                body.Append(CreateSpacerParagraph());
            }
        }

        private static void AppendContact(Body body, BrochureContactConfig contact, CancellationToken ct)
        {
            body.Append(CreateParagraph("CONTACT", 14, bold: true));
            body.Append(CreateParagraph(contact.OfficeAddress, 9));

            foreach (var office in contact.Offices)
            {
                ct.ThrowIfCancellationRequested();

                body.Append(CreateParagraph(office.Region, 11, bold: true));
                body.Append(CreateParagraph(office.Contact, 9));
                body.Append(CreateParagraph(office.Phone, 9));
                body.Append(CreateParagraph(office.Email, 9));
                body.Append(CreateParagraph(office.Hours, 9));
                body.Append(CreateSpacerParagraph());
            }
        }

        private static Paragraph CreateParagraph(string? text, int fontSize, bool bold = false, bool italic = false)
        {
            var runProperties = new RunProperties(new FontSize { Val = (fontSize * 2).ToString() });

            if (bold)
                runProperties.Append(new Bold());

            if (italic)
                runProperties.Append(new Italic());

            var paragraph = new Paragraph(
                new ParagraphProperties(
                    new SpacingBetweenLines
                    {
                        After = "0",
                        Line = "240",
                        LineRule = LineSpacingRuleValues.Auto
                    }),
                new Run(runProperties, new Text(text ?? string.Empty) { Space = SpaceProcessingModeValues.Preserve }));

            return paragraph;
        }

        private static Paragraph CreateSpacerParagraph() => CreateParagraph(string.Empty, 1);
    }
}
