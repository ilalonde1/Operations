#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Renders a BdReportDocument to DOCX bytes via DocumentFormat.OpenXml —
/// native Word Heading 1/2/3 styles (navigation pane works), no Word COM, so
/// generation runs on any machine including KOR-APP01 cron. Pure function:
/// model in, bytes out. Every block type the HTML preview renders is mirrored
/// here (one model, two renderers): KPI strips become bordered one-row
/// tables, chips become shaded runs, heading accent rules become paragraph
/// bottom borders.
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

    /// <summary>
    /// Renders a single full-page PNG (a WebView2 screenshot of one of the live
    /// BD visuals — heat-graph / treemap / attack cards) into a landscape DOCX
    /// with a title heading. The visuals have no block model; this is their Word
    /// export path, paired with WebView2's PrintToPdfAsync for PDF.
    /// </summary>
    public static byte[] RenderImage(byte[] pngBytes, string title, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(pngBytes);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth), "Image dimensions must be positive.");
        }

        using var ms = new MemoryStream();
        using (var word = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = word.AddMainDocumentPart();
            main.Document = new Document();

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = BuildStyles();
            stylesPart.Styles.Save();

            var imagePart = main.AddImagePart(ImagePartType.Png);
            using (var pngStream = new MemoryStream(pngBytes))
            {
                imagePart.FeedData(pngStream);
            }

            var body = new Body();
            body.Append(StyledParagraph(HeadingStyleId(1), Run(title)));

            // Landscape Letter content box (11in × 8.5in less 0.5in margins):
            // 10in wide, ~7in tall. Fit the screenshot inside, preserving aspect.
            const long emuPerInch = 914400L;
            const long maxWidthEmu = 10L * emuPerInch;
            const long maxHeightEmu = 7L * emuPerInch;
            long widthEmu = maxWidthEmu;
            long heightEmu = (long)(widthEmu * ((double)pixelHeight / pixelWidth));
            if (heightEmu > maxHeightEmu)
            {
                heightEmu = maxHeightEmu;
                widthEmu = (long)(heightEmu * ((double)pixelWidth / pixelHeight));
            }

            body.Append(new Paragraph(new Run(BuildInlineImage(
                main.GetIdOfPart(imagePart), widthEmu, heightEmu))));

            body.Append(new SectionProperties(
                new PageSize { Width = 15840, Height = 12240, Orient = PageOrientationValues.Landscape },
                new PageMargin
                {
                    Top = 720, Bottom = 720, Left = 720, Right = 720,
                }));

            main.Document.Append(body);
            main.Document.Save();
        }

        return ms.ToArray();
    }

    private static Drawing BuildInlineImage(string relationshipId, long widthEmu, long heightEmu)
    {
        var picture = new PIC.Picture(
            new PIC.NonVisualPictureProperties(
                new PIC.NonVisualDrawingProperties { Id = 0U, Name = "BD Visual.png" },
                new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(
                new A.Blip { Embed = relationshipId },
                new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(
                new A.Transform2D(
                    new A.Offset { X = 0L, Y = 0L },
                    new A.Extents { Cx = widthEmu, Cy = heightEmu }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle }));

        var graphic = new A.Graphic(
            new A.GraphicData(picture) { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" });

        var inline = new DW.Inline(
            new DW.Extent { Cx = widthEmu, Cy = heightEmu },
            new DW.EffectExtent { LeftEdge = 0L, TopEdge = 0L, RightEdge = 0L, BottomEdge = 0L },
            new DW.DocProperties { Id = 1U, Name = "BD Visual" },
            new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
            graphic)
        {
            DistanceFromTop = 0U,
            DistanceFromBottom = 0U,
            DistanceFromLeft = 0U,
            DistanceFromRight = 0U,
        };

        return new Drawing(inline);
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

            case KpiStripBlock k:
                yield return RenderKpiStrip(k);
                // Word needs an empty paragraph after a table so following
                // content does not merge into it (same rule as TableBlock).
                yield return StyledParagraph(null, Run(string.Empty));
                break;

            case ChipRowBlock cr:
                yield return RenderChipRow(cr);
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

    private static Run Run(
        string text,
        bool bold = false,
        bool italic = false,
        int? sizeHalfPoints = null,
        string? colorHex = null,
        bool caps = false,
        string? shadeFillHex = null)
    {
        // rPr child order is schema-fixed: b, i, caps, color, sz, shd.
        var props = new RunProperties();
        if (bold)
        {
            props.Append(new Bold());
        }

        if (italic)
        {
            props.Append(new Italic());
        }

        if (caps)
        {
            props.Append(new Caps());
        }

        if (colorHex is not null)
        {
            props.Append(new Color { Val = colorHex });
        }

        if (sizeHalfPoints is { } size)
        {
            props.Append(new FontSize { Val = size.ToString() });
        }

        if (shadeFillHex is not null)
        {
            props.Append(new Shading { Val = ShadingPatternValues.Clear, Fill = shadeFillHex });
        }

        var run = new Run();
        if (props.HasChildren)
        {
            run.Append(props);
        }

        // DB prose (drain-written JSON) can legally carry XML-illegal control
        // characters that make the OpenXml XmlWriter throw, and embedded
        // newlines that one Text node would silently flatten. Strip the
        // former, render the latter as explicit line breaks (review M4).
        var lines = (text ?? string.Empty).Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (i > 0)
            {
                run.Append(new Break());
            }

            run.Append(new Text(Sanitize(lines[i])) { Space = SpaceProcessingModeValues.Preserve });
        }

        return run;
    }

    private static string Sanitize(string value)
    {
        if (value.All(c => !char.IsControl(c) || c == '\t'))
        {
            return value;
        }

        return new string(value.Where(c => !char.IsControl(c) || c == '\t').ToArray());
    }

    /// <summary>
    /// KPI callout cards: a one-row auto-width borderless table; each cell is
    /// a card with a 3pt tone-colored top rule, a 20pt bold value, and a 7.5pt
    /// uppercase muted label — the DOCX twin of the preview's div.kpis flexbox.
    /// </summary>
    private static Table RenderKpiStrip(KpiStripBlock block)
    {
        // tblPr child order: tblW, tblCellSpacing, tblLayout.
        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
            new TableCellSpacing { Type = TableWidthUnitValues.Dxa, Width = "40" },
            new TableLayout { Type = TableLayoutValues.Autofit }));

        var grid = new TableGrid();
        for (var i = 0; i < block.Items.Count; i++)
        {
            grid.Append(new GridColumn());
        }

        table.Append(grid);

        var row = new TableRow();
        foreach (var item in block.Items)
        {
            var accent = item.Tone is { } tone ? KorReportStyles.ChipColor(tone) : KorReportStyles.AccentColor;
            var valueColor = item.Tone is { } t ? KorReportStyles.ChipColor(t) : KorReportStyles.BrandColor;

            // tcPr child order: tcBorders, tcMar.
            var cellProps = new TableCellProperties(
                new TableCellBorders(
                    new TopBorder { Val = BorderValues.Single, Size = KorReportStyles.KpiAccentRuleSize, Color = accent },
                    new LeftBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor },
                    new BottomBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor },
                    new RightBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor }),
                new TableCellMargin(
                    new TopMargin { Type = TableWidthUnitValues.Dxa, Width = "80" },
                    new LeftMargin { Type = TableWidthUnitValues.Dxa, Width = "160" },
                    new BottomMargin { Type = TableWidthUnitValues.Dxa, Width = "60" },
                    new RightMargin { Type = TableWidthUnitValues.Dxa, Width = "160" }));

            var valueParagraph = new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
                Run(item.Value, bold: true, sizeHalfPoints: KorReportStyles.KpiValueSizeHalfPoints, colorHex: valueColor));
            var labelParagraph = new Paragraph(
                new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
                Run(item.Label, sizeHalfPoints: KorReportStyles.KpiLabelSizeHalfPoints, colorHex: KorReportStyles.MutedTextColor, caps: true));

            row.Append(new TableCell(cellProps, valueParagraph, labelParagraph));
        }

        table.Append(row);
        return table;
    }

    /// <summary>Chips as shaded bold-white runs — the DOCX twin of span.chip pills.</summary>
    private static Paragraph RenderChipRow(ChipRowBlock block)
    {
        var runs = new List<Run>();
        foreach (var chip in block.Chips)
        {
            if (runs.Count > 0)
            {
                runs.Add(Run("  ", sizeHalfPoints: KorReportStyles.ChipSizeHalfPoints));
            }

            // Word run shading has no padding; NBSP pads the pill text.
            runs.Add(Run(
                " " + chip.Text + " ",
                bold: true,
                sizeHalfPoints: KorReportStyles.ChipSizeHalfPoints,
                colorHex: "FFFFFF",
                shadeFillHex: KorReportStyles.ChipColor(chip.Tone)));
        }

        return StyledParagraph(null, runs.ToArray());
    }

    private static Table RenderTable(TableBlock block)
    {
        // Child order is schema-fixed: tblPr, tblGrid, then rows; borders are
        // top/left/bottom/right/insideH/insideV in exactly that order.
        var table = new Table(new TableProperties(
            new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }, // 100%
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor },
                new LeftBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor },
                new BottomBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor },
                new RightBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor },
                new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Color = KorReportStyles.CardBorderColor })));

        var grid = new TableGrid();
        for (var i = 0; i < block.Headers.Count; i++)
        {
            grid.Append(new GridColumn());
        }

        table.Append(grid);
        table.Append(TableRowOf(block.Headers, block.Alignments, headerRow: true, zebra: false));
        for (var r = 0; r < block.Rows.Count; r++)
        {
            // Pad/trim to header width so a short row cannot shift columns.
            var row = block.Rows[r];
            var cells = Enumerable.Range(0, block.Headers.Count)
                .Select(i => i < row.Count ? row[i] : string.Empty)
                .ToList();
            // Zebra on the same rows as the preview's nth-child(even).
            table.Append(TableRowOf(cells, block.Alignments, headerRow: false, zebra: r % 2 == 1));
        }

        return table;
    }

    private static TableRow TableRowOf(
        IReadOnlyList<string> cells,
        IReadOnlyList<ColumnAlignment>? alignments,
        bool headerRow,
        bool zebra)
    {
        var row = new TableRow();
        for (var i = 0; i < cells.Count; i++)
        {
            // pPr child order: spacing, jc.
            var paragraphProps = new ParagraphProperties(new SpacingBetweenLines { After = "0" });
            if (JustificationFor(alignments, i) is { } justification)
            {
                paragraphProps.Append(new Justification { Val = justification });
            }

            var paragraph = new Paragraph(
                paragraphProps,
                Run(cells[i],
                    bold: headerRow,
                    sizeHalfPoints: KorReportStyles.TableSizeHalfPoints,
                    colorHex: headerRow ? "FFFFFF" : null));

            var cell = new TableCell();
            if (headerRow)
            {
                cell.Append(new TableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Fill = KorReportStyles.BrandColor }));
            }
            else if (zebra)
            {
                cell.Append(new TableCellProperties(
                    new Shading { Val = ShadingPatternValues.Clear, Fill = KorReportStyles.ZebraFillColor }));
            }

            cell.Append(paragraph);
            row.Append(cell);
        }

        return row;
    }

    private static JustificationValues? JustificationFor(IReadOnlyList<ColumnAlignment>? alignments, int column)
    {
        if (alignments is null || column >= alignments.Count)
        {
            return null;
        }

        return alignments[column] switch
        {
            ColumnAlignment.Right => JustificationValues.Right,
            ColumnAlignment.Center => JustificationValues.Center,
            _ => null,
        };
    }

    private static Styles BuildStyles()
    {
        return new Styles(
            NormalStyle(),
            HeadingStyle("Heading1", "heading 1", KorReportStyles.Heading1SizeHalfPoints, KorReportStyles.Heading1SpaceBefore, KorReportStyles.Heading1SpaceAfter, outlineLevel: 0, accentRuleSize: KorReportStyles.Heading1RuleSize),
            HeadingStyle("Heading2", "heading 2", KorReportStyles.Heading2SizeHalfPoints, KorReportStyles.Heading2SpaceBefore, KorReportStyles.Heading2SpaceAfter, outlineLevel: 1, accentRuleSize: KorReportStyles.Heading2RuleSize),
            HeadingStyle("Heading3", "heading 3", KorReportStyles.Heading3SizeHalfPoints, KorReportStyles.Heading3SpaceBefore, KorReportStyles.Heading3SpaceAfter, outlineLevel: 2, accentRuleSize: null));
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

    private static Style HeadingStyle(string id, string name, int sizeHalfPoints, int spaceBefore, int spaceAfter, int outlineLevel, int? accentRuleSize)
    {
        // pPr child order: keepNext, pBdr, spacing, outlineLvl.
        var paragraphProps = new StyleParagraphProperties();
        paragraphProps.Append(new KeepNext());
        if (accentRuleSize is { } ruleSize)
        {
            paragraphProps.Append(new ParagraphBorders(
                new BottomBorder
                {
                    Val = BorderValues.Single,
                    Size = (uint)ruleSize,
                    Space = 2,
                    Color = KorReportStyles.AccentColor,
                }));
        }

        paragraphProps.Append(new SpacingBetweenLines { Before = spaceBefore.ToString(), After = spaceAfter.ToString() });
        paragraphProps.Append(new OutlineLevel { Val = outlineLevel });

        return new Style(
            new StyleName { Val = name },
            new BasedOn { Val = "Normal" },
            new NextParagraphStyle { Val = "Normal" },
            new PrimaryStyle(),
            paragraphProps,
            new StyleRunProperties(
                new RunFonts { Ascii = KorReportStyles.FontFamily, HighAnsi = KorReportStyles.FontFamily },
                new Bold(),
                new Color { Val = KorReportStyles.BrandColor },
                new FontSize { Val = sizeHalfPoints.ToString() }))
        {
            Type = StyleValues.Paragraph,
            StyleId = id,
        };
    }
}
