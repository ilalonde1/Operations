#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Renders a BdReportDocument to DOCX bytes via DocumentFormat.OpenXml —
/// native Word Heading 1/2/3 styles (navigation pane works), no Word COM, so
/// generation runs on any machine including KOR-APP01 cron. Pure function:
/// model in, bytes out.
/// </summary>
public static class DocxBuilder
{
    public static byte[] Render(BdReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        using var ms = new MemoryStream();
        using (var word = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new Document();

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = BuildStyles();
            stylesPart.Styles.Save();

            var body = new Body();
            foreach (var block in document.Blocks)
            {
                foreach (var element in RenderBlock(block))
                {
                    body.Append(element);
                }
            }

            body.Append(new SectionProperties(new PageMargin
            {
                Top = KorReportStyles.PageMarginTopBottom,
                Bottom = KorReportStyles.PageMarginTopBottom,
                Left = KorReportStyles.PageMarginLeftRight,
                Right = KorReportStyles.PageMarginLeftRight,
            }));

            main.Document.Append(body);
            main.Document.Save();
        }

        return ms.ToArray();
    }

    private static IEnumerable<OpenXmlElement> RenderBlock(BdReportBlock block)
    {
        switch (block)
        {
            case HeadingBlock h:
                yield return StyledParagraph(HeadingStyleId(h.Level), Run(h.Text));
                break;

            case ParagraphBlock p:
                yield return StyledParagraph(null, Run(p.Text));
                break;

            case LabelValueBlock lv:
                yield return StyledParagraph(null, Run(lv.Label, bold: true), Run(lv.Value));
                break;

            case ItalicNoteBlock n:
                yield return StyledParagraph(null, Run(n.Text, italic: true, sizeHalfPoints: KorReportStyles.NoteSizeHalfPoints));
                break;

            case TableBlock t:
                yield return RenderTable(t);
                // Word needs an empty paragraph after a table so following
                // content does not merge into it (the PS builders TypeParagraph
                // after EndKey for the same reason).
                yield return StyledParagraph(null, Run(string.Empty));
                break;

            default:
                throw new NotSupportedException($"Unknown report block type {block.GetType().Name}.");
        }
    }

    private static string HeadingStyleId(int level) => level switch
    {
        1 => "Heading1",
        2 => "Heading2",
        3 => "Heading3",
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Heading level must be 1-3."),
    };

    private static Paragraph StyledParagraph(string? styleId, params Run[] runs)
    {
        var p = new Paragraph();
        if (styleId is not null)
        {
            p.Append(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        }

        p.Append(runs.Cast<OpenXmlElement>().ToArray());
        return p;
    }

    private static Run Run(string text, bool bold = false, bool italic = false, int? sizeHalfPoints = null)
    {
        var props = new RunProperties();
        if (bold)
        {
            props.Append(new Bold());
        }

        if (italic)
        {
            props.Append(new Italic());
        }

        if (sizeHalfPoints is { } size)
        {
            props.Append(new FontSize { Val = size.ToString() });
        }

        var run = new Run();
        if (props.HasChildren)
        {
            run.Append(props);
        }

        run.Append(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Table RenderTable(TableBlock block)
    {
        // Child order is schema-fixed: tblPr, tblGrid, then rows; borders are
        // top/left/bottom/right/insideH/insideV in exactly that order.
        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }, // 100%
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.TableBorderColor },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.TableBorderColor },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.TableBorderColor },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.TableBorderColor },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.TableBorderColor },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.TableBorderColor })));

        var grid = new TableGrid();
        for (var i = 0; i < block.Headers.Count; i++)
        {
            grid.Append(new GridColumn());
        }

        table.Append(grid);
        table.Append(TableRowOf(block.Headers, bold: true));
        foreach (var row in block.Rows)
        {
            // Pad/trim to header width so a short row cannot shift columns.
            var cells = Enumerable.Range(0, block.Headers.Count)
                .Select(i => i < row.Count ? row[i] : string.Empty)
                .ToList();
            table.Append(TableRowOf(cells, bold: false));
        }

        return table;
    }

    private static TableRow TableRowOf(IReadOnlyList<string> cells, bool bold)
    {
        var row = new TableRow();
        foreach (var cell in cells)
        {
            var paragraph = new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
                Run(cell, bold: bold, sizeHalfPoints: KorReportStyles.TableSizeHalfPoints));
            row.Append(new TableCell(paragraph));
        }

        return row;
    }

    private static Styles BuildStyles()
    {
        return new Styles(
            NormalStyle(),
            HeadingStyle("Heading1", "heading 1", KorReportStyles.Heading1SizeHalfPoints, KorReportStyles.Heading1SpaceBefore, KorReportStyles.Heading1SpaceAfter, outlineLevel: 0),
            HeadingStyle("Heading2", "heading 2", KorReportStyles.Heading2SizeHalfPoints, KorReportStyles.Heading2SpaceBefore, KorReportStyles.Heading2SpaceAfter, outlineLevel: 1),
            HeadingStyle("Heading3", "heading 3", KorReportStyles.Heading3SizeHalfPoints, KorReportStyles.Heading3SpaceBefore, KorReportStyles.Heading3SpaceAfter, outlineLevel: 2));
    }

    private static Style NormalStyle()
    {
        return new Style(
            new StyleName { Val = "Normal" },
            new PrimaryStyle(),
            new StyleParagraphProperties(new SpacingBetweenLines
            {
                After = KorReportStyles.NormalSpaceAfter.ToString(),
                Line = "240",
                LineRule = LineSpacingRuleValues.Auto,
            }),
            new StyleRunProperties(
                new RunFonts { Ascii = KorReportStyles.FontFamily, HighAnsi = KorReportStyles.FontFamily },
                new FontSize { Val = KorReportStyles.NormalSizeHalfPoints.ToString() }))
        {
            Type = StyleValues.Paragraph,
            StyleId = "Normal",
            Default = true,
        };
    }

    private static Style HeadingStyle(string id, string name, int sizeHalfPoints, int spaceBefore, int spaceAfter, int outlineLevel)
    {
        return new Style(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new PrimaryStyle(),
            new StyleParagraphProperties(
                new KeepNext(),
                new SpacingBetweenLines { Before = spaceBefore.ToString(), After = spaceAfter.ToString() },
                new OutlineLevel { Val = outlineLevel }),
            new StyleRunProperties(
                new RunFonts { Ascii = KorReportStyles.FontFamily, HighAnsi = KorReportStyles.FontFamily },
                new Bold(),
                new FontSize { Val = sizeHalfPoints.ToString() }))
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
        };
    }
}
