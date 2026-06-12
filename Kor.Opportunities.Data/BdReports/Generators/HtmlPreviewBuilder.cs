#nullable enable
using System;
using System.Net;
using System.Text;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Renders the SAME BdReportDocument the DOCX renderer reads, as a
/// self-contained HTML page for the WebView2 preview pane. Typography and
/// palette mirror KorReportStyles so the preview matches the DOCX one-to-one
/// (acceptance criterion 3 of BD-UI-Plan-2026-06-08); every block type here
/// has a DocxBuilder counterpart. All text is HTML-encoded.
/// </summary>
public static class HtmlPreviewBuilder
{
    public static string Render(BdReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine("<html><head><meta charset=\"utf-8\"/>");
        sb.Append("<title>").Append(WebUtility.HtmlEncode(document.Title)).AppendLine("</title>");
        sb.AppendLine("<style>");
        sb.AppendLine($"  body {{ font-family: {KorReportStyles.FontFamily}, 'Segoe UI', sans-serif; font-size: 10pt; margin: 24px 36px; color: #1a1a1a; }}");
        sb.AppendLine($"  h1 {{ font-size: 16pt; margin: 0 0 8pt 0; color: #{KorReportStyles.BrandColor}; border-bottom: 1.5pt solid #{KorReportStyles.AccentColor}; padding-bottom: 4pt; }}");
        sb.AppendLine($"  h2 {{ font-size: 12pt; margin: 16pt 0 4pt 0; color: #{KorReportStyles.BrandColor}; border-bottom: 0.75pt solid #{KorReportStyles.AccentColor}; padding-bottom: 2pt; }}");
        sb.AppendLine($"  h3 {{ font-size: 10.5pt; margin: 8pt 0 2pt 0; color: #{KorReportStyles.BrandColor}; }}");
        sb.AppendLine("  p  { margin: 0 0 4pt 0; }");
        sb.AppendLine("  p.note { font-size: 9pt; font-style: italic; }");
        sb.AppendLine("  table { border-collapse: collapse; width: 100%; font-size: 8.5pt; margin: 2pt 0 6pt 0; }");
        sb.AppendLine($"  th {{ background: #{KorReportStyles.BrandColor}; color: #fff; font-weight: 600; border: 1px solid #{KorReportStyles.BrandColor}; padding: 3px 6px; text-align: left; vertical-align: top; }}");
        sb.AppendLine($"  td {{ border: 1px solid #{KorReportStyles.CardBorderColor}; padding: 2px 6px; text-align: left; vertical-align: top; }}");
        sb.AppendLine($"  tbody tr:nth-child(even) td {{ background: #{KorReportStyles.ZebraFillColor}; }}");
        sb.AppendLine("  th.r, td.r { text-align: right; }");
        sb.AppendLine("  th.c, td.c { text-align: center; }");
        sb.AppendLine("  div.kpis { display: flex; flex-wrap: wrap; gap: 10px; margin: 8pt 0 10pt 0; }");
        sb.AppendLine($"  div.kpi {{ border: 1px solid #{KorReportStyles.CardBorderColor}; border-top: 3pt solid #{KorReportStyles.AccentColor}; border-radius: 6px; padding: 8px 16px 7px 16px; min-width: 88px; }}");
        sb.AppendLine($"  div.kpi .v {{ font-size: 20pt; font-weight: 700; line-height: 1.1; color: #{KorReportStyles.BrandColor}; }}");
        sb.AppendLine($"  div.kpi .l {{ font-size: 7.5pt; text-transform: uppercase; letter-spacing: 0.06em; color: #{KorReportStyles.MutedTextColor}; margin-top: 2px; }}");
        sb.AppendLine("  p.chips { margin: 2pt 0 6pt 0; }");
        sb.AppendLine("  span.chip { display: inline-block; border-radius: 999px; color: #fff; font-size: 8pt; font-weight: 600; padding: 2px 10px; margin: 0 6px 3px 0; }");
        sb.AppendLine($"  a {{ color: #{KorReportStyles.ChipInfoColor}; text-decoration: none; }}");
        sb.AppendLine("  a:hover { text-decoration: underline; }");
        sb.AppendLine("  h3 a { color: inherit; border-bottom: 1px dotted currentColor; }");
        sb.AppendLine("</style></head><body>");

        foreach (var block in document.Blocks)
        {
            AppendBlock(sb, block);
        }

        sb.AppendLine("</body></html>");
        return sb.ToString();
    }

    private static void AppendBlock(StringBuilder sb, BdReportBlock block)
    {
        switch (block)
        {
            case HeadingBlock h when h.Level is >= 1 and <= 3:
                sb.Append("<h").Append(h.Level).Append('>')
                  .Append(Linked(WebUtility.HtmlEncode(h.Text), h.Link))
                  .Append("</h").Append(h.Level).AppendLine(">");
                break;

            case HeadingBlock h:
                throw new ArgumentOutOfRangeException(nameof(block), h.Level, "Heading level must be 1-3.");

            case ParagraphBlock p:
                // Empty paragraphs are deliberate spacers in the PS builders.
                sb.Append("<p>").Append(string.IsNullOrEmpty(p.Text) ? "&nbsp;" : Encode(p.Text)).AppendLine("</p>");
                break;

            case LabelValueBlock lv:
                sb.Append("<p><b>").Append(Encode(lv.Label)).Append("</b>")
                  .Append(Linked(Encode(lv.Value), lv.Link)).AppendLine("</p>");
                break;

            case ItalicNoteBlock n:
                sb.Append("<p class=\"note\">").Append(Encode(n.Text)).AppendLine("</p>");
                break;

            case KpiStripBlock k:
                sb.AppendLine("<div class=\"kpis\">");
                foreach (var item in k.Items)
                {
                    sb.Append("<div class=\"kpi\"");
                    if (item.Tone is { } tone)
                    {
                        var color = KorReportStyles.ChipColor(tone);
                        sb.Append(" style=\"border-top-color:#").Append(color).Append("\"><div class=\"v\" style=\"color:#").Append(color).Append("\">");
                    }
                    else
                    {
                        sb.Append("><div class=\"v\">");
                    }

                    sb.Append(Encode(item.Value)).Append("</div><div class=\"l\">")
                      .Append(Encode(item.Label)).AppendLine("</div></div>");
                }

                sb.AppendLine("</div>");
                break;

            case ChipRowBlock cr:
                sb.Append("<p class=\"chips\">");
                foreach (var chip in cr.Chips)
                {
                    sb.Append("<span class=\"chip\" style=\"background:#")
                      .Append(KorReportStyles.ChipColor(chip.Tone)).Append("\">")
                      .Append(Encode(chip.Text)).Append("</span>");
                }

                sb.AppendLine("</p>");
                break;

            case TableBlock t:
                sb.AppendLine("<table><thead><tr>");
                for (var i = 0; i < t.Headers.Count; i++)
                {
                    sb.Append("<th").Append(AlignClass(t.Alignments, i)).Append('>')
                      .Append(Encode(t.Headers[i])).AppendLine("</th>");
                }

                sb.AppendLine("</tr></thead><tbody>");
                for (var r = 0; r < t.Rows.Count; r++)
                {
                    var row = t.Rows[r];
                    var rowLinks = t.CellLinks is { } links && r < links.Count ? links[r] : null;
                    sb.Append("<tr>");
                    for (var i = 0; i < t.Headers.Count; i++)
                    {
                        var cell = i < row.Count ? row[i] : string.Empty;
                        var link = rowLinks is not null && i < rowLinks.Count ? rowLinks[i] : null;
                        sb.Append("<td").Append(AlignClass(t.Alignments, i)).Append('>')
                          .Append(Linked(Encode(cell), link)).Append("</td>");
                    }

                    sb.AppendLine("</tr>");
                }

                sb.AppendLine("</tbody></table>");
                break;

            default:
                throw new NotSupportedException($"Unknown report block type {block.GetType().Name}.");
        }
    }

    private static string AlignClass(System.Collections.Generic.IReadOnlyList<ColumnAlignment>? alignments, int column)
    {
        if (alignments is null || column >= alignments.Count)
        {
            return string.Empty;
        }

        return alignments[column] switch
        {
            ColumnAlignment.Right => " class=\"r\"",
            ColumnAlignment.Center => " class=\"c\"",
            _ => string.Empty,
        };
    }

    // Encode-then-break: newlines in DB prose render as explicit line breaks,
    // matching the DOCX renderer's Break() handling (one model, two renderers).
    private static string Encode(string text)
        => WebUtility.HtmlEncode(text).Replace("\r\n", "<br/>").Replace("\n", "<br/>");

    // Wraps already-encoded inner HTML in a kor:// drill-down anchor. Only
    // KorReportLinks-parseable targets become anchors — a malformed link
    // degrades to plain text instead of emitting a dead/garbage href, and
    // empty cells never render an invisible anchor.
    private static string Linked(string encodedInner, string? link)
    {
        if (link is null || encodedInner.Length == 0 || !KorReportLinks.TryParse(link, out _, out _))
        {
            return encodedInner;
        }

        return "<a href=\"" + WebUtility.HtmlEncode(link) + "\">" + encodedInner + "</a>";
    }
}
