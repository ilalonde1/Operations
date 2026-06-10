#nullable enable
using System;
using System.Net;
using System.Text;

namespace Kor.Opportunities.Data.BdReports.Generators;

/// <summary>
/// Renders the SAME BdReportDocument the DOCX renderer reads, as a
/// self-contained HTML page for the WebView2 preview pane. Typography mirrors
/// KorReportStyles so the preview matches the DOCX one-to-one (acceptance
/// criterion 3 of BD-UI-Plan-2026-06-08). All text is HTML-encoded.
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
        sb.AppendLine("  h1 { font-size: 16pt; margin: 0 0 6pt 0; }");
        sb.AppendLine("  h2 { font-size: 12pt; margin: 10pt 0 3pt 0; }");
        sb.AppendLine("  h3 { font-size: 10.5pt; margin: 6pt 0 2pt 0; }");
        sb.AppendLine("  p  { margin: 0 0 4pt 0; }");
        sb.AppendLine("  p.note { font-size: 9pt; font-style: italic; }");
        sb.AppendLine($"  table {{ border-collapse: collapse; width: 100%; font-size: 9pt; margin: 0 0 4pt 0; }}");
        sb.AppendLine($"  th, td {{ border: 1px solid #{KorReportStyles.TableBorderColor}; padding: 2px 5px; text-align: left; vertical-align: top; }}");
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
                  .Append(WebUtility.HtmlEncode(h.Text))
                  .Append("</h").Append(h.Level).AppendLine(">");
                break;

            case HeadingBlock h:
                throw new ArgumentOutOfRangeException(nameof(block), h.Level, "Heading level must be 1-3.");

            case ParagraphBlock p:
                // Empty paragraphs are deliberate spacers in the PS builders.
                sb.Append("<p>").Append(string.IsNullOrEmpty(p.Text) ? "&nbsp;" : WebUtility.HtmlEncode(p.Text)).AppendLine("</p>");
                break;

            case LabelValueBlock lv:
                sb.Append("<p><b>").Append(WebUtility.HtmlEncode(lv.Label)).Append("</b>")
                  .Append(WebUtility.HtmlEncode(lv.Value)).AppendLine("</p>");
                break;

            case ItalicNoteBlock n:
                sb.Append("<p class=\"note\">").Append(WebUtility.HtmlEncode(n.Text)).AppendLine("</p>");
                break;

            case TableBlock t:
                sb.AppendLine("<table><thead><tr>");
                foreach (var header in t.Headers)
                {
                    sb.Append("<th>").Append(WebUtility.HtmlEncode(header)).AppendLine("</th>");
                }

                sb.AppendLine("</tr></thead><tbody>");
                foreach (var row in t.Rows)
                {
                    sb.Append("<tr>");
                    for (var i = 0; i < t.Headers.Count; i++)
                    {
                        var cell = i < row.Count ? row[i] : string.Empty;
                        sb.Append("<td>").Append(WebUtility.HtmlEncode(cell)).Append("</td>");
                    }

                    sb.AppendLine("</tr>");
                }

                sb.AppendLine("</tbody></table>");
                break;

            default:
                throw new NotSupportedException($"Unknown report block type {block.GetType().Name}.");
        }
    }
}
