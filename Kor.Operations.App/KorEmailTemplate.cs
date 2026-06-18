#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;

namespace Kor.Operations
{
    /// <summary>
    /// Shared, brand-matched HTML email shell for the Transmittals senders
    /// (QuickTransferRunner, InboundUploadRunner). Centralised so the two
    /// notification emails cannot drift apart.
    ///
    /// IMPORTANT — sanitiser contract: GraphFacade.SendMailInnerAsync wraps the
    /// returned fragment in &lt;html&gt;&lt;body&gt;&lt;div&gt;…&lt;/div&gt; and runs it through
    /// Ganss HtmlSanitizer (default config + the "data" scheme). That sanitiser
    /// STRIPS &lt;style&gt; blocks / &lt;head&gt; CSS, so this template uses table layout
    /// with 100% inline styles only — which is also the correct cross-client
    /// email practice. Do NOT emit &lt;html&gt;/&lt;head&gt;/&lt;body&gt; here (Graph adds them)
    /// and do NOT move styling into a &lt;style&gt; block (it will be deleted).
    ///
    /// Palette is KorReportStyles / PursuitBriefPdfExporter parity (KorTheme.xaml):
    /// slate #3F5364 primary, accent #FF5B35, text #111827, muted #6B7280.
    /// </summary>
    internal static class KorEmailTemplate
    {
        private const string Slate = "#3F5364";
        private const string SlateTint = "#C4CCD3";
        private const string Accent = "#FF5B35";
        private const string Text = "#111827";
        private const string Muted = "#6B7280";
        private const string Border = "#E5E7EB";
        private const string Pale = "#F8FAFC";
        private const string FontStack = "'Segoe UI',Arial,Helvetica,sans-serif";

        internal static string E(string? s) => WebUtility.HtmlEncode(s ?? string.Empty);

        /// <summary>
        /// Wraps caller-supplied inner body HTML in the branded KOR shell
        /// (header band + white card + footer). <paramref name="eyebrow"/> is the
        /// small uppercase label on the band (e.g. "FILE TRANSFER").
        /// <paramref name="pixelUrl"/>, when present, appends a 1x1 open-tracking
        /// pixel after the card.
        /// </summary>
        internal static string Shell(string eyebrow, string innerBodyHtml, string? pixelUrl = null)
        {
            var sb = new StringBuilder();

            // Left-aligned 600px block (no centring wrapper, no card border) so it
            // flows naturally above the user's left-aligned signature.
            // IMPORTANT for Outlook: cell fills use the bgcolor ATTRIBUTE, not just
            // CSS background-color — the Word rendering engine ignores the CSS.
            sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
              .Append("style=\"width:600px;max-width:600px;border-collapse:collapse;font-family:").Append(FontStack).Append(";\">");

            // Header band — text wordmark (no image: avoids Gmail blocking data:/hosted logos).
            sb.Append("<tr><td bgcolor=\"").Append(Slate).Append("\" style=\"background-color:").Append(Slate)
              .Append(";padding:16px 22px;\">")
              .Append("<div style=\"color:").Append(SlateTint)
              .Append(";font-size:11px;letter-spacing:1.5px;font-weight:600;text-transform:uppercase;\">")
              .Append(E(eyebrow)).Append("</div>")
              .Append("<div style=\"color:#ffffff;font-size:20px;font-weight:700;letter-spacing:.5px;padding-top:3px;\">")
              .Append("KOR <span style=\"font-weight:400;color:").Append(SlateTint).Append(";\">Structural</span></div>")
              .Append("</td></tr>");

            // Thin accent rule under the band.
            sb.Append("<tr><td bgcolor=\"").Append(Accent).Append("\" style=\"background-color:").Append(Accent)
              .Append(";height:3px;line-height:3px;font-size:0;\">&nbsp;</td></tr>");

            // Body (left-aligned).
            sb.Append("<tr><td align=\"left\" style=\"padding:20px 22px;color:").Append(Text)
              .Append(";font-size:14px;line-height:1.6;text-align:left;font-family:").Append(FontStack).Append(";\">")
              .Append(innerBodyHtml)
              .Append("</td></tr>");

            // Footer — single subtle line; the user's signature carries contact detail.
            sb.Append("<tr><td style=\"border-top:1px solid ").Append(Border)
              .Append(";padding:12px 22px;color:").Append(Muted)
              .Append(";font-size:11px;line-height:1.5;font-family:").Append(FontStack).Append(";\">")
              .Append("This message was sent automatically via KOR Operations.")
              .Append("</td></tr>");

            sb.Append("</table>");

            if (!string.IsNullOrWhiteSpace(pixelUrl))
            {
                sb.Append("<img src=\"").Append(E(pixelUrl))
                  .Append("\" width=\"1\" height=\"1\" alt=\"\" style=\"display:none;width:1px;height:1px;max-height:1px;max-width:1px;overflow:hidden;\" />");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Accent call-to-action button (table cell + inline-block anchor),
        /// replacing the old bare "Click here" link that read as spam. The fill
        /// uses the bgcolor attribute and the label color is set on a nested span
        /// so Outlook renders white text instead of its default link blue.
        /// </summary>
        internal static string Button(string url, string text)
        {
            return new StringBuilder()
                .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:6px 0;border-collapse:separate;\"><tr>")
                .Append("<td bgcolor=\"").Append(Accent)
                .Append("\" style=\"background-color:").Append(Accent).Append(";border-radius:4px;\">")
                .Append("<a href=\"").Append(E(url))
                .Append("\" style=\"display:inline-block;padding:11px 26px;font-size:14px;font-weight:600;")
                .Append("text-decoration:none;color:#ffffff;font-family:").Append(FontStack).Append(";\">")
                .Append("<span style=\"color:#ffffff;text-decoration:none;\">").Append(E(text)).Append(" &rarr;</span>")
                .Append("</a></td></tr></table>")
                .ToString();
        }

        /// <summary>
        /// To/Cc recipient block so each per-recipient send still shows the full
        /// distribution (restores the dropped recipient list for the transmittal
        /// flow, which tracks To and Cc separately).
        /// </summary>
        internal static string RecipientBlock(IEnumerable<string>? to, IEnumerable<string>? cc)
        {
            static string Join(IEnumerable<string>? xs) => string.Join("; ",
                (xs ?? Enumerable.Empty<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim()));

            var toStr = Join(to);
            var ccStr = Join(cc);
            if (toStr.Length == 0 && ccStr.Length == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.Append("<div style=\"margin:18px 0 0;padding-top:14px;border-top:1px solid ").Append(Border)
              .Append(";color:").Append(Muted).Append(";font-size:12px;line-height:1.6;\">");
            if (toStr.Length > 0)
            {
                sb.Append("<div><span style=\"font-weight:600;color:").Append(Text)
                  .Append(";\">To:</span> ").Append(E(toStr)).Append("</div>");
            }
            if (ccStr.Length > 0)
            {
                sb.Append("<div><span style=\"font-weight:600;color:").Append(Text)
                  .Append(";\">Cc:</span> ").Append(E(ccStr)).Append("</div>");
            }
            sb.Append("</div>");
            return sb.ToString();
        }

        /// <summary>
        /// "Sent to:" recipient block so each per-recipient send still shows the
        /// full distribution (restores the dropped recipient list).
        /// </summary>
        internal static string RecipientLine(IEnumerable<string>? recipients)
        {
            var list = (recipients ?? Enumerable.Empty<string>())
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Select(r => r.Trim())
                .ToList();

            if (list.Count == 0) return string.Empty;

            return new StringBuilder()
                .Append("<p style=\"margin:18px 0 0;padding-top:14px;border-top:1px solid ").Append(Border)
                .Append(";color:").Append(Muted).Append(";font-size:12px;line-height:1.5;\">")
                .Append("<span style=\"font-weight:600;color:").Append(Text).Append(";\">Sent to:</span> ")
                .Append(E(string.Join("; ", list)))
                .Append("</p>")
                .ToString();
        }
    }
}
