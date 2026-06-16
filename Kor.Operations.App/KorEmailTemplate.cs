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

            sb.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" ")
              .Append("style=\"background-color:").Append(Pale).Append(";margin:0;padding:0;width:100%;\">")
              .Append("<tr><td align=\"center\" style=\"padding:24px 12px;\">");

            // Centred 600px card.
            sb.Append("<table role=\"presentation\" width=\"600\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" align=\"center\" ")
              .Append("style=\"width:600px;max-width:600px;background-color:#ffffff;border:1px solid ").Append(Border)
              .Append(";border-radius:8px;font-family:").Append(FontStack).Append(";\">");

            // Header band — text wordmark (no image: avoids Gmail blocking data:/hosted logos).
            sb.Append("<tr><td style=\"background-color:").Append(Slate)
              .Append(";border-radius:8px 8px 0 0;padding:20px 28px;\">")
              .Append("<div style=\"color:").Append(SlateTint)
              .Append(";font-size:11px;letter-spacing:1.5px;font-weight:600;text-transform:uppercase;\">")
              .Append(E(eyebrow)).Append("</div>")
              .Append("<div style=\"color:#ffffff;font-size:22px;font-weight:700;letter-spacing:1px;padding-top:2px;\">")
              .Append("KOR <span style=\"font-weight:400;color:").Append(SlateTint).Append(";\">Structural</span></div>")
              .Append("</td></tr>");

            // Body.
            sb.Append("<tr><td style=\"padding:28px;color:").Append(Text)
              .Append(";font-size:14px;line-height:1.6;font-family:").Append(FontStack).Append(";\">")
              .Append(innerBodyHtml)
              .Append("</td></tr>");

            // Footer.
            sb.Append("<tr><td style=\"border-top:1px solid ").Append(Border)
              .Append(";padding:16px 28px;color:").Append(Muted)
              .Append(";font-size:12px;line-height:1.5;font-family:").Append(FontStack).Append(";\">")
              .Append("KOR Structural Engineering &nbsp;&middot;&nbsp; Vancouver &middot; Los Angeles &middot; San Diego<br/>")
              .Append("This message was sent automatically via KOR Operations.")
              .Append("</td></tr>");

            sb.Append("</table></td></tr></table>");

            if (!string.IsNullOrWhiteSpace(pixelUrl))
            {
                sb.Append("<img src=\"").Append(E(pixelUrl))
                  .Append("\" width=\"1\" height=\"1\" alt=\"\" style=\"display:none;width:1px;height:1px;max-height:1px;max-width:1px;overflow:hidden;\" />");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Bulletproof accent call-to-action button (table cell + inline-block
        /// anchor) — renders as a real button across Outlook/Gmail/Apple Mail,
        /// replacing the old bare "Click here" link that read as spam.
        /// </summary>
        internal static string Button(string url, string text)
        {
            return new StringBuilder()
                .Append("<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" style=\"margin:4px 0;\"><tr>")
                .Append("<td align=\"center\" bgcolor=\"").Append(Accent)
                .Append("\" style=\"background-color:").Append(Accent).Append(";border-radius:6px;\">")
                .Append("<a href=\"").Append(E(url))
                .Append("\" style=\"display:inline-block;padding:12px 28px;color:#ffffff;font-size:14px;font-weight:600;")
                .Append("text-decoration:none;font-family:").Append(FontStack).Append(";\">")
                .Append(E(text)).Append(" &rarr;</a>")
                .Append("</td></tr></table>")
                .ToString();
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
