#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Kor.Opportunities.Data.MajorProjects;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Kor.Operations.App.BusinessDevelopment.Workspace;

public sealed record PursuitBriefPdfSnapshot(
    PursuitBrief Brief,
    string KorEdgeDisplay,
    string OwnerContactsDisplay,
    string OwnerBdRecordDisplay,
    string ArchitectWarmthDisplay,
    string ThePlayDisplay,
    string FitScoreDisplay);

public static class PursuitBriefPdfExporter
{
    // Round 49: brand-matched to KorTheme.xaml (Brand.Primary.Brush = #3F5364).
    // The old "#1E3A8A" navy was generic Tailwind and looked like a stock template.
    private const string Brand = "#3F5364";          // KOR slate primary
    private const string BrandEyebrow = "#C4CCD3";   // light slate tint for labels on the band
    private const string BrandFactLabel = "#9FA9B3"; // muted slate for header-fact small labels
    private const string Purple = "#7C3AED";
    private const string Text = "#111827";
    private const string Muted = "#6B7280";
    private const string Border = "#E5E7EB";
    private const string Pale = "#F8FAFC";

    public static void Export(PursuitBriefPdfSnapshot snapshot, string path)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A PDF output path is required.", nameof(path));
        }

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        Document.Create(container => Compose(container, snapshot)).GeneratePdf(path);
    }

    private static void Compose(IDocumentContainer container, PursuitBriefPdfSnapshot snapshot)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.Letter);
            page.Margin(34);
            page.DefaultTextStyle(TextStyle.Default.FontFamily("Mulish").FontSize(9.5f).FontColor(Text));
            page.Content().Column(column =>
            {
                column.Spacing(10);
                column.Item().Element(body => RenderHeaderBand(body, snapshot.Brief));
                column.Item().Element(body => RenderProcurement(body, snapshot.Brief.OwnerProcurement));
                column.Item().Element(body => RenderPrimeSeat(body, snapshot.Brief.Architect, snapshot.ArchitectWarmthDisplay));
                column.Item().Element(body => RenderWhoToCall(body, snapshot.Brief, snapshot.OwnerContactsDisplay));
                column.Item().Element(body => RenderCompetition(body, snapshot.Brief.CompetitorNotes, snapshot.OwnerBdRecordDisplay));
                column.Item().Element(body => RenderTextSection(body, "KOR's edge", snapshot.KorEdgeDisplay));
                column.Item().Element(body => RenderPlayFit(body, snapshot.ThePlayDisplay, snapshot.FitScoreDisplay));
            });
            page.Footer().Element(RenderFooter);
        });
    }

    private static void RenderHeaderBand(IContainer container, PursuitBrief brief)
    {
        // Round 49: trimmed padding + spacing + font size so the band stops
        // dominating the page (see ss1 from 2026-05-30).
        container
            .Background(Brand)
            .Padding(10)
            .Column(column =>
            {
                column.Spacing(4);
                column.Item().Text(Placeholder(brief.Project.ProjectName))
                    .FontSize(16)
                    .Bold()
                    .FontColor(Colors.White);
                column.Item().PaddingTop(3).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    RenderHeaderFact(table, "Owner", brief.Project.Owner);
                    RenderHeaderFact(table, "Market", brief.Project.Market);
                    RenderHeaderFact(table, "City", brief.Project.City);
                    RenderHeaderFact(table, "Sector", brief.Project.Sector);
                    RenderHeaderFact(table, "Stage", brief.Project.Stage);
                    RenderHeaderFact(table, "Est. cost", brief.Project.EstCostDisplay);
                    RenderHeaderFact(table, "Source URL", brief.Project.SourceUrl);
                });
            });
    }

    private static void RenderHeaderFact(TableDescriptor table, string label, string? value)
    {
        // Round 49: tighter cell padding + brand-tint label.
        table.Cell().PaddingRight(8).PaddingBottom(4).Column(column =>
        {
            column.Item().Text(label).FontSize(7).FontColor(BrandFactLabel).SemiBold();
            column.Item().Text(Placeholder(value)).FontSize(9).FontColor(Colors.White);
        });
    }

    private static void RenderProcurement(IContainer container, PursuitBriefOwnerProcurement procurement)
    {
        var hasData = HasText(procurement.ProcurementMethod)
            || HasText(procurement.RosterProgram)
            || HasText(procurement.EvaluationCriteria)
            || HasText(procurement.BudgetCadence);

        RenderSection(container, "How it's bought", body =>
        {
            if (!hasData)
            {
                body.Item().Text("No procurement profile on file for this owner.").Italic().FontColor(Muted);
                return;
            }

            body.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.RelativeColumn();
                    columns.RelativeColumn();
                });
                RenderFact(table, "Procurement method", procurement.ProcurementMethod);
                RenderFact(table, "Roster program", procurement.RosterProgram);
                RenderFact(table, "Evaluation criteria", procurement.EvaluationCriteria);
                RenderFact(table, "Budget cadence", procurement.BudgetCadence);
            });
        });
    }

    private static void RenderPrimeSeat(IContainer container, PursuitBriefArchitect architect, string architectWarmth)
    {
        RenderSection(container, "Likely prime & structural seat", body =>
        {
            body.Item().Row(row =>
            {
                row.RelativeItem().Text(Placeholder(architect.ArchitectName)).FontSize(12).SemiBold();
                row.ConstantItem(120).AlignRight().Text(Placeholder(architect.SeatStatus)).FontColor(SeatStatusColor(architect.SeatStatus)).Bold();
            });
            body.Item().PaddingTop(4).Text(Placeholder(architect.SeatPriority)).FontColor(Muted);
            body.Item().PaddingTop(6).Text(Placeholder(architect.DisplacementRead));
            body.Item().PaddingTop(8).Text("Architect warmth").FontSize(8).FontColor(Muted).SemiBold();
            body.Item().Text(Placeholder(architectWarmth)).Italic();
            body.Item().PaddingTop(8).Text("Recurring structural partners").FontSize(8).FontColor(Muted).SemiBold();
            RenderStringList(body, architect.RecurringStructuralPartners);
        });
    }

    private static void RenderWhoToCall(IContainer container, PursuitBrief brief, string ownerContactsDisplay)
    {
        RenderSection(container, "Who to call", body =>
        {
            body.Item().Text("Architect contacts").SemiBold();
            RenderPeople(body, brief.ArchitectContacts);
            body.Item().PaddingTop(8).Text("Owner contacts").SemiBold();
            RenderPeople(body, brief.OwnerContacts);
            body.Item().PaddingTop(8).Text("KOR knows at the owner").SemiBold();
            body.Item().Text(Placeholder(ownerContactsDisplay)).Italic();
        });
    }

    private static void RenderCompetition(IContainer container, IReadOnlyList<PursuitBriefCompetitorNote> notes, string ownerBdRecordDisplay)
    {
        RenderSection(container, "Competition", body =>
        {
            body.Item().Text("Owner BD record").SemiBold();
            body.Item().Text(Placeholder(ownerBdRecordDisplay)).Italic();
            body.Item().PaddingTop(8).Text("Competitor notes").SemiBold();
            if (notes.Count == 0)
            {
                body.Item().Text("No competitor notes on file.").Italic().FontColor(Muted);
                return;
            }

            foreach (var note in notes)
            {
                body.Item().Text(text =>
                {
                    text.Span("- " + Placeholder(note.Firm)).SemiBold();
                    text.Span("  ");
                    text.Span(Placeholder(note.CapacityRead));
                });
            }
        });
    }

    private static void RenderTextSection(IContainer container, string title, string value)
    {
        RenderSection(container, title, body =>
        {
            body.Item().Text(Placeholder(value)).Italic();
        });
    }

    private static void RenderPlayFit(IContainer container, string thePlayDisplay, string fitScoreDisplay)
    {
        RenderSection(container, "The play / fit", body =>
        {
            body.Item().Text(Placeholder(thePlayDisplay)).Italic();
            body.Item().PaddingTop(8).Text(text =>
            {
                text.Span("Fit score: ").SemiBold();
                text.Span(Placeholder(fitScoreDisplay));
            });
        });
    }

    private static void RenderSection(IContainer container, string title, Action<ColumnDescriptor> renderBody)
    {
        container
            .Border(1)
            .BorderColor(Border)
            .Background(Pale)
            .Padding(12)
            .Column(column =>
            {
                column.Spacing(5);
                column.Item().Text(title).FontSize(13).Bold().FontColor(Brand);
                column.Item().LineHorizontal(0.5f).LineColor(Border);
                renderBody(column);
            });
    }

    private static void RenderFact(TableDescriptor table, string label, string? value)
    {
        table.Cell().PaddingRight(12).PaddingBottom(8).Column(column =>
        {
            column.Item().Text(label).FontSize(8).FontColor(Muted).SemiBold();
            column.Item().Text(Placeholder(value));
        });
    }

    private static void RenderStringList(ColumnDescriptor body, IReadOnlyList<string> values)
    {
        var clean = values.Where(HasText).ToArray();
        if (clean.Length == 0)
        {
            body.Item().Text("No recurring structural partners on file.").Italic().FontColor(Muted);
            return;
        }

        foreach (var value in clean)
        {
            body.Item().Text("- " + value.Trim());
        }
    }

    private static void RenderPeople(ColumnDescriptor body, IReadOnlyList<PursuitBriefPerson> people)
    {
        if (people.Count == 0)
        {
            body.Item().Text("No contacts on file.").Italic().FontColor(Muted);
            return;
        }

        foreach (var person in people)
        {
            var parts = new[] { person.Title, person.Role, person.Email }
                .Where(HasText)
                .Select(value => value!.Trim());
            body.Item().Text(text =>
            {
                text.Span("- " + Placeholder(person.Name)).SemiBold();
                var details = string.Join("  ", parts);
                if (!string.IsNullOrWhiteSpace(details))
                {
                    text.Span("  " + details);
                }
            });
        }
    }

    private static void RenderFooter(IContainer container)
    {
        var generated = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        container.PaddingTop(8).BorderTop(0.5f).BorderColor(Border).PaddingTop(6).Row(row =>
        {
            row.RelativeItem().Text($"Generated {generated}").FontSize(8).FontColor(Muted);
            row.ConstantItem(90).AlignRight().Text(text =>
            {
                text.DefaultTextStyle(TextStyle.Default.FontSize(8).FontColor(Muted));
                text.Span("Page ");
                text.CurrentPageNumber();
                text.Span(" of ");
                text.TotalPages();
            });
        });
    }

    private static string SeatStatusColor(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized is "open" or "rotating" ? "#047857"
            : normalized is "locked" or "preferred" ? "#B45309"
            : Purple;
    }

    private static bool HasText(string? value)
        => !string.IsNullOrWhiteSpace(value);

    private static string Placeholder(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Not on file." : value.Trim();
}
